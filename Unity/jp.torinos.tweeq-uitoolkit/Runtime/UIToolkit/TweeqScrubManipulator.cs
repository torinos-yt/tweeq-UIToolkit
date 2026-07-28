using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// The movement delta for a single scrub frame, plus modifier keys.
    /// </summary>
    /// <remarks>
    /// The delta is relative to the last dispatch (in the target's local coordinates).
    /// The modifier keys are kept at a granularity that can be passed straight through to
    /// Tweeq.Core's <c>GestureModifiers</c>.
    /// </remarks>
    public readonly struct ScrubUpdate
    {
        /// <summary>The horizontal movement since the last dispatch (px).</summary>
        public readonly float DeltaX;

        /// <summary>The vertical movement since the last dispatch (px).</summary>
        public readonly float DeltaY;

        /// <summary>Whether Shift (fast) is held.</summary>
        public readonly bool Shift;

        /// <summary>Whether Alt (fine) is held.</summary>
        public readonly bool Alt;

        /// <summary>Creates an instance with all fields specified.</summary>
        public ScrubUpdate(float deltaX, float deltaY, bool shift, bool alt)
        {
            DeltaX = deltaX;
            DeltaY = deltaY;
            Shift = shift;
            Alt = alt;
        }
    }

    /// <summary>
    /// A Manipulator that holds only the pointer wiring for "press → scrub once past the threshold, else click".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Extracted from NumberInput's pointer wiring into a form reusable from an external asmdef
    /// (ext-custom-widgets-spec.md EXT-01-B). It holds no value math at all, so the caller passes
    /// <see cref="ScrubUpdated"/>'s movement delta into things like <c>TweakGesture</c> to build values.
    /// </para>
    /// <para>
    /// There are two cancellation entry points (KeyDown(Escape) on the target, and PointerCaptureOut).
    /// Catching Escape requires the target itself to hold focus, so moving focus there is the caller's
    /// responsibility.
    /// </para>
    /// </remarks>
    public sealed class TweeqScrubManipulator : PointerManipulator
    {
        #region Constants

        /// <summary>The movement (px) treated as a drag for mouse input.</summary>
        public const float MOUSE_DRAG_THRESHOLD = 3f;

        /// <summary>The movement (px) treated as a drag for touch/pen input. Looser than mouse, since raw finger jitter is larger.</summary>
        public const float TOUCH_DRAG_THRESHOLD = 5f;

        #endregion

        #region Fields

        int _pointerId = PointerId.invalidPointerId;
        bool _pointerDown;
        bool _scrubbing;
        bool _cursorHidden;
        float _dragThreshold = MOUSE_DRAG_THRESHOLD;
        Vector2 _pressPosition;
        Vector2 _previousPosition;
        bool _shiftHeld;
        bool _altHeld;

        #endregion

        #region Public API

        /// <summary>Whether to hide the OS cursor while scrubbing (default false).</summary>
        public bool HideCursorWhileScrubbing { get; set; }

        /// <summary>Whether currently scrubbing. Read by the rendering side to switch to a "grabbed" appearance.</summary>
        public bool IsScrubbing
        {
            get { return _scrubbing; }
        }

        /// <summary>Fires once, the moment the threshold is crossed and scrubbing begins.</summary>
        public event Action ScrubBegan;

        /// <summary>Fires on every movement while scrubbing. Right after <see cref="ScrubBegan"/>, it won't fire with a zero delta.</summary>
        public event Action<ScrubUpdate> ScrubUpdated;

        /// <summary>The button was released while still scrubbing (i.e. committed).</summary>
        public event Action ScrubEnded;

        /// <summary>Scrubbing was interrupted by Escape or PointerCaptureOut.</summary>
        public event Action ScrubCancelled;

        /// <summary>Released while still below the threshold (i.e. a click).</summary>
        public event Action Clicked;

        #endregion

        #region Manipulator

        protected override void RegisterCallbacksOnTarget()
        {
            this.target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            this.target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.target.RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.target.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            this.target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            this.target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            this.target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            this.target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            this.target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            this.target.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            ResetSession();
        }

        #endregion

        #region Pointer

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _pointerDown || this.target == null)
            {
                return;
            }

            _pointerDown = true;
            _scrubbing = false;
            _pointerId = evt.pointerId;
            _dragThreshold = evt.pointerType == UnityEngine.UIElements.PointerType.mouse
                ? MOUSE_DRAG_THRESHOLD
                : TOUCH_DRAG_THRESHOLD;
            _pressPosition = LocalPosition(evt);
            _previousPosition = _pressPosition;
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            if (this.target.panel != null)
            {
                this.target.CapturePointer(_pointerId);
            }

            // Don't call StopPropagation here. Whether this is a click or a scrub isn't decided yet,
            // and swallowing the press would also take out the caller's focus/caret handling
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || !_pointerDown || evt.pointerId != _pointerId)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            Vector2 position = LocalPosition(evt);

            if (!_scrubbing)
            {
                if (Vector2.Distance(position, _pressPosition) < _dragThreshold)
                {
                    return;
                }

                BeginScrub(position);
                evt.StopPropagation();
                return;
            }

            Vector2 delta = position - _previousPosition;
            _previousPosition = position;

            Action<ScrubUpdate> updated = ScrubUpdated;
            if (updated != null)
            {
                updated(new ScrubUpdate(delta.x, delta.y, _shiftHeld, _altHeld));
            }

            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_pointerDown || evt.pointerId != _pointerId)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            int pointerId = _pointerId;

            // Fold the state before releasing. ReleasePointer calls PointerCaptureOut back in,
            // so without folding first, both Commit and Cancel would fire
            ResetSession();
            ReleasePointerSafely(pointerId);

            if (wasScrubbing)
            {
                Action ended = ScrubEnded;
                if (ended != null)
                {
                    ended();
                }

                evt.StopPropagation();
                return;
            }

            Action clicked = Clicked;
            if (clicked != null)
            {
                clicked();
            }
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (!_pointerDown && !_scrubbing)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            ResetSession();

            if (!wasScrubbing)
            {
                return;
            }

            Action cancelled = ScrubCancelled;
            if (cancelled != null)
            {
                cancelled();
            }
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            if (!_pointerDown && !_scrubbing)
            {
                return;
            }

            bool wasScrubbing = _scrubbing;
            int pointerId = _pointerId;

            ResetSession();
            ReleasePointerSafely(pointerId);

            if (wasScrubbing)
            {
                Action cancelled = ScrubCancelled;
                if (cancelled != null)
                {
                    cancelled();
                }
            }

            evt.StopPropagation();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            ResetSession();
        }

        #endregion

        #region Session

        void BeginScrub(Vector2 position)
        {
            _scrubbing = true;

            // Use the point where the threshold was crossed as the origin. Movement up to that point isn't carried into the value
            _previousPosition = position;

            if (HideCursorWhileScrubbing)
            {
                HideCursor();
            }

            Action began = ScrubBegan;
            if (began != null)
            {
                began();
            }
        }

        void ResetSession()
        {
            _pointerDown = false;
            _scrubbing = false;
            _pointerId = PointerId.invalidPointerId;
            RestoreCursor();
        }

        void ReleasePointerSafely(int pointerId)
        {
            if (this.target == null || this.target.panel == null
                || pointerId == PointerId.invalidPointerId)
            {
                return;
            }

            if (this.target.HasPointerCapture(pointerId))
            {
                this.target.ReleasePointer(pointerId);
            }
        }

        Vector2 LocalPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            Vector2 world = new Vector2(position.x, position.y);
            return this.target != null ? this.target.WorldToLocal(world) : world;
        }

        void HideCursor()
        {
            // No panel means logic-only execution such as an EditMode test. Don't touch the OS cursor
            if (_cursorHidden || this.target == null || this.target.panel == null)
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
    }
}
