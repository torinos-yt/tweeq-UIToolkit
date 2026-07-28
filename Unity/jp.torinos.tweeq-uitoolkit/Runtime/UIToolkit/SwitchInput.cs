using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A boolean toggle switch (spec §2). Toggles on click; a left/right swipe can set true/false directly.
    /// Doesn't participate in corner-radius merging, and has no disabled state (matching Vue; spec Unity-specific decision 3).
    /// </summary>
    [UxmlElement]
    public partial class SwitchInput : VisualElement, INotifyValueChanged<bool>, ITweeqThemed
    {
        #region Constants

        // The track is 48x24 (twice its height).
        const float TRACK_WIDTH_FACTOR = 2f;

        // The handle is 16x16 with a 4px inset. It stretches 4px toward the center to become 20px while dragging.
        const float HANDLE_INSET = 4f;

        // The active-family transition is 64ms (per the spec's transition table).
        const float ACTIVE_TRANSITION_DURATION = 0.064f;

        // The focus ring is a 1px pill with a -3px inset.
        const float FOCUS_RING_INSET = 3f;
        const float FOCUS_RING_WIDTH = 1f;

        // The gap to the label is 1em (rem12 = 12px).
        const float LABEL_GAP = 12f;

        #endregion

        #region Fields

        bool _value;
        string _label = string.Empty;
        TweeqTheme _theme = TweeqTheme.Dark();

        VisualElement _track;
        VisualElement _handle;
        VisualElement _ring;
        Label _labelElement;
        BoolTweakOverlay _overlay;

        readonly BoolSwipeGesture _gesture;

        bool _hovered;
        bool _focused;

        #endregion

        #region Public API

        /// <summary>Fires once for each click / swipe release / key input.</summary>
        public event Action<bool> Confirmed;

        /// <summary>On/off.</summary>
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

        /// <summary>The label placed to the right of the track. Hidden when empty.</summary>
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

        /// <summary>Sets the value without firing ChangeEvent.</summary>
        public void SetValueWithoutNotify(bool newValue)
        {
            _value = newValue;
            Refresh();
        }

        #endregion

        #region Construction

        public SwitchInput()
        {
            this.AddToClassList("tweeq-switch-input");

            // To receive keyboard shortcuts (T/F/Space...).
            this.focusable = true;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;
            this.style.flexShrink = 0f;

            // The focus ring and preview overlay both spill outside the track.
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

            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            Refresh();
        }

        void BuildChildren()
        {
            _track = new VisualElement { name = "tweeq-switch-track" };
            _track.style.flexShrink = 0f;
            _track.style.overflow = Overflow.Visible;
            _track.RegisterCallback<PointerEnterEvent>(OnTrackPointerEnter);
            _track.RegisterCallback<PointerLeaveEvent>(OnTrackPointerLeave);
            this.hierarchy.Add(_track);

            _handle = new VisualElement
            {
                name = "tweeq-switch-handle",
                pickingMode = PickingMode.Ignore,
            };
            _handle.style.position = Position.Absolute;
            _handle.style.top = HANDLE_INSET;
            _track.hierarchy.Add(_handle);

            // The ring also extends 3px outside the track, so it's drawn on a separate layer with the same rect as the track.
            _ring = new VisualElement
            {
                name = "tweeq-switch-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _ring.style.position = Position.Absolute;
            _ring.style.left = 0f;
            _ring.style.top = 0f;
            _ring.style.right = 0f;
            _ring.style.bottom = 0f;
            _ring.style.overflow = Overflow.Visible;
            _ring.generateVisualContent += OnGenerateRingContent;
            _track.hierarchy.Add(_ring);

            _labelElement = new Label(string.Empty)
            {
                name = "tweeq-switch-label",
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

            float height = _theme.InputHeight;
            this.style.minHeight = height;

            if (_track != null)
            {
                _track.style.width = height * TRACK_WIDTH_FACTOR;
                _track.style.height = height;

                // border-radius 9999px, i.e. it becomes a pill at half the height.
                SetBorderRadius(_track, height * 0.5f);
                ApplyTransition(_track, new[] { "background-color" });
            }

            if (_handle != null)
            {
                float size = HandleSize;
                _handle.style.height = size;
                SetBorderRadius(_handle, size * 0.5f);
                ApplyTransition(_handle, new[] { "left", "width", "background-color" });
            }
        }

        // Spec §2: the track background and the handle's left/width/background are all 64ms.
        // Vue uses cubic-bezier(0.4,0,0.2,1), but since UI Toolkit has no identical curve,
        // EaseInOutCubic is used as an approximation (same judgment as RotaryInput / NumberInput).
        static void ApplyTransition(VisualElement element, string[] properties)
        {
            if (element == null || properties == null)
            {
                return;
            }

            List<StylePropertyName> names = new List<StylePropertyName>(properties.Length);
            List<TimeValue> durations = new List<TimeValue>(properties.Length);
            List<EasingFunction> easings = new List<EasingFunction>(properties.Length);

            for (int index = 0; index < properties.Length; index++)
            {
                names.Add(new StylePropertyName(properties[index]));
                durations.Add(new TimeValue(ACTIVE_TRANSITION_DURATION, TimeUnit.Second));
                easings.Add(new EasingFunction(EasingMode.EaseInOutCubic));
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(names);
            element.style.transitionDuration = new StyleList<TimeValue>(durations);
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(easings);
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

        void OnTrackPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            Refresh();
        }

        void OnTrackPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            Refresh();
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
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

        float TrackWidth => _theme != null ? _theme.InputHeight * TRACK_WIDTH_FACTOR : 0f;

        float HandleSize => _theme != null ? _theme.InputHeight - HANDLE_INSET * 2f : 0f;

        // Stretches 4px toward the center while dragging (the outer edge stays fixed).
        float HandleTweakingWidth => _theme != null ? _theme.InputHeight - HANDLE_INSET : 0f;

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            UpdateTrack();
            UpdateHandle();
            UpdateLabelColor();
            UpdateOverlay();

            _ring?.MarkDirtyRepaint();
        }

        void UpdateTrack()
        {
            if (_track == null)
            {
                return;
            }

            if (_value)
            {
                _track.style.backgroundColor = _hovered ? _theme.AccentHover : _theme.Accent;
            }
            else
            {
                _track.style.backgroundColor = _hovered ? _theme.InputHover : _theme.Input;
            }
        }

        void UpdateHandle()
        {
            if (_handle == null)
            {
                return;
            }

            bool tweaking = _gesture != null && _gesture.Dragging;
            float width = tweaking ? HandleTweakingWidth : HandleSize;

            // The on side grows while keeping its outer edge fixed to the right end (track width - inset).
            float left = _value ? TrackWidth - HANDLE_INSET - width : HANDLE_INSET;

            _handle.style.width = width;
            _handle.style.left = left;
            _handle.style.backgroundColor = _value ? _theme.Background : _theme.TextSubtle;
        }

        void UpdateLabelColor()
        {
            if (_labelElement == null)
            {
                return;
            }

            _labelElement.style.color = _theme.Text;
        }

        void UpdateOverlay()
        {
            if (_gesture == null || _track == null)
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
                _track.hierarchy.Add(_overlay);
                return;
            }

            _overlay.Sync(_theme, _gesture.PreviewValue, _theme.InputHeight);
        }

        #endregion

        #region Painting

        // Spec §2: equivalent to :focus, so the ring is shown even when focus came from a click.
        void OnGenerateRingContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null || !_focused || _track == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            float width = _track.layout.width;
            float height = _track.layout.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return;
            }

            // Vue puts a 1px border on an element inset by -3px, so the line's center sits at -2.5px.
            float offset = FOCUS_RING_INSET - FOCUS_RING_WIDTH * 0.5f;
            Rect ring = new Rect(
                -offset,
                -offset,
                width + offset * 2f,
                height + offset * 2f);

            painter.strokeColor = _theme.Accent;
            painter.lineWidth = FOCUS_RING_WIDTH;
            painter.lineCap = LineCap.Butt;
            TracePill(painter, ring);
            painter.Stroke();
        }

        static void TracePill(Painter2D painter, Rect rect)
        {
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (radius <= 0f)
            {
                return;
            }

            float centerY = rect.yMin + rect.height * 0.5f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin + radius, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax - radius, rect.yMin));
            painter.Arc(
                new Vector2(rect.xMax - radius, centerY),
                radius,
                new Angle(-90f, AngleUnit.Degree),
                new Angle(90f, AngleUnit.Degree));
            painter.LineTo(new Vector2(rect.xMin + radius, rect.yMax));
            painter.Arc(
                new Vector2(rect.xMin + radius, centerY),
                radius,
                new Angle(90f, AngleUnit.Degree),
                new Angle(270f, AngleUnit.Degree));
            painter.ClosePath();
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
