using System;
using UnityEngine;
using UnityEngine.UIElements;

// Same reason as ColorInput (pulling in Tweeq.Core wholesale would make
// TweeqRect / TweeqVec2 ambiguous with the UnityEngine side) — only alias in the types we use
using HSVA = Tweeq.Core.Hsva;
using CoreRgba = Tweeq.Core.Rgba;
using TweeqColorLogic = Tweeq.Core.TweeqColorLogic;
using TweeqFormat = Tweeq.Core.TweeqFormat;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Per-frame rendering parameters passed to <see cref="ColorTweakOverlay"/>.
    /// All coordinates are panel-space (= local coordinates of the overlay layer).
    /// </summary>
    struct ColorTweakOverlayState
    {
        public TweeqTheme Theme;

        /// <summary>Drag start position. Reference point for the preview circle and label.</summary>
        public Vector2 Origin;

        public ColorTweakMode Mode;

        /// <summary>Current HSVA. The pad's slide amount and each slider's gradient are derived from this.</summary>
        public HSVA Hsva;

        /// <summary>Current color (including alpha).</summary>
        public Color Value;

        /// <summary>Rendering width, which also serves as the sensitivity baseline (= PopupWidth = 240).</summary>
        public float TweakWidth;

        /// <summary>SV gradient laid down in pad mode. If null, the pad surface is not shown.</summary>
        public Texture2D SvTexture;
    }

    /// <summary>
    /// An overlay that is only alive while a ColorInput channel is being scrubbed (m6-wave2-spec.md §A).
    /// Hangs directly off <see cref="TweeqOverlayLayer"/>, covers the entire panel, and draws
    /// the pad surface / hue ring / single-channel slider / preview circle / value label.
    ///
    /// Split into 3 layers so draw order is guaranteed by the hierarchy (UI Toolkit draws in
    /// "own generateVisualContent → children" order, so if the pad were placed as a child it
    /// would cover the preview circle drawn by the parent).
    /// </summary>
    sealed class ColorTweakOverlay : VisualElement
    {
        #region Constants

        // Preview circle radius 21.6 = InputHeight(24) × 0.9 (radius of a circle from scaling the Vue original's 24px box by 1.8)
        const float PREVIEW_RADIUS_FACTOR = 0.9f;
        const float PREVIEW_BORDER_WIDTH = 1f;

        // Hue ring: diameter 240 (= TweakWidth), line width 4px, 60 segments
        const int HUE_SEGMENTS = 60;
        const float HUE_RING_WIDTH = 4f;
        const int HUE_TICK_COUNT = 6;
        const float HUE_TICK_RADIUS = 1.8f;

        // Overlap width (degrees / px) to hide the seams between painted segments
        const float SEGMENT_OVERLAP_DEGREES = 0.5f;
        const float SEGMENT_OVERLAP_PIXELS = 1f;

        // Single-channel slider: 240×12 (val alone is a vertical 12×240)
        const float SLIDER_THICKNESS = 12f;
        const int SLIDER_SEGMENTS = 60;
        const float SLIDER_BORDER_WIDTH = 1f;

        // Current-position marker. A white core with a thin dark shade around it (same idea as a picker cursor)
        const float MARKER_WIDTH = 3f;
        const float MARKER_SHADE_WIDTH = 1f;
        const float MARKER_OVERHANG = 2f;

        const float LABEL_FONT_SIZE = 10f;
        const float LABEL_PADDING_X = 6f;
        const float LABEL_PADDING_Y = 4f;
        const float LABEL_RADIUS = 4f;
        const float LABEL_BORDER_WIDTH = 1f;

        // The label sits InputHeight×1.7 above origin, plus half its own height (equivalent to the Vue original's translate)
        const float LABEL_GAP_FACTOR = 1.7f;

        // Screen-edge clamp (matching the convention used by another port)
        const float LABEL_EDGE_MARGIN = 4f;

        // One checkerboard cell. Same 6px as the ColorInput side
        const float CHECKER_CELL = 6f;

        const double HUE_RANGE = 360.0;
        const double PERCENT_SCALE = 100.0;
        const double BYTE_SCALE = 255.0;

        // Granularity of the display key. Percent display is F1, so 0.1% = 1/1000 of the value
        const double PERCENT_KEY_SCALE = 1000.0;
        const double HUE_KEY_SCALE = 10.0;

        #endregion

        #region Fields

        // The Vue original fixes these at white / #ddd (does not follow the theme). Same values as ColorInput
        static readonly Color CheckerLight = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        static readonly Color CheckerDark = new Color32(0xDD, 0xDD, 0xDD, 0xFF);

        static readonly Color MarkerCore = new Color(1f, 1f, 1f, 1f);
        static readonly Color MarkerShade = new Color(0f, 0f, 0f, 0.2f);

        // Once the monospace font is resolved, reuse it across all instances.
        // OS fonts are generated dynamically, so keep a reference so it isn't garbage collected
        static FontDefinition SharedMonospaceDefinition;
        static Font SharedOsMonospaceFont;
        static bool MonospaceResolved;

        ColorTweakOverlayState _state;
        bool _hasState;

        // Only apply the font on the frame the theme actually changes (Sync runs every frame during a scrub)
        TweeqTheme _fontTheme;

        VisualElement _pad;
        VisualElement _paint;
        Label _label;

        Texture2D _padTexture;

        // Only rebuild the label when the display actually changes. The key is an integer quantized to the display resolution
        bool _hasLabelKey;
        ColorTweakMode _labelMode;
        long _labelKey0;
        long _labelKey1;

        #endregion

        #region Construction

        public ColorTweakOverlay()
        {
            this.name = "tweeq-color-tweak-overlay";
            this.pickingMode = PickingMode.Ignore;
            this.style.position = Position.Absolute;
            this.style.left = 0f;
            this.style.top = 0f;
            this.style.right = 0f;
            this.style.bottom = 0f;
            this.style.overflow = Overflow.Visible;

            BuildPad();
            BuildPaint();
            BuildLabel();
        }

        void BuildPad()
        {
            _pad = new VisualElement
            {
                name = "tweeq-color-tweak-pad",
                pickingMode = PickingMode.Ignore,
            };
            _pad.style.position = Position.Absolute;
            _pad.style.overflow = Overflow.Hidden;
            _pad.style.display = DisplayStyle.None;

            // background-size defaults to auto (= native resolution). Stretch 64×64 up to 240px
            _pad.style.backgroundSize =
                new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
            _pad.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            _pad.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left);
            _pad.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top);

            this.Add(_pad);
        }

        void BuildPaint()
        {
            _paint = new VisualElement
            {
                name = "tweeq-color-tweak-paint",
                pickingMode = PickingMode.Ignore,
            };
            _paint.style.position = Position.Absolute;
            _paint.style.left = 0f;
            _paint.style.top = 0f;
            _paint.style.right = 0f;
            _paint.style.bottom = 0f;
            _paint.style.overflow = Overflow.Visible;
            _paint.generateVisualContent += OnGeneratePaint;
            this.Add(_paint);
        }

        void BuildLabel()
        {
            _label = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
            _label.style.position = Position.Absolute;
            _label.style.fontSize = LABEL_FONT_SIZE;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _label.style.paddingLeft = LABEL_PADDING_X;
            _label.style.paddingRight = LABEL_PADDING_X;
            _label.style.paddingTop = LABEL_PADDING_Y;
            _label.style.paddingBottom = LABEL_PADDING_Y;
            _label.style.marginLeft = 0f;
            _label.style.marginRight = 0f;
            _label.style.marginTop = 0f;
            _label.style.marginBottom = 0f;

            SetBorderWidth(_label, LABEL_BORDER_WIDTH);
            SetBorderRadius(_label, LABEL_RADIUS);

            // Apply the default monospace font up front so digits don't shift before the theme arrives
            TweeqFonts.Apply(_label, GetMonospaceFont());

            // Centering needs the actually-resolved size, so reposition once it's settled (same trick as RotaryInput)
            _label.RegisterCallback<GeometryChangedEvent>(OnLabelGeometryChanged);
            this.Add(_label);
        }

        // With a variable-width font, the label would shift as digit counts change. The first choice is the bundled
        // Geist Mono (TweeqFonts.CodeFont); an OS-side monospace lookup is kept as a fallback so alignment still
        // holds even in configurations where the font has been stripped from the package
        static FontDefinition GetMonospaceFont()
        {
            if (MonospaceResolved)
            {
                return SharedMonospaceDefinition;
            }

            MonospaceResolved = true;

            FontDefinition bundled = TweeqFonts.CodeFont;
            if (!TweeqFonts.IsEmpty(bundled))
            {
                SharedMonospaceDefinition = bundled;
                return SharedMonospaceDefinition;
            }

            SharedOsMonospaceFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New" },
                Mathf.RoundToInt(LABEL_FONT_SIZE));

            if (SharedOsMonospaceFont != null)
            {
                SharedOsMonospaceFont.hideFlags = HideFlags.HideAndDontSave;
                SharedMonospaceDefinition = FontDefinition.FromFont(SharedOsMonospaceFont);
            }

            return SharedMonospaceDefinition;
        }

        #endregion

        #region Sync

        /// <summary>Updates the rendering parameters. Draws nothing on frames where Theme is null.</summary>
        public void Sync(in ColorTweakOverlayState state)
        {
            _state = state;
            _hasState = state.Theme != null && state.TweakWidth > 0f;

            if (!_hasState)
            {
                return;
            }

            SyncPad();
            SyncLabel();
            _paint.MarkDirtyRepaint();
        }

        void SyncPad()
        {
            bool visible = _state.Mode == ColorTweakMode.Pad && _state.SvTexture != null;
            _pad.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (!visible)
            {
                return;
            }

            if (!ReferenceEquals(_padTexture, _state.SvTexture))
            {
                _padTexture = _state.SvTexture;
                _pad.style.backgroundImage = new StyleBackground(_padTexture);
            }

            float size = _state.TweakWidth;
            _pad.style.width = size;
            _pad.style.height = size;
            _pad.style.borderTopLeftRadius = _state.Theme.InputRadius;
            _pad.style.borderTopRightRadius = _state.Theme.InputRadius;
            _pad.style.borderBottomLeftRadius = _state.Theme.InputRadius;
            _pad.style.borderBottomRightRadius = _state.Theme.InputRadius;

            // Slide so the current S / V always lines up with origin (matches the Vue original's padStyle)
            _pad.style.left = _state.Origin.x - (float)Clamp01(_state.Hsva.S) * size;
            _pad.style.top = _state.Origin.y - (float)(1.0 - Clamp01(_state.Hsva.V)) * size;
        }

        void SyncLabel()
        {
            TweeqTheme theme = _state.Theme;
            _label.style.backgroundColor = theme.SurfaceOpaque;
            _label.style.color = theme.Text;
            SetBorderColor(_label, theme.Border);

            if (!ReferenceEquals(_fontTheme, theme))
            {
                _fontTheme = theme;

                // Even for a theme whose FontCode is empty (= "don't override" setting), we still want to
                // guarantee a monospace font, so fall back to the default monospace only in that case
                FontDefinition font = TweeqFonts.IsEmpty(theme.FontCode)
                    ? GetMonospaceFont()
                    : theme.FontCode;

                TweeqFonts.Apply(_label, font);
            }

            SyncLabelText();
            UpdateLabelPosition();
        }

        // Sync runs even on frames where the pointer doesn't move, so only rebuild the string when the display changes
        void SyncLabelText()
        {
            ComputeLabelKey(out long key0, out long key1);

            if (_hasLabelKey && _labelMode == _state.Mode && _labelKey0 == key0 && _labelKey1 == key1)
            {
                return;
            }

            _label.text = BuildLabelText();
            _hasLabelKey = true;
            _labelMode = _state.Mode;
            _labelKey0 = key0;
            _labelKey1 = key1;
        }

        void ComputeLabelKey(out long key0, out long key1)
        {
            key0 = 0L;
            key1 = 0L;

            switch (_state.Mode)
            {
                case ColorTweakMode.Pad:
                    key0 = PercentKey(_state.Hsva.S);
                    key1 = PercentKey(_state.Hsva.V);
                    break;

                case ColorTweakMode.Hue:
                    key0 = (long)Math.Round(_state.Hsva.H * HUE_KEY_SCALE, MidpointRounding.AwayFromZero);
                    break;

                case ColorTweakMode.Saturation:
                    key0 = PercentKey(_state.Hsva.S);
                    break;

                case ColorTweakMode.Value:
                    key0 = PercentKey(_state.Hsva.V);
                    break;

                case ColorTweakMode.Alpha:
                    key0 = PercentKey(_state.Hsva.A);
                    break;

                default:
                    key0 = (long)Math.Round(ChannelValue() * BYTE_SCALE, MidpointRounding.AwayFromZero);
                    break;
            }
        }

        static long PercentKey(double normalized)
        {
            return (long)Math.Round(Clamp01(normalized) * PERCENT_KEY_SCALE, MidpointRounding.AwayFromZero);
        }

        string BuildLabelText()
        {
            switch (_state.Mode)
            {
                case ColorTweakMode.Pad:
                    return "Sat " + Percent(_state.Hsva.S) + "%  Val " + Percent(_state.Hsva.V) + "%";

                case ColorTweakMode.Hue:
                    return "Hue " + TweeqFormat.Format(_state.Hsva.H, 1, true) + "°";

                case ColorTweakMode.Saturation:
                    return "Sat " + Percent(_state.Hsva.S) + "%";

                case ColorTweakMode.Value:
                    return "Val " + Percent(_state.Hsva.V) + "%";

                case ColorTweakMode.Alpha:
                    return "α " + Percent(_state.Hsva.A) + "%";

                case ColorTweakMode.Red:
                    return "R " + Byte255(ChannelValue());

                case ColorTweakMode.Green:
                    return "G " + Byte255(ChannelValue());

                default:
                    return "B " + Byte255(ChannelValue());
            }
        }

        static string Percent(double normalized)
        {
            return TweeqFormat.Format(Clamp01(normalized) * PERCENT_SCALE, 1, true);
        }

        // r/g/b are internally 0-1, but displayed as 0-255 (per the mapping table in spec §A)
        static string Byte255(double normalized)
        {
            return TweeqFormat.Format(Clamp01(normalized) * BYTE_SCALE, 0, true);
        }

        void OnLabelGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateLabelPosition();
        }

        void UpdateLabelPosition()
        {
            if (!_hasState)
            {
                return;
            }

            float width = _label.resolvedStyle.width;
            float height = _label.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height))
            {
                return;
            }

            float left = _state.Origin.x - width * 0.5f;
            float top = _state.Origin.y
                - (_state.Theme.InputHeight * LABEL_GAP_FACTOR + height * 0.5f)
                - height * 0.5f;

            Rect bounds = this.contentRect;
            if (!float.IsNaN(bounds.width) && bounds.width > 0f && bounds.height > 0f)
            {
                // Edge clamping only applies to the side that overflows. If the label is bigger than the bounds, prefer the top-left
                left = Mathf.Min(left, bounds.xMax - LABEL_EDGE_MARGIN - width);
                left = Mathf.Max(left, bounds.xMin + LABEL_EDGE_MARGIN);
                top = Mathf.Min(top, bounds.yMax - LABEL_EDGE_MARGIN - height);
                top = Mathf.Max(top, bounds.yMin + LABEL_EDGE_MARGIN);
            }

            _label.style.left = left;
            _label.style.top = top;
        }

        #endregion

        #region Painting

        void OnGeneratePaint(MeshGenerationContext context)
        {
            if (!_hasState || context == null)
            {
                return;
            }

            TweeqTheme theme = _state.Theme;
            if (theme == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            switch (_state.Mode)
            {
                case ColorTweakMode.Pad:
                    // The pad surface is handled by the texture-background child element
                    break;

                case ColorTweakMode.Hue:
                    PaintHueRing(painter, theme);
                    break;

                default:
                    PaintSlider(painter, theme);
                    break;
            }

            PaintPreview(painter, theme);
        }

        // A hue ring centered on origin with diameter TweakWidth. Rotates the whole ring backward by
        // the hue amount so the current hue always faces straight up (the Vue original's rotate: h * -360deg)
        void PaintHueRing(Painter2D painter, TweeqTheme theme)
        {
            float radius = _state.TweakWidth * 0.5f - HUE_RING_WIDTH * 0.5f;
            if (radius <= 0f)
            {
                return;
            }

            Vector2 center = _state.Origin;
            double step = HUE_RANGE / HUE_SEGMENTS;

            painter.lineWidth = HUE_RING_WIDTH;
            painter.lineCap = LineCap.Butt;

            for (int index = 0; index < HUE_SEGMENTS; index++)
            {
                double hue = index * step;
                painter.strokeColor = ToColor(new HSVA(hue + step * 0.5, 1.0, 1.0, 1.0));

                float start = (float)RingAngle(hue);
                painter.BeginPath();
                painter.Arc(
                    center,
                    radius,
                    new Angle(start, AngleUnit.Degree),
                    new Angle(start + (float)step + SEGMENT_OVERLAP_DEGREES, AngleUnit.Degree),
                    ArcDirection.Clockwise);
                painter.Stroke();
            }

            // Tick marks every 60°. The Vue original punches holes with a mask, so here we paint with the background color to fake the cutout
            painter.fillColor = theme.Background;

            for (int index = 0; index < HUE_TICK_COUNT; index++)
            {
                double hue = index * (HUE_RANGE / HUE_TICK_COUNT);
                Vector2 direction = AngleDirection(RingAngle(hue));
                FillCircle(painter, center + direction * radius, HUE_TICK_RADIUS);
            }
        }

        // The orientation that fixes the current hue straight up (-90°)
        double RingAngle(double hue)
        {
            return hue - _state.Hsva.H - 90.0;
        }

        void PaintSlider(Painter2D painter, TweeqTheme theme)
        {
            // Only val is vertical (per spec §A's mapping: val alone uses dy)
            bool vertical = _state.Mode == ColorTweakMode.Value;
            float length = _state.TweakWidth;
            Vector2 origin = _state.Origin;

            Rect rect = vertical
                ? new Rect(
                    origin.x - SLIDER_THICKNESS * 0.5f,
                    origin.y - length * 0.5f,
                    SLIDER_THICKNESS,
                    length)
                : new Rect(
                    origin.x - length * 0.5f,
                    origin.y - SLIDER_THICKNESS * 0.5f,
                    length,
                    SLIDER_THICKNESS);

            if (_state.Mode == ColorTweakMode.Alpha)
            {
                PaintCheckerboard(painter, rect);
            }

            // Uses segmented painting rather than vertex-color interpolation (context.Allocate).
            // Within a single element, Painter2D and Allocate draw order isn't guaranteed,
            // which would let the border or marker sink beneath the gradient
            for (int index = 0; index < SLIDER_SEGMENTS; index++)
            {
                double from = index / (double)SLIDER_SEGMENTS;
                double to = (index + 1) / (double)SLIDER_SEGMENTS;
                painter.fillColor = ChannelColor((from + to) * 0.5);

                if (vertical)
                {
                    // For vertical, the bottom edge is 0
                    float top = rect.yMax - (float)to * length;
                    FillRect(
                        painter,
                        rect.xMin,
                        top,
                        SLIDER_THICKNESS,
                        (float)(to - from) * length + SEGMENT_OVERLAP_PIXELS);
                }
                else
                {
                    FillRect(
                        painter,
                        rect.xMin + (float)from * length,
                        rect.yMin,
                        (float)(to - from) * length + SEGMENT_OVERLAP_PIXELS,
                        SLIDER_THICKNESS);
                }
            }

            StrokeRect(painter, rect, theme.Border, SLIDER_BORDER_WIDTH);
            PaintSliderMarker(painter, rect, vertical, length);
        }

        void PaintSliderMarker(Painter2D painter, Rect rect, bool vertical, float length)
        {
            float value = (float)Clamp01(ChannelValue());

            Vector2 from;
            Vector2 to;

            if (vertical)
            {
                float y = rect.yMax - value * length;
                from = new Vector2(rect.xMin - MARKER_OVERHANG, y);
                to = new Vector2(rect.xMax + MARKER_OVERHANG, y);
            }
            else
            {
                float x = rect.xMin + value * length;
                from = new Vector2(x, rect.yMin - MARKER_OVERHANG);
                to = new Vector2(x, rect.yMax + MARKER_OVERHANG);
            }

            painter.lineCap = LineCap.Butt;

            painter.strokeColor = MarkerCore;
            painter.lineWidth = MARKER_WIDTH;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();

            painter.strokeColor = MarkerShade;
            painter.lineWidth = MARKER_SHADE_WIDTH;
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        void PaintPreview(Painter2D painter, TweeqTheme theme)
        {
            float radius = theme.InputHeight * PREVIEW_RADIUS_FACTOR;
            if (radius <= 0f)
            {
                return;
            }

            Color fill = _state.Value;
            if (_state.Mode != ColorTweakMode.Alpha)
            {
                // If alpha were carried through, the preview would disappear at full transparency and its position
                // would become unreadable (the Vue original also forces opacity outside of alpha mode)
                fill.a = 1f;
            }

            // Alpha mode draws while staying semi-transparent, so the guide behind it would show through and
            // make the color unreadable. Lay down a Background layer first, then draw the color on top
            Vector2 center = _state.Origin;

            painter.fillColor = theme.Background;
            FillCircle(painter, center, radius);

            painter.fillColor = fill;
            FillCircle(painter, center, radius);

            painter.strokeColor = theme.Border;
            painter.lineWidth = PREVIEW_BORDER_WIDTH;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.Arc(
                center,
                Mathf.Max(0.5f, radius - PREVIEW_BORDER_WIDTH * 0.5f),
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();
        }

        #endregion

        #region Channel

        // The color corresponding to slider position t (0-1). For sat / val / r / g / b / alpha,
        // swapping out a single channel is all that's needed
        Color ChannelColor(double t)
        {
            double amount = Clamp01(t);

            switch (_state.Mode)
            {
                case ColorTweakMode.Saturation:
                    return ToColor(new HSVA(_state.Hsva.H, amount, _state.Hsva.V, 1.0));

                case ColorTweakMode.Value:
                    return ToColor(new HSVA(_state.Hsva.H, _state.Hsva.S, amount, 1.0));

                case ColorTweakMode.Alpha:
                    return new Color(_state.Value.r, _state.Value.g, _state.Value.b, (float)amount);

                case ColorTweakMode.Red:
                    return new Color((float)amount, _state.Value.g, _state.Value.b, 1f);

                case ColorTweakMode.Green:
                    return new Color(_state.Value.r, (float)amount, _state.Value.b, 1f);

                default:
                    return new Color(_state.Value.r, _state.Value.g, (float)amount, 1f);
            }
        }

        double ChannelValue()
        {
            switch (_state.Mode)
            {
                case ColorTweakMode.Saturation:
                    return _state.Hsva.S;

                case ColorTweakMode.Value:
                    return _state.Hsva.V;

                case ColorTweakMode.Alpha:
                    return _state.Hsva.A;

                case ColorTweakMode.Red:
                    return _state.Value.r;

                case ColorTweakMode.Green:
                    return _state.Value.g;

                default:
                    return _state.Value.b;
            }
        }

        static Color ToColor(HSVA hsva)
        {
            CoreRgba rgba = TweeqColorLogic.HsvaToRgba(hsva);
            return new Color((float)rgba.R, (float)rgba.G, (float)rgba.B, (float)rgba.A);
        }

        #endregion

        #region Helpers

        static void PaintCheckerboard(Painter2D painter, Rect rect)
        {
            painter.fillColor = CheckerLight;
            FillRect(painter, rect.xMin, rect.yMin, rect.width, rect.height);

            painter.fillColor = CheckerDark;

            int columns = Mathf.CeilToInt(rect.width / CHECKER_CELL);
            int rows = Mathf.CeilToInt(rect.height / CHECKER_CELL);

            for (int row = 0; row < rows; row++)
            {
                float y = row * CHECKER_CELL;
                float cellHeight = Mathf.Min(CHECKER_CELL, rect.height - y);

                for (int column = (row & 1) == 0 ? 1 : 0; column < columns; column += 2)
                {
                    float x = column * CHECKER_CELL;
                    FillRect(
                        painter,
                        rect.xMin + x,
                        rect.yMin + y,
                        Mathf.Min(CHECKER_CELL, rect.width - x),
                        cellHeight);
                }
            }
        }

        static void FillRect(Painter2D painter, float x, float y, float width, float height)
        {
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + width, y));
            painter.LineTo(new Vector2(x + width, y + height));
            painter.LineTo(new Vector2(x, y + height));
            painter.ClosePath();
            painter.Fill();
        }

        static void StrokeRect(Painter2D painter, Rect rect, Color color, float width)
        {
            float inset = width * 0.5f;

            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin + inset, rect.yMin + inset));
            painter.LineTo(new Vector2(rect.xMax - inset, rect.yMin + inset));
            painter.LineTo(new Vector2(rect.xMax - inset, rect.yMax - inset));
            painter.LineTo(new Vector2(rect.xMin + inset, rect.yMax - inset));
            painter.ClosePath();
            painter.Stroke();
        }

        static void FillCircle(Painter2D painter, Vector2 center, float radius)
        {
            if (radius <= 0f)
            {
                return;
            }

            painter.BeginPath();
            painter.Arc(
                center,
                radius,
                new Angle(0f, AngleUnit.Degree),
                new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Fill();
        }

        static Vector2 AngleDirection(double degrees)
        {
            float radians = Mathf.Deg2Rad * (float)degrees;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        static double Clamp01(double value)
        {
            if (double.IsNaN(value))
            {
                return 0.0;
            }

            return value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }

        static void SetBorderWidth(VisualElement element, float width)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
        }

        static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        static void SetBorderRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        #endregion
    }
}
