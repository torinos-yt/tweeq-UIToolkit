using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Segments are built from Label elements. There's no naming collision, but alias it to match the other Inputs' notation
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A segmented switch (spec §5). Lays an indicator underneath that slides to the measured rect
    /// of the selected segment. Does not participate in corner-rounding fusion.
    ///
    /// The original takes generic options, but the Unity version is fixed to string[] + index
    /// (Unity decision 2). The icon column and responsive tiers (rowIcon/colFull/colIcon) are out of v1 scope.
    /// </summary>
    [UxmlElement]
    public partial class RadioInput : VisualElement, INotifyValueChanged<int>, ITweeqThemed
    {
        #region Constants

        // Actual size converting the original's padding: 0 .75em at rem12
        const float SEGMENT_PADDING = 9f;

        // spec §5: 1px gap between segments
        const float SEGMENT_GAP = 1f;

        const float FOCUS_RING_WIDTH = 1f;

        // spec §5: only slide for user-driven value changes. Duration the flag is held (the original's 250ms)
        const long ANIMATING_HOLD_MS = 250;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        int _value;
        string[] _options = Array.Empty<string>();

        readonly List<UILabel> _segments = new List<UILabel>();
        readonly VisualElement _indicator;
        readonly VisualElement _focusRing;

        int _hoveredIndex = -1;
        bool _dragging;
        bool _focused;
        int _pointerId = PointerId.invalidPointerId;

        // Only apply a transition to the indicator while this is true. Sliding on resize "looks like a bug", so kill it
        bool _animating;
        IVisualElementScheduledItem _animatingItem;

        #endregion

        #region Public API

        /// <summary>Fires on drag release and on every arrow-key operation.</summary>
        public event Action<int> Confirmed;

        /// <summary>
        /// The options. Both getter and setter pass through a copy (decoupling the caller's array
        /// from internal state). If the selected index falls outside the new length, it is folded
        /// back into range without notifying.
        /// </summary>
        // In UXML this can be written as a variable-length string[] (comma-separated)
        [UxmlAttribute("options")]
        public string[] Options
        {
            get
            {
                string[] copy = new string[_options.Length];
                Array.Copy(_options, copy, _options.Length);
                return copy;
            }

            set
            {
                if (value == null)
                {
                    _options = Array.Empty<string>();
                }
                else
                {
                    _options = new string[value.Length];
                    for (int i = 0; i < value.Length; i++)
                    {
                        _options[i] = value[i] ?? string.Empty;
                    }
                }

                if (_value >= _options.Length)
                {
                    _value = _options.Length > 0 ? _options.Length - 1 : 0;
                }

                RebuildSegments();
                Refresh();
            }
        }

        /// <summary>The selected index. Out-of-range assignments are ignored (spec API contract).</summary>
        // UXML attributes are applied in declaration order, so this is placed after Options.
        // In reverse order, this would be discarded by the out-of-range check while options is
        // unset (i.e. length 0), and value would have no effect
        [UxmlAttribute]
        public int value
        {
            get => _value;
            set
            {
                if (!IsValidIndex(value) || _value == value)
                {
                    return;
                }

                int previous = _value;
                SetValueWithoutNotify(value);
                NotifyValueChanged(previous, _value);
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

        /// <summary>Sets the value without firing a ChangeEvent. Out-of-range values are ignored.</summary>
        public void SetValueWithoutNotify(int newValue)
        {
            if (!IsValidIndex(newValue))
            {
                return;
            }

            _value = newValue;
            Refresh();
        }

        /// <summary>
        /// Wrap-around calculation for arrow-key movement (spec §5; a deliberate addition carried
        /// over from a reference implementation).
        /// Returns 0 if count is 0 or less.
        /// </summary>
        public static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        #endregion

        #region Construction

        public RadioInput()
        {
            this.AddToClassList("tweeq-radio-input");

            // The root itself holds focus so it can receive arrow keys (segments are non-focusable)
            this.focusable = true;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Stretch;
            this.style.flexShrink = 0f;

            // spec §5: clip so the indicator doesn't overflow past the corners
            this.style.overflow = Overflow.Hidden;

            _indicator = new VisualElement
            {
                name = "tweeq-radio-indicator",
                pickingMode = PickingMode.Ignore,
            };
            _indicator.style.position = Position.Absolute;
            _indicator.style.left = 0f;
            _indicator.style.top = 0f;
            _indicator.style.width = 0f;
            _indicator.style.height = 0f;
            _indicator.style.display = DisplayStyle.None;

            // Added before the segments, meaning it draws underneath them (UI Toolkit has no z-index)
            this.hierarchy.Add(_indicator);

            _focusRing = new VisualElement
            {
                name = "tweeq-radio-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _focusRing.style.position = Position.Absolute;
            _focusRing.style.left = 0f;
            _focusRing.style.top = 0f;
            _focusRing.style.right = 0f;
            _focusRing.style.bottom = 0f;
            _focusRing.style.display = DisplayStyle.None;
            SetBorderWidth(_focusRing, FOCUS_RING_WIDTH);

            // Always on top. RebuildSegments re-appends this at the end each time segments are rebuilt
            this.hierarchy.Add(_focusRing);

            ApplyStaticStyles();

            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            this.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            this.RegisterCallback<KeyDownEvent>(OnKeyDown);

            // Arrow keys also fire a NavigationMoveEvent separately from KeyDown, and that one ends up
            // moving focus (feedback-fixes-01.md A-5)
            this.RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            Refresh();
        }

        public RadioInput(string[] options)
            : this()
        {
            this.Options = options;
        }

        void ApplyStaticStyles()
        {
            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;
            this.style.backgroundColor = _theme.Input;
            SetCornerRadius(this, _theme.InputRadius);
            SetCornerRadius(_focusRing, _theme.InputRadius);
            SetBorderColor(_focusRing, _theme.Accent);
            SetCornerRadius(_indicator, _theme.InputRadius);

            ApplyIndicatorTransition(_animating);

            for (int i = 0; i < _segments.Count; i++)
            {
                ApplySegmentStyles(_segments[i], i);
            }
        }

        void RebuildSegments()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                this.hierarchy.Remove(_segments[i]);
            }

            _segments.Clear();
            _hoveredIndex = -1;

            for (int i = 0; i < _options.Length; i++)
            {
                UILabel segment = new UILabel(_options[i])
                {
                    name = "tweeq-radio-segment",

                    // Hit-testing is done on the root side by looking at layout rects (we want the same path even while capturing)
                    pickingMode = PickingMode.Ignore,
                };
                ApplySegmentStyles(segment, i);
                this.hierarchy.Add(segment);
                _segments.Add(segment);
            }

            // The focus ring is always on top. Re-append it whenever segments are re-added
            if (_focusRing.parent == this)
            {
                this.hierarchy.Remove(_focusRing);
            }

            this.hierarchy.Add(_focusRing);
        }

        void ApplySegmentStyles(UILabel segment, int index)
        {
            segment.style.flexGrow = 1f;
            segment.style.flexShrink = 1f;
            segment.style.minWidth = 0f;
            segment.style.paddingLeft = SEGMENT_PADDING;
            segment.style.paddingRight = SEGMENT_PADDING;
            segment.style.paddingTop = 0f;
            segment.style.paddingBottom = 0f;
            segment.style.marginTop = 0f;
            segment.style.marginBottom = 0f;
            segment.style.marginRight = 0f;

            // UI Toolkit's inline styles have no flex gap, so build it from margins on everything but the first
            segment.style.marginLeft = index == 0 ? 0f : SEGMENT_GAP;

            segment.style.unityTextAlign = TextAnchor.MiddleCenter;
            segment.style.whiteSpace = WhiteSpace.NoWrap;
            segment.style.overflow = Overflow.Hidden;
            segment.style.textOverflow = TextOverflow.Ellipsis;
            SetCornerRadius(segment, _theme.InputRadius);

            ApplyTransition(
                segment,
                _theme.HoverTransitionDuration,
                EasingMode.EaseInOutCubic,
                "background-color",
                "color");
        }

        // Only the indicator uses plain ease (an explicit exception in spec §5).
        // While animating is off, the transition duration is 0, killing sliding on resize
        void ApplyIndicatorTransition(bool animate)
        {
            float duration = animate ? _theme.HoverTransitionDuration : 0f;

            List<StylePropertyName> names = new List<StylePropertyName>
            {
                new StylePropertyName("translate"),
                new StylePropertyName("width"),
                new StylePropertyName("height"),
                new StylePropertyName("background-color"),
            };

            List<TimeValue> durations = new List<TimeValue>
            {
                new TimeValue(duration, TimeUnit.Second),
                new TimeValue(duration, TimeUnit.Second),
                new TimeValue(duration, TimeUnit.Second),

                // Color alone always follows the hover-family 0.15s, regardless of whether it's user-driven
                new TimeValue(_theme.HoverTransitionDuration, TimeUnit.Second),
            };

            List<EasingFunction> easings = new List<EasingFunction>
            {
                new EasingFunction(EasingMode.Ease),
                new EasingFunction(EasingMode.Ease),
                new EasingFunction(EasingMode.Ease),
                new EasingFunction(EasingMode.EaseInOutCubic),
            };

            _indicator.style.transitionProperty = new StyleList<StylePropertyName>(names);
            _indicator.style.transitionDuration = new StyleList<TimeValue>(durations);
            _indicator.style.transitionTimingFunction = new StyleList<EasingFunction>(easings);
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            Color activeText = TweeqTheme.ContrastText(_theme.Accent);

            for (int i = 0; i < _segments.Count; i++)
            {
                UILabel segment = _segments[i];
                bool active = i == _value;
                bool hovered = i == _hoveredIndex;

                segment.style.color = active ? activeText : _theme.Text;

                // Only show the hover fill color for inactive segments. The active one is represented by the indicator's color instead
                segment.style.backgroundColor = !active && hovered
                    ? _theme.InputHover
                    : Color.clear;
            }

            _focusRing.style.display = _focused ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateIndicator();
        }

        void UpdateIndicator()
        {
            if (_value < 0 || _value >= _segments.Count)
            {
                _indicator.style.display = DisplayStyle.None;
                return;
            }

            Rect rect = _segments[_value].layout;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                // Layout not yet resolved. Called again by GeometryChangedEvent
                return;
            }

            _indicator.style.display = DisplayStyle.Flex;

            // Finalize the transition settings before writing geometry (so duration takes effect in the same frame)
            ApplyIndicatorTransition(_animating);

            _indicator.style.translate = new Translate(rect.x, rect.y);
            _indicator.style.width = rect.width;
            _indicator.style.height = rect.height;

            // While dragging / actively hovered, use the hover-side color to represent "being held"
            bool held = _dragging || _hoveredIndex == _value;
            _indicator.style.backgroundColor = held ? _theme.AccentHover : _theme.Accent;
        }

        void NotifyValueChanged(int previous, int current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<int> changeEvent = ChangeEvent<int>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Interaction

        // A user-driven value change. Allows the slide transition, then routes through the normal value path
        bool SetValueFromUser(int next)
        {
            if (!IsValidIndex(next) || _value == next)
            {
                return false;
            }

            MarkAnimating();
            this.value = next;
            return true;
        }

        void MarkAnimating()
        {
            _animatingItem?.Pause();
            _animatingItem = null;

            if (this.panel == null)
            {
                // The scheduler doesn't run, meaning the flag can never be cleared. Avoid leaving it stuck on, and do nothing instead
                _animating = false;
                return;
            }

            _animating = true;
            _animatingItem = this.schedule.Execute(() =>
            {
                _animatingItem = null;
                _animating = false;
            }).StartingIn(ANIMATING_HOLD_MS);
        }

        // Hit-testing along the main axis (X). Returns the first segment whose measured rect contains it, scanning from the left
        int IndexAt(float x)
        {
            if (_segments.Count == 0)
            {
                return -1;
            }

            for (int i = 0; i < _segments.Count; i++)
            {
                Rect rect = _segments[i].layout;
                if (float.IsNaN(rect.xMax))
                {
                    continue;
                }

                if (x < rect.xMax)
                {
                    return i;
                }
            }

            return _segments.Count - 1;
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _segments.Count == 0)
            {
                return;
            }

            _pointerId = evt.pointerId;
            _dragging = true;

            if (this.panel != null)
            {
                this.CapturePointer(_pointerId);
                this.Focus();
            }

            Vector2 local = LocalPosition(evt);
            _hoveredIndex = IndexAt(local.x);
            SetValueFromUser(_hoveredIndex);
            Refresh();

            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            Vector2 local = LocalPosition(evt);

            if (!_dragging)
            {
                int hovered = IndexAt(local.x);
                if (hovered == _hoveredIndex)
                {
                    return;
                }

                _hoveredIndex = hovered;
                Refresh();
                return;
            }

            if (evt.pointerId != _pointerId)
            {
                return;
            }

            // While dragging, selection moves the instant a boundary is crossed rather than being decided on release (spec §5)
            _hoveredIndex = IndexAt(local.x);
            SetValueFromUser(_hoveredIndex);
            Refresh();
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_dragging || evt.pointerId != _pointerId)
            {
                return;
            }

            int pointerId = _pointerId;
            _dragging = false;
            _pointerId = PointerId.invalidPointerId;
            ReleasePointerSafely(pointerId);

            Refresh();
            Confirmed?.Invoke(_value);
            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            // If capture is taken away, fold up the drag without confirming
            _dragging = false;
            _pointerId = PointerId.invalidPointerId;
            Refresh();
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            if (evt == null || _dragging)
            {
                return;
            }

            // Take the hit here too, so the hover fill color shows even for usage that doesn't move right after entering
            _hoveredIndex = IndexAt(LocalPosition(evt).x);
            Refresh();
        }

        void OnPointerLeave(PointerLeaveEvent evt)
        {
            if (_dragging)
            {
                return;
            }

            _hoveredIndex = -1;
            Refresh();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _segments.Count == 0)
            {
                return;
            }

            int direction;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow:
                case KeyCode.UpArrow:
                    direction = -1;
                    break;

                case KeyCode.RightArrow:
                case KeyCode.DownArrow:
                    direction = 1;
                    break;

                default:
                    return;
            }

            int next = WrapIndex(_value + direction, _segments.Count);
            if (SetValueFromUser(next))
            {
                Confirmed?.Invoke(_value);
            }

            evt.StopPropagation();
        }

        // feedback-fixes-01.md A-5: <-> up/down only change the selection. Focus is not moved.
        // Next/Previous (Tab) is left as normal focus traversal
        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Left:
                case NavigationMoveEvent.Direction.Right:
                case NavigationMoveEvent.Direction.Up:
                case NavigationMoveEvent.Direction.Down:
                    break;

                default:
                    return;
            }

            evt.StopPropagation();

            // In Unity 6, this is the way to stop "the focus move itself" (PreventDefault is deprecated)
            this.focusController?.IgnoreEvent(evt);
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

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // A change in either the root or a segment changes what the indicator needs to follow.
            // If it isn't user-driven, _animating is off, so this just repositions without transitioning
            UpdateIndicator();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            _animatingItem?.Pause();
            _animatingItem = null;
            _animating = false;
            _dragging = false;
            _focused = false;
            _hoveredIndex = -1;
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

        // Convert from panel coordinates to local, so the coordinate system doesn't drift even while capturing
        Vector2 LocalPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return this.WorldToLocal(new Vector2(position.x, position.y));
        }

        bool IsValidIndex(int index)
        {
            return index >= 0 && index < _options.Length;
        }

        #endregion

        #region Helpers

        static void ApplyTransition(
            VisualElement element, float duration, EasingMode easing, params string[] properties)
        {
            if (element == null || properties == null || properties.Length == 0)
            {
                return;
            }

            List<StylePropertyName> names = new List<StylePropertyName>(properties.Length);
            List<TimeValue> durations = new List<TimeValue>(properties.Length);
            List<EasingFunction> easings = new List<EasingFunction>(properties.Length);

            for (int i = 0; i < properties.Length; i++)
            {
                names.Add(new StylePropertyName(properties[i]));
                durations.Add(new TimeValue(duration, TimeUnit.Second));
                easings.Add(new EasingFunction(easing));
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(names);
            element.style.transitionDuration = new StyleList<TimeValue>(durations);
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(easings);
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

        static void SetCornerRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        #endregion
    }
}
