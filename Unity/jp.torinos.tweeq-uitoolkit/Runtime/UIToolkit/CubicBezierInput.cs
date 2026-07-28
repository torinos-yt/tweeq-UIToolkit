using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>The control point a picker drag is currently moving.</summary>
    public enum CubicBezierHandle
    {
        /// <summary>Nothing is grabbed.</summary>
        None,

        /// <summary>The handle leaving P0=(0,0), i.e. <c>value.xy</c>.</summary>
        P1,

        /// <summary>The handle arriving at P3=(1,1), i.e. <c>value.zw</c>.</summary>
        P2,
    }

    /// <summary>
    /// Cubic bezier easing input (m10-cubicbezier-spec.md). The field is a square preview button;
    /// clicking it opens a <see cref="TweeqPopover"/> holding a square pad with the two draggable
    /// control-point handles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value is a <see cref="Vector4"/> laid out as (x1, y1, x2, y2), with P0=(0,0) and P3=(1,1)
    /// fixed. Vue models it as a readonly 4-tuple; using a Unity-native vector here is the same kind
    /// of deviation as ColorInput taking <see cref="UnityEngine.Color"/> instead of a CSS string.
    /// Both axes are clamped to [0,1], matching the uv clamp the Vue picker applies.
    /// </para>
    /// <para>
    /// Open/close and the drag session are kept as a panel-independent imperative layer
    /// (<see cref="Open"/> / <see cref="BeginDrag"/> / <see cref="UpdateDrag"/> ...), with the
    /// popover and the painting layered on top, so EditMode tests can drive the contract directly
    /// (the same split as ColorInput / DropdownInput).
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class CubicBezierInput
        : VisualElement,
          INotifyValueChanged<Vector4>,
          ITweeqThemed,
          ITweeqInputBox,
          ITweeqConfirmable<Vector4>
    {
        #region Constants

        /// <summary>
        /// The initial curve. Vue declares modelValue as a required prop and ships no default, so
        /// CSS <c>ease</c> is adopted here (recorded as a deviation).
        /// </summary>
        public static readonly Vector4 DEFAULT_VALUE = new Vector4(0.25f, 0.1f, 0.25f, 1f);

        // Vue InputCubicBezier.vue:67 — .icon is inset 2px inside the input-height box
        const float PREVIEW_INSET = 2f;

        // Vue InputCubicBezier.vue:74 — stroke-width 1.5 under vector-effect: non-scaling-stroke,
        // so the number is in px rather than in viewBox units
        const float PREVIEW_LINE_WIDTH = 1.5f;

        // Vue InputCubicBezierPicker.vue:82-88 — path/circle stroke-width 2, line stroke-width 1
        const float CURVE_LINE_WIDTH = 2f;
        const float CONTROL_LINE_WIDTH = 1f;
        const float HANDLE_LINE_WIDTH = 2f;

        // Vue InputCubicBezierPicker.vue:57-58 — circle r=.035 in a 0..1 viewBox, i.e. 3.5% of the pad's side
        const float HANDLE_RADIUS_RATIO = 0.035f;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        Vector4 _value = DEFAULT_VALUE;

        bool _disabled;
        bool _invalid;
        bool _hovered;
        bool _open;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        // The picker (built once a panel is attached; not rebuilt per open).
        TweeqPopover _popover;
        VisualElement _pad;

        CubicBezierHandle _dragHandle = CubicBezierHandle.None;
        CubicBezierHandle _hoveredHandle = CubicBezierHandle.None;
        int _dragPointerId = PointerId.invalidPointerId;

        // Where Escape rolls the drag back to.
        Vector4 _valueOnDragStart = DEFAULT_VALUE;

        // Light dismiss also fires on a field press (the popover catches it via TrickleDown on the
        // panel root, so it closes before our own PointerDown runs). Suppressing only same-frame
        // reopens turns the interaction back into a toggle (same trick as ColorInput).
        bool _suppressReopen;
        IVisualElementScheduledItem _reopenGuardItem;
        bool _openOnPress;

        // The panel root the Escape guard is registered on. Held so the exact same element can be
        // unregistered even if the hierarchy moves.
        VisualElement _guardRoot;

        // Click detection for the field. Only Clicked is used; the field itself has no scrub gesture.
        readonly TweeqScrubManipulator _clickManipulator = new TweeqScrubManipulator();

        // Kept as instances so registering / unregistering never allocates a delegate.
        readonly EventCallback<KeyDownEvent> _onGuardKeyDown;

        #endregion

        #region Public API

        /// <summary>Fires once per drag, on release. Escape cancels instead, and does not fire this.</summary>
        public event Action<Vector4> Confirmed;

        /// <summary>
        /// The control points (x1, y1, x2, y2). Both axes are clamped to [0,1]; an assignment
        /// holding NaN / infinity keeps the current value instead.
        /// </summary>
        [UxmlAttribute]
        public Vector4 value
        {
            get => _value;
            set
            {
                if (!TrySanitize(value, out Vector4 next) || SameValue(_value, next))
                {
                    return;
                }

                Vector4 previous = _value;
                SetValueWithoutNotify(next);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>Sets the value without sending a ChangeEvent. Clamping and the NaN guard still apply.</summary>
        public void SetValueWithoutNotify(Vector4 newValue)
        {
            if (!TrySanitize(newValue, out Vector4 next))
            {
                return;
            }

            _value = next;
            Refresh();
        }

        /// <summary>The color theme. Falls back to Dark() when null is passed.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();

                if (_popover != null)
                {
                    _popover.Theme = _theme;
                }

                ApplyStaticStyles();
                Refresh();
            }
        }

        /// <summary>Whether the control is disabled. Closes the picker too if it is open.</summary>
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

                if (_disabled)
                {
                    // Leaving the picker open at the moment of disabling would leave no way to close it.
                    CancelDrag();
                    Close();
                }

                this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
                ApplyBackground();
                Refresh();
            }
        }

        /// <summary>Externally supplied invalid-value display (m7-disabled-invalid-spec.md: recolor only).</summary>
        [UxmlAttribute]
        public bool Invalid
        {
            get => _invalid;
            set
            {
                if (_invalid == value)
                {
                    return;
                }

                _invalid = value;
                Refresh();
            }
        }

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

        /// <summary>Whether the picker is open (logical state; holds even without a panel).</summary>
        public bool IsOpen => _open;

        /// <summary>The handle currently being dragged.</summary>
        public CubicBezierHandle ActiveHandle => _dragHandle;

        /// <summary>Opens the picker. Does nothing while disabled.</summary>
        public void Open()
        {
            if (_open || _disabled)
            {
                return;
            }

            _open = true;
            ShowPopover();
            Refresh();
        }

        /// <summary>
        /// Closes the picker. The value is kept — everything edited while open has already been
        /// notified incrementally, so this is not a rollback point (same contract as ColorInput).
        /// </summary>
        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _popover?.Close();
            ApplyBackground();
            Refresh();
        }

        /// <summary>
        /// Grabs a handle. Nothing moves yet — the value only follows once
        /// <see cref="UpdateDrag"/> arrives, so grabbing without moving leaves the curve untouched.
        /// </summary>
        public void BeginDrag(CubicBezierHandle handle)
        {
            if (_disabled || handle == CubicBezierHandle.None)
            {
                return;
            }

            _dragHandle = handle;
            _valueOnDragStart = _value;
            Refresh();
        }

        /// <summary>
        /// Moves the grabbed handle. <paramref name="u"/> / <paramref name="v"/> are normalized
        /// inside the pad, with <b>v pointing up</b> (Vue's <c>invlerp([left, bottom], [right, top])</c>).
        /// Both are clamped to [0,1]. Meant to be called on every pointermove, so a ChangeEvent goes
        /// out every time (no throttling, matching Vue).
        /// </summary>
        public void UpdateDrag(float u, float v)
        {
            if (_dragHandle == CubicBezierHandle.None || _disabled)
            {
                return;
            }

            float x = Clamp01(u);
            float y = Clamp01(v);

            Vector4 next = _value;

            if (_dragHandle == CubicBezierHandle.P1)
            {
                next.x = x;
                next.y = y;
            }
            else
            {
                next.z = x;
                next.w = y;
            }

            this.value = next;
        }

        /// <summary>Ends the drag and fires <see cref="Confirmed"/> exactly once.</summary>
        public void EndDrag()
        {
            if (_dragHandle == CubicBezierHandle.None)
            {
                return;
            }

            _dragHandle = CubicBezierHandle.None;
            Refresh();
            Confirmed?.Invoke(_value);
        }

        /// <summary>
        /// Ends the drag by reverting to the value it started from (Escape).
        /// <see cref="Confirmed"/> does not fire.
        /// </summary>
        public void CancelDrag()
        {
            if (_dragHandle == CubicBezierHandle.None)
            {
                return;
            }

            // Cleared before releasing the pointer, because releasing calls PointerCaptureOut back
            // in and that path would otherwise confirm the drag we are cancelling.
            _dragHandle = CubicBezierHandle.None;
            Vector4 restored = _valueOnDragStart;
            ReleasePadPointer();

            // The value was notified live during the drag, so the rollback has to be notified too.
            this.value = restored;
            Refresh();
        }

        /// <summary>
        /// Hit-tests the two handles in pad-local coordinates. Exposed so the picker geometry can be
        /// verified without a laid-out panel; the topmost handle (P2, drawn last in Vue) wins an overlap.
        /// </summary>
        public static CubicBezierHandle HitTestHandles(Vector4 curve, Rect padRect, Vector2 padLocalPoint)
        {
            if (!TryResolvePlot(padRect, out Rect plot, out float radius))
            {
                return CubicBezierHandle.None;
            }

            // The drawn disc plus half the stroke — in Vue the circle is filled, so the whole disc is clickable.
            float hitRadius = radius + HANDLE_LINE_WIDTH * 0.5f;

            Vector2 p2 = PlotPoint(plot, curve.z, curve.w);
            if ((padLocalPoint - p2).sqrMagnitude <= hitRadius * hitRadius)
            {
                return CubicBezierHandle.P2;
            }

            Vector2 p1 = PlotPoint(plot, curve.x, curve.y);
            if ((padLocalPoint - p1).sqrMagnitude <= hitRadius * hitRadius)
            {
                return CubicBezierHandle.P1;
            }

            return CubicBezierHandle.None;
        }

        /// <summary>
        /// Converts a pad-local point into clamped [0,1] uv, with v pointing up. Companion of
        /// <see cref="HitTestHandles"/>, exposed for the same reason.
        /// </summary>
        public static Vector2 PadToUv(Rect padRect, Vector2 padLocalPoint)
        {
            if (!TryResolvePlot(padRect, out Rect plot, out float _))
            {
                return Vector2.zero;
            }

            return new Vector2(
                Clamp01((padLocalPoint.x - plot.x) / plot.width),
                Clamp01(1f - (padLocalPoint.y - plot.y) / plot.height));
        }

        #endregion

        #region Construction

        public CubicBezierInput()
        {
            this.AddToClassList("tweeq-cubic-bezier-input");
            this.name = "tweeq-cubic-bezier-input";

            // The field itself holds focus so it can receive Enter / Space / Escape.
            this.focusable = true;
            this.style.flexShrink = 0f;
            this.style.overflow = Overflow.Hidden;

            _onGuardKeyDown = OnGuardKeyDown;

            this.generateVisualContent += OnGeneratePreview;

            _clickManipulator.Clicked += OnFieldClicked;
            this.AddManipulator(_clickManipulator);

            // The manipulator deliberately leaves PointerDown unhandled, so this can sit alongside it
            // and latch the open/closed state before light dismiss gets a chance to change it.
            this.RegisterCallback<PointerDownEvent>(OnFieldPointerDown);
            this.RegisterCallback<PointerEnterEvent>(OnFieldPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnFieldPointerLeave);
            this.RegisterCallback<KeyDownEvent>(OnFieldKeyDown);
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            ApplyStaticStyles();
        }

        void ApplyStaticStyles()
        {
            if (_theme == null)
            {
                return;
            }

            // Vue: width / height are both input-height, i.e. a square button.
            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;

            TweeqInputBoxStyles.ApplyBackgroundTransition(this, _theme);
            ApplyCornerRadius();
            ApplyBackground();
            ApplyPadStyles();
        }

        void ApplyCornerRadius()
        {
            TweeqInputBoxStyles.ApplyCornerRadius(this, _theme, _inlinePosition, _blockPosition);
        }

        void ApplyBackground()
        {
            if (_theme == null)
            {
                return;
            }

            if (_disabled)
            {
                TweeqInputBoxStyles.ApplyDisabledChrome(this, _theme, true);
                return;
            }

            TweeqInputBoxStyles.ApplyDisabledChrome(this, _theme, false);

            // Vue lights the button up both on hover and while open (`&:hover, &.open`).
            this.style.backgroundColor = TweeqInputBoxStyles.ResolveBackground(_theme, _hovered || _open);
        }

        #endregion

        #region Picker presentation

        void EnsurePadElement()
        {
            if (_pad != null)
            {
                return;
            }

            _pad = new VisualElement { name = "tweeq-cubic-bezier-pad" };
            _pad.style.flexShrink = 0f;
            _pad.generateVisualContent += OnGeneratePad;
            _pad.RegisterCallback<PointerDownEvent>(OnPadPointerDown);
            _pad.RegisterCallback<PointerMoveEvent>(OnPadPointerMove);
            _pad.RegisterCallback<PointerUpEvent>(OnPadPointerUp);
            _pad.RegisterCallback<PointerCaptureOutEvent>(OnPadPointerCaptureOut);
            _pad.RegisterCallback<PointerLeaveEvent>(OnPadPointerLeave);

            ApplyPadStyles();
        }

        void ApplyPadStyles()
        {
            if (_pad == null || _theme == null)
            {
                return;
            }

            // Vue sizes the floating panel as popup-width square; a Chrome=true popover draws
            // PopupPadding itself, so the pad gets what is left inside it.
            float size = Mathf.Max(0f, _theme.PopupWidth - _theme.PopupPadding * 2f);
            _pad.style.width = size;
            _pad.style.height = size;
        }

        void ShowPopover()
        {
            if (this.panel == null || _theme == null)
            {
                // Nowhere to place it without a panel. The logical state still advances; nothing throws.
                return;
            }

            EnsurePadElement();

            if (_popover == null)
            {
                // Escape / outside-click closing is left to the popover's own LightDismiss.
                _popover = new TweeqPopover
                {
                    Context = this,
                    Theme = _theme,
                    Arrow = false,
                    Chrome = true,
                    Placement = Tweeq.Core.PopoverPlacement.BottomStart,
                };
                _popover.Closed += OnPopoverClosed;
                _popover.Add(_pad);
            }

            _popover.Theme = _theme;
            _popover.Open(this);
        }

        void OnPopoverClosed()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _suppressReopen = true;

            if (this.panel != null)
            {
                if (_reopenGuardItem == null)
                {
                    _reopenGuardItem = this.schedule.Execute(ClearReopenGuard);
                }

                _reopenGuardItem.ExecuteLater(0L);
            }
            else
            {
                _suppressReopen = false;
            }

            ApplyBackground();
            Refresh();
        }

        void ClearReopenGuard()
        {
            _suppressReopen = false;
        }

        #endregion

        #region Field interaction

        // Light dismiss runs before this, so the open/closed state that decides the toggle has to be
        // latched at press time rather than read again on release.
        void OnFieldPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled)
            {
                return;
            }

            _openOnPress = _open || _suppressReopen;

            if (this.panel != null)
            {
                this.Focus();
            }
        }

        void OnFieldClicked()
        {
            if (_disabled)
            {
                return;
            }

            if (_openOnPress)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        void OnFieldPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            ApplyBackground();
        }

        void OnFieldPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            ApplyBackground();
        }

        void OnFieldKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    Toggle();
                    evt.StopPropagation();
                    return;

                case KeyCode.Escape:
                    if (_dragHandle != CubicBezierHandle.None)
                    {
                        CancelDrag();
                        evt.StopPropagation();
                    }
                    else if (_open)
                    {
                        Close();
                        evt.StopPropagation();
                    }

                    return;
            }
        }

        void Toggle()
        {
            if (_open)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            RegisterGuard();
            Refresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            UnregisterGuard();

            // Re-parenting interrupts the operation. Rolling back is the safe reading here, because
            // the popover goes away with it and there is no release event left to confirm on.
            CancelDrag();
            Close();

            _hovered = false;
            _hoveredHandle = CubicBezierHandle.None;
            _suppressReopen = false;
            _openOnPress = false;
        }

        // Escape reaches the popover's light dismiss (registered TrickleDown on the panel root)
        // before any handler of ours, and closing the popover would tear the pad out mid-drag —
        // which PointerCaptureOut would then report as a confirm. Registering on the outermost
        // element before the popover ever opens puts this first in line, so a drag can swallow
        // Escape and cancel instead of closing.
        void RegisterGuard()
        {
            VisualElement root = this.panel != null ? this.panel.visualTree : null;
            if (root == null || _guardRoot == root)
            {
                return;
            }

            UnregisterGuard();
            _guardRoot = root;
            _guardRoot.RegisterCallback(_onGuardKeyDown, TrickleDown.TrickleDown);
        }

        void UnregisterGuard()
        {
            if (_guardRoot == null)
            {
                return;
            }

            _guardRoot.UnregisterCallback(_onGuardKeyDown, TrickleDown.TrickleDown);
            _guardRoot = null;
        }

        void OnGuardKeyDown(KeyDownEvent evt)
        {
            if (evt == null
                || evt.keyCode != KeyCode.Escape
                || _dragHandle == CubicBezierHandle.None)
            {
                return;
            }

            CancelDrag();

            // Immediate, because light dismiss listens on this very same element: plain
            // StopPropagation only skips the elements further along the path, so the popover would
            // still close out from under a drag that was merely cancelled.
            evt.StopImmediatePropagation();
        }

        #endregion

        #region Picker interaction

        void OnPadPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled || _pad == null)
            {
                return;
            }

            Vector2 local = PadLocal(evt.position);
            CubicBezierHandle handle = HitTestHandles(_value, _pad.contentRect, local);
            if (handle == CubicBezierHandle.None)
            {
                // Empty pad space does nothing, exactly as in Vue (only a circle starts a drag).
                return;
            }

            _dragPointerId = evt.pointerId;
            BeginDrag(handle);

            if (this.panel != null)
            {
                _pad.CapturePointer(_dragPointerId);
            }

            evt.StopPropagation();
        }

        void OnPadPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || _pad == null)
            {
                return;
            }

            Vector2 local = PadLocal(evt.position);

            if (_dragHandle == CubicBezierHandle.None)
            {
                SetHoveredHandle(HitTestHandles(_value, _pad.contentRect, local));
                return;
            }

            if (evt.pointerId != _dragPointerId)
            {
                return;
            }

            Vector2 uv = PadToUv(_pad.contentRect, local);
            UpdateDrag(uv.x, uv.y);
            evt.StopPropagation();
        }

        void OnPadPointerUp(PointerUpEvent evt)
        {
            if (evt == null
                || _dragHandle == CubicBezierHandle.None
                || evt.pointerId != _dragPointerId)
            {
                return;
            }

            ReleasePadPointer();
            EndDrag();
            evt.StopPropagation();
        }

        // Losing the grab counts as finishing there. Rolling back stays Escape's job alone
        // (the same judgment ColorInput's picker drag makes).
        void OnPadPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            _dragPointerId = PointerId.invalidPointerId;
            EndDrag();
        }

        void OnPadPointerLeave(PointerLeaveEvent evt)
        {
            SetHoveredHandle(CubicBezierHandle.None);
        }

        void SetHoveredHandle(CubicBezierHandle handle)
        {
            if (_hoveredHandle == handle)
            {
                return;
            }

            _hoveredHandle = handle;
            _pad?.MarkDirtyRepaint();
        }

        void ReleasePadPointer()
        {
            int pointerId = _dragPointerId;
            _dragPointerId = PointerId.invalidPointerId;

            if (this.panel == null || _pad == null || pointerId == PointerId.invalidPointerId)
            {
                return;
            }

            if (_pad.HasPointerCapture(pointerId))
            {
                _pad.ReleasePointer(pointerId);
            }
        }

        Vector2 PadLocal(Vector3 worldPosition)
        {
            return _pad.WorldToLocal(new Vector2(worldPosition.x, worldPosition.y));
        }

        #endregion

        #region Value

        void NotifyValueChanged(Vector4 previous, Vector4 current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<Vector4> changeEvent = ChangeEvent<Vector4>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        void Refresh()
        {
            this.MarkDirtyRepaint();

            if (_open)
            {
                _pad?.MarkDirtyRepaint();
            }
        }

        #endregion

        #region Painting

        // Vue draws the preview as `M 0,0 C x1,y1 x2,y2 1,1` flipped on Y (SVG's y grows downward,
        // while the easing convention grows upward), scaled to fit with the viewBox's default
        // preserveAspectRatio — hence a centered square even when the field is stretched wide.
        void OnGeneratePreview(MeshGenerationContext context)
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

            Rect rect = this.contentRect;
            if (!IsUsableRect(rect))
            {
                return;
            }

            float side = Mathf.Min(rect.width, rect.height) - PREVIEW_INSET * 2f;
            if (side <= 0f)
            {
                return;
            }

            Rect plot = new Rect(
                (rect.width - side) * 0.5f,
                (rect.height - side) * 0.5f,
                side,
                side);

            painter.strokeColor = ResolveCurveColor();
            painter.lineWidth = PREVIEW_LINE_WIDTH;
            painter.lineCap = LineCap.Round;
            StrokeCurve(painter, plot, _value);
        }

        void OnGeneratePad(MeshGenerationContext context)
        {
            if (context == null || _theme == null || _pad == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            if (!TryResolvePlot(_pad.contentRect, out Rect plot, out float radius))
            {
                return;
            }

            Color accent = _theme.Accent;
            Vector2 p1 = PlotPoint(plot, _value.x, _value.y);
            Vector2 p2 = PlotPoint(plot, _value.z, _value.w);

            painter.lineCap = LineCap.Round;

            // Control lines from each fixed endpoint to its handle.
            painter.strokeColor = accent;
            painter.lineWidth = CONTROL_LINE_WIDTH;
            StrokeLine(painter, PlotPoint(plot, 0f, 0f), p1);
            StrokeLine(painter, PlotPoint(plot, 1f, 1f), p2);

            painter.lineWidth = CURVE_LINE_WIDTH;
            StrokeCurve(painter, plot, _value);

            PaintHandle(painter, p1, radius, IsHandleActive(CubicBezierHandle.P1));
            PaintHandle(painter, p2, radius, IsHandleActive(CubicBezierHandle.P2));
        }

        bool IsHandleActive(CubicBezierHandle handle)
        {
            return _dragHandle == handle
                || (_dragHandle == CubicBezierHandle.None && _hoveredHandle == handle);
        }

        // Vue fills the circle with the background color and swaps to accent on hover.
        void PaintHandle(Painter2D painter, Vector2 center, float radius, bool active)
        {
            painter.fillColor = active ? _theme.Accent : _theme.Background;
            painter.BeginPath();
            painter.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = _theme.Accent;
            painter.lineWidth = HANDLE_LINE_WIDTH;
            painter.BeginPath();
            painter.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            painter.ClosePath();
            painter.Stroke();
        }

        static void StrokeLine(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        static void StrokeCurve(Painter2D painter, Rect plot, Vector4 curve)
        {
            painter.BeginPath();
            painter.MoveTo(PlotPoint(plot, 0f, 0f));
            painter.BezierCurveTo(
                PlotPoint(plot, curve.x, curve.y),
                PlotPoint(plot, curve.z, curve.w),
                PlotPoint(plot, 1f, 1f));
            painter.Stroke();
        }

        // Disabled dims the accent down to TextSubtle and invalid recolors to Error, following the
        // shared rules in m7-disabled-invalid-spec.md rather than adding chrome of its own.
        Color ResolveCurveColor()
        {
            if (_disabled)
            {
                return _theme.TextSubtle;
            }

            return _invalid ? _theme.Error : _theme.Accent;
        }

        #endregion

        #region Geometry

        // The handles sit exactly on (0,0) and (1,1) at the extremes, so the unit square is inset by
        // one handle's outer radius. Without it the discs would be cut in half at the pad's edge.
        static bool TryResolvePlot(Rect padRect, out Rect plot, out float radius)
        {
            plot = default;
            radius = 0f;

            if (!IsUsableRect(padRect))
            {
                return false;
            }

            float size = Mathf.Min(padRect.width, padRect.height);
            radius = size * HANDLE_RADIUS_RATIO;

            float margin = radius + HANDLE_LINE_WIDTH * 0.5f;
            float side = size - margin * 2f;
            if (side <= 0f)
            {
                return false;
            }

            plot = new Rect(
                (padRect.width - side) * 0.5f,
                (padRect.height - side) * 0.5f,
                side,
                side);
            return true;
        }

        // y is flipped here: the value space grows upward, the panel's grows downward.
        static Vector2 PlotPoint(Rect plot, float x, float y)
        {
            return new Vector2(plot.x + x * plot.width, plot.y + (1f - y) * plot.height);
        }

        #endregion

        #region Helpers

        // A whole assignment is rejected when any component is non-finite, rather than patching that
        // one component: half-applying a broken vector would leave the curve in a state the caller
        // never asked for.
        static bool TrySanitize(Vector4 candidate, out Vector4 sanitized)
        {
            sanitized = default;

            if (!IsFinite(candidate.x)
                || !IsFinite(candidate.y)
                || !IsFinite(candidate.z)
                || !IsFinite(candidate.w))
            {
                return false;
            }

            sanitized = new Vector4(
                Clamp01(candidate.x),
                Clamp01(candidate.y),
                Clamp01(candidate.z),
                Clamp01(candidate.w));
            return true;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value))
            {
                return 0f;
            }

            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        // Vector4's == is an approximate comparison and swallows sub-epsilon moves, so components are compared exactly.
        static bool SameValue(Vector4 a, Vector4 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        }

        static bool IsUsableRect(Rect rect)
        {
            return !float.IsNaN(rect.width)
                && !float.IsNaN(rect.height)
                && rect.width > 0f
                && rect.height > 0f;
        }

        #endregion
    }
}
