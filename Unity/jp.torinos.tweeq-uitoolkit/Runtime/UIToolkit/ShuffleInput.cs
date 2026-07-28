using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A button that produces the next value via <see cref="Generate"/> each time it's pressed (equivalent to Vue's InputShuffle).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This doesn't know the meaning of the value itself and only "seeds the next value with the current
    /// one," so <c>INotifyValueChanged</c> is not implemented (there's no fixed type to be ChangeEvent's counterpart).
    /// </para>
    /// <para>
    /// The die face, per Vue's own comment, is purely decorative ("the die face is just flair") and never
    /// corresponds to the value at all. It rotates 90 degrees and re-rolls the face on every click.
    /// </para>
    /// </remarks>
    // Generics can't be made [UxmlElement], so this isn't exposed to UXML directly (handled by the string-specialized wrapper instead).
    public class ShuffleInput<T> : VisualElement, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // Vue's SvgIcon uses viewBox 32 / stroke-width 2. The coordinates are kept as-is and shrunk at draw time.
        const float VIEWBOX_SIZE = 32f;
        const float STROKE_WIDTH = 2f;

        // The body's rounded square (the outline of Vue's path "M24,29H8c-2.8,0...").
        const float BODY_MIN = 3f;
        const float BODY_MAX = 29f;
        const float BODY_RADIUS = 5f;

        // A pip looks like an r=1 circle drawn with stroke-width 2, i.e. a filled circle of radius 2.
        const float DOT_RADIUS = 2f;

        const int MIN_FACE = 1;
        const int MAX_FACE = 6;

        // Vue: iconRot += 90
        const float ROTATION_STEP = 90f;

        const float DISABLED_OPACITY = 0.4f;
        const float FOCUS_RING_WIDTH = 1f;

        // Coordinates for pips 1-6 (based on viewBox 32). Copied directly from Vue's SvgIcon circles.
        static readonly Vector2[][] FACE_DOTS =
        {
            new[] { new Vector2(16f, 16f) },
            new[] { new Vector2(11f, 21f), new Vector2(21f, 11f) },
            new[] { new Vector2(16f, 16f), new Vector2(10f, 22f), new Vector2(22f, 10f) },
            new[]
            {
                new Vector2(10f, 22f), new Vector2(22f, 10f),
                new Vector2(10f, 10f), new Vector2(22f, 22f),
            },
            new[]
            {
                new Vector2(16f, 16f),
                new Vector2(10f, 22f), new Vector2(22f, 10f),
                new Vector2(10f, 10f), new Vector2(22f, 22f),
            },
            new[]
            {
                new Vector2(10f, 10f), new Vector2(10f, 16f), new Vector2(10f, 22f),
                new Vector2(22f, 10f), new Vector2(22f, 16f), new Vector2(22f, 22f),
            },
        };

        #endregion

        #region Fields

        readonly VisualElement _icon;
        readonly VisualElement _focusRing;

        TweeqTheme _theme = TweeqTheme.Dark();

        T _value;
        bool _disabled;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        float _iconRotation;

        // Vue's iconNum = ref(3)
        int _iconFace = 3;

        bool _hovered;
        bool _focused;
        int _pointerId = PointerId.invalidPointerId;

        #endregion

        #region Public API

        /// <summary>
        /// Produces the next value from the current one. While null, clicking does nothing
        /// (Vue treats this as a required prop, so being unset is treated here as "not wired up yet").
        /// </summary>
        public Func<T, T> Generate { get; set; }

        /// <summary>Fires when the value changes.</summary>
        public event Action<T> ValueChanged;

        /// <summary>Fires once per click, paired with <see cref="ValueChanged"/>.</summary>
        public event Action<T> Confirmed;

        /// <summary>The current value. Also the seed passed into the next <see cref="Generate"/> call.</summary>
        public T value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value))
                {
                    return;
                }

                SetValueWithoutNotify(value);
                ValueChanged?.Invoke(_value);
            }
        }

        /// <summary>Non-interactive state. Neither clicks nor key operations go through.</summary>
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
                _hovered = false;
                ApplyInteractivity();
                Refresh();
            }
        }

        /// <summary>The color theme. Falls back to Dark() when null is passed.</summary>
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

        /// <summary>The die's current rotation angle (degrees). Purely decorative.</summary>
        public float IconRotation => _iconRotation;

        /// <summary>The die's current face (1-6). A decoration unrelated to the value.</summary>
        public int IconFace => _iconFace;

        /// <summary>Position within a horizontal group.</summary>
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

        /// <summary>
        /// A programmatic click. Does nothing if Disabled or if <see cref="Generate"/> is unset.
        /// Panel-independent, so this can also be used to fire from tests.
        /// </summary>
        public void PerformClick()
        {
            if (_disabled)
            {
                return;
            }

            Func<T, T> generate = Generate;
            if (generate == null)
            {
                return;
            }

            RollIcon();

            T next = generate(_value);
            _value = next;

            ValueChanged?.Invoke(next);
            Confirmed?.Invoke(next);
        }

        /// <summary>Sets the value without firing a notification. The decoration is left untouched too.</summary>
        public void SetValueWithoutNotify(T newValue)
        {
            _value = newValue;
        }

        #endregion

        #region Construction

        public ShuffleInput()
        {
            this.AddToClassList("tweeq-shuffle-input");

            this.focusable = true;
            this.style.flexShrink = 0f;

            // Must not be Hidden, since the focus ring is placed 1px outside.
            this.style.overflow = Overflow.Visible;

            _icon = new VisualElement
            {
                name = "tweeq-shuffle-icon",
                pickingMode = PickingMode.Ignore,
            };
            _icon.style.position = Position.Absolute;
            _icon.style.left = 0f;
            _icon.style.top = 0f;
            _icon.style.right = 0f;
            _icon.style.bottom = 0f;
            _icon.generateVisualContent += OnGenerateIcon;
            this.hierarchy.Add(_icon);

            // The fill is faint (a Subtle-family color), so focus is expressed with just a single outer ring (same judgment as ButtonInput).
            _focusRing = new VisualElement
            {
                name = "tweeq-shuffle-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _focusRing.style.position = Position.Absolute;
            _focusRing.style.left = -FOCUS_RING_WIDTH;
            _focusRing.style.top = -FOCUS_RING_WIDTH;
            _focusRing.style.right = -FOCUS_RING_WIDTH;
            _focusRing.style.bottom = -FOCUS_RING_WIDTH;
            _focusRing.style.display = DisplayStyle.None;
            SetBorderWidth(_focusRing, FOCUS_RING_WIDTH);
            this.hierarchy.Add(_focusRing);

            ApplyStaticStyles();
            ApplyInteractivity();
            ApplyIconTransform();

            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            this.RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            Refresh();
        }

        #endregion

        #region Styles

        void ApplyStaticStyles()
        {
            float size = _theme != null ? _theme.InputHeight : 0f;

            this.style.width = size;
            this.style.height = size;

            // InputGroup.ApplyStretch assigns basis 0 to children with no explicit flexBasis.
            // basis wins over width, so without setting this explicitly, the 24px square would collapse to zero width.
            this.style.flexGrow = 0f;
            this.style.flexBasis = size;

            ApplyCornerRadius();

            ApplyTransition(
                this,
                _theme != null ? _theme.HoverTransitionDuration : 0f,
                EasingMode.EaseInOutCubic,
                "background-color");

            // Vue: transition transform .3s cubic-bezier(0.19, 1.6, 0.42, 1).
            // EaseOutBack is the closest match among curves with overshoot. The duration matches the theme's hover transition.
            ApplyTransition(
                _icon,
                _theme != null ? _theme.HoverTransitionDuration : 0f,
                EasingMode.EaseOutBack,
                "rotate");

            if (_theme != null)
            {
                SetBorderColor(_focusRing, _theme.Accent);
            }
        }

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            this.focusable = !_disabled;
            this.style.opacity = _disabled ? DISABLED_OPACITY : 1f;

            if (_disabled)
            {
                _focused = false;
            }
        }

        // Corner-radius table from spec §1. The settings for both axes are combined via OR (collapsed if either axis says to collapse).
        void ApplyCornerRadius()
        {
            float radius = _theme != null ? _theme.InputRadius : 0f;

            bool topLeft = true;
            bool topRight = true;
            bool bottomLeft = true;
            bool bottomRight = true;

            switch (_inlinePosition)
            {
                case TweeqBoxPosition.Start:
                    topRight = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    topLeft = false;
                    topRight = false;
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    topLeft = false;
                    bottomLeft = false;
                    break;
            }

            switch (_blockPosition)
            {
                case TweeqBoxPosition.Start:
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    topLeft = false;
                    topRight = false;
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    topLeft = false;
                    topRight = false;
                    break;
            }

            SetCornerRadius(this, radius, topLeft, topRight, bottomLeft, bottomRight);

            // The outer ring sits 1px outside, so its radius is also grown by 1px to keep the same visual appearance.
            SetCornerRadius(
                _focusRing,
                radius + FOCUS_RING_WIDTH,
                topLeft,
                topRight,
                bottomLeft,
                bottomRight);
        }

        #endregion

        #region Presentation

        // Vue's rest state has no background, but this is designed to blend in with a neighbor via
        // InputGroup, so the rest state here uses the same "Input surface + Accent icon" as ButtonInput's Subtle.
        Color CurrentBackground => _hovered && !_disabled
            ? (_theme != null ? _theme.AccentHover : Color.clear)
            : (_theme != null ? _theme.Input : Color.clear);

        Color CurrentIconColor
        {
            get
            {
                if (_theme == null)
                {
                    return Color.white;
                }

                return _hovered && !_disabled
                    ? TweeqTheme.ContrastText(_theme.AccentHover)
                    : _theme.Accent;
            }
        }

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            this.style.backgroundColor = CurrentBackground;
            _focusRing.style.display = _focused && !_disabled
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            _icon.MarkDirtyRepaint();
        }

        void RollIcon()
        {
            _iconRotation += ROTATION_STEP;

            // Vue: random(1, 6) (upper bound inclusive).
            _iconFace = UnityEngine.Random.Range(MIN_FACE, MAX_FACE + 1);

            ApplyIconTransform();
            _icon.MarkDirtyRepaint();
        }

        void ApplyIconTransform()
        {
            _icon.style.rotate = new Rotate(new Angle(_iconRotation, AngleUnit.Degree));
        }

        #endregion

        #region Events

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled)
            {
                return;
            }

            _pointerId = evt.pointerId;

            if (this.panel != null)
            {
                this.CapturePointer(_pointerId);
            }

            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || _pointerId == PointerId.invalidPointerId
                || evt.pointerId != _pointerId)
            {
                return;
            }

            int pointerId = _pointerId;
            _pointerId = PointerId.invalidPointerId;
            ReleasePointerSafely(pointerId);

            if (_disabled)
            {
                return;
            }

            // Releasing after dragging the pressed finger outside doesn't count as a click.
            Vector3 position = evt.position;
            bool inside = this.ContainsPoint(this.WorldToLocal(new Vector2(position.x, position.y)));

            // Focus gained via pointer is released as soon as the pointer is released (same judgment as ButtonInput).
            if (_focused)
            {
                this.Blur();
            }

            if (inside)
            {
                PerformClick();
            }

            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _pointerId = PointerId.invalidPointerId;
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            if (_disabled)
            {
                return;
            }

            _hovered = true;
            Refresh();
        }

        void OnPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            Refresh();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            bool activate = evt.keyCode == KeyCode.Return
                || evt.keyCode == KeyCode.KeypadEnter
                || evt.keyCode == KeyCode.Space;

            if (!activate)
            {
                return;
            }

            PerformClick();
            evt.StopPropagation();
        }

        void OnFocusIn(FocusInEvent evt)
        {
            _focused = true;
            Refresh();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            _focused = false;
            Refresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            _hovered = false;
            _focused = false;
            _pointerId = PointerId.invalidPointerId;
        }

        void ReleasePointerSafely(int pointerId)
        {
            if (this.panel == null || pointerId == PointerId.invalidPointerId)
            {
                return;
            }

            if (this.HasPointerCapture(pointerId))
            {
                this.ReleasePointer(pointerId);
            }
        }

        #endregion

        #region Painting

        void OnGenerateIcon(MeshGenerationContext context)
        {
            Painter2D painter = context?.painter2D;
            if (painter == null || _theme == null)
            {
                return;
            }

            Rect rect = _icon.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            // No icon-font dependency (Unity decision 1). The viewBox-32 SVG is copied at uniform scale.
            float scale = Mathf.Min(rect.width, rect.height) / VIEWBOX_SIZE;
            float originX = (rect.width - VIEWBOX_SIZE * scale) * 0.5f;
            float originY = (rect.height - VIEWBOX_SIZE * scale) * 0.5f;

            Color color = CurrentIconColor;
            painter.strokeColor = color;
            painter.fillColor = color;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;
            painter.lineWidth = STROKE_WIDTH * scale;

            Rect body = new Rect(
                originX + BODY_MIN * scale,
                originY + BODY_MIN * scale,
                (BODY_MAX - BODY_MIN) * scale,
                (BODY_MAX - BODY_MIN) * scale);

            TraceRoundedRect(painter, body, BODY_RADIUS * scale);
            painter.Stroke();

            int index = Mathf.Clamp(_iconFace, MIN_FACE, MAX_FACE) - 1;
            Vector2[] dots = FACE_DOTS[index];
            float dotRadius = DOT_RADIUS * scale;

            for (int i = 0; i < dots.Length; i++)
            {
                Vector2 center = new Vector2(
                    originX + dots[i].x * scale,
                    originY + dots[i].y * scale);

                painter.BeginPath();
                painter.Arc(
                    center,
                    dotRadius,
                    new Angle(0f, AngleUnit.Degree),
                    new Angle(360f, AngleUnit.Degree));
                painter.ClosePath();
                painter.Fill();
            }
        }

        // Painter2D has no rounded-rectangle primitive, so it's traced using ArcTo.
        static void TraceRoundedRect(Painter2D painter, Rect rect, float radius)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float limit = Mathf.Min(rect.width, rect.height) * 0.5f;
            float r = Mathf.Clamp(radius, 0f, limit);

            float x0 = rect.xMin;
            float y0 = rect.yMin;
            float x1 = rect.xMax;
            float y1 = rect.yMax;

            painter.BeginPath();
            painter.MoveTo(new Vector2(x0 + r, y0));
            painter.ArcTo(new Vector2(x1, y0), new Vector2(x1, y1), r);
            painter.ArcTo(new Vector2(x1, y1), new Vector2(x0, y1), r);
            painter.ArcTo(new Vector2(x0, y1), new Vector2(x0, y0), r);
            painter.ArcTo(new Vector2(x0, y0), new Vector2(x1, y0), r);
            painter.ClosePath();
        }

        #endregion

        #region Helpers

        static void ApplyTransition(
            VisualElement element, float duration, EasingMode easing, string property)
        {
            if (element == null)
            {
                return;
            }

            element.style.transitionProperty =
                new StyleList<StylePropertyName>(new List<StylePropertyName>
                {
                    new StylePropertyName(property),
                });
            element.style.transitionDuration =
                new StyleList<TimeValue>(new List<TimeValue>
                {
                    new TimeValue(duration, TimeUnit.Second),
                });
            element.style.transitionTimingFunction =
                new StyleList<EasingFunction>(new List<EasingFunction>
                {
                    new EasingFunction(easing),
                });
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

        static void SetCornerRadius(
            VisualElement element,
            float radius,
            bool topLeft,
            bool topRight,
            bool bottomLeft,
            bool bottomRight)
        {
            element.style.borderTopLeftRadius = topLeft ? radius : 0f;
            element.style.borderTopRightRadius = topRight ? radius : 0f;
            element.style.borderBottomLeftRadius = bottomLeft ? radius : 0f;
            element.style.borderBottomRightRadius = bottomRight ? radius : 0f;
        }

        #endregion
    }
}
