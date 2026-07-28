using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A boolean checkbox (spec §1). Click to toggle; a left/right swipe can directly specify true/false.
    /// Participates in corner-radius fusion (<see cref="ITweeqInputBox"/>).
    /// </summary>
    [UxmlElement]
    public partial class CheckboxInput
        : VisualElement, INotifyValueChanged<bool>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        const float ICON_SIZE = 18f;
        const float MARK_STROKE_WIDTH = 2f;

        // The off-state mark "sets" TextSubtle's alpha to 0.3 (not multiplies it — matches the Vue original's set-alpha)
        const float MARK_OFF_ALPHA = 0.3f;

        // active-family transition of 64ms (per the spec's transition table)
        const float ACTIVE_TRANSITION_DURATION = 0.064f;

        const float FOCUS_RING_WIDTH = 1f;
        const float DISABLED_BORDER_WIDTH = 1f;

        // Gap to the label is 1em (rem12 = 12px)
        const float LABEL_GAP = 12f;

        // Checkmark (normalized coordinates within an 18px icon). mdi:check-bold simplified to a 2-segment polyline.
        // The y values are chosen so the polyline's top and bottom ends are symmetric about the box's center
        static readonly Vector2 MARK_START = new Vector2(0.18f, 0.50f);
        static readonly Vector2 MARK_ELBOW = new Vector2(0.42f, 0.74f);
        static readonly Vector2 MARK_END = new Vector2(0.82f, 0.26f);

        #endregion

        #region Fields

        bool _value;
        string _label = string.Empty;
        bool _disabled;
        TweeqTheme _theme = TweeqTheme.Dark();

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        // Result of deciding which corners to flatten/keep. Shared between the style and the focus-ring drawing
        bool _radiusTopLeft = true;
        bool _radiusTopRight = true;
        bool _radiusBottomLeft = true;
        bool _radiusBottomRight = true;

        VisualElement _box;
        VisualElement _ring;
        Label _labelElement;
        BoolTweakOverlay _overlay;

        readonly BoolSwipeGesture _gesture;

        bool _hovered;
        bool _focused;

        // UI Toolkit has no :focus-visible, so we track ourselves whether the most recent focus came from a pointer.
        // The Vue original's checkbox uses :focus-visible, so a mere click does not show the ring
        bool _focusFromPointer;

        #endregion

        #region Public API

        /// <summary>Fires once per click / swipe release / key input.</summary>
        public event Action<bool> Confirmed;

        /// <summary>Checked state.</summary>
        [UxmlAttribute]
        public bool value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                bool previous = _value;
                SetValueWithoutNotify(value);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>Label placed to the right of the box. Hidden if empty.</summary>
        [UxmlAttribute("label")]
        public string Label
        {
            get => _label;
            set
            {
                _label = value ?? string.Empty;
                ApplyLabel();
            }
        }

        /// <summary>Disabled (non-interactive) state (spec §1).</summary>
        [UxmlAttribute("disabled")]
        public bool Disabled
        {
            get => _disabled;
            set
            {
                if (_disabled == value)
                {
                    return;
                }

                _disabled = value;

                // If a drag is still active at the moment of disabling, there would be no way to release it
                if (_disabled)
                {
                    _gesture.Cancel();
                }

                _gesture.Disabled = _disabled;
                this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
                Refresh();
            }
        }

        /// <summary>Color theme. Falls back to Dark() if null is passed.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyStaticStyles();
                Refresh();
            }
        }

        /// <summary>Position within a horizontal group. Setting it flattens the box's corners as per the table in spec §1.</summary>
        public TweeqBoxPosition InlinePosition
        {
            get => _inlinePosition;
            set
            {
                if (_inlinePosition == value)
                {
                    return;
                }

                _inlinePosition = value;
                ApplyCornerRadius();
            }
        }

        /// <summary>Position within a vertical group.</summary>
        public TweeqBoxPosition BlockPosition
        {
            get => _blockPosition;
            set
            {
                if (_blockPosition == value)
                {
                    return;
                }

                _blockPosition = value;
                ApplyCornerRadius();
            }
        }

        /// <summary>Sets the value without firing a ChangeEvent.</summary>
        public void SetValueWithoutNotify(bool newValue)
        {
            _value = newValue;
            Refresh();
        }

        #endregion

        #region Construction

        public CheckboxInput()
        {
            this.AddToClassList("tweeq-checkbox-input");

            // To receive keyboard shortcuts (T/F/Space...)
            this.focusable = true;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;
            this.style.flexShrink = 0f;

            // The preview overlay during dragging spills outside the box
            this.style.overflow = Overflow.Visible;

            BuildChildren();
            ApplyStaticStyles();
            ApplyLabel();

            _gesture = new BoolSwipeGesture(this)
            {
                ValueGetter = () => _value,
                ValueChanged = OnGestureValueChanged,
                Confirmed = OnGestureConfirmed,
                StateChanged = OnGestureStateChanged,
            };

            this.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            Refresh();
        }

        void BuildChildren()
        {
            _box = new VisualElement { name = "tweeq-checkbox-box" };
            _box.style.flexShrink = 0f;
            _box.style.overflow = Overflow.Visible;

            // The checkmark is drawn via the box's own generateVisualContent.
            // Drawing order is element background -> generated mesh -> child elements, so it lands above the background and below the ring
            _box.generateVisualContent += OnGenerateBoxContent;
            _box.RegisterCallback<PointerEnterEvent>(OnBoxPointerEnter);
            _box.RegisterCallback<PointerLeaveEvent>(OnBoxPointerLeave);
            this.hierarchy.Add(_box);

            // The focus ring also extends 1px outside the box, so it is drawn on a separate layer sharing the same rect as the box
            _ring = new VisualElement
            {
                name = "tweeq-checkbox-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _ring.style.position = Position.Absolute;
            _ring.style.left = 0f;
            _ring.style.top = 0f;
            _ring.style.right = 0f;
            _ring.style.bottom = 0f;
            _ring.style.overflow = Overflow.Visible;
            _ring.generateVisualContent += OnGenerateRingContent;
            _box.hierarchy.Add(_ring);

            _labelElement = new Label(string.Empty)
            {
                name = "tweeq-checkbox-label",
                pickingMode = PickingMode.Ignore,
            };
            _labelElement.style.marginLeft = LABEL_GAP;
            _labelElement.style.marginRight = 0f;
            _labelElement.style.marginTop = 0f;
            _labelElement.style.marginBottom = 0f;
            _labelElement.style.paddingLeft = 0f;
            _labelElement.style.paddingRight = 0f;
            this.hierarchy.Add(_labelElement);
        }

        void ApplyStaticStyles()
        {
            if (_theme == null)
            {
                return;
            }

            float size = _theme.InputHeight;
            this.style.minHeight = size;

            if (_box != null)
            {
                _box.style.width = size;
                _box.style.height = size;

                // Spec §1: only the box's background transitions, at 64ms. The Vue original uses cubic-bezier(0.4,0,0.2,1), but
                // UI Toolkit has no identical curve, so EaseInOutCubic is used as an approximation
                _box.style.transitionProperty = new StyleList<StylePropertyName>(
                    new List<StylePropertyName> { new StylePropertyName("background-color") });
                _box.style.transitionDuration = new StyleList<TimeValue>(
                    new List<TimeValue> { new TimeValue(ACTIVE_TRANSITION_DURATION, TimeUnit.Second) });
                _box.style.transitionTimingFunction = new StyleList<EasingFunction>(
                    new List<EasingFunction> { new EasingFunction(EasingMode.EaseInOutCubic) });
            }

            TweeqFonts.Apply(_labelElement, _theme.FontUi);

            ApplyCornerRadius();
        }

        void ApplyLabel()
        {
            if (_labelElement == null)
            {
                return;
            }

            _labelElement.text = _label;
            _labelElement.style.display = string.IsNullOrEmpty(_label)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        // Corner-radius table from spec §1. The two axes' settings are combined with OR (if either says "flatten," it flattens)
        void ApplyCornerRadius()
        {
            _radiusTopLeft = true;
            _radiusTopRight = true;
            _radiusBottomLeft = true;
            _radiusBottomRight = true;

            switch (_inlinePosition)
            {
                case TweeqBoxPosition.Start:
                    _radiusTopRight = false;
                    _radiusBottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    _radiusTopLeft = false;
                    _radiusTopRight = false;
                    _radiusBottomLeft = false;
                    _radiusBottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    _radiusTopLeft = false;
                    _radiusBottomLeft = false;
                    break;
            }

            switch (_blockPosition)
            {
                case TweeqBoxPosition.Start:
                    _radiusBottomLeft = false;
                    _radiusBottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    _radiusTopLeft = false;
                    _radiusTopRight = false;
                    _radiusBottomLeft = false;
                    _radiusBottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    _radiusTopLeft = false;
                    _radiusTopRight = false;
                    break;
            }

            if (_box == null)
            {
                return;
            }

            float radius = _theme != null ? _theme.InputRadius : 0f;
            _box.style.borderTopLeftRadius = _radiusTopLeft ? radius : 0f;
            _box.style.borderTopRightRadius = _radiusTopRight ? radius : 0f;
            _box.style.borderBottomLeftRadius = _radiusBottomLeft ? radius : 0f;
            _box.style.borderBottomRightRadius = _radiusBottomRight ? radius : 0f;

            _ring?.MarkDirtyRepaint();
        }

        #endregion

        #region Events

        void OnGestureValueChanged(bool next)
        {
            this.value = next;
        }

        void OnGestureConfirmed(bool confirmed)
        {
            Confirmed?.Invoke(confirmed);
        }

        void OnGestureStateChanged()
        {
            Refresh();
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            // Record "focus originated from a pointer" before BoolSwipeGesture calls Focus()
            _focusFromPointer = true;
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            // Promote to the equivalent of :focus-visible the moment a key is touched
            if (_focusFromPointer)
            {
                _focusFromPointer = false;
                Refresh();
            }
        }

        void OnFocusIn(FocusInEvent evt)
        {
            _focused = true;
            Refresh();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            _focused = false;
            _focusFromPointer = false;
            Refresh();
        }

        void OnBoxPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            Refresh();
        }

        void OnBoxPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            Refresh();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            _box?.MarkDirtyRepaint();
            _ring?.MarkDirtyRepaint();
        }

        void NotifyValueChanged(bool previous, bool current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<bool> changeEvent = ChangeEvent<bool>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            UpdateBoxBackground();
            UpdateLabelColor();
            UpdateOverlay();

            _box?.MarkDirtyRepaint();
            _ring?.MarkDirtyRepaint();
        }

        void UpdateBoxBackground()
        {
            if (_box == null)
            {
                return;
            }

            if (_disabled)
            {
                // Spec §1: unchecked is transparent + 1px Border outline; checked is filled with TextSubtle
                if (_value)
                {
                    SetBorderWidth(_box, 0f);
                    _box.style.backgroundColor = _theme.TextSubtle;
                }
                else
                {
                    SetBorderWidth(_box, DISABLED_BORDER_WIDTH);
                    SetBorderColor(_box, _theme.Border);
                    _box.style.backgroundColor = Color.clear;
                }

                return;
            }

            SetBorderWidth(_box, 0f);

            if (_value)
            {
                _box.style.backgroundColor = _hovered ? _theme.AccentHover : _theme.Accent;
            }
            else
            {
                _box.style.backgroundColor = _hovered ? _theme.InputHover : _theme.Input;
            }
        }

        void UpdateLabelColor()
        {
            if (_labelElement == null)
            {
                return;
            }

            _labelElement.style.color = _disabled ? _theme.TextMuted : _theme.Text;
        }

        void UpdateOverlay()
        {
            if (_gesture == null || _box == null)
            {
                return;
            }

            if (!_gesture.Dragging)
            {
                if (_overlay != null)
                {
                    _overlay.RemoveFromHierarchy();
                    _overlay = null;
                }

                return;
            }

            if (_overlay == null)
            {
                _overlay = new BoolTweakOverlay();
                _overlay.Sync(_theme, _gesture.PreviewValue, _theme.InputHeight);
                _box.hierarchy.Add(_overlay);
                return;
            }

            _overlay.Sync(_theme, _gesture.PreviewValue, _theme.InputHeight);
        }

        #endregion

        #region Painting

        // The generated mesh's coordinate origin is the element's border-box top-left, so the layout's actual size is used as-is
        Rect BoxRect()
        {
            if (_box == null)
            {
                return Rect.zero;
            }

            float width = _box.layout.width;
            float height = _box.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height))
            {
                return Rect.zero;
            }

            return new Rect(0f, 0f, width, height);
        }

        // Spec §1: the mark is always drawn, only the color changes (no transition = instant)
        void OnGenerateBoxContent(MeshGenerationContext context)
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

            Rect rect = BoxRect();
            if (rect.width < ICON_SIZE || rect.height < ICON_SIZE)
            {
                return;
            }

            Rect icon = new Rect(
                rect.xMin + (rect.width - ICON_SIZE) * 0.5f,
                rect.yMin + (rect.height - ICON_SIZE) * 0.5f,
                ICON_SIZE,
                ICON_SIZE);

            Color color;
            if (_value)
            {
                // Even when disabled, only the fill changes to TextSubtle -- the mark remains readable, staying at the background color
                color = _theme.Background;
            }
            else
            {
                color = _theme.TextSubtle;
                color.a = MARK_OFF_ALPHA;
            }

            painter.strokeColor = color;
            painter.lineWidth = MARK_STROKE_WIDTH;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(MapToRect(icon, MARK_START));
            painter.LineTo(MapToRect(icon, MARK_ELBOW));
            painter.LineTo(MapToRect(icon, MARK_END));
            painter.Stroke();
        }

        // Spec §1: off = outer 1px Accent / on = double ring of inner 1px Input + outer 1px Accent
        void OnGenerateRingContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null || !ShowFocusRing)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = BoxRect();
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float radius = _theme.InputRadius;
            float half = FOCUS_RING_WIDTH * 0.5f;

            painter.lineWidth = FOCUS_RING_WIDTH;
            painter.lineCap = LineCap.Butt;

            if (_value)
            {
                // The inset 1px ring. Shifting inward by half the line width covers [edge-1, edge]
                painter.strokeColor = _theme.Input;
                TraceRoundedRect(painter, Expand(rect, -half), radius - half);
                painter.Stroke();
            }

            // The outer 1px ring (equivalent to box-shadow 0 0 0 1px)
            painter.strokeColor = _theme.Accent;
            TraceRoundedRect(painter, Expand(rect, half), radius + half);
            painter.Stroke();
        }

        bool ShowFocusRing => _focused && !_focusFromPointer && !_disabled;

        void TraceRoundedRect(Painter2D painter, Rect rect, float radius)
        {
            float limit = Mathf.Min(rect.width, rect.height) * 0.5f;
            float clamped = Mathf.Clamp(radius, 0f, limit);

            float topLeft = _radiusTopLeft ? clamped : 0f;
            float topRight = _radiusTopRight ? clamped : 0f;
            float bottomLeft = _radiusBottomLeft ? clamped : 0f;
            float bottomRight = _radiusBottomRight ? clamped : 0f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin + topLeft, rect.yMin));

            TraceCorner(
                painter,
                new Vector2(rect.xMax - topRight, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax - topRight, rect.yMin + topRight),
                topRight,
                -90f,
                0f);

            TraceCorner(
                painter,
                new Vector2(rect.xMax, rect.yMax - bottomRight),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMax - bottomRight, rect.yMax - bottomRight),
                bottomRight,
                0f,
                90f);

            TraceCorner(
                painter,
                new Vector2(rect.xMin + bottomLeft, rect.yMax),
                new Vector2(rect.xMin, rect.yMax),
                new Vector2(rect.xMin + bottomLeft, rect.yMax - bottomLeft),
                bottomLeft,
                90f,
                180f);

            TraceCorner(
                painter,
                new Vector2(rect.xMin, rect.yMin + topLeft),
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMin + topLeft, rect.yMin + topLeft),
                topLeft,
                180f,
                270f);

            painter.ClosePath();
        }

        // Draw a straight line to the edge's end point, then round the corner. For a radius-0 corner the Arc degenerates, so fold it with a straight line instead
        static void TraceCorner(
            Painter2D painter,
            Vector2 edgeEnd,
            Vector2 sharpCorner,
            Vector2 arcCenter,
            float radius,
            float startAngle,
            float endAngle)
        {
            if (radius <= 0f)
            {
                painter.LineTo(sharpCorner);
                return;
            }

            painter.LineTo(edgeEnd);
            painter.Arc(
                arcCenter,
                radius,
                new Angle(startAngle, AngleUnit.Degree),
                new Angle(endAngle, AngleUnit.Degree));
        }

        static Rect Expand(Rect rect, float amount)
        {
            return new Rect(
                rect.xMin - amount,
                rect.yMin - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }

        static Vector2 MapToRect(Rect rect, Vector2 normalized)
        {
            return new Vector2(
                rect.xMin + rect.width * normalized.x,
                rect.yMin + rect.height * normalized.y);
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

        #endregion
    }
}
