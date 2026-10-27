using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>The edge from which the balloon's arrow grows. <see cref="TweeqArrowSide.None"/> is a rounded rectangle with no arrow.</summary>
    public enum TweeqArrowSide
    {
        None,
        Top,
        Bottom,
        Left,
        Right,
    }

    /// <summary>
    /// A speech-balloon-shaped surface. The rounded rectangle and the arrow are drawn as "a single outline"
    /// with Painter2D, so no border seam appears at the arrow's base (equivalent to what the Vue original
    /// does with clip-path + SVG stroke).
    /// Content is added normally via <see cref="VisualElement.contentContainer"/>.
    /// </summary>
    [UxmlElement]
    public sealed partial class TweeqBalloon : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>The arrow's base width (px). Fixed as part of the visual language.</summary>
        public const float ARROW_WIDTH = 14f;

        /// <summary>The arrow's protrusion amount (px).</summary>
        public const float ARROW_HEIGHT = 7f;

        /// <summary>The gap left between the arrow's tip and the anchor (px).</summary>
        public const float ARROW_GAP = 2f;

        const float BORDER_WIDTH = 1f;

        /// <summary>Default value for the shadow blur radius. Equivalent to Vue's drop-shadow(0 2px 12px).</summary>
        public const float DEFAULT_SHADOW_BLUR = 12f;

        /// <summary>Default value for the shadow's downward offset.</summary>
        public const float DEFAULT_SHADOW_OFFSET_Y = 2f;

        // There's no box-shadow, so we approximate the blur by stacking fills of the outline grown outward.
        // 8 bands keep each step under ~3px at a 20px blur; the vertices are only rebuilt on repaint
        const int SHADOW_LAYERS = 8;

        // The scale used when appearing. The origin is the arrow's tip (so it appears to grow from the pointed-at location)
        const float POP_IN_SCALE = 0.96f;

        #endregion

        #region Fields

        // The transition definition is immutable, so create just one per type and share it across all instances
        // (style.transition* requires a List every time, so allocating a new one on every open would produce garbage)
        static readonly StyleList<StylePropertyName> ScaleProperty =
            new StyleList<StylePropertyName>(new List<StylePropertyName> { new StylePropertyName("scale") });

        static readonly StyleList<EasingFunction> EaseOut =
            new StyleList<EasingFunction>(new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) });

        static readonly StyleList<TimeValue> InstantDuration =
            new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) });

        static readonly float ArrowSideLength =
            Mathf.Sqrt(ARROW_WIDTH * ARROW_WIDTH * 0.25f + ARROW_HEIGHT * ARROW_HEIGHT);

        TweeqTheme _theme = TweeqTheme.Dark();
        TweeqArrowSide _arrowSide = TweeqArrowSide.None;
        float _arrowOffset;

        // NaN = follow the theme default. Some cases (like the tooltip's pill shape) want to override this partially
        float _radius = float.NaN;
        float _paddingVertical = float.NaN;
        float _paddingHorizontal = float.NaN;
        Color? _fillColorOverride;

        float _shadowBlur = DEFAULT_SHADOW_BLUR;
        float _shadowOffsetY = DEFAULT_SHADOW_OFFSET_Y;

        VisualElement _content;

        // The duration derived from the theme. Only recreated when the theme is swapped
        StyleList<TimeValue> _popInDuration;

        // Reuse a single scheduled item for the appear animation's "restore scale on the next frame" step (don't new it every time)
        IVisualElementScheduledItem _popInItem;

        #endregion

        #region Public API

        /// <summary>The edge from which the arrow grows.</summary>
        [UxmlAttribute("arrow-side")]
        public TweeqArrowSide ArrowSide
        {
            get => _arrowSide;
            set
            {
                if (_arrowSide == value)
                {
                    return;
                }

                _arrowSide = value;
                ApplyArrowPadding();
                UpdateTransformOrigin();
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// The arrow's center position. Measured in px from this element's top-left, along the edge the
        /// arrow grows from. Clamped at draw time so it doesn't bite into the rounded corners.
        /// </summary>
        // Radius / Padding* hold a "NaN = theme default" sentinel, so they aren't exposed to UXML
        // (if UI Builder writes 0 as the default value, theme-following silently breaks)
        [UxmlAttribute("arrow-offset")]
        public float ArrowOffset
        {
            get => _arrowOffset;
            set
            {
                if (Mathf.Approximately(_arrowOffset, value))
                {
                    return;
                }

                _arrowOffset = value;
                UpdateTransformOrigin();
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>The corner radius. NaN follows the theme's RadiusPopup.</summary>
        public float Radius
        {
            get => _radius;
            set
            {
                _radius = value;
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>The content's vertical padding. NaN follows the theme's PopupPadding.</summary>
        public float PaddingVertical
        {
            get => _paddingVertical;
            set
            {
                _paddingVertical = value;
                ApplyContentPadding();
            }
        }

        /// <summary>The content's horizontal padding. NaN follows the theme's PopupPadding.</summary>
        public float PaddingHorizontal
        {
            get => _paddingHorizontal;
            set
            {
                _paddingHorizontal = value;
                ApplyContentPadding();
            }
        }

        /// <summary>
        /// Optional Painter2D fill override. Null keeps the theme's SurfaceOpaque behavior.
        /// Hosts that need an application-specific opaque modal surface can set this without
        /// replacing the balloon or changing the shared theme.
        /// </summary>
        public Color? FillColorOverride
        {
            get => _fillColorOverride;
            set
            {
                if (_fillColorOverride == value)
                {
                    return;
                }

                _fillColorOverride = value;
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>The shadow blur radius (px).</summary>
        public float ShadowBlur
        {
            get => _shadowBlur;
            set
            {
                _shadowBlur = value;
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>The shadow's downward offset (px).</summary>
        public float ShadowOffsetY
        {
            get => _shadowOffsetY;
            set
            {
                _shadowOffsetY = value;
                this.MarkDirtyRepaint();
            }
        }

        /// <summary>The color theme. Falls back to Dark() if null is passed.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyTheme();
            }
        }

        /// <summary>Content goes into the content layer (the outer element holds the padding for the arrow).</summary>
        public override VisualElement contentContainer => _content;

        /// <summary>
        /// Plays the appear animation (scale 0.96→1 with the arrow's tip as the origin) from the start.
        /// Call this right after adding to the panel. The scheduler doesn't run for detached elements.
        /// </summary>
        public void PlayIn()
        {
            UpdateTransformOrigin();

            // From the second reuse onward, scale is still left at 1, so setting 0.96 directly
            // would play a "shrink" animation first. Apply duration 0 only for the shrinking step
            // (this plays the role that Vue's @starting-style handled)
            this.style.transitionDuration = InstantDuration;
            this.style.scale = new StyleScale(new Scale(new Vector3(POP_IN_SCALE, POP_IN_SCALE, 1f)));

            if (this.panel == null)
            {
                // The scheduler doesn't run, so restore immediately to avoid getting stuck shrunk
                FinishPopIn();
                return;
            }

            if (_popInItem == null)
            {
                _popInItem = this.schedule.Execute(FinishPopIn);
            }

            _popInItem.ExecuteLater(0L);
        }

        #endregion

        #region Construction

        public TweeqBalloon()
        {
            this.name = "tweeq-balloon";

            // Prevent the shadow, and the 1px stroke spanning the edge when there's no arrow, from being clipped
            this.style.overflow = Overflow.Visible;
            this.style.alignSelf = Align.FlexStart;

            _content = new VisualElement { name = "tweeq-balloon-content" };
            this.hierarchy.Add(_content);

            this.generateVisualContent += OnGenerateVisualContent;
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            ApplyArrowPadding();
            ApplyTheme();
            UpdateTransformOrigin();
        }

        #endregion

        #region Presentation

        void ApplyTheme()
        {
            ApplyContentPadding();
            ApplyPopInTransition();
            this.MarkDirtyRepaint();
        }

        // Touching the transition every frame would pile up StyleList allocations, so only set it when the theme changes
        void ApplyPopInTransition()
        {
            float duration = _theme != null ? _theme.ActiveTransitionDuration : 0.064f;

            _popInDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(duration, TimeUnit.Second) });

            this.style.transitionProperty = ScaleProperty;
            this.style.transitionTimingFunction = EaseOut;
            this.style.transitionDuration = _popInDuration;
        }

        void ApplyContentPadding()
        {
            if (_content == null)
            {
                return;
            }

            float fallback = _theme != null ? _theme.PopupPadding : 9f;
            float vertical = Resolve(_paddingVertical, fallback);
            float horizontal = Resolve(_paddingHorizontal, fallback);

            _content.style.paddingTop = vertical;
            _content.style.paddingBottom = vertical;
            _content.style.paddingLeft = horizontal;
            _content.style.paddingRight = horizontal;
        }

        // The arrow reserves its space as outer padding. This makes the outline path's coordinates
        // map 1:1 to the content's layout, so the drawing side doesn't need to hold a duplicate offset
        void ApplyArrowPadding()
        {
            float depth = ARROW_HEIGHT + ARROW_GAP;
            this.style.paddingTop = _arrowSide == TweeqArrowSide.Top ? depth : 0f;
            this.style.paddingBottom = _arrowSide == TweeqArrowSide.Bottom ? depth : 0f;
            this.style.paddingLeft = _arrowSide == TweeqArrowSide.Left ? depth : 0f;
            this.style.paddingRight = _arrowSide == TweeqArrowSide.Right ? depth : 0f;
        }

        void FinishPopIn()
        {
            this.style.transitionDuration = _popInDuration;
            this.style.scale = new StyleScale(new Scale(Vector3.one));
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // The bottom/right origin is "size - GAP", so it's repositioned once the actual size is known
            UpdateTransformOrigin();
        }

        // Place the appear animation's scale origin at the arrow's tip (center if there's no arrow)
        void UpdateTransformOrigin()
        {
            float width = this.layout.width;
            float height = this.layout.height;

            switch (_arrowSide)
            {
                case TweeqArrowSide.Top:
                    SetTransformOrigin(new Length(_arrowOffset), new Length(ARROW_GAP));
                    break;
                case TweeqArrowSide.Bottom:
                    SetTransformOrigin(
                        new Length(_arrowOffset),
                        IsUsableSize(height)
                            ? new Length(height - ARROW_GAP)
                            : new Length(100f, LengthUnit.Percent));
                    break;
                case TweeqArrowSide.Left:
                    SetTransformOrigin(new Length(ARROW_GAP), new Length(_arrowOffset));
                    break;
                case TweeqArrowSide.Right:
                    SetTransformOrigin(
                        IsUsableSize(width)
                            ? new Length(width - ARROW_GAP)
                            : new Length(100f, LengthUnit.Percent),
                        new Length(_arrowOffset));
                    break;
                default:
                    SetTransformOrigin(
                        new Length(50f, LengthUnit.Percent),
                        new Length(50f, LengthUnit.Percent));
                    break;
            }
        }

        void SetTransformOrigin(Length x, Length y)
        {
            this.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(x, y, 0f));
        }

        #endregion

        #region Painting

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

            float layerWidth = this.layout.width;
            float layerHeight = this.layout.height;
            if (!IsUsableSize(layerWidth) || !IsUsableSize(layerHeight))
            {
                return;
            }

            // The main body's (rounded rectangle) rect. Subtract the arrow's padding from the outside
            float originX = _arrowSide == TweeqArrowSide.Left ? ARROW_HEIGHT + ARROW_GAP : 0f;
            float originY = _arrowSide == TweeqArrowSide.Top ? ARROW_HEIGHT + ARROW_GAP : 0f;
            float width = layerWidth - originX
                - (_arrowSide == TweeqArrowSide.Right ? ARROW_HEIGHT + ARROW_GAP : 0f);
            float height = layerHeight - originY
                - (_arrowSide == TweeqArrowSide.Bottom ? ARROW_HEIGHT + ARROW_GAP : 0f);
            if (!IsUsableSize(width) || !IsUsableSize(height))
            {
                return;
            }

            float radius = Mathf.Max(
                0f,
                Mathf.Min(Resolve(_radius, _theme.RadiusPopup), width * 0.5f, height * 0.5f));

            PaintShadow(painter, originX, originY, width, height, radius);

            BuildOutline(painter, originX, originY, width, height, radius, 0f, 0f);
            // The semi-transparent Surface color assumes a blur behind it (Vue). Without blur the background would show through, so we composite it opaque
            painter.fillColor = _fillColorOverride ?? _theme.SurfaceOpaque;
            painter.Fill();

            painter.strokeColor = _theme.Border;
            painter.lineWidth = BORDER_WIDTH;
            painter.lineJoin = LineJoin.Miter;
            painter.Stroke();
        }

        // A substitute for drop-shadow. Fills of the outline grown by increasing spread are stacked from the
        // outside in. Thick strokes were used before, but a stroke wider than the corner radius folds over itself
        // on the arc and doubles its alpha into radial streaks; a grown outline is still convex at the corners and can't.
        // Each layer's alpha is solved so the stacked result hits the falloff curve at the middle of its band
        void PaintShadow(Painter2D painter, float originX, float originY, float width, float height, float radius)
        {
            Color shadow = _theme.Shadow;
            if (shadow.a <= 0f || _shadowBlur <= 0f)
            {
                return;
            }

            float outerAlpha = 0f;
            for (int index = SHADOW_LAYERS; index >= 1; index--)
            {
                float spread = _shadowBlur * index / SHADOW_LAYERS;
                float bandAlpha = shadow.a * ShadowFalloff((index - 0.5f) / SHADOW_LAYERS);

                Color layerColor = shadow;
                layerColor.a = 1f - (1f - bandAlpha) / (1f - outerAlpha);
                outerAlpha = bandAlpha;

                painter.fillColor = layerColor;
                BuildOutline(painter, originX, originY, width, height, radius, _shadowOffsetY, spread);
                painter.Fill();
            }
        }

        // CSS drop-shadow is about half strength at the shape's edge and fades out with a Gaussian tail.
        // (1 - t)^1.5 / 2 stays close to that curve (sigma = blur / 2) while reaching exactly zero
        // at the blur distance; a square falls off visibly faster than the Gaussian in the outer half
        static float ShadowFalloff(float t)
        {
            float rest = 1f - t;
            return 0.5f * rest * Mathf.Sqrt(rest);
        }

        // Traces the same path (clockwise) as the Vue original's Balloon.vue SVG path, using Painter2D.
        // Rounded corners use ArcTo (equivalent to canvas's arcTo), and the arrow is expressed as a polyline inserted partway along the edge.
        // spread grows the outline outward for the shadow: the rect and corner radius grow together, so the arc
        // tangent points stay where the body's are
        void BuildOutline(
            Painter2D painter,
            float originX,
            float originY,
            float width,
            float height,
            float radius,
            float offsetY,
            float spread)
        {
            // The arrow stays anchored to the body's edge, not the grown one
            float bodyLeft = originX;
            float bodyTop = originY + offsetY;
            float bodyRight = originX + width;
            float bodyBottom = originY + height + offsetY;

            float left = bodyLeft - spread;
            float top = bodyTop - spread;
            float right = bodyRight + spread;
            float bottom = bodyBottom + spread;
            float corner = radius + spread;
            float half = ARROW_WIDTH * 0.5f;

            // Offsetting the arrow's sides along their normals slides the concave base corners outward along the edge by this much
            float baseGrow = spread * (ArrowSideLength - half) / ARROW_HEIGHT;

            painter.BeginPath();
            painter.MoveTo(new Vector2(left + corner, top));

            if (_arrowSide == TweeqArrowSide.Top)
            {
                float center = ClampAlongEdge(bodyLeft + _arrowOffset, bodyLeft, bodyRight, radius, half);
                Vector2 span = GrowArrowBase(center, half, baseGrow, bodyLeft, bodyRight, radius);
                TraceArrow(
                    painter, new Vector2(span.x, top), new Vector2(center, top - ARROW_HEIGHT), new Vector2(span.y, top),
                    new Vector2(1f, 0f), new Vector2(0f, -1f), spread);
            }

            painter.LineTo(new Vector2(right - corner, top));
            painter.ArcTo(new Vector2(right, top), new Vector2(right, bottom), corner);

            if (_arrowSide == TweeqArrowSide.Right)
            {
                float center = ClampAlongEdge(bodyTop + _arrowOffset, bodyTop, bodyBottom, radius, half);
                Vector2 span = GrowArrowBase(center, half, baseGrow, bodyTop, bodyBottom, radius);
                TraceArrow(
                    painter, new Vector2(right, span.x), new Vector2(right + ARROW_HEIGHT, center), new Vector2(right, span.y),
                    new Vector2(0f, 1f), new Vector2(1f, 0f), spread);
            }

            painter.LineTo(new Vector2(right, bottom - corner));
            painter.ArcTo(new Vector2(right, bottom), new Vector2(left, bottom), corner);

            if (_arrowSide == TweeqArrowSide.Bottom)
            {
                float center = ClampAlongEdge(bodyLeft + _arrowOffset, bodyLeft, bodyRight, radius, half);
                Vector2 span = GrowArrowBase(center, half, baseGrow, bodyLeft, bodyRight, radius);
                TraceArrow(
                    painter, new Vector2(span.y, bottom), new Vector2(center, bottom + ARROW_HEIGHT), new Vector2(span.x, bottom),
                    new Vector2(-1f, 0f), new Vector2(0f, 1f), spread);
            }

            painter.LineTo(new Vector2(left + corner, bottom));
            painter.ArcTo(new Vector2(left, bottom), new Vector2(left, top), corner);

            if (_arrowSide == TweeqArrowSide.Left)
            {
                float center = ClampAlongEdge(bodyTop + _arrowOffset, bodyTop, bodyBottom, radius, half);
                Vector2 span = GrowArrowBase(center, half, baseGrow, bodyTop, bodyBottom, radius);
                TraceArrow(
                    painter, new Vector2(left, span.y), new Vector2(left - ARROW_HEIGHT, center), new Vector2(left, span.x),
                    new Vector2(0f, -1f), new Vector2(-1f, 0f), spread);
            }

            painter.LineTo(new Vector2(left, top + corner));
            painter.ArcTo(new Vector2(left, top), new Vector2(right, top), corner);
            painter.ClosePath();
        }

        #endregion

        #region Helpers

        // If the arrow's base bites into the rounded corner the outline breaks, so push it into the straight segment
        static float ClampAlongEdge(float value, float min, float max, float radius, float half)
        {
            float low = min + radius + half;
            float high = max - radius - half;
            if (high < low)
            {
                // If the edge is too short to have a straight segment to place the arrow on, center it instead (choosing distortion over breakage)
                return (min + max) * 0.5f;
            }

            return Mathf.Clamp(value, low, high);
        }

        // Walks the arrow from one base end to the other. along is the walking direction, outward points away from the body.
        // A grown arrow's exact offset rounds its tip; two chords through the apex stand in for that arc,
        // while the shoulder points keep the side segments parallel to the body's sides
        static void TraceArrow(
            Painter2D painter, Vector2 start, Vector2 apex, Vector2 end, Vector2 along, Vector2 outward, float spread)
        {
            painter.LineTo(start);

            if (spread > 0f)
            {
                // Skipped at zero spread: repeated points would give the body's miter-joined border a zero-length segment
                Vector2 sideways = along * (spread * ARROW_HEIGHT / ArrowSideLength);
                Vector2 drop = outward * (spread * (1f - ARROW_WIDTH * 0.5f / ArrowSideLength));
                painter.LineTo(apex - sideways - drop);
                painter.LineTo(apex);
                painter.LineTo(apex + sideways - drop);
            }
            else
            {
                painter.LineTo(apex);
            }

            painter.LineTo(end);
        }

        // Widening the base past the arc's tangent point would fold the path back over the corner, so it stops there.
        // It never gets narrower than the body's own arrow. Returns (low end, high end) along the edge
        static Vector2 GrowArrowBase(float center, float half, float grow, float min, float max, float radius)
        {
            return new Vector2(
                Mathf.Min(center - half, Mathf.Max(center - half - grow, min + radius)),
                Mathf.Max(center + half, Mathf.Min(center + half + grow, max - radius)));
        }

        static float Resolve(float value, float fallback)
        {
            return float.IsNaN(value) ? fallback : value;
        }

        static bool IsUsableSize(float value)
        {
            return !float.IsNaN(value) && value > 0f;
        }

        #endregion
    }
}
