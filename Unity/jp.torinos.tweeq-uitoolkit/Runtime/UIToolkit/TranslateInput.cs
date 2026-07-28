using System;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

// The class itself has no Label-equivalent property, but as with Rotary, reference it under an alias to avoid a type-name collision
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A 2-axis drag scrubber (M6 wave 2 spec §C). Grabbing the 24x24 button and moving it translates
    /// the dragged pixel amount directly into a value delta, and while dragging a dot grid centered on
    /// the origin spreads out behind it.
    /// </summary>
    /// <remarks>
    /// Sensitivity is px 1:1 x speed. Both the Vue original and another reference implementation share the same
    /// 3-tier 5 / 0.1 / 1 scale, and modifier keys are re-evaluated on every event.
    /// Value accumulation uses the Vue approach of clamping "previous value + delta" (another reference implementation
    /// instead uses "start value + total movement").
    /// This is the one adopted here, since it keeps following as soon as you pull back even after holding at a clamped edge.
    /// </remarks>
    [UxmlElement]
    public partial class TranslateInput : VisualElement, INotifyValueChanged<Vector2>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        const float DEFAULT_SIZE = 24f;

        // Same ruling as RotaryInput (m7-disabled-invalid-spec.md). The Vue version leaves appearance unchanged, but
        // having the adjacent numeric field dim while only the button still looks alive is a recipe for an accident on a live gig
        const float DISABLED_OPACITY = 0.4f;

        // px-to-value multiplier. Matches Vue's computed speed / another reference implementation's speed
        const float SPEED_COARSE = 5f;
        const float SPEED_FINE = 0.1f;
        const float SPEED_NORMAL = 1f;

        // The overlay's grid scale. Matches Vue's computed gridScale (moves opposite to speed)
        const float GRID_SCALE_COARSE = 0.5f;
        const float GRID_SCALE_FINE = 4f;
        const float GRID_SCALE_NORMAL = 2f;

        // Interpolation factor for one frame of Vue's useRafFn
        const float GRID_SCALE_LERP = 0.4f;
        const long GRID_TICK_MS = 16;

        // A difference at or below this gets snapped in a single frame (to stop repainting every frame)
        const float GRID_SCALE_EPSILON = 1e-3f;

        // Vue: .overlay-grid's inset calc(-150px + h/2) = a box 300 in diameter. Another reference implementation uses radius 150
        const float OVERLAY_RADIUS = 150f;
        const float GRID_UNIT = 10f;
        const float DOT_RADIUS = 1f;

        // Vue's mask radial-gradient(closest-side, black 50%, transparent 100%).
        // Opaque out to 50% of the radius, then falls off to zero toward the outer edge
        const float MASK_SOLID_RATIO = 0.5f;

        // Quantize density into bands and fold each band down to a single Fill call (Filling dot by dot would blow up the draw call count)
        const int ALPHA_BANDS = 6;

        // The smaller the grid scale, the more dots there are. Cap it by spacing before it gets too dense
        const float MIN_DOT_SPACING = 4f;

        const float AXIS_LINE_WIDTH = 2f;
        const float RANGE_LINE_WIDTH = 1f;

        // The 3x3 dots on the button face (measured from another reference implementation's paint_grid_icon)
        const float ICON_SPACING = 3.5f;
        const float ICON_DOT_RADIUS = 1f;

        const float FOCUS_RING_WIDTH = 1f;

        // Vue: translate(-50%, calc(-100% - h * .2)) = the label's bottom edge sits h*0.2 further above the box's top edge
        const float LABEL_GAP_RATIO = 0.2f;
        const float LABEL_FONT_SIZE = 11f;
        const float LABEL_PADDING_X = 6f;
        const float LABEL_PADDING_Y = 4f;
        const float LABEL_RADIUS = 4f;
        const float LABEL_EDGE_MARGIN = 4f;
        const float LABEL_AXIS_GAP = 4f;
        const float LABEL_VALUE_MIN_WIDTH = 30f;

        #endregion

        #region Fields

        Vector2 _value;
        Vector2 _min = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        Vector2 _max = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        bool _showOverlayLabel = true;
        bool _disabled;
        TweeqTheme _theme = TweeqTheme.Dark();

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        readonly VisualElement _focusInner;
        readonly VisualElement _focusOuter;

        TranslateOverlay _overlay;
        readonly IVisualElementScheduledItem _gridItem;

        int _pointerId = PointerId.invalidPointerId;
        bool _dragging;
        Vector2 _previousPanelPosition;
        Vector2 _valueOnDragStart;
        bool _cursorHidden;

        bool _shiftHeld;
        bool _altHeld;
        bool _lockX;
        bool _lockY;

        float _gridScaleAnimated = GRID_SCALE_NORMAL;

        bool _hovered;
        bool _focused;

        #endregion

        #region Public API

        /// <summary>Fires every time the value changes (at most once per pointer move).</summary>
        public event Action<Vector2> ValueChanged;

        /// <summary>Fires exactly once per gesture, when the drag is confirmed (pointer released).</summary>
        public event Action<Vector2> Confirmed;

        /// <summary>The current value.</summary>
        [UxmlAttribute]
        public Vector2 value
        {
            get => _value;
            set
            {
                if (_value.Equals(value))
                {
                    return;
                }

                Vector2 previous = _value;
                SetValueWithoutNotify(value);
                Notify(previous, _value);
            }
        }

        /// <summary>The lower bound. Unlimited (negative infinity) by default.</summary>
        [UxmlAttribute]
        public Vector2 Min
        {
            get => _min;
            set
            {
                _min = value;
                UpdateOverlay();
            }
        }

        /// <summary>The upper bound. Unlimited (positive infinity) by default.</summary>
        [UxmlAttribute]
        public Vector2 Max
        {
            get => _max;
            set
            {
                _max = value;
                UpdateOverlay();
            }
        }

        /// <summary>Applies the same lower bound to both axes (the spec's "scalar form").</summary>
        public void SetMin(float uniform)
        {
            this.Min = new Vector2(uniform, uniform);
        }

        /// <summary>Applies the same upper bound to both axes (the spec's "scalar form").</summary>
        public void SetMax(float uniform)
        {
            this.Max = new Vector2(uniform, uniform);
        }

        /// <summary>Whether to show the current-value label on the overlay while dragging. Default true (same as Vue).</summary>
        [UxmlAttribute]
        public bool ShowOverlayLabel
        {
            get => _showOverlayLabel;
            set
            {
                if (_showOverlayLabel == value)
                {
                    return;
                }

                _showOverlayLabel = value;
                UpdateOverlay();
            }
        }

        /// <summary>
        /// The disabled state. If set while dragging, discards the gesture and reverts to the start value.
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

                if (_disabled && _dragging)
                {
                    // If a drag is still alive the instant it's disabled, there's no way left to release it -
                    // i.e. no way to get the hidden cursor back. Release the held capture through the same steps as Escape
                    int pointerId = _pointerId;
                    _pointerId = PointerId.invalidPointerId;
                    CancelTranslateDrag();
                    ReleasePointerSafely(pointerId);
                }

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

        /// <summary>The current sensitivity (Shift=5 / Alt=0.1 / default 1).</summary>
        public float Speed
        {
            get
            {
                if (_shiftHeld)
                {
                    return SPEED_COARSE;
                }

                return _altHeld ? SPEED_FINE : SPEED_NORMAL;
            }
        }

        /// <summary>The current target grid scale (Shift=0.5 / Alt=4 / default 2).</summary>
        public float GridScaleTarget
        {
            get
            {
                if (_shiftHeld)
                {
                    return GRID_SCALE_COARSE;
                }

                return _altHeld ? GRID_SCALE_FINE : GRID_SCALE_NORMAL;
            }
        }

        /// <summary>Whether a drag session is in progress.</summary>
        public bool Dragging => _dragging;

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

        /// <summary>Sets the value without firing a ChangeEvent.</summary>
        public void SetValueWithoutNotify(Vector2 newValue)
        {
            _value = newValue;
            UpdateOverlay();
        }

        /// <summary>
        /// Overrides the modifier-key-equivalent state (Shift=coarse / Alt=fine).
        /// Pointer and key events also update through this same path, so tests can use this in their place.
        /// </summary>
        public void SetTweakModifiers(bool shift, bool alt)
        {
            if (_shiftHeld == shift && _altHeld == alt)
            {
                return;
            }

            _shiftHeld = shift;
            _altHeld = alt;
            UpdateOverlay();
        }

        /// <summary>Axis lock equivalent to the X / Y keys. The contract is "active only while held", so releasing it is also the caller's responsibility.</summary>
        public void SetAxisLocks(bool lockHorizontal, bool lockVertical)
        {
            if (_lockX == lockHorizontal && _lockY == lockVertical)
            {
                return;
            }

            _lockX = lockHorizontal;
            _lockY = lockVertical;
            UpdateOverlay();
        }

        /// <summary>Begins a drag session (panel-independent).</summary>
        public void BeginTranslateDrag()
        {
            if (_disabled || _dragging)
            {
                return;
            }

            _dragging = true;
            _valueOnDragStart = _value;

            // Vue's raf keeps running constantly, so it's already at the target value by the time the drag starts.
            // Without matching it here, the grid would visibly stretch and shrink right after starting
            _gridScaleAnimated = GridScaleTarget;

            HideCursor();
            AcquireOverlay();
            _gridItem?.Resume();
            Refresh();
        }

        /// <summary>
        /// Applies the movement delta while dragging (panel-space px, positive downward).
        /// The value's Y increases when dragging upward (Vue follows the DOM convention of down=+Y, but this is a
        /// deliberate deviation that flips it to match Unity's coordinate sense; see "TranslateInput" in m6-wave2-spec.md).
        /// </summary>
        public void UpdateTranslateDrag(Vector2 pixelDelta)
        {
            if (!_dragging)
            {
                return;
            }

            Vector2 delta = pixelDelta * this.Speed;
            delta.y = -delta.y;

            // A lock that applies only while held. X means "horizontal only" = discard the vertical component
            if (_lockX)
            {
                delta.y = 0f;
            }

            if (_lockY)
            {
                delta.x = 0f;
            }

            Vector2 next = new Vector2(
                ClampAxis(_value.x + delta.x, _min.x, _max.x),
                ClampAxis(_value.y + delta.y, _min.y, _max.y));

            if (next.Equals(_value))
            {
                UpdateOverlay();
                return;
            }

            Vector2 previous = _value;
            _value = next;
            UpdateOverlay();
            Notify(previous, next);
        }

        /// <summary>Confirms and ends the drag. Confirmed fires exactly once.</summary>
        public void EndTranslateDrag()
        {
            if (!_dragging)
            {
                return;
            }

            StopDragSession();
            Confirmed?.Invoke(_value);
        }

        /// <summary>Discards the drag and reverts to the start value (equivalent to Escape). Confirmed does not fire.</summary>
        public void CancelTranslateDrag()
        {
            if (!_dragging)
            {
                return;
            }

            Vector2 restored = _valueOnDragStart;
            StopDragSession();

            // This rolls back a value that was already notified during the drag, so notify here as well
            this.value = restored;
        }

        #endregion

        #region Construction

        public TranslateInput()
        {
            this.AddToClassList("tweeq-translate-input");

            this.focusable = true;
            this.style.width = DEFAULT_SIZE;
            this.style.height = DEFAULT_SIZE;
            this.style.flexShrink = 0f;

            // InputGroup.ApplyStretch hands out basis 0 to children with no flexBasis specified.
            // Since basis wins over width, without setting this explicitly the 24px square collapses down to the icon's intrinsic width
            this.style.flexGrow = 0f;
            this.style.flexBasis = DEFAULT_SIZE;

            // The focus ring sits 1px outside, so this must not be Hidden
            this.style.overflow = Overflow.Visible;

            _focusInner = CreateRing(0f);
            _focusOuter = CreateRing(-FOCUS_RING_WIDTH);
            this.hierarchy.Add(_focusInner);
            this.hierarchy.Add(_focusOuter);

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

            // Create just one scheduled item and reuse it via Resume/Pause (avoids allocating a closure on every drag)
            _gridItem = this.schedule.Execute(OnGridTick).Every(GRID_TICK_MS);
            _gridItem.Pause();

            ApplyStaticStyles();
            Refresh();
        }

        VisualElement CreateRing(float inset)
        {
            VisualElement ring = new VisualElement
            {
                name = "tweeq-translate-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            ring.style.position = Position.Absolute;
            ring.style.left = inset;
            ring.style.top = inset;
            ring.style.right = inset;
            ring.style.bottom = inset;
            ring.style.display = DisplayStyle.None;
            SetBorderWidth(ring, FOCUS_RING_WIDTH);
            return ring;
        }

        void ApplyStaticStyles()
        {
            this.style.width = _theme.InputHeight;
            this.style.height = _theme.InputHeight;
            ApplyCornerRadius();

            SetBorderColor(_focusInner, _theme.Input);
            SetBorderColor(_focusOuter, _theme.Accent);
        }

        // Corner-radius table from spec §1. The two axes combine via OR (if either side says to "flatten" a corner, it flattens)
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
            SetCornerRadius(_focusInner, radius, topLeft, topRight, bottomLeft, bottomRight);

            // The outer ring sits 1px further out, so also grow its radius by 1px to keep the same appearance
            SetCornerRadius(
                _focusOuter,
                radius + FOCUS_RING_WIDTH,
                topLeft,
                topRight,
                bottomLeft,
                bottomRight);
        }

        #endregion

        #region Pointer

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _dragging || _disabled)
            {
                return;
            }

            _pointerId = evt.pointerId;
            _previousPanelPosition = PanelPosition(evt);
            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            // Take focus so we can receive X / Y / Escape
            this.Focus();

            if (this.panel != null)
            {
                this.CapturePointer(_pointerId);
            }

            // Vue's useDrag has dragDelaySeconds 0 = treated as dragging the instant it's pressed (no threshold)
            BeginTranslateDrag();
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || !_dragging || evt.pointerId != _pointerId)
            {
                return;
            }

            Vector2 position = PanelPosition(evt);
            Vector2 delta = position - _previousPanelPosition;
            _previousPanelPosition = position;

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            UpdateTranslateDrag(delta);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_dragging || evt.pointerId != _pointerId)
            {
                return;
            }

            int pointerId = _pointerId;
            _pointerId = PointerId.invalidPointerId;

            // Confirm first. Reversing the order would let the PointerCaptureOut thrown by ReleasePointer
            // collapse the session, and Confirmed would never fire
            EndTranslateDrag();
            ReleasePointerSafely(pointerId);
            evt.StopPropagation();
        }

        // Don't leave drag state (i.e. hidden cursor / overlay) behind even if capture is lost
        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _pointerId = PointerId.invalidPointerId;

            if (!_dragging)
            {
                return;
            }

            // Confirm the value wherever it ended up moving. The confirm event only fires on "release", so don't emit it here
            StopDragSession();
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            Refresh();
        }

        void OnPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            Refresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // Don't leave the cursor and overlay behind even when detached from the panel
            _pointerId = PointerId.invalidPointerId;
            _hovered = false;
            _focused = false;
            StopDragSession();
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

        #region Keyboard

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            SetTweakModifiers(
                (evt.modifiers & EventModifiers.Shift) != 0,
                (evt.modifiers & EventModifiers.Alt) != 0);

            switch (evt.keyCode)
            {
                case KeyCode.X:
                    SetAxisLocks(true, _lockY);
                    evt.StopPropagation();
                    break;

                case KeyCode.Y:
                    SetAxisLocks(_lockX, true);
                    evt.StopPropagation();
                    break;

                case KeyCode.Escape:
                    if (_dragging)
                    {
                        int pointerId = _pointerId;
                        _pointerId = PointerId.invalidPointerId;
                        CancelTranslateDrag();
                        ReleasePointerSafely(pointerId);
                        evt.StopPropagation();
                    }

                    break;
            }
        }

        void OnKeyUp(KeyUpEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            SetTweakModifiers(
                (evt.modifiers & EventModifiers.Shift) != 0,
                (evt.modifiers & EventModifiers.Alt) != 0);

            switch (evt.keyCode)
            {
                case KeyCode.X:
                    SetAxisLocks(false, _lockY);
                    evt.StopPropagation();
                    break;

                case KeyCode.Y:
                    SetAxisLocks(_lockX, false);
                    evt.StopPropagation();
                    break;
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

            // Keys treated as "held down" are released the moment focus is lost (since KeyUp never arrives)
            SetAxisLocks(false, false);
            SetTweakModifiers(false, false);
            Refresh();
        }

        #endregion

        #region Drag session

        void StopDragSession()
        {
            _dragging = false;
            _gridItem?.Pause();
            RestoreCursor();
            ReleaseOverlay();
            Refresh();
        }

        void Notify(Vector2 previous, Vector2 current)
        {
            if (this.panel != null)
            {
                using (ChangeEvent<Vector2> changeEvent = ChangeEvent<Vector2>.GetPooled(previous, current))
                {
                    changeEvent.target = this;
                    this.SendEvent(changeEvent);
                }
            }

            ValueChanged?.Invoke(current);
        }

        static float ClampAxis(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
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

        void OnGridTick()
        {
            if (!_dragging)
            {
                _gridItem?.Pause();
                return;
            }

            float target = GridScaleTarget;
            if (Mathf.Abs(_gridScaleAnimated - target) <= GRID_SCALE_EPSILON)
            {
                if (_gridScaleAnimated == target)
                {
                    return;
                }

                _gridScaleAnimated = target;
            }
            else
            {
                _gridScaleAnimated = Mathf.Lerp(_gridScaleAnimated, target, GRID_SCALE_LERP);
            }

            UpdateOverlay();
        }

        void AcquireOverlay()
        {
            if (_overlay != null)
            {
                return;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(this);
            if (layer == null)
            {
                // Give up on the guide if not attached to a panel (the operation itself still works)
                return;
            }

            _overlay = new TranslateOverlay();
            layer.Add(_overlay);
            UpdateOverlay();
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

            TranslateOverlayState state = new TranslateOverlayState
            {
                Theme = _theme,
                Center = this.worldBound.center,
                Value = _value,
                Min = _min,
                Max = _max,
                GridScale = _gridScaleAnimated,
                LockX = _lockX,
                LockY = _lockY,
                ShowLabel = _showOverlayLabel,

                // Vue: precisionOf(speed). Only becomes 1 decimal place when it's 0.1
                Precision = TweeqMath.PrecisionOf(this.Speed),
            };

            _overlay.Sync(in state);
        }

        #endregion

        #region Presentation

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            this.focusable = !_disabled;
            this.style.opacity = _disabled ? DISABLED_OPACITY : 1f;

            if (!_disabled)
            {
                return;
            }

            // Make sure the hover color, focus ring, and held-down keys don't linger in the dimmed state
            _hovered = false;
            _focused = false;
            SetAxisLocks(false, false);
            SetTweakModifiers(false, false);
        }

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            this.style.backgroundColor = _hovered || _dragging ? _theme.AccentHover : _theme.Accent;

            _focusInner.style.display = _focused ? DisplayStyle.Flex : DisplayStyle.None;
            _focusOuter.style.display = _focused ? DisplayStyle.Flex : DisplayStyle.None;

            this.MarkDirtyRepaint();
        }

        // The 3x3 dot icon on the button face (equivalent to Vue's mingcute:dot-grid-fill)
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

            Vector2 center = this.contentRect.center;
            painter.fillColor = TweeqTheme.ContrastText(_theme.Accent);

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    Vector2 dot = new Vector2(center.x + x * ICON_SPACING, center.y + y * ICON_SPACING);
                    painter.BeginPath();
                    painter.Arc(
                        dot,
                        ICON_DOT_RADIUS,
                        new Angle(0f, AngleUnit.Degree),
                        new Angle(360f, AngleUnit.Degree));
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }

        #endregion

        #region Helpers

        // The overlay draws in panel coordinates, so keep the raw, untransformed position
        static Vector2 PanelPosition(IPointerEvent evt)
        {
            Vector3 position = evt.position;
            return new Vector2(position.x, position.y);
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

        #region Overlay implementation

        /// <summary>Drawing parameters for the overlay that only lives during a drag. Coordinates are in panel space.</summary>
        struct TranslateOverlayState
        {
            public TweeqTheme Theme;
            public Vector2 Center;
            public Vector2 Value;
            public Vector2 Min;
            public Vector2 Max;
            public float GridScale;
            public bool LockX;
            public bool LockY;
            public bool ShowLabel;
            public int Precision;
        }

        /// <summary>
        /// The layer that draws the dot grid, axis-lock lines, range frame, and current-value label.
        /// </summary>
        sealed class TranslateOverlay : VisualElement
        {
            #region Fields

            TranslateOverlayState _state;
            bool _hasState;

            VisualElement _labelRoot;
            UILabel _xAxis;
            UILabel _yAxis;
            ValueLabel _xValue;
            ValueLabel _yValue;

            // The theme whose font was applied most recently. Sync runs every frame during a drag,
            // so assignment of the managed value (FontDefinition) is restricted to only when the theme actually changes.
            TweeqTheme _fontTheme;

            #endregion

            #region Construction

            public TranslateOverlay()
            {
                this.name = "tweeq-translate-overlay";
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
                _labelRoot.style.paddingLeft = LABEL_PADDING_X;
                _labelRoot.style.paddingRight = LABEL_PADDING_X;
                _labelRoot.style.paddingTop = LABEL_PADDING_Y;
                _labelRoot.style.paddingBottom = LABEL_PADDING_Y;
                _labelRoot.style.display = DisplayStyle.None;
                SetBorderWidth(_labelRoot, 1f);
                SetCornerRadius(_labelRoot, LABEL_RADIUS, true, true, true, true);

                // Centering needs the actual resolved size, so it's repositioned once that size is finalized.
                _labelRoot.RegisterCallback<GeometryChangedEvent>(OnLabelGeometryChanged);

                _xAxis = CreateAxisLabel("X", 0f);
                _xValue = new ValueLabel();
                _labelRoot.Add(_xAxis);
                _labelRoot.Add(_xValue.Element);

                _yAxis = CreateAxisLabel("Y", LABEL_AXIS_GAP * 2f);
                _yValue = new ValueLabel();
                _labelRoot.Add(_yAxis);
                _labelRoot.Add(_yValue.Element);

                this.Add(_labelRoot);
            }

            static UILabel CreateAxisLabel(string text, float marginLeft)
            {
                UILabel label = new UILabel(text) { pickingMode = PickingMode.Ignore };
                label.style.fontSize = LABEL_FONT_SIZE;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.marginLeft = marginLeft;
                label.style.marginRight = LABEL_AXIS_GAP;
                label.style.marginTop = 0f;
                label.style.marginBottom = 0f;
                return label;
            }

            #endregion

            #region Sync

            public void Sync(in TranslateOverlayState state)
            {
                _state = state;
                _hasState = state.Theme != null;
                if (!_hasState)
                {
                    return;
                }

                SyncLabel();
                this.MarkDirtyRepaint();
            }

            void SyncLabel()
            {
                _labelRoot.style.display = _state.ShowLabel ? DisplayStyle.Flex : DisplayStyle.None;
                if (!_state.ShowLabel)
                {
                    return;
                }

                TweeqTheme theme = _state.Theme;
                _labelRoot.style.backgroundColor = theme.SurfaceOpaque;
                SetBorderColor(_labelRoot, theme.Border);

                // Only the axis names get the muted color (same intent as Vue's :deep(i)).
                _xAxis.style.color = theme.TextMuted;
                _yAxis.style.color = theme.TextMuted;
                _xValue.Element.style.color = theme.Text;
                _yValue.Element.style.color = theme.Text;

                if (!ReferenceEquals(_fontTheme, theme))
                {
                    _fontTheme = theme;

                    // These are fields for reading the raw value, so the numeric font is used (the X / Y axis names stay on the UI default).
                    TweeqFonts.Apply(_xValue.Element, theme.FontNumeric);
                    TweeqFonts.Apply(_yValue.Element, theme.FontNumeric);
                }

                _xValue.Sync(_state.Value.x, _state.Precision);
                _yValue.Sync(_state.Value.y, _state.Precision);

                UpdateLabelPosition();
            }

            void OnLabelGeometryChanged(GeometryChangedEvent evt)
            {
                UpdateLabelPosition();
            }

            void UpdateLabelPosition()
            {
                if (_labelRoot == null || !_hasState)
                {
                    return;
                }

                float width = _labelRoot.resolvedStyle.width;
                float height = _labelRoot.resolvedStyle.height;
                float inputHeight = _state.Theme.InputHeight;

                float left = _state.Center.x - width * 0.5f;
                float top = _state.Center.y - inputHeight * (0.5f + LABEL_GAP_RATIO) - height;

                Rect bounds = this.contentRect;
                if (bounds.width > 0f && bounds.height > 0f)
                {
                    left = Mathf.Clamp(
                        left, bounds.xMin + LABEL_EDGE_MARGIN, Mathf.Max(bounds.xMax - width - LABEL_EDGE_MARGIN, bounds.xMin));
                    top = Mathf.Clamp(
                        top, bounds.yMin + LABEL_EDGE_MARGIN, Mathf.Max(bounds.yMax - height - LABEL_EDGE_MARGIN, bounds.yMin));
                }

                _labelRoot.style.left = left;
                _labelRoot.style.top = top;
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

                PaintGrid(painter, theme);
                PaintAxisLocks(painter, theme);
                PaintRange(painter, theme);
            }

            void PaintGrid(Painter2D painter, TweeqTheme theme)
            {
                float scale = _state.GridScale;
                if (!(scale > 0f))
                {
                    return;
                }

                float spacing = Mathf.Max(GRID_UNIT * scale, MIN_DOT_SPACING);
                Vector2 center = _state.Center;

                // The grid scrolls in the opposite direction of the value (same as Vue's background-position,
                // and the same as another reference implementation's rem_euclid). For Y, the value space treats
                // up as positive (a deviation to match Unity) while the panel treats down as positive, so the sign is used as-is.
                float offsetX = Repeat(-_state.Value.x * scale, spacing);
                float offsetY = Repeat(_state.Value.y * scale, spacing);

                float left = center.x - OVERLAY_RADIUS + offsetX;
                float top = center.y - OVERLAY_RADIUS + offsetY;
                float right = center.x + OVERLAY_RADIUS;
                float bottom = center.y + OVERLAY_RADIUS;

                Color baseColor = theme.TextSubtle;
                float solidRadius = OVERLAY_RADIUS * MASK_SOLID_RATIO;

                for (int band = 0; band < ALPHA_BANDS; band++)
                {
                    // At opacity a, distance is R*(1 - a/2). The band's inner/outer radii are the inverse of that.
                    float alphaHigh = 1f - band / (float)ALPHA_BANDS;
                    float alphaLow = 1f - (band + 1) / (float)ALPHA_BANDS;

                    float inner = band == 0 ? 0f : OVERLAY_RADIUS * (1f - alphaHigh * 0.5f);
                    float outer = OVERLAY_RADIUS * (1f - alphaLow * 0.5f);

                    // The innermost band fully includes "the circle where the mask caps out at 1."
                    if (band == 0)
                    {
                        outer = Mathf.Max(outer, solidRadius);
                    }

                    float innerSqr = inner * inner;
                    float outerSqr = outer * outer;

                    Color color = baseColor;
                    color.a = baseColor.a * (band == 0 ? 1f : (alphaHigh + alphaLow) * 0.5f);
                    painter.fillColor = color;
                    painter.BeginPath();

                    bool any = false;
                    for (float y = top; y <= bottom; y += spacing)
                    {
                        float dy = y - center.y;
                        for (float x = left; x <= right; x += spacing)
                        {
                            float dx = x - center.x;
                            float distanceSqr = dx * dx + dy * dy;
                            if (distanceSqr < innerSqr || distanceSqr >= outerSqr)
                            {
                                continue;
                            }

                            // A 1px-radius dot is indistinguishable from a rectangle, so each is drawn as a rectangle that folds into one Fill call per band.
                            painter.MoveTo(new Vector2(x - DOT_RADIUS, y - DOT_RADIUS));
                            painter.LineTo(new Vector2(x + DOT_RADIUS, y - DOT_RADIUS));
                            painter.LineTo(new Vector2(x + DOT_RADIUS, y + DOT_RADIUS));
                            painter.LineTo(new Vector2(x - DOT_RADIUS, y + DOT_RADIUS));
                            painter.ClosePath();
                            any = true;
                        }
                    }

                    if (any)
                    {
                        painter.Fill();
                    }
                }
            }

            void PaintAxisLocks(Painter2D painter, TweeqTheme theme)
            {
                if (!_state.LockX && !_state.LockY)
                {
                    return;
                }

                Vector2 center = _state.Center;
                painter.strokeColor = theme.Accent;
                painter.lineWidth = AXIS_LINE_WIDTH;
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();

                if (_state.LockX)
                {
                    painter.MoveTo(new Vector2(center.x - OVERLAY_RADIUS, center.y));
                    painter.LineTo(new Vector2(center.x + OVERLAY_RADIUS, center.y));
                }

                if (_state.LockY)
                {
                    painter.MoveTo(new Vector2(center.x, center.y - OVERLAY_RADIUS));
                    painter.LineTo(new Vector2(center.x, center.y + OVERLAY_RADIUS));
                }

                painter.Stroke();
            }

            // Vue's .zero / another reference implementation's range_rect. The frame is only shown when the movable range is finite.
            void PaintRange(Painter2D painter, TweeqTheme theme)
            {
                Vector2 min = _state.Min;
                Vector2 max = _state.Max;
                if (!IsFinite(min.x) || !IsFinite(min.y) || !IsFinite(max.x) || !IsFinite(max.y))
                {
                    return;
                }

                float scale = _state.GridScale;
                Vector2 center = _state.Center;
                float x0 = center.x + (min.x - _state.Value.x) * scale;
                float x1 = center.x + (max.x - _state.Value.x) * scale;

                // The value space treats up as positive (a deviation to match Unity), so min.y (the bottom edge) is below center in panel space.
                float y0 = center.y + (_state.Value.y - min.y) * scale;
                float y1 = center.y + (_state.Value.y - max.y) * scale;

                painter.strokeColor = theme.Accent;
                painter.lineWidth = RANGE_LINE_WIDTH;
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x0, y0));
                painter.LineTo(new Vector2(x1, y0));
                painter.LineTo(new Vector2(x1, y1));
                painter.LineTo(new Vector2(x0, y1));
                painter.ClosePath();
                painter.Stroke();
            }

            static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }

            // Keeps even negative values within [0, length) (equivalent to Rust's rem_euclid).
            static float Repeat(float value, float length)
            {
                if (length <= 0f)
                {
                    return 0f;
                }

                float result = value - Mathf.Floor(value / length) * length;
                return result < 0f ? 0f : result;
            }

            #endregion

            #region Value label

            /// <summary>
            /// A value label that only rebuilds its string when the display actually changes.
            /// Sync runs every frame during a drag, so skimping here is what keeps the GC from kicking in.
            /// </summary>
            sealed class ValueLabel
            {
                readonly UILabel _label;

                double _key;
                int _precision = -1;
                bool _hasKey;

                public ValueLabel()
                {
                    _label = new UILabel(string.Empty) { pickingMode = PickingMode.Ignore };
                    _label.style.fontSize = LABEL_FONT_SIZE;
                    _label.style.unityTextAlign = TextAnchor.MiddleRight;
                    _label.style.minWidth = LABEL_VALUE_MIN_WIDTH;
                    _label.style.marginLeft = 0f;
                    _label.style.marginRight = 0f;
                    _label.style.marginTop = 0f;
                    _label.style.marginBottom = 0f;
                }

                public UILabel Element => _label;

                public void Sync(double value, int precision)
                {
                    bool cacheable = TryGetKey(value, precision, out double key);
                    if (cacheable && _hasKey && _precision == precision
                        && TweeqFormat.SameValueBits(_key, key))
                    {
                        return;
                    }

                    _label.text = TweeqFormat.Format(value, precision, true);

                    // Values near a rounding boundary or non-finite values can't be turned into a key, so force a rebuild on the next frame too.
                    _hasKey = cacheable;
                    _key = key;
                    _precision = precision;
                }

                // Display is rounded to `precision` digits, so the string ends up the same as long as they match at that granularity.
                static bool TryGetKey(double value, int precision, out double key)
                {
                    key = 0.0;
                    if (!TweeqMath.IsFinite(value))
                    {
                        return false;
                    }

                    double scale = Math.Pow(10.0, TweeqFormat.ClampDigits(precision));
                    double scaled = value * scale;
                    key = Math.Round(scaled, MidpointRounding.AwayFromZero);
                    return Math.Abs(scaled - key) < 0.5 - 1e-6;
                }
            }

            #endregion
        }

        #endregion
    }
}
