using System;
using System.Collections.Generic;
using System.Globalization;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// One tick on a <see cref="TweeqRuler"/>.
    /// </summary>
    /// <remarks>
    /// Always construct through a constructor: <c>default(RulerScale)</c> is fully transparent,
    /// since a readonly struct cannot default <see cref="Opacity"/> to 1.
    /// </remarks>
    public readonly struct RulerScale
    {
        /// <summary>Where the tick sits, in the ruler's own units.</summary>
        public readonly double Value;

        /// <summary>The text beside the tick. Null draws the tick line only.</summary>
        public readonly string Label;

        /// <summary>Tick and label opacity, for fading a level of subdivision in and out.</summary>
        public readonly float Opacity;

        public RulerScale(double value)
            : this(value, null, 1f)
        {
        }

        public RulerScale(double value, string label)
            : this(value, label, 1f)
        {
        }

        public RulerScale(double value, string label, float opacity)
        {
            this.Value = value;
            this.Label = label;
            this.Opacity = opacity;
        }
    }

    /// <summary>
    /// A horizontal scale strip. Port of the Vue original's Ruler.vue, and independent of
    /// <see cref="TweeqTimeline"/> exactly as in the original.
    /// </summary>
    /// <remarks>
    /// The original paints the unit grid with a repeating CSS gradient and each labelled tick with
    /// a bordered div; UI Toolkit has neither, so the lines go through Painter2D while the labels
    /// are pooled child Labels.
    /// </remarks>
    [UxmlElement]
    public partial class TweeqRuler : VisualElement, ITweeqThemed
    {
        #region Constants

        const float DEFAULT_HEIGHT = 16f;
        const float LABEL_FONT_SIZE = 9f;

        const float TICK_WIDTH = 1f;

        // Below this the unit grid turns into a solid block, so it is dropped instead.
        const float MIN_GRID_SPACING = 3f;

        // Upper bounds so a broken range can never spin the draw loop or spawn endless Labels.
        const int MAX_GRID_LINES = 2000;
        const int MAX_SCALES = 512;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        double _rangeStart;
        double _rangeEnd = 1.0;

        IList<RulerScale> _scales;

        // Auto scales are only rebuilt when the covered integer span actually changes, so panning
        // within one frame allocates no label strings.
        readonly List<RulerScale> _autoScales = new List<RulerScale>();
        bool _autoValid;
        double _autoFirst;
        double _autoLast;

        readonly List<Label> _labels = new List<Label>();

        float _viewportWidth;

        int _dragPointerId = PointerId.invalidPointerId;

        #endregion

        #region Public API

        /// <summary>Value at the ruler's left edge.</summary>
        [UxmlAttribute("range-start")]
        public double RangeStart
        {
            get => _rangeStart;
            set
            {
                if (!TweeqMath.IsFinite(value) || _rangeStart == value)
                {
                    return;
                }

                _rangeStart = value;
                Refresh();
            }
        }

        /// <summary>Value at the ruler's right edge.</summary>
        [UxmlAttribute("range-end")]
        public double RangeEnd
        {
            get => _rangeEnd;
            set
            {
                if (!TweeqMath.IsFinite(value) || _rangeEnd == value)
                {
                    return;
                }

                _rangeEnd = value;
                Refresh();
            }
        }

        /// <summary>
        /// The ticks to draw. Null falls back to one per integer value, as in the original.
        /// <see cref="TweeqRulerScales"/> builds thinned-out sets for a given label spacing.
        /// </summary>
        public IList<RulerScale> Scales
        {
            get => _scales;
            set
            {
                _scales = value;
                Refresh();
            }
        }

        /// <summary>
        /// The value under the pointer while dragging. Fires on press with no threshold, so a
        /// single click also reports once (the original's pointerCapture drag).
        /// </summary>
        public event Action<double> Dragged;

        /// <summary>The color theme. Falls back to Dark() when null is passed.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyLabelStyles();
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>The width in px that the value/pixel mapping is based on.</summary>
        public float ViewportWidth => _viewportWidth;

        /// <summary>Pixels per unit at the current width. 0 while the range or width is degenerate.</summary>
        public float PixelsPerUnit
        {
            get
            {
                double span = _rangeEnd - _rangeStart;
                if (_viewportWidth <= 0f || !TweeqMath.IsFinite(span) || span == 0.0)
                {
                    return 0f;
                }

                return (float)(_viewportWidth / span);
            }
        }

        /// <summary>
        /// Rebases the value/pixel mapping on a width. Normally driven by GeometryChangedEvent;
        /// public so a host (or a test) can drive it before layout has run.
        /// </summary>
        public void SetViewportWidth(float width)
        {
            float sanitized = float.IsNaN(width) || width < 0f ? 0f : width;
            if (_viewportWidth == sanitized)
            {
                return;
            }

            _viewportWidth = sanitized;
            SyncLabels();
            this.MarkDirtyRepaint();
        }

        /// <summary>The local x of a value.</summary>
        public float ValueToLocalX(double value)
        {
            if (!TweeqMath.IsFinite(value))
            {
                return 0f;
            }

            return (float)((value - _rangeStart) * this.PixelsPerUnit);
        }

        /// <summary>The value at a local x. Not clamped to the range.</summary>
        public double LocalXToValue(float x)
        {
            float pixelsPerUnit = this.PixelsPerUnit;
            if (float.IsNaN(x) || pixelsPerUnit <= 0f)
            {
                return _rangeStart;
            }

            return _rangeStart + x / pixelsPerUnit;
        }

        /// <summary>
        /// The ticks actually drawn: <see cref="Scales"/> when set, otherwise the per-integer
        /// fallback. Treat it as read-only; the fallback is a buffer this ruler reuses.
        /// </summary>
        public IList<RulerScale> ResolvedScales => EffectiveScales();

        /// <summary>Recomputes tick positions and labels. Call after mutating a <see cref="Scales"/> list in place.</summary>
        /// <remarks>
        /// The automatic fallback is not thrown away here: its content depends only on which
        /// integers the range covers, and that is checked when it is rebuilt. Panning within one
        /// unit therefore repositions the labels without rebuilding any strings.
        /// </remarks>
        public void Refresh()
        {
            SyncLabels();
            this.MarkDirtyRepaint();
        }

        #endregion

        #region Construction

        public TweeqRuler()
        {
            this.AddToClassList("tweeq-ruler");
            this.style.position = Position.Relative;
            this.style.height = DEFAULT_HEIGHT;
            this.style.overflow = Overflow.Hidden;
            this.style.flexShrink = 0f;

            this.generateVisualContent += OnGenerateVisualContent;

            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            this.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        #endregion

        #region Input

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _dragPointerId != PointerId.invalidPointerId)
            {
                return;
            }

            _dragPointerId = evt.pointerId;

            if (this.panel != null)
            {
                this.CapturePointer(_dragPointerId);
            }

            EmitDrag(evt.position.x);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || _dragPointerId == PointerId.invalidPointerId
                || evt.pointerId != _dragPointerId)
            {
                return;
            }

            EmitDrag(evt.position.x);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || evt.pointerId != _dragPointerId)
            {
                return;
            }

            EndDrag();
            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            EndDrag();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (evt == null || ReferenceEquals(evt.target, this))
            {
                EndDrag();
            }
        }

        void EndDrag()
        {
            if (_dragPointerId == PointerId.invalidPointerId)
            {
                return;
            }

            int pointerId = _dragPointerId;
            _dragPointerId = PointerId.invalidPointerId;

            if (this.panel != null && this.HasPointerCapture(pointerId))
            {
                this.ReleasePointer(pointerId);
            }
        }

        void EmitDrag(float panelX)
        {
            if (Dragged == null)
            {
                return;
            }

            float localX = this.WorldToLocal(new Vector2(panelX, 0f)).x;

            // The original maps through scalar.fit, which clamps, so a pointer dragged off the
            // strip keeps reporting the nearer edge instead of running past the range.
            double value = TweeqMath.Clamp(
                LocalXToValue(localX),
                Math.Min(_rangeStart, _rangeEnd),
                Math.Max(_rangeStart, _rangeEnd));

            Dragged.Invoke(value);
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt == null || !ReferenceEquals(evt.target, this))
            {
                return;
            }

            SetViewportWidth(evt.newRect.width);
        }

        #endregion

        #region Scales

        IList<RulerScale> EffectiveScales()
        {
            if (_scales != null)
            {
                return _scales;
            }

            RebuildAutoScales();
            return _autoScales;
        }

        // One tick per integer value, matching the original's lodash range(ceil(start), floor(end)+1).
        void RebuildAutoScales()
        {
            double first = Math.Ceiling(_rangeStart);
            double last = Math.Floor(_rangeEnd);

            if (_autoValid && _autoFirst == first && _autoLast == last)
            {
                return;
            }

            _autoValid = true;
            _autoFirst = first;
            _autoLast = last;
            _autoScales.Clear();

            if (!TweeqMath.IsFinite(first) || !TweeqMath.IsFinite(last) || last < first)
            {
                return;
            }

            int count = (int)Math.Min(last - first + 1.0, MAX_SCALES);
            for (int index = 0; index < count; index++)
            {
                double value = first + index;

                // The original prints scale.value when no label is given; this port treats a null
                // label as "line only", so the number is baked in here instead.
                _autoScales.Add(new RulerScale(
                    value, value.ToString(CultureInfo.InvariantCulture)));
            }
        }

        #endregion

        #region Labels

        void SyncLabels()
        {
            IList<RulerScale> scales = EffectiveScales();
            int count = Math.Min(scales.Count, MAX_SCALES);

            for (int index = 0; index < count; index++)
            {
                RulerScale scale = scales[index];
                Label label = LabelAt(index);

                if (string.IsNullOrEmpty(scale.Label))
                {
                    label.style.display = DisplayStyle.None;
                    continue;
                }

                label.style.display = DisplayStyle.Flex;

                // Comparing first keeps a pan from re-uploading identical text every frame.
                if (!string.Equals(label.text, scale.Label, StringComparison.Ordinal))
                {
                    label.text = scale.Label;
                }

                label.style.translate = new Translate(ValueToLocalX(scale.Value), 0f);
                label.style.opacity = scale.Opacity;
            }

            for (int index = count; index < _labels.Count; index++)
            {
                _labels[index].style.display = DisplayStyle.None;
            }
        }

        Label LabelAt(int index)
        {
            while (_labels.Count <= index)
            {
                Label created = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
                created.style.position = Position.Absolute;
                created.style.left = 0f;
                created.style.top = 0f;
                created.style.marginLeft = ResolveLabelIndent(_theme);
                created.style.marginRight = 0f;
                created.style.marginTop = 0f;
                created.style.marginBottom = 0f;
                created.style.fontSize = ResolveLabelFontSize(_theme);
                created.style.color = _theme.TextSubtle;
                TweeqFonts.Apply(created, _theme.FontNumeric);

                _labels.Add(created);
                this.hierarchy.Add(created);
            }

            return _labels[index];
        }

        void ApplyLabelStyles()
        {
            float fontSize = ResolveLabelFontSize(_theme);
            float indent = ResolveLabelIndent(_theme);
            for (int index = 0; index < _labels.Count; index++)
            {
                _labels[index].style.fontSize = fontSize;
                _labels[index].style.marginLeft = indent;
                _labels[index].style.color = _theme.TextSubtle;
                TweeqFonts.Apply(_labels[index], _theme.FontNumeric);
            }
        }

        static float ResolveLabelFontSize(TweeqTheme theme)
        {
            return theme != null ? theme.FontSizeRuler : LABEL_FONT_SIZE;
        }

        static float ResolveLabelIndent(TweeqTheme theme)
        {
            return ResolveLabelFontSize(theme) * 0.4f;
        }

        #endregion

        #region Painting

        void OnGenerateVisualContent(MeshGenerationContext context)
        {
            Painter2D painter = context?.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = this.contentRect;
            if (float.IsNaN(rect.width) || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            PaintUnitGrid(painter, rect);
            PaintScales(painter, rect);
        }

        // Stands in for the original's repeating linear-gradient: one hairline per unit.
        void PaintUnitGrid(Painter2D painter, Rect rect)
        {
            float spacing = this.PixelsPerUnit;
            if (spacing < MIN_GRID_SPACING || float.IsNaN(spacing))
            {
                return;
            }

            double first = Math.Ceiling(_rangeStart);
            double last = Math.Floor(_rangeEnd);
            if (!TweeqMath.IsFinite(first) || !TweeqMath.IsFinite(last) || last < first)
            {
                return;
            }

            int count = (int)Math.Min(last - first + 1.0, MAX_GRID_LINES);

            painter.strokeColor = _theme.Border;
            painter.lineWidth = TICK_WIDTH;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();

            for (int index = 0; index < count; index++)
            {
                float x = ValueToLocalX(first + index);
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, rect.height));
            }

            painter.Stroke();
        }

        void PaintScales(Painter2D painter, Rect rect)
        {
            IList<RulerScale> scales = EffectiveScales();
            int count = Math.Min(scales.Count, MAX_SCALES);

            painter.lineWidth = TICK_WIDTH;
            painter.lineCap = LineCap.Butt;

            Color baseColor = _theme.TextMuted;

            for (int index = 0; index < count; index++)
            {
                RulerScale scale = scales[index];
                float x = ValueToLocalX(scale.Value);
                if (x < 0f || x > rect.width)
                {
                    continue;
                }

                // Opacity is per tick, so each one needs its own stroke rather than a batched path.
                Color color = baseColor;
                color.a *= Mathf.Clamp01(scale.Opacity);
                painter.strokeColor = color;

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, rect.height));
                painter.Stroke();
            }
        }

        #endregion
    }

    /// <summary>
    /// Builds thinned-out <see cref="RulerScale"/> sets. The original leaves this entirely to the
    /// host; these are the two cases this port needs often enough to share.
    /// </summary>
    public static class TweeqRulerScales
    {
        #region Constants

        /// <summary>A readable default gap between labels (px).</summary>
        public const double DEFAULT_MIN_GAP_PX = 48.0;

        const int MAX_SCALES = 512;

        // A 1-2-5 progression is the standard "power of ten" family: every step divides the decade
        // evenly, so labels stay on round numbers at any zoom.
        static readonly double[] Mantissas = { 1.0, 2.0, 5.0 };

        // Timecode reads in whole frames, then in the second-based groupings a clock actually has.
        static readonly double[] FrameSteps = { 1.0, 2.0, 5.0, 10.0, 15.0, 20.0, 30.0 };

        static readonly double[] SecondSteps =
        {
            1.0, 2.0, 5.0, 10.0, 15.0, 30.0,
            60.0, 120.0, 300.0, 600.0, 900.0, 1800.0, 3600.0,
        };

        // Cached so label formatting never builds a format string per tick.
        static readonly string[] Formats = { "0", "0.#", "0.##", "0.###", "0.####" };

        #endregion

        #region Numeric

        /// <summary>
        /// Ticks on a 1-2-5 progression, coarse enough that neighbouring labels stay at least
        /// <paramref name="minGapPx"/> apart at the given pixel width.
        /// </summary>
        public static List<RulerScale> Build(
            double start, double end, double minGapPx, float width)
        {
            List<RulerScale> results = new List<RulerScale>();
            Build(results, start, end, minGapPx, width);
            return results;
        }

        /// <summary>
        /// Fills a caller-owned list, so a host syncing on every visible range change can reuse
        /// one buffer instead of allocating per frame.
        /// </summary>
        public static void Build(
            List<RulerScale> results, double start, double end, double minGapPx, float width)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();

            double step = NiceStep(start, end, minGapPx, width);
            if (!TweeqMath.IsFinite(step) || step <= 0.0)
            {
                return;
            }

            string format = Formats[Math.Min(TweeqMath.PrecisionOf(step), Formats.Length - 1)];

            double first = Math.Ceiling(start / step) * step;
            for (int index = 0; index < MAX_SCALES; index++)
            {
                // Accumulating index * step rather than adding repeatedly keeps the last tick from
                // drifting off the round value its label claims.
                double value = first + index * step;
                if (value > end)
                {
                    return;
                }

                results.Add(new RulerScale(
                    value, value.ToString(format, CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// The coarsest-but-smallest 1-2-5 step that keeps labels <paramref name="minGapPx"/>
        /// apart. NaN when the range or width is degenerate.
        /// </summary>
        public static double NiceStep(double start, double end, double minGapPx, float width)
        {
            double span = end - start;
            if (!TweeqMath.IsFinite(span) || span <= 0.0
                || float.IsNaN(width) || width <= 0f
                || !TweeqMath.IsFinite(minGapPx) || minGapPx <= 0.0)
            {
                return double.NaN;
            }

            double pixelsPerUnit = width / span;
            double minStep = minGapPx / pixelsPerUnit;
            if (!TweeqMath.IsFinite(minStep) || minStep <= 0.0)
            {
                return double.NaN;
            }

            double exponent = Math.Floor(Math.Log10(minStep));
            for (int decade = 0; decade < 3; decade++)
            {
                double scale = Math.Pow(10.0, exponent + decade);
                for (int index = 0; index < Mantissas.Length; index++)
                {
                    double step = Mantissas[index] * scale;
                    if (step >= minStep)
                    {
                        return step;
                    }
                }
            }

            return Math.Pow(10.0, exponent + 3);
        }

        #endregion

        #region Timecode

        /// <summary>
        /// Ticks labelled as timecode via <see cref="TimecodeLogic.FormatTimecode"/>, stepped on
        /// frame- then second-sized groupings instead of a plain decimal progression.
        /// </summary>
        public static List<RulerScale> BuildTimecode(
            double start, double end, double fps, double minGapPx, float width)
        {
            List<RulerScale> results = new List<RulerScale>();
            BuildTimecode(results, start, end, fps, minGapPx, width);
            return results;
        }

        /// <summary>Fills a caller-owned list. See <see cref="BuildTimecode(double,double,double,double,float)"/>.</summary>
        public static void BuildTimecode(
            List<RulerScale> results, double start, double end, double fps, double minGapPx,
            float width)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();

            double step = TimecodeStep(start, end, fps, minGapPx, width);
            if (!TweeqMath.IsFinite(step) || step <= 0.0)
            {
                return;
            }

            double first = Math.Ceiling(start / step) * step;
            for (int index = 0; index < MAX_SCALES; index++)
            {
                double value = first + index * step;
                if (value > end)
                {
                    return;
                }

                results.Add(new RulerScale(value, TimecodeLogic.FormatTimecode(value, fps)));
            }
        }

        /// <summary>
        /// The smallest frame- or second-sized step whose labels stay <paramref name="minGapPx"/>
        /// apart. NaN when the range, fps or width is degenerate.
        /// </summary>
        public static double TimecodeStep(
            double start, double end, double fps, double minGapPx, float width)
        {
            double span = end - start;
            if (!TweeqMath.IsFinite(span) || span <= 0.0
                || !TweeqMath.IsFinite(fps) || fps <= 0.0
                || float.IsNaN(width) || width <= 0f
                || !TweeqMath.IsFinite(minGapPx) || minGapPx <= 0.0)
            {
                return double.NaN;
            }

            double pixelsPerUnit = width / span;
            double minStep = minGapPx / pixelsPerUnit;

            for (int index = 0; index < FrameSteps.Length; index++)
            {
                double step = FrameSteps[index];
                if (step < fps && step >= minStep)
                {
                    return step;
                }
            }

            for (int index = 0; index < SecondSteps.Length; index++)
            {
                double step = SecondSteps[index] * fps;
                if (step >= minStep)
                {
                    return step;
                }
            }

            // Past an hour per label, keep going in whole hours rather than giving up.
            double hour = SecondSteps[SecondSteps.Length - 1] * fps;
            return Math.Ceiling(minStep / hour) * hour;
        }

        #endregion
    }
}
