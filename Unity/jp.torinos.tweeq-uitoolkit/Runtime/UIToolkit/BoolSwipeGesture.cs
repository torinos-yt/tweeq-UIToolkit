using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// The swipe-toggle state machine shared by CheckboxInput / SwitchInput (spec "Common: swipe toggle").
    ///
    /// Reason composition was chosen over inheritance: what the two widgets share is only "how to translate
    /// pointer/key input into a bool change"; the appearance (rounded box vs. pill) and child element composition
    /// are completely different. A shared base VisualElement would end up as a layout-less abstract class lined
    /// with nothing but rendering hooks, making both widgets harder to read.
    /// </summary>
    sealed class BoolSwipeGesture
    {
        #region Constants

        const float MOUSE_DRAG_THRESHOLD = 3f;
        const float TOUCH_DRAG_THRESHOLD = 5f;

        // Even without reaching the threshold, a 0.2s hold enters drag (spec "Common")
        const long HOLD_DRAG_DELAY_MS = 200;

        // Dead zone for the preview value. Same value as Vue's tweakThreshold, 3px regardless of pointer type
        const float PREVIEW_DEAD_ZONE = 3f;

        #endregion

        #region Fields

        readonly VisualElement _owner;

        int _pointerId = PointerId.invalidPointerId;
        bool _pointerDown;
        bool _dragging;
        float _dragThreshold = MOUSE_DRAG_THRESHOLD;
        Vector2 _pressPosition;
        Vector2 _pointerPosition;
        bool _valueOnDragStart;
        bool _previewValue;
        IVisualElementScheduledItem _holdItem;

        #endregion

        #region Public API

        /// <summary>Source for reading the current value. Treated as false when unset.</summary>
        public Func<bool> ValueGetter { get; set; }

        /// <summary>Called every time the preview value changes (i.e. reflects immediately even mid-drag. Spec "Common").</summary>
        public Action<bool> ValueChanged { get; set; }

        /// <summary>Called exactly once per click / release / key input.</summary>
        public Action<bool> Confirmed { get; set; }

        /// <summary>Redraw hook fired when the pressed/dragging state changes.</summary>
        public Action StateChanged { get; set; }

        /// <summary>Whether operation is rejected. Always false for SwitchInput (spec section 2: no disabled).</summary>
        public bool Disabled { get; set; }

        /// <summary>Whether currently dragging past the threshold.</summary>
        public bool Dragging => _dragging;

        /// <summary>Whether currently pressed (including below the threshold).</summary>
        public bool Pressed => _pointerDown;

        /// <summary>The preview value while dragging. Meaningless when <see cref="Dragging"/> is false.</summary>
        public bool PreviewValue => _previewValue;

        public BoolSwipeGesture(VisualElement owner)
        {
            _owner = owner;
            if (_owner == null)
            {
                return;
            }

            _owner.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _owner.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _owner.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _owner.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            _owner.RegisterCallback<KeyDownEvent>(OnKeyDown);
            _owner.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        /// <summary>
        /// Aborts an in-progress operation. Confirmed does not fire.
        /// Call this in situations like becoming Disabled, where "there is no longer a way to release."
        /// </summary>
        public void Cancel()
        {
            if (!_pointerDown && !_dragging)
            {
                return;
            }

            int pointerId = _pointerId;
            ResetState();
            ReleasePointerSafely(pointerId);
            StateChanged?.Invoke();
        }

        #endregion

        #region Pointer

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _pointerDown || Disabled || _owner == null)
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
            _pointerPosition = _pressPosition;

            // Take focus so keyboard shortcuts (T/F/Space...) can be received.
            // The Vue version also calls input.focus() in onClick / onDragEnd
            _owner.Focus();

            if (_owner.panel != null)
            {
                _owner.CapturePointer(_pointerId);
                _holdItem = _owner.schedule.Execute(OnHoldElapsed).StartingIn(HOLD_DRAG_DELAY_MS);
            }

            evt.StopPropagation();
            StateChanged?.Invoke();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || !_pointerDown || evt.pointerId != _pointerId)
            {
                return;
            }

            _pointerPosition = LocalPosition(evt);

            if (!_dragging)
            {
                if (Vector2.Distance(_pointerPosition, _pressPosition) < _dragThreshold)
                {
                    return;
                }

                BeginDrag();
                evt.StopPropagation();
                return;
            }

            UpdatePreview();
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

            // Tear down state before releasing, so the PointerCaptureOutEvent fired by the release is a no-op
            ResetState();
            ReleasePointerSafely(pointerId);
            StateChanged?.Invoke();

            if (wasDragging)
            {
                // The value while dragging was already reflected via preview, so here we only confirm
                Confirmed?.Invoke(CurrentValue);
            }
            else
            {
                // Released below the threshold = a click -> flip the value
                bool next = !CurrentValue;
                ValueChanged?.Invoke(next);
                Confirmed?.Invoke(next);
            }

            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!_pointerDown && !_dragging)
            {
                return;
            }

            bool wasDragging = _dragging;
            ResetState();
            StateChanged?.Invoke();

            // Even if capture is taken away, the dragging value has already been notified outward.
            // Skipping confirmation would leave a "changed but never committed" state, so confirm here too
            if (wasDragging)
            {
                Confirmed?.Invoke(CurrentValue);
            }
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // The whole element is going away, so there's no listener left either. Just tear down state (Confirmed does not fire)
            ResetState();
        }

        void OnHoldElapsed()
        {
            if (!_pointerDown || _dragging || Disabled)
            {
                return;
            }

            BeginDrag();
        }

        #endregion

        #region Keyboard

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || Disabled)
            {
                return;
            }

            if (!TryResolveKey(evt.keyCode, CurrentValue, out bool next))
            {
                return;
            }

            // Avoid also triggering an app-side shortcut bound to the same key (same call as Vue)
            evt.StopPropagation();

            ValueChanged?.Invoke(next);
            Confirmed?.Invoke(next);
        }

        // Spec "Common": T/Y/1/P -> true, F/N/0/M -> false, Space -> toggle
        static bool TryResolveKey(KeyCode keyCode, bool current, out bool next)
        {
            switch (keyCode)
            {
                case KeyCode.T:
                case KeyCode.Y:
                case KeyCode.P:
                case KeyCode.Alpha1:
                case KeyCode.Keypad1:
                    next = true;
                    return true;

                case KeyCode.F:
                case KeyCode.N:
                case KeyCode.M:
                case KeyCode.Alpha0:
                case KeyCode.Keypad0:
                    next = false;
                    return true;

                case KeyCode.Space:
                    next = !current;
                    return true;
            }

            next = current;
            return false;
        }

        #endregion

        #region Drag session

        void BeginDrag()
        {
            _dragging = true;
            _valueOnDragStart = CurrentValue;
            StopHoldTimer();

            // Right after starting, dx≈0, i.e. within the dead zone, so the preview is always the inverse of the start value.
            // The first change notification fires at the same timing as Vue's watch on tweakingValue (null -> value)
            UpdatePreview();
            StateChanged?.Invoke();
        }

        void UpdatePreview()
        {
            float dx = _pointerPosition.x - _pressPosition.x;

            bool preview = Mathf.Abs(dx) <= PREVIEW_DEAD_ZONE
                ? !_valueOnDragStart
                : dx > 0f;

            _previewValue = preview;

            // Notify only when the value changes (same behavior as Vue's watch on tweakingValue).
            // We compare against the actual value rather than the previous preview so that, even if an external
            // party rejects the value, "the value shown == the value notified" is preserved
            if (preview == CurrentValue)
            {
                StateChanged?.Invoke();
                return;
            }

            ValueChanged?.Invoke(preview);
            StateChanged?.Invoke();
        }

        void ResetState()
        {
            _pointerDown = false;
            _dragging = false;
            _pointerId = PointerId.invalidPointerId;
            StopHoldTimer();
        }

        void StopHoldTimer()
        {
            if (_holdItem == null)
            {
                return;
            }

            _holdItem.Pause();
            _holdItem = null;
        }

        void ReleasePointerSafely(int pointerId)
        {
            if (_owner == null || _owner.panel == null || pointerId == PointerId.invalidPointerId)
            {
                return;
            }

            if (_owner.HasPointerCapture(pointerId))
            {
                _owner.ReleasePointer(pointerId);
            }
        }

        bool CurrentValue => ValueGetter != null && ValueGetter();

        // Convert from panel coordinates to local so the coordinate system stays consistent even while capturing
        Vector2 LocalPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            Vector2 panelPosition = new Vector2(position.x, position.y);
            return _owner == null ? panelPosition : _owner.WorldToLocal(panelPosition);
        }

        #endregion
    }
}
