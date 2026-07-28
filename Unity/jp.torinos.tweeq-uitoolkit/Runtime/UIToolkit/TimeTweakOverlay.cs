using System;
using System.Collections.Generic;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Clock face that lives only while a time field is being scrubbed. It stacks a tick ring
    /// with the frame / second / minute / hour hands and accents the hand of the active scale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Vue original draws an SVG with viewBox 0 0 100 100 at 360px, and its strokes use
    /// vector-effect: non-scaling-stroke so only the widths stay in screen pixels. Radii are
    /// therefore kept in viewBox units and multiplied by 3.6 when painting, while the stroke
    /// widths are used verbatim as pixels.
    /// </para>
    /// <para>
    /// It lives on the <see cref="TweeqOverlayLayer"/>: a 360px box centred on the field that
    /// opens with scale .5 -> 1 and opacity 0 -> 1 (the Vue v-enter-from). The radial-gradient
    /// mask that punches out the centre has no UI Toolkit counterpart and is omitted.
    /// </para>
    /// </remarks>
    sealed class TimeTweakOverlay : VisualElement
    {
        #region Constants

        // Vue $size = 360px against viewBox = 100
        const float SIZE = 360f;
        const float VIEWBOX = 100f;
        const float UNIT = SIZE / VIEWBOX;
        const float CENTER = SIZE * 0.5f;

        // Radii in viewBox units. The inner second radius is negative so the hand crosses the
        // centre and sticks out on the opposite side
        const float METER_INNER_RADIUS = 48f;
        const float METER_OUTER_RADIUS = 49f;
        const float FRAME_RADIUS = 48f;
        const float SECOND_INNER_RADIUS = -15f;
        const float SECOND_OUTER_RADIUS = 45f;
        const float MINUTE_OUTER_RADIUS = 40f;
        const float HOUR_OUTER_RADIUS = 20f;

        // Vue stroke-width values; non-scaling-stroke keeps them in pixels
        const float METER_WIDTH = 1f;
        const float FRAME_WIDTH = 10f;
        const float SECOND_WIDTH = 1f;
        const float MINUTE_WIDTH = 3f;
        const float HOUR_WIDTH = 5f;

        // The tick ring carries one tick per frame only at the frames scale, 12 otherwise
        const int COARSE_METER_COUNT = 12;

        // Guards the paint loop against a broken frame rate
        const int MAX_METER_COUNT = 480;

        const double SECONDS_PER_MINUTE = 60.0;
        const double SECONDS_PER_HOUR = 3600.0;
        const double MINUTES_PER_HOUR = 60.0;
        const double HOURS_PER_DAY = 24.0;
        const double HOURS_PER_CLOCK = 12.0;

        const float ENTER_SCALE = 0.5f;

        #endregion

        #region Fields

        TweeqTheme _theme;
        double _frames;
        double _frameRate = 24.0;
        int _scale;
        bool _hasState;
        bool _expanded;

        #endregion

        #region Construction

        public TimeTweakOverlay()
        {
            this.name = "tweeq-time-tweak-overlay";
            this.pickingMode = PickingMode.Ignore;
            this.style.position = Position.Absolute;
            this.style.width = SIZE;
            this.style.height = SIZE;
            this.style.overflow = Overflow.Visible;
            this.style.opacity = 0f;
            this.style.scale = new StyleScale(new Scale(new Vector3(ENTER_SCALE, ENTER_SCALE, 1f)));

            this.generateVisualContent += OnGenerateVisualContent;
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        #endregion

        #region Public API

        /// <summary>Updates the paint parameters. center is the field centre in panel space.</summary>
        public void Sync(TweeqTheme theme, Vector2 center, double frames, double frameRate, int scale)
        {
            if (theme == null)
            {
                return;
            }

            bool changed = !_hasState
                || !ReferenceEquals(_theme, theme)
                || _scale != scale
                || !TweeqFormat.SameValueBits(_frames, frames)
                || !TweeqFormat.SameValueBits(_frameRate, frameRate);

            _theme = theme;
            _frames = frames;
            _frameRate = frameRate;
            _scale = scale;
            _hasState = true;

            ApplyTransition(theme);
            ApplyPosition(center);

            if (changed)
            {
                this.MarkDirtyRepaint();
            }
        }

        #endregion

        #region Layout

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (_expanded)
            {
                return;
            }

            // The transition only runs once the collapsed state has been drawn for a frame
            this.schedule.Execute(() =>
            {
                _expanded = true;
                this.style.opacity = 1f;
                this.style.scale = new StyleScale(new Scale(Vector3.one));
            }).StartingIn(0);
        }

        void ApplyPosition(Vector2 center)
        {
            if (float.IsNaN(center.x) || float.IsNaN(center.y))
            {
                return;
            }

            this.style.left = center.x - CENTER;
            this.style.top = center.y - CENTER;
        }

        void ApplyTransition(TweeqTheme theme)
        {
            if (_expanded)
            {
                return;
            }

            // Vue uses cubic-bezier(0.4,0,0.2,1); UI Toolkit has no identical curve, so
            // EaseInOutCubic approximates it (same call as the other tweeq widgets)
            this.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName>
                {
                    new StylePropertyName("opacity"),
                    new StylePropertyName("scale"),
                });
            this.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue>
                {
                    new TimeValue(theme.HoverTransitionDuration, TimeUnit.Second),
                    new TimeValue(theme.HoverTransitionDuration, TimeUnit.Second),
                });
            this.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction>
                {
                    new EasingFunction(EasingMode.EaseInOutCubic),
                    new EasingFunction(EasingMode.EaseInOutCubic),
                });
        }

        #endregion

        #region Painting

        void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (!_hasState || context == null || _theme == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null || !TweeqMath.IsFinite(_frameRate) || _frameRate <= 0.0
                || !TweeqMath.IsFinite(_frames))
            {
                return;
            }

            PaintMeters(painter);
            PaintSecond(painter);
            PaintMinute(painter);
            PaintHour(painter);

            // The frame mark is a fat dot, so it goes last and does not bury the thin hands
            PaintFrame(painter);
        }

        void PaintMeters(Painter2D painter)
        {
            int count = _scale == TimecodeLogic.SCALE_FRAMES
                ? (int)Math.Ceiling(_frameRate)
                : COARSE_METER_COUNT;

            if (count < 1)
            {
                count = 1;
            }
            else if (count > MAX_METER_COUNT)
            {
                count = MAX_METER_COUNT;
            }

            painter.strokeColor = _theme.TextSubtle;
            painter.lineWidth = METER_WIDTH;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();

            for (int index = 0; index < count; index++)
            {
                double t = index / (double)count;
                painter.MoveTo(Polar(t, METER_INNER_RADIUS));
                painter.LineTo(Polar(t, METER_OUTER_RADIUS));
            }

            painter.Stroke();
        }

        // Vue draws a zero-length line with stroke-width 10, which a butt cap renders as nothing.
        // A filled circle is what that was meant to be: a dot 10 pixels across
        void PaintFrame(Painter2D painter)
        {
            double t = _frames % _frameRate / _frameRate;

            painter.fillColor = _scale == TimecodeLogic.SCALE_FRAMES ? _theme.Accent : _theme.Border;
            painter.BeginPath();
            painter.Arc(
                Polar(t, FRAME_RADIUS),
                FRAME_WIDTH * 0.5f,
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Fill();
        }

        void PaintSecond(Painter2D painter)
        {
            double seconds = Math.Floor(_frames / _frameRate) % SECONDS_PER_MINUTE;
            PaintHand(
                painter,
                seconds / SECONDS_PER_MINUTE,
                SECOND_INNER_RADIUS,
                SECOND_OUTER_RADIUS,
                SECOND_WIDTH,
                TimecodeLogic.SCALE_SECONDS);
        }

        void PaintMinute(Painter2D painter)
        {
            double minutes =
                Math.Floor(_frames / (_frameRate * SECONDS_PER_MINUTE)) % MINUTES_PER_HOUR;
            PaintHand(
                painter,
                minutes / MINUTES_PER_HOUR,
                0f,
                MINUTE_OUTER_RADIUS,
                MINUTE_WIDTH,
                TimecodeLogic.SCALE_MINUTES);
        }

        void PaintHour(Painter2D painter)
        {
            double hours = Math.Floor(_frames / (_frameRate * SECONDS_PER_HOUR)) % HOURS_PER_DAY;

            // Vue hides the hand at hour 0 because 0 and 12 land on the same spoke
            if (hours == 0.0)
            {
                return;
            }

            PaintHand(
                painter,
                hours / HOURS_PER_CLOCK,
                0f,
                HOUR_OUTER_RADIUS,
                HOUR_WIDTH,
                TimecodeLogic.SCALE_HOURS);
        }

        void PaintHand(
            Painter2D painter, double t, float innerRadius, float outerRadius, float width, int scale)
        {
            painter.strokeColor = _scale == scale ? _theme.Accent : _theme.TextSubtle;
            painter.lineWidth = width;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.MoveTo(Polar(t, innerRadius));
            painter.LineTo(Polar(t, outerRadius));
            painter.Stroke();
        }

        // t = 0 points straight up (Vue deg = t * 360 - 90). The radius arrives in viewBox units
        static Vector2 Polar(double t, float viewBoxRadius)
        {
            if (!TweeqMath.IsFinite(t))
            {
                t = 0.0;
            }

            float degrees = (float)(t * 360.0) - 90f;
            float radians = Mathf.Deg2Rad * degrees;
            float radius = viewBoxRadius * UNIT;
            return new Vector2(
                CENTER + Mathf.Cos(radians) * radius,
                CENTER + Mathf.Sin(radians) * radius);
        }

        #endregion
    }
}
