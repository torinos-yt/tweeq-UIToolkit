using System;
using System.Collections.Generic;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A circular angle scrubber. The value is in degrees, and multi-turn rotation lets it hold values beyond +/-360.
    /// Has a relative mode (default) and an absolute mode (hovering the needle side, or the A key).
    /// </summary>
    [UxmlElement]
    public partial class RotaryInput : VisualElement, INotifyValueChanged<float>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        const float DEFAULT_SIZE = 24f;

        // The Vue version and another reference implementation both leave the knob's appearance unchanged
        // when disabled, only turning off the functionality. But with the neighboring NumberInput dimming
        // while only the knob still looks alive, that's an accident waiting to happen in a live performance
        // setting. Dimming it the same way ButtonInput does is adopted as an intentional deviation
        // (ruling in m7-disabled-invalid-spec.md).
        const float DISABLED_OPACITY = 0.4f;
        const float MOUSE_DRAG_THRESHOLD = 3f;
        const float TOUCH_DRAG_THRESHOLD = 5f;

        // Swells to 1.8x while hovered/dragging (Vue's transform: scale(1.8)).
        const float HOVER_SCALE = 1.8f;

        // The focus ring is a 24px box with an inset of -3px, i.e. a 30px diameter.
        const float FOCUS_RING_INSET = 3f;
        const float FOCUS_RING_WIDTH = 1f;

        // The snap-ring band (same values as another reference implementation's SNAP_INNER_RADIUS_FACTOR / SNAP_OUTER_RADIUS).
        const float SNAP_RING_INNER_FACTOR = 4f;
        const float SNAP_RING_OUTER_RADIUS = 160f;

        // The amount by which concentric circles are offset per turn (24 * 0.25 = 6px).
        const float ARC_RADIUS_STEP_FACTOR = 0.25f;
        const float MIN_ARC_RADIUS = 8f;

        // Upper bound so the draw loop doesn't blow up even if the value is broken.
        const int MAX_METER_LINES = 720;
        const int MAX_TURN_CIRCLES = 64;

        const double FINE_SCALE = 0.1;

        // The Vue version defaults angleOffset to -90 (value 0 points straight up). Since the API contract
        // defaults AngleOffset to 0 instead, the "0deg = straight up" baseline is absorbed here, and the
        // same value is applied to both drawing and absolute-mode calculations.
        const double UP_ANGLE_OFFSET = -90.0;

        // The Vue version's tip path (viewBox 32, center 16) = radius ratios of 4/16 and 14/16.
        const float INDICATOR_INNER_RATIO = 0.25f;
        const float INDICATOR_OUTER_RATIO = 0.875f;
        const float INDICATOR_WIDTH = 3f;

        // Near the center, which side of the needle you're on becomes unstable, so absolute-mode detection is disabled there.
        const float ABSOLUTE_DEAD_ZONE_RATIO = 0.4375f;

        // Near the center the direction vector goes erratic, so angle isn't taken from a vector shorter than this length (squared).
        const float MIN_VECTOR_SQR_LENGTH = 1f;

        #endregion

        #region Fields

        float _value;

        // The raw accumulated angle before snapping. Snapping is only applied on the output side and never left in here.
        double _local;

        double _snap = 45.0;
        double _step;
        double _angleOffset;
        bool _disabled;
        TweeqTheme _theme = TweeqTheme.Dark();

        // The layer that scales. Drawing is split out so the focus ring doesn't get caught up in it.
        VisualElement _knob;
        TweakOverlay _overlay;

        int _pointerId = PointerId.invalidPointerId;
        bool _pointerDown;
        bool _dragging;
        Vector2 _pressPosition;
        Vector2 _previousPosition;
        Vector2 _originPanelPosition;
        Vector2 _pointerPanelPosition;
        float _valueOnDragStart;
        float _dragThreshold = MOUSE_DRAG_THRESHOLD;
        float _pointerDistance;

        bool _absoluteKeyHeld;
        bool _relativeKeyHeld;
        bool _absoluteKeyWasLast;
        bool _snapKeyHeld;
        bool _shiftHeld;
        bool _altHeld;

        bool _modeByPointer;
        bool _cursorHidden;

        bool _hovered;
        bool _focused;

        #endregion

        #region Public API

        /// <summary>Fires when a drag is confirmed (when the pointer is released).</summary>
        public event Action<float> Confirmed;

        /// <summary>The current angle (degrees).</summary>
        [UxmlAttribute]
        public float value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                float previous = _value;
                SetValueWithoutNotify(value);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>The snap angle (degrees). Default 45.</summary>
        [UxmlAttribute]
        public double Snap
        {
            get => _snap;
            set
            {
                _snap = value;
                Refresh();
            }
        }

        /// <summary>The quantization step for the output. Disabled at 0 or below.</summary>
        [UxmlAttribute]
        public double Step
        {
            get => _step;
            set
            {
                _step = value;
                Refresh();
            }
        }

        /// <summary>The indicator's angle offset (degrees). Default 0 (0deg is straight up).</summary>
        [UxmlAttribute]
        public double AngleOffset
        {
            get => _angleOffset;
            set
            {
                _angleOffset = value;
                Refresh();
            }
        }

        /// <summary>
        /// Non-interactive state. If set while dragging, the gesture is discarded and the value reverts to the start value.
        /// </summary>
        [UxmlAttribute]
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

                if (_disabled && (_pointerDown || _dragging))
                {
                    // If a drag is still alive at the moment of disabling, there would be no way left to
                    // release it, i.e. no way left to recover the hidden cursor.
                    CancelDrag();
                }

                ApplyInteractivity();
                UpdateVisualState();
            }
        }

        /// <summary>The color theme. Falls back to Dark() when null is passed.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyKnobTransition();
                Refresh();
            }
        }

        /// <summary>
        /// Position within a horizontal group. Since the knob is circular and has no corners to collapse, this is a no-op that only retains the value (spec §5-1).
        /// </summary>
        public TweeqBoxPosition InlinePosition { get; set; } = TweeqBoxPosition.None;

        /// <summary>Position within a vertical group. A no-op, same as InlinePosition.</summary>
        public TweeqBoxPosition BlockPosition { get; set; } = TweeqBoxPosition.None;

        /// <summary>Whether a drag session is in progress.</summary>
        public bool Dragging => _dragging;

        /// <summary>
        /// Begins a drag session (panel-independent). Real operation goes through pointer events, but
        /// this is left open for external drivers and tests (same setup as TranslateInput).
        /// </summary>
        /// <remarks>
        /// Since no pointer coordinate is involved, absolute mode's pull-to-position never happens (equivalent to relative mode).
        /// </remarks>
        public void BeginRotaryDrag()
        {
            if (_disabled || _dragging)
            {
                return;
            }

            _dragging = true;
            _valueOnDragStart = _value;
            _local = _value;

            HideCursor();
            AcquireOverlay();
            UpdateVisualState();
        }

        /// <summary>Applies an angle increment (degrees) during a drag.</summary>
        public void UpdateRotaryDrag(double deltaDegrees)
        {
            if (!_dragging)
            {
                return;
            }

            ApplyDelta(deltaDegrees);
        }

        /// <summary>Confirms and ends the drag. <see cref="Confirmed"/> fires exactly once.</summary>
        public void EndRotaryDrag()
        {
            if (!_dragging)
            {
                return;
            }

            int pointerId = _pointerId;
            ResetDragState();
            ReleasePointerSafely(pointerId);
            UpdateVisualState();
            Confirmed?.Invoke(_value);
        }

        /// <summary>Discards the drag and reverts to the start value (equivalent to Escape). <see cref="Confirmed"/> does not fire.</summary>
        public void CancelRotaryDrag()
        {
            if (!_dragging)
            {
                return;
            }

            CancelDrag();
        }

        /// <summary>Sets the value without firing ChangeEvent. The accumulated angle is also synced.</summary>
        public void SetValueWithoutNotify(float newValue)
        {
            _value = newValue;

            // An external set happens outside any drag session, so the raw accumulator is kept in sync too.
            _local = newValue;
            Refresh();
        }

        #endregion

        #region Construction

        public RotaryInput()
        {
            this.focusable = true;
            this.style.width = DEFAULT_SIZE;
            this.style.height = DEFAULT_SIZE;
            this.style.flexShrink = 0f;

            // So the knob swollen to 1.8x and the focus ring aren't clipped.
            this.style.overflow = Overflow.Visible;

            _knob = new VisualElement
            {
                name = "tweeq-rotary-knob",

                // Hit testing is consolidated on the outer (non-scaling) layer.
                pickingMode = PickingMode.Ignore,
            };
            _knob.style.position = Position.Absolute;
            _knob.style.left = 0f;
            _knob.style.top = 0f;
            _knob.style.right = 0f;
            _knob.style.bottom = 0f;
            _knob.style.overflow = Overflow.Visible;
            _knob.generateVisualContent += OnGenerateKnobContent;
            this.hierarchy.Add(_knob);

            ApplyKnobTransition();
            ApplyKnobScale();

            this.generateVisualContent += OnGenerateVisualContent;

            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            this.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            this.RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.RegisterCallback<KeyUpEvent>(OnKeyUp);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        #endregion

        #region Pointer

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _pointerDown || _disabled)
            {
                return;
            }

            _pointerDown = true;
            _dragging = false;
            _pointerId = evt.pointerId;
            _dragThreshold = evt.pointerType == UnityEngine.UIElements.PointerType.mouse
                ? MOUSE_DRAG_THRESHOLD
                : TOUCH_DRAG_THRESHOLD;

            _pressPosition = LocalPosition(evt);
            _previousPosition = _pressPosition;
            _originPanelPosition = PanelPosition(evt);
            _pointerPanelPosition = _originPanelPosition;
            _pointerDistance = Vector2.Distance(_pressPosition, Center());
            _valueOnDragStart = _value;
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            // The mode is fixed at drag start, so it's decided exactly once, from the position at the moment of the press.
            UpdateModeByPointer(_pressPosition);

            // Takes focus in order to receive KeyDown/KeyUp (Q/A/R/Escape).
            this.Focus();

            if (this.panel != null)
            {
                this.CapturePointer(_pointerId);
            }

            evt.StopPropagation();
            Refresh();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (!_pointerDown)
            {
                // Mode detection is only updated while hovered (frozen during a drag).
                UpdateModeByPointer(LocalPosition(evt));
                return;
            }

            if (evt.pointerId != _pointerId)
            {
                return;
            }

            Vector2 position = LocalPosition(evt);
            Vector2 center = Center();
            _pointerPanelPosition = PanelPosition(evt);
            _pointerDistance = Vector2.Distance(position, center);
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            if (!_dragging)
            {
                UpdateModeByPointer(position);

                if (Vector2.Distance(position, _pressPosition) < _dragThreshold)
                {
                    return;
                }

                BeginDrag(position);
                evt.StopPropagation();
                return;
            }

            double delta = ComputeDelta(_previousPosition, position, center);
            _previousPosition = position;
            ApplyDelta(delta);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_pointerDown || evt.pointerId != _pointerId)
            {
                return;
            }

            bool wasDragging = _dragging;
            int pointerId = _pointerId;
            ResetDragState();
            ReleasePointerSafely(pointerId);

            if (wasDragging)
            {
                Confirmed?.Invoke(_value);
            }

            evt.StopPropagation();
            UpdateVisualState();
        }

        // Never leaves the drag state (i.e. the hidden cursor and overlay) stranded even if capture is lost.
        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!_pointerDown && !_dragging)
            {
                return;
            }

            ResetDragState();
            UpdateVisualState();
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            UpdateVisualState();

            if (evt != null)
            {
                UpdateModeByPointer(LocalPosition(evt));
            }
        }

        void OnPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;

            if (!_dragging)
            {
                _modeByPointer = false;
            }

            UpdateVisualState();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // Never leaves the cursor and overlay stranded even after detaching from the panel.
            ResetDragState();
        }

        #endregion

        #region Keyboard

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            switch (evt.keyCode)
            {
                case KeyCode.A:
                    _absoluteKeyHeld = true;
                    _absoluteKeyWasLast = true;
                    evt.StopPropagation();
                    break;
                case KeyCode.R:
                    _relativeKeyHeld = true;
                    _absoluteKeyWasLast = false;
                    evt.StopPropagation();
                    break;
                case KeyCode.Q:
                    _snapKeyHeld = true;
                    evt.StopPropagation();
                    break;
                case KeyCode.Escape:
                    if (_pointerDown || _dragging)
                    {
                        CancelDrag();
                        evt.StopPropagation();
                    }

                    break;
            }

            if (_dragging)
            {
                // Toggling snap/mode reflects into the output immediately (the accumulated angle isn't touched).
                ApplyDelta(0.0);
            }

            Refresh();
        }

        void OnKeyUp(KeyUpEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            switch (evt.keyCode)
            {
                case KeyCode.A:
                    _absoluteKeyHeld = false;
                    evt.StopPropagation();
                    break;
                case KeyCode.R:
                    _relativeKeyHeld = false;
                    evt.StopPropagation();
                    break;
                case KeyCode.Q:
                    _snapKeyHeld = false;
                    evt.StopPropagation();
                    break;
            }

            if (_dragging)
            {
                ApplyDelta(0.0);
            }

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
            _absoluteKeyHeld = false;
            _relativeKeyHeld = false;
            _snapKeyHeld = false;
            Refresh();
        }

        #endregion

        #region Drag session

        void BeginDrag(Vector2 position)
        {
            _dragging = true;
            _previousPosition = position;
            _valueOnDragStart = _value;
            _local = _value;

            HideCursor();
            AcquireOverlay();

            // If grabbed in absolute mode, it's immediately pulled to the pointer's angle (equivalent to the Vue version's onDragStart).
            if (AbsoluteMode)
            {
                ApplyDelta(AbsoluteDelta(position, Center()));
            }
            else
            {
                ApplyDelta(0.0);
            }

            UpdateVisualState();
        }

        void CancelDrag()
        {
            int pointerId = _pointerId;
            float restored = _valueOnDragStart;
            ResetDragState();
            ReleasePointerSafely(pointerId);

            // The value notified during the drag is being rolled back, so notify this too.
            this.value = restored;
            UpdateVisualState();
        }

        void ResetDragState()
        {
            _pointerDown = false;
            _dragging = false;
            _pointerId = PointerId.invalidPointerId;
            RestoreCursor();
            ReleaseOverlay();
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

        double ComputeDelta(Vector2 previous, Vector2 current, Vector2 center)
        {
            if (AbsoluteMode)
            {
                return AbsoluteDelta(current, center);
            }

            Vector2 previousVector = previous - center;
            Vector2 currentVector = current - center;
            if (previousVector.sqrMagnitude <= MIN_VECTOR_SQR_LENGTH
                || currentVector.sqrMagnitude <= MIN_VECTOR_SQR_LENGTH)
            {
                return 0.0;
            }

            // Accumulates the signed angle between the previous and current frame's vectors, so multi-turn rotation is handled naturally.
            return TweeqMath.SignedAngleBetween(ScreenAngle(currentVector), ScreenAngle(previousVector));
        }

        double AbsoluteDelta(Vector2 position, Vector2 center)
        {
            Vector2 vector = position - center;
            if (vector.sqrMagnitude <= MIN_VECTOR_SQR_LENGTH)
            {
                return 0.0;
            }

            double target = ScreenAngle(vector) - _angleOffset - UP_ANGLE_OFFSET;

            // Using the raw accumulated value as the reference rather than the snapped output keeps snapping from leaking into the accumulator.
            return TweeqMath.SignedAngleBetween(target, _local);
        }

        void ApplyDelta(double delta)
        {
            if (_altHeld)
            {
                delta *= FINE_SCALE;
            }

            var result = RotaryLogic.GetDragValue(_local, delta, _snap, ShouldSnap);
            _local = result.local;

            float next = (float)TweeqMath.Quantize(result.output, _step, 0.0);
            if (next == _value)
            {
                Refresh();
                return;
            }

            float previous = _value;
            _value = next;
            Refresh();
            NotifyValueChanged(previous, next);
        }

        void NotifyValueChanged(float previous, float current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<float> changeEvent = ChangeEvent<float>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Mode

        /// <summary>Whether it's "swollen" from hover/drag.</summary>
        bool Active => _hovered || _dragging;

        bool AbsoluteMode
        {
            get
            {
                // While A/R is held, the key wins (whichever of the two was pressed most recently, if both).
                // If neither is held, it's left to the mode derived from pointer position.
                if (_absoluteKeyHeld && _relativeKeyHeld)
                {
                    return _absoluteKeyWasLast;
                }

                if (_absoluteKeyHeld || _relativeKeyHeld)
                {
                    return _absoluteKeyHeld;
                }

                return _modeByPointer;
            }
        }

        bool ShouldSnap
        {
            get
            {
                if (_shiftHeld || _snapKeyHeld)
                {
                    return true;
                }

                float inner = _theme != null ? _theme.InputHeight * SNAP_RING_INNER_FACTOR : 0f;
                return _dragging
                    && inner <= _pointerDistance
                    && _pointerDistance <= SNAP_RING_OUTER_RADIUS;
            }
        }

        // Entering the half-circle wedge the needle points toward (outside the dead zone) switches to absolute mode.
        // While dragging, this is never updated at all, to preserve the mode fixed at the start.
        void UpdateModeByPointer(Vector2 localPosition)
        {
            if (_dragging)
            {
                return;
            }

            bool absolute = false;
            Vector2 offset = localPosition - Center();
            float distance = offset.magnitude;
            float radius = KnobVisualRadius();

            if (radius > 0f && distance <= radius && distance > radius * ABSOLUTE_DEAD_ZONE_RATIO)
            {
                Vector2 tipDirection = AngleDirection(DisplayAngle());
                absolute = Vector2.Dot(offset, tipDirection) > 0f;
            }

            if (absolute == _modeByPointer)
            {
                return;
            }

            _modeByPointer = absolute;
            Refresh();
        }

        #endregion

        #region Cursor

        void HideCursor()
        {
            if (_cursorHidden)
            {
                return;
            }

            _cursorHidden = true;
            UnityEngine.Cursor.visible = false;
        }

        void RestoreCursor()
        {
            if (!_cursorHidden)
            {
                return;
            }

            _cursorHidden = false;
            UnityEngine.Cursor.visible = true;
        }

        #endregion

        #region Overlay

        void AcquireOverlay()
        {
            if (_overlay != null)
            {
                return;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(this);
            if (layer == null)
            {
                // Give up on the guide if no panel is attached (the operation itself still proceeds).
                return;
            }

            _overlay = new TweakOverlay();
            layer.Add(_overlay);
        }

        void ReleaseOverlay()
        {
            if (_overlay == null)
            {
                return;
            }

            _overlay.RemoveFromHierarchy();
            _overlay = null;
        }

        void UpdateOverlay()
        {
            if (_overlay == null)
            {
                return;
            }

            if (!_dragging || _theme == null)
            {
                ReleaseOverlay();
                return;
            }

            double offset = _angleOffset + UP_ANGLE_OFFSET;
            TweakOverlayState state = new TweakOverlayState
            {
                Theme = _theme,
                Center = this.worldBound.center,
                Origin = _originPanelPosition,
                Pointer = _pointerPanelPosition,
                StartAngle = _valueOnDragStart + offset,

                // Vue uses model.value (the snapped, quantized output) for the arc's end point.
                // Passing the raw accumulated angle _local would make it slip and swirl even while snapping.
                CurrentAngle = _value + offset,
                ValueAngle = _value + offset,
                Value = _value,
                Snap = _snap,
                AngleOffset = offset,
                Absolute = AbsoluteMode,
                DoSnap = ShouldSnap,
            };

            _overlay.Sync(in state);
        }

        #endregion

        #region Geometry

        Vector2 Center()
        {
            Rect rect = this.contentRect;
            return rect.center;
        }

        float KnobRadius()
        {
            Rect rect = this.contentRect;
            return Mathf.Min(rect.width, rect.height) * 0.5f;
        }

        // The visual radius. Detection is performed against the post-scale circle.
        float KnobVisualRadius()
        {
            // Uses the interpolated value mid-animation rather than the target scale (1.8).
            // Judging by the target value would treat even a position right at the knob's outer edge
            // as absolute mode right when hovering starts (while it's still small), making the scale
            // appear to begin with the dark accentSoft.
            float scale = Active ? HOVER_SCALE : 1f;
            if (_knob != null)
            {
                Vector3 resolved = _knob.resolvedStyle.scale.value;
                if (!float.IsNaN(resolved.x) && resolved.x > 0f)
                {
                    scale = resolved.x;
                }
            }

            return KnobRadius() * scale;
        }

        // Converts from panel coordinates to local so the coordinate system doesn't drift during capture either.
        Vector2 LocalPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return this.WorldToLocal(new Vector2(position.x, position.y));
        }

        // The overlay is drawn in panel coordinates, so the raw, untransformed position is kept too.
        static Vector2 PanelPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return new Vector2(position.x, position.y);
        }

        // Screen coordinates have y pointing down, so clockwise ends up being positive.
        static double ScreenAngle(Vector2 vector)
        {
            return Mathf.Rad2Deg * Mathf.Atan2(vector.y, vector.x);
        }

        static Vector2 AngleDirection(double degrees)
        {
            float radians = Mathf.Deg2Rad * (float)degrees;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        double DisplayAngle()
        {
            return _value + _angleOffset + UP_ANGLE_OFFSET;
        }

        // Tolerance so float rounding error doesn't cause a "value exactly at the snap angle" to be missed.
        static bool NearlyMultiple(double value, double step)
        {
            if (!TweeqMath.IsFinite(value) || !TweeqMath.IsFinite(step) || step == 0.0)
            {
                return false;
            }

            double snapped = Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
            double tolerance = Math.Max(1e-3, Math.Abs(value) * 1e-5);
            return Math.Abs(snapped - value) <= tolerance;
        }

        #endregion

        #region Knob presentation

        void ApplyKnobTransition()
        {
            if (_knob == null)
            {
                return;
            }

            float duration = _theme != null ? _theme.HoverTransitionDuration : 0.15f;

            // Vue uses cubic-bezier(0.4, 0, 0.2, 1) (the Material standard). UI Toolkit's EasingMode has
            // no identical curve, so EaseInOutCubic, whose ramp-up and settle are the closest match, is used as an approximation.
            _knob.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("scale") });
            _knob.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(duration, TimeUnit.Second) });
            _knob.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.EaseInOutCubic) });
        }

        void ApplyKnobScale()
        {
            if (_knob == null)
            {
                return;
            }

            float scale = Active ? HOVER_SCALE : 1f;
            _knob.style.scale = new StyleScale(new Scale(new Vector3(scale, scale, 1f)));
        }

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            this.focusable = !_disabled;
            this.style.opacity = _disabled ? DISABLED_OPACITY : 1f;

            if (!_disabled)
            {
                return;
            }

            // The visual state is also cleared, so it doesn't stay swollen or keep the ring while dimmed.
            _focused = false;
            _hovered = false;
            _modeByPointer = false;
            _absoluteKeyHeld = false;
            _relativeKeyHeld = false;
            _snapKeyHeld = false;
        }

        void UpdateVisualState()
        {
            ApplyKnobScale();
            Refresh();
        }

        // The outer layer (focus ring) and the knob are separate layers, so both are always marked dirty.
        void Refresh()
        {
            this.MarkDirtyRepaint();

            if (_knob != null)
            {
                _knob.MarkDirtyRepaint();
            }

            UpdateOverlay();
        }

        #endregion

        #region Painting

        // The outer layer never scales. It only draws the spec §1 focus ring.
        void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null || !_focused)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            float radius = KnobRadius() + FOCUS_RING_INSET;
            if (radius <= 0f)
            {
                return;
            }

            painter.strokeColor = _theme.AccentHover;
            painter.lineWidth = FOCUS_RING_WIDTH;
            painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            painter.Arc(Center(), radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();
        }

        // The knob layer. Since scale applies to this element, drawing is always done in an unscaled 24px box.
        void OnGenerateKnobContent(MeshGenerationContext context)
        {
            if (context == null || _theme == null || _knob == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _knob.contentRect;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (radius <= 0f)
            {
                return;
            }

            Vector2 center = rect.center;
            bool absoluteHover = Active && AbsoluteMode;

            Color disc = _theme.Accent;
            if (absoluteHover)
            {
                disc = _theme.AccentSoft;
            }
            else if (Active || _focused)
            {
                disc = _theme.AccentHover;
            }

            painter.fillColor = disc;
            painter.BeginPath();
            painter.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Fill();

            Vector2 direction = AngleDirection(DisplayAngle());
            painter.strokeColor = absoluteHover ? _theme.AccentHover : _theme.Input;
            painter.lineWidth = INDICATOR_WIDTH;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(center + direction * (radius * INDICATOR_INNER_RATIO));
            painter.LineTo(center + direction * (radius * INDICATOR_OUTER_RATIO));
            painter.Stroke();
        }

        #endregion

        #region Tweak overlay

        /// <summary>Drawing parameters for the overlay that only lives during a drag. Angles include the drawing offset.</summary>
        struct TweakOverlayState
        {
            public TweeqTheme Theme;
            public Vector2 Center;
            public Vector2 Origin;
            public Vector2 Pointer;
            public double StartAngle;
            public double CurrentAngle;
            public double ValueAngle;
            public double Value;
            public double Snap;
            public double AngleOffset;
            public bool Absolute;
            public bool DoSnap;
        }

        /// <summary>
        /// The layer that draws the snap meter, multi-turn circles, arc + arrow, absolute guide line, and value label.
        /// All coordinates are in panel space (i.e. this element's local coordinates).
        /// </summary>
        sealed class TweakOverlay : VisualElement
        {
            #region Constants

            const float PILL_HEIGHT = 20f;
            const float PILL_PADDING = 8f;
            const float PILL_FONT_SIZE = 11f;
            const float CHEVRON_FONT_SIZE = 14f;
            const float CHEVRON_GAP = 4f;
            const float LABEL_EDGE_MARGIN = 40f;

            const float GUIDE_WIDTH = 2f;
            const float METER_WIDTH = 1f;
            const float METER_SNAP_WIDTH = 2f;

            // A sweep angle below this is treated as "hasn't moved yet" (degrees).
            const double MIN_ARC_SWEEP = 1e-4;

            // A filled triangle with a total length of 6px (tip 4 + tail 2) and a width of 6px.
            const float ARROW_TIP_OFFSET = 4f;
            const float ARROW_TAIL_OFFSET = 2f;
            const float ARROW_HALF_WIDTH = 3f;

            #endregion

            #region Fields

            TweakOverlayState _state;
            bool _hasState;

            VisualElement _labelRoot;
            VisualElement _arrowsRoot;
            VisualElement _pill;
            Label _valueLabel;
            Label _leftChevron;
            Label _rightChevron;
            Vector2 _labelPoint;

            // The theme whose font was applied most recently. Sync runs every frame during a drag,
            // so assignment of the managed value (FontDefinition) is restricted to only when the theme actually changes.
            TweeqTheme _fontTheme;

            // The angle display is in 0.1deg increments, so the string isn't rebuilt for frames that round to the same display.
            long _angleKeyRevolutions;
            double _angleKeyTenths;
            bool _hasAngleKey;

            #endregion

            #region Construction

            public TweakOverlay()
            {
                this.name = "tweeq-rotary-tweak-overlay";
                this.pickingMode = PickingMode.Ignore;
                this.style.position = Position.Absolute;
                this.style.left = 0f;
                this.style.top = 0f;
                this.style.right = 0f;
                this.style.bottom = 0f;
                this.style.overflow = Overflow.Visible;

                this.generateVisualContent += OnGenerateVisualContent;
                BuildLabel();
            }

            void BuildLabel()
            {
                _labelRoot = new VisualElement { pickingMode = PickingMode.Ignore };
                _labelRoot.style.position = Position.Absolute;
                _labelRoot.style.flexDirection = FlexDirection.Row;
                _labelRoot.style.alignItems = Align.Center;

                // Centering needs the actual resolved size, so it's repositioned once that size is finalized.
                _labelRoot.RegisterCallback<GeometryChangedEvent>(OnLabelGeometryChanged);

                _leftChevron = CreateChevron("<");
                _rightChevron = CreateChevron(">");

                _pill = new VisualElement { pickingMode = PickingMode.Ignore };
                _pill.style.height = PILL_HEIGHT;
                _pill.style.minWidth = PILL_HEIGHT;
                _pill.style.flexDirection = FlexDirection.Row;
                _pill.style.alignItems = Align.Center;
                _pill.style.justifyContent = Justify.Center;
                _pill.style.paddingLeft = PILL_PADDING;
                _pill.style.paddingRight = PILL_PADDING;
                _pill.style.flexShrink = 0f;
                SetBorderWidth(_pill, 1f);

                // With a fixed height, a "true pill" shape can be computed as radius = height/2.
                SetBorderRadius(_pill, PILL_HEIGHT * 0.5f);

                _valueLabel = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
                _valueLabel.style.fontSize = PILL_FONT_SIZE;
                _valueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                ClearMargin(_valueLabel);
                _pill.Add(_valueLabel);

                // Equivalent to Vue's .arrows: the pill itself stays horizontal, and only this layer rotates toward the knob-to-pointer direction.
                _arrowsRoot = new VisualElement { pickingMode = PickingMode.Ignore };
                _arrowsRoot.style.position = Position.Absolute;
                _arrowsRoot.style.left = 0f;
                _arrowsRoot.style.top = 0f;
                _arrowsRoot.style.right = 0f;
                _arrowsRoot.style.bottom = 0f;
                _arrowsRoot.style.overflow = Overflow.Visible;
                _arrowsRoot.Add(_leftChevron);
                _arrowsRoot.Add(_rightChevron);

                _labelRoot.Add(_pill);
                _labelRoot.Add(_arrowsRoot);
                this.Add(_labelRoot);
            }

            static Label CreateChevron(string text)
            {
                Label label = new Label(text) { pickingMode = PickingMode.Ignore };
                label.style.fontSize = CHEVRON_FONT_SIZE;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                ClearMargin(label);

                // Extends outward (left/right) of the pill. Equivalent to Vue's right:100% / left:100%.
                label.style.position = Position.Absolute;
                label.style.top = Length.Percent(50);
                label.style.translate = new StyleTranslate(new Translate(0f, Length.Percent(-50)));
                if (text == "<")
                {
                    label.style.right = Length.Percent(100);
                    label.style.marginRight = CHEVRON_GAP;
                }
                else
                {
                    label.style.left = Length.Percent(100);
                    label.style.marginLeft = CHEVRON_GAP;
                }

                return label;
            }

            #endregion

            #region Sync

            public void Sync(in TweakOverlayState state)
            {
                _state = state;
                _hasState = state.Theme != null;
                if (!_hasState)
                {
                    return;
                }

                ApplyLabelStyle(state.Theme);
                SyncValueLabel(state.Value);
                UpdateLabelTransform();
                this.MarkDirtyRepaint();
            }

            // Sync runs even on frames where the pointer isn't moving, so a string is only built when the display actually changes.
            void SyncValueLabel(double value)
            {
                bool cacheable = TweeqFormat.TryGetAngleDisplayKey(
                    value, out long revolutions, out double tenths);

                if (cacheable
                    && _hasAngleKey
                    && _angleKeyRevolutions == revolutions
                    && TweeqFormat.SameValueBits(_angleKeyTenths, tenths))
                {
                    return;
                }

                _valueLabel.text = TweeqFormat.FormatAngle(value);

                // Values near a rounding boundary or non-finite values can't be turned into a key, so force a rebuild on the next frame too.
                _hasAngleKey = cacheable;
                _angleKeyRevolutions = revolutions;
                _angleKeyTenths = tenths;
            }

            void ApplyLabelStyle(TweeqTheme theme)
            {
                _pill.style.backgroundColor = theme.SurfaceOpaque;
                SetBorderColor(_pill, theme.Border);
                _valueLabel.style.color = theme.Text;
                _leftChevron.style.color = theme.Accent;
                _rightChevron.style.color = theme.Accent;

                if (!ReferenceEquals(_fontTheme, theme))
                {
                    _fontTheme = theme;

                    // This is a field for reading the raw angle, so it uses the numeric font (the chevrons are symbols, so they stay on the UI default).
                    TweeqFonts.Apply(_valueLabel, theme.FontNumeric);
                }
            }

            void UpdateLabelTransform()
            {
                Rect bounds = this.contentRect;
                Vector2 target = _state.Pointer;

                if (bounds.width > LABEL_EDGE_MARGIN * 2f && bounds.height > LABEL_EDGE_MARGIN * 2f)
                {
                    Rect inner = new Rect(
                        bounds.xMin + LABEL_EDGE_MARGIN,
                        bounds.yMin + LABEL_EDGE_MARGIN,
                        bounds.width - LABEL_EDGE_MARGIN * 2f,
                        bounds.height - LABEL_EDGE_MARGIN * 2f);
                    target = ClampAlongRay(_state.Origin, target, inner);
                }

                _labelPoint = target;

                Vector2 pointerVector = _state.Pointer - _state.Center;
                if (pointerVector.sqrMagnitude > MIN_VECTOR_SQR_LENGTH)
                {
                    // Orienting it perpendicular to the knob-to-pointer direction makes it readable along the drag direction.
                    // Only the chevron layer rotates (the pill body stays horizontal, same as Vue).
                    float degrees = (float)(ScreenAngle(pointerVector) + 90.0);
                    _arrowsRoot.style.rotate = new StyleRotate(new Rotate(new Angle(degrees, AngleUnit.Degree)));
                }

                UpdateLabelPosition();
            }

            void OnLabelGeometryChanged(GeometryChangedEvent evt)
            {
                UpdateLabelPosition();
            }

            void UpdateLabelPosition()
            {
                if (_labelRoot == null)
                {
                    return;
                }

                float width = _labelRoot.resolvedStyle.width;
                float height = _labelRoot.resolvedStyle.height;
                _labelRoot.style.left = _labelPoint.x - width * 0.5f;
                _labelRoot.style.top = _labelPoint.y - height * 0.5f;
            }

            #endregion

            #region Painting

            void OnGenerateVisualContent(MeshGenerationContext context)
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

                PaintSnapMeter(painter, theme);

                if (_state.Absolute)
                {
                    PaintAbsoluteGuide(painter, theme);
                }
                else
                {
                    PaintRelativePath(painter, theme);
                }

                PaintActiveTick(painter, theme);
            }

            void PaintSnapMeter(Painter2D painter, TweeqTheme theme)
            {
                double snap = Math.Abs(_state.Snap);
                if (!TweeqMath.IsFinite(snap) || snap <= 0.0)
                {
                    return;
                }

                float inner = theme.InputHeight * SNAP_RING_INNER_FACTOR;
                float outer = SNAP_RING_OUTER_RADIUS;
                if (outer <= inner)
                {
                    return;
                }

                int count = (int)TweeqMath.Clamp(Math.Ceiling(360.0 / snap), 1.0, MAX_METER_LINES);

                painter.lineCap = LineCap.Butt;
                painter.lineWidth = _state.DoSnap ? METER_SNAP_WIDTH : METER_WIDTH;
                painter.strokeColor = _state.DoSnap ? theme.AccentSoftHover : theme.Border;
                painter.BeginPath();

                for (int index = 0; index < count; index++)
                {
                    Vector2 direction = AngleDirection(index * snap + _state.AngleOffset);
                    painter.MoveTo(_state.Center + direction * inner);
                    painter.LineTo(_state.Center + direction * outer);
                }

                painter.Stroke();
            }

            void PaintActiveTick(Painter2D painter, TweeqTheme theme)
            {
                if (!_state.DoSnap || !NearlyMultiple(_state.Value, _state.Snap))
                {
                    return;
                }

                float inner = theme.InputHeight * SNAP_RING_INNER_FACTOR;
                float outer = SNAP_RING_OUTER_RADIUS;
                if (outer <= inner)
                {
                    return;
                }

                Vector2 direction = AngleDirection(_state.ValueAngle);
                painter.lineCap = LineCap.Butt;
                painter.lineWidth = METER_SNAP_WIDTH;
                painter.strokeColor = theme.Accent;
                painter.BeginPath();
                painter.MoveTo(_state.Center + direction * inner);
                painter.LineTo(_state.Center + direction * outer);
                painter.Stroke();
            }

            void PaintAbsoluteGuide(Painter2D painter, TweeqTheme theme)
            {
                // The cursor is hidden, so this line stands in directly for the pointer.
                float innerRadius = theme.InputHeight;
                float distance = Mathf.Max(Vector2.Distance(_state.Pointer, _state.Center), innerRadius);
                Vector2 direction = AngleDirection(_state.ValueAngle);

                painter.lineCap = LineCap.Butt;
                painter.lineWidth = GUIDE_WIDTH;
                painter.strokeColor = theme.Accent;
                painter.BeginPath();
                painter.MoveTo(_state.Center + direction * innerRadius);
                painter.LineTo(_state.Center + direction * distance);
                painter.Stroke();
            }

            void PaintRelativePath(Painter2D painter, TweeqTheme theme)
            {
                double total = _state.CurrentAngle - _state.StartAngle;
                if (!TweeqMath.IsFinite(total))
                {
                    return;
                }

                float baseRadius = theme.InputHeight * SNAP_RING_INNER_FACTOR;
                float step = theme.InputHeight * ARC_RADIUS_STEP_FACTOR;
                float sign = total < 0.0 ? -1f : 1f;
                int turns = Mathf.Clamp((int)Math.Floor(Math.Abs(total) / 360.0), 0, MAX_TURN_CIRCLES);

                painter.lineCap = LineCap.Butt;
                painter.lineWidth = GUIDE_WIDTH;
                painter.strokeColor = theme.Accent;

                for (int index = 0; index < turns; index++)
                {
                    float radius = Mathf.Max(MIN_ARC_RADIUS, baseRadius + sign * index * step);
                    painter.BeginPath();
                    painter.Arc(
                        _state.Center,
                        radius,
                        new Angle(0f, AngleUnit.Degree),
                        new Angle(360f, AngleUnit.Degree));
                    painter.ClosePath();
                    painter.Stroke();
                }

                double remainder = total - turns * (double)sign * 360.0;
                float arcRadius = Mathf.Max(MIN_ARC_RADIUS, baseRadius + sign * turns * step);

                // With multi-turn rotation, the start angle can reach into the thousands of degrees. Since the
                // arc's shape is only ever determined by its remainder mod 360, this folds it down first.
                double startAngle = TweeqMath.UnsignedMod(_state.StartAngle, 360.0);
                double endAngle = startAngle + remainder;
                bool forward = remainder >= 0.0;

                // Passing a 0 sweep angle to Arc would be indistinguishable from a full circle, so no arc is drawn until movement actually starts.
                if (Math.Abs(remainder) > MIN_ARC_SWEEP)
                {
                    // UI Toolkit has y pointing down, so the direction of increasing angle is clockwise on screen.
                    painter.BeginPath();
                    painter.Arc(
                        _state.Center,
                        arcRadius,
                        new Angle((float)startAngle, AngleUnit.Degree),
                        new Angle((float)endAngle, AngleUnit.Degree),
                        forward ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
                    painter.Stroke();
                }

                PaintArrowHead(painter, theme, endAngle, arcRadius, forward);
            }

            void PaintArrowHead(Painter2D painter, TweeqTheme theme, double endAngle, float radius, bool forward)
            {
                Vector2 direction = AngleDirection(endAngle);
                Vector2 endPoint = _state.Center + direction * radius;

                // The arc's tangent is perpendicular to the radius vector. For the reverse direction, the orientation is flipped (same setup as another reference implementation).
                Vector2 tangent = forward
                    ? new Vector2(-direction.y, direction.x)
                    : new Vector2(direction.y, -direction.x);
                Vector2 normal = new Vector2(-tangent.y, tangent.x);

                Vector2 tip = endPoint + tangent * ARROW_TIP_OFFSET;
                Vector2 left = endPoint - tangent * ARROW_TAIL_OFFSET + normal * ARROW_HALF_WIDTH;
                Vector2 right = endPoint - tangent * ARROW_TAIL_OFFSET - normal * ARROW_HALF_WIDTH;

                painter.fillColor = theme.Accent;
                painter.BeginPath();
                painter.MoveTo(tip);
                painter.LineTo(left);
                painter.LineTo(right);
                painter.ClosePath();
                painter.Fill();
            }

            #endregion

            #region Helpers

            // Pulls the label back inward while keeping it on the "start point -> pointer" ray.
            static Vector2 ClampAlongRay(Vector2 origin, Vector2 target, Rect bounds)
            {
                if (bounds.Contains(target))
                {
                    return target;
                }

                Vector2 direction = target - origin;
                if (direction.sqrMagnitude <= Mathf.Epsilon)
                {
                    return ClampToRect(target, bounds);
                }

                float amount = 1f;
                if (direction.x > 0f)
                {
                    amount = Mathf.Min(amount, (bounds.xMax - origin.x) / direction.x);
                }
                else if (direction.x < 0f)
                {
                    amount = Mathf.Min(amount, (bounds.xMin - origin.x) / direction.x);
                }

                if (direction.y > 0f)
                {
                    amount = Mathf.Min(amount, (bounds.yMax - origin.y) / direction.y);
                }
                else if (direction.y < 0f)
                {
                    amount = Mathf.Min(amount, (bounds.yMin - origin.y) / direction.y);
                }

                return ClampToRect(origin + direction * Mathf.Clamp01(amount), bounds);
            }

            static Vector2 ClampToRect(Vector2 point, Rect bounds)
            {
                return new Vector2(
                    Mathf.Clamp(point.x, bounds.xMin, bounds.xMax),
                    Mathf.Clamp(point.y, bounds.yMin, bounds.yMax));
            }

            static void ClearMargin(VisualElement element)
            {
                element.style.marginLeft = 0f;
                element.style.marginRight = 0f;
                element.style.marginTop = 0f;
                element.style.marginBottom = 0f;
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

        #endregion
    }
}
