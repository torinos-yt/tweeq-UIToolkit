using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Preview overlay that only lives while a swipe toggle is in progress (spec "Common").
    /// Draws two 18px circular badges to the left and right of the control, coloring only the preview-value side Accent.
    ///
    /// Placement is "a child of the control itself" rather than TweeqOverlayLayer (the frontmost panel layer).
    /// Badge position is a purely local quantity, ±1.2×24px relative to the control's box, so there is no reason
    /// to convert to panel coordinates, and layout following (scroll, group expand/collapse) can be left to the
    /// parent. The precondition is that the parent keeps overflow set to Visible.
    /// ParameterGroup reverts to Visible once fully expanded, so it isn't clipped under normal layout.
    /// </summary>
    sealed class BoolTweakOverlay : VisualElement
    {
        #region Constants

        // Expands from ±1.0x to ±1.2x on appearance (spec "Common" / Vue's v-enter-from)
        const float COLLAPSED_FACTOR = 1.0f;
        const float EXPANDED_FACTOR = 1.2f;

        const float BADGE_SIZE = 18f;
        const float BADGE_STROKE_WIDTH = 2f;

        // Active-family transition is 64ms (spec's transition table)
        const float ACTIVE_TRANSITION_DURATION = 0.064f;

        // The check inside the check-circle glyph is roughly 60% the size of the circle
        const float BADGE_CHECK_SCALE = 0.62f;

        // The check mark (normalized coordinates within a unit square). Simplified mdi:check-bold into a 2-segment polyline
        static readonly Vector2 MARK_START = new Vector2(0.18f, 0.50f);
        static readonly Vector2 MARK_ELBOW = new Vector2(0.42f, 0.74f);
        static readonly Vector2 MARK_END = new Vector2(0.82f, 0.26f);

        #endregion

        #region Fields

        TweeqTheme _theme;
        bool _previewValue;
        float _unit = 24f;
        bool _expanded;

        #endregion

        #region Construction

        public BoolTweakOverlay()
        {
            this.name = "tweeq-bool-tweak-overlay";
            this.pickingMode = PickingMode.Ignore;
            this.style.position = Position.Absolute;
            this.style.top = 0f;
            this.style.bottom = 0f;
            this.style.overflow = Overflow.Visible;

            ApplyInsets();
            ApplyTransition();

            this.generateVisualContent += OnGenerateVisualContent;
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        void ApplyTransition()
        {
            // Vue uses cubic-bezier(0.4,0,0.2,1). Since UI Toolkit has no identical curve,
            // approximate it with EaseInOutCubic (same call as RotaryInput / NumberInput)
            this.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName>
                {
                    new StylePropertyName("left"),
                    new StylePropertyName("right"),
                });
            this.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue>
                {
                    new TimeValue(ACTIVE_TRANSITION_DURATION, TimeUnit.Second),
                    new TimeValue(ACTIVE_TRANSITION_DURATION, TimeUnit.Second),
                });
            this.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction>
                {
                    new EasingFunction(EasingMode.EaseInOutCubic),
                    new EasingFunction(EasingMode.EaseInOutCubic),
                });
        }

        #endregion

        #region Public API

        /// <summary>Updates the drawing parameters. unit is the control's reference height (= InputHeight).</summary>
        public void Sync(TweeqTheme theme, bool previewValue, float unit)
        {
            _theme = theme;
            _previewValue = previewValue;

            if (unit > 0f && !float.IsNaN(unit))
            {
                _unit = unit;
            }

            ApplyInsets();
            this.MarkDirtyRepaint();
        }

        #endregion

        #region Internals

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (_expanded)
            {
                return;
            }

            // The transition doesn't run unless the collapsed inset has been drawn for 1 frame first
            this.schedule.Execute(() =>
            {
                _expanded = true;
                ApplyInsets();
            }).StartingIn(0);
        }

        void ApplyInsets()
        {
            float amount = _unit * (_expanded ? EXPANDED_FACTOR : COLLAPSED_FACTOR);
            this.style.left = -amount;
            this.style.right = -amount;
        }

        void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            float width = this.layout.width;
            float height = this.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width < BADGE_SIZE || height <= 0f)
            {
                return;
            }

            float radius = BADGE_SIZE * 0.5f;
            float centerY = height * 0.5f;

            // Painter2D can't interpolate colors, so we give up on the 64ms color transition and switch instantly instead
            Color offColor = _previewValue ? _theme.Border : _theme.Accent;
            Color onColor = _previewValue ? _theme.Accent : _theme.Border;

            PaintOffBadge(painter, new Vector2(radius, centerY), radius, offColor);
            PaintOnBadge(painter, new Vector2(width - radius, centerY), radius, onColor);
        }

        // Equivalent to ic:baseline-radio-button-unchecked. Draws a ring over a Background-colored circle
        void PaintOffBadge(Painter2D painter, Vector2 center, float radius, Color color)
        {
            FillCircle(painter, center, radius, _theme.Background);

            painter.strokeColor = color;
            painter.lineWidth = BADGE_STROKE_WIDTH;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.Arc(
                center,
                Mathf.Max(radius - BADGE_STROKE_WIDTH * 0.5f, 0.5f),
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();
        }

        // Equivalent to ic:baseline-check-circle. The shape of a filled circle with the check cut out (i.e. drawn in the background color)
        void PaintOnBadge(Painter2D painter, Vector2 center, float radius, Color color)
        {
            FillCircle(painter, center, radius, color);

            float half = radius * BADGE_CHECK_SCALE;
            Rect box = new Rect(center.x - half, center.y - half, half * 2f, half * 2f);
            PaintCheck(painter, box, BADGE_STROKE_WIDTH, _theme.Background);
        }

        static void FillCircle(Painter2D painter, Vector2 center, float radius, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(
                center,
                radius,
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Fill();
        }

        static void PaintCheck(Painter2D painter, Rect box, float strokeWidth, Color color)
        {
            painter.strokeColor = color;
            painter.lineWidth = strokeWidth;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(Map(box, MARK_START));
            painter.LineTo(Map(box, MARK_ELBOW));
            painter.LineTo(Map(box, MARK_END));
            painter.Stroke();
        }

        static Vector2 Map(Rect box, Vector2 normalized)
        {
            return new Vector2(
                box.xMin + box.width * normalized.x,
                box.yMin + box.height * normalized.y);
        }

        #endregion
    }
}
