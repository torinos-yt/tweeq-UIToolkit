using System;
using System.Collections.Generic;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A pannable / zoomable frame viewport with a custom scrollbar. Port of the Vue original's
    /// Timeline.vue. It is a primitive on its own: it does not embed a <see cref="TweeqRuler"/>,
    /// same as the original.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original hands the visible range to a slot and lets the host place its own children.
    /// UI Toolkit has no slots, so hosts add children to <see cref="contentContainer"/> and
    /// declare "this element lives at frame N" via <see cref="PinItem"/>; the timeline then keeps
    /// its translate (and width, when a length is given) in step with the visible range. Children
    /// that are never pinned are left alone, so the original's "host does its own placement from
    /// the visible range" style stays available.
    /// </para>
    /// <para>
    /// The visible window is stored as a start frame plus the pixels-per-frame zoom; the end is
    /// always derived from the viewport width, which is what the original's width/frameWidth watch
    /// amounts to. Pan, zoom and pin updates are on the per-frame path, so they allocate nothing.
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqTimeline : VisualElement, ITweeqThemed
    {
        #region Constants

        const float DEFAULT_HEIGHT = 96f;

        // The original's grid-template-rows uses --tq-scrollbar-width for the scrollbar band.
        const float SCROLLBAR_HEIGHT = 6f;

        // Vue: .knob background is color-mix(in srgb, var(--tq-color-text) 20%, transparent).
        const float KNOB_ALPHA = 0.2f;

        // In/Out banding. The lit side stays very faint so clips keep reading as the foreground.
        const float IN_OUT_FILL_ALPHA = 0.08f;
        const float IN_OUT_DIM_ALPHA = 0.35f;
        const float IN_OUT_LINE_WIDTH = 1f;

        // FocusInOut leaves this fraction of the In/Out span as breathing room on each side.
        const float FOCUS_MARGIN_RATIO = 0.05f;

        const int MIDDLE_BUTTON = 2;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        readonly VisualElement _container;
        readonly VisualElement _underlay;
        readonly VisualElement _content;
        readonly VisualElement _overlay;
        readonly VisualElement _scrollbar;
        readonly VisualElement _knob;

        double _rangeStart;
        double _rangeEnd = 100.0;
        double _frameWidth = TimelineLogic.DEFAULT_FRAME_WIDTH;
        double _frameWidthMin = TimelineLogic.DEFAULT_FRAME_WIDTH_MIN;
        double _frameWidthMax = TimelineLogic.DEFAULT_FRAME_WIDTH_MAX;
        double _overscroll = TimelineLogic.DEFAULT_OVERSCROLL;
        double _wheelSensitivity = 1.0;

        double _visibleStart;
        float _viewportWidth;

        double? _inPoint;
        double? _outPoint;

        // Reused so a pan (which touches every pin every frame) never allocates.
        readonly List<PinnedItem> _pinned = new List<PinnedItem>();

        int _panPointerId = PointerId.invalidPointerId;
        float _panOriginX;
        double _panOriginStart;

        int _knobPointerId = PointerId.invalidPointerId;
        float _knobOriginX;
        double _knobOriginStart;

        IVisualElementScheduledItem _confirmItem;
        bool _confirmPending;

        #endregion

        #region Public API

        /// <summary>Start of the whole content range, in frames. Default 0.</summary>
        [UxmlAttribute("range-start")]
        public double RangeStart
        {
            get => _rangeStart;
            set
            {
                if (!TweeqMath.IsFinite(value) || _rangeStart == value)
                {
                    return;
                }

                _rangeStart = value;
                Invalidate();
            }
        }

        /// <summary>End of the whole content range, in frames. Default 100.</summary>
        [UxmlAttribute("range-end")]
        public double RangeEnd
        {
            get => _rangeEnd;
            set
            {
                if (!TweeqMath.IsFinite(value) || _rangeEnd == value)
                {
                    return;
                }

                _rangeEnd = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Pixels per frame, i.e. the zoom. Clamped into
        /// [<see cref="FrameWidthMin"/>, <see cref="FrameWidthMax"/>]. Default 60.
        /// </summary>
        /// <remarks>
        /// Assigning here does not raise <see cref="FrameWidthChanged"/>: that event mirrors the
        /// original's <c>update:frameWidth</c>, which only reports the gesture back to the host.
        /// </remarks>
        [UxmlAttribute("frame-width")]
        public double FrameWidth
        {
            get => _frameWidth;
            set
            {
                double clamped = ClampFrameWidth(value);
                if (!TweeqMath.IsFinite(clamped) || _frameWidth == clamped)
                {
                    return;
                }

                _frameWidth = clamped;
                Invalidate();
            }
        }

        /// <summary>Lower zoom bound. Default 10.</summary>
        public double FrameWidthMin
        {
            get => _frameWidthMin;
            set
            {
                _frameWidthMin = value;
                this.FrameWidth = _frameWidth;
            }
        }

        /// <summary>Upper zoom bound. Default 100.</summary>
        public double FrameWidthMax
        {
            get => _frameWidthMax;
            set
            {
                _frameWidthMax = value;
                this.FrameWidth = _frameWidth;
            }
        }

        /// <summary>
        /// How far past the content you may scroll, as a fraction of the viewport. 0.5 means the
        /// content edge can travel to the middle of the view. Default 0.5.
        /// </summary>
        [UxmlAttribute]
        public double Overscroll
        {
            get => _overscroll;
            set
            {
                if (!TweeqMath.IsFinite(value) || _overscroll == value)
                {
                    return;
                }

                _overscroll = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Multiplies raw wheel deltas before they are read as pan pixels / zoom exponent. 1 is
        /// faithful to the original.
        /// </summary>
        /// <remarks>
        /// The original's coefficients assume browser wheel deltas (about 100 units per notch),
        /// while Unity reports single digits, which would make a notch of Alt+wheel move the zoom
        /// by well under a percent. This exists so an application can restore the intended feel
        /// without the widget silently baking in a platform-specific factor.
        /// </remarks>
        public double WheelSensitivity
        {
            get => _wheelSensitivity;
            set => _wheelSensitivity = TweeqMath.IsFinite(value) ? value : 1.0;
        }

        /// <summary>First frame of the visible window.</summary>
        public double VisibleStart => _visibleStart;

        /// <summary>Frame just past the visible window (derived from the viewport width).</summary>
        public double VisibleEnd => _visibleStart + VisibleFrames;

        /// <summary>How many frames fit in the viewport at the current zoom.</summary>
        public double VisibleFrames =>
            _frameWidth > 0.0 && _viewportWidth > 0f ? _viewportWidth / _frameWidth : 0.0;

        /// <summary>The viewport width in px that the frame/pixel mapping is based on.</summary>
        public float ViewportWidth => _viewportWidth;

        /// <summary>Fires whenever the visible window moves: pan, zoom, resize or navigation.</summary>
        public event Action VisibleRangeChanged;

        /// <summary>
        /// The live zoom value during a wheel zoom (the original's <c>update:frameWidth</c>).
        /// </summary>
        public event Action<double> FrameWidthChanged;

        /// <summary>
        /// Fires once the zoom settles, 300ms after the last wheel notch (the original's debounced
        /// <c>confirm</c>). Hosts use it to close a transaction opened on the first
        /// <see cref="FrameWidthChanged"/>.
        /// </summary>
        public event Action Confirmed;

        /// <summary>Fires when <see cref="InPoint"/> or <see cref="OutPoint"/> changes.</summary>
        public event Action InOutChanged;

        /// <summary>
        /// Start of the marked range, or null when unset. Not in the original; added for the DCC
        /// style use case. The timeline only stores, draws and focuses it — setting it from keys
        /// or drags is the host's job.
        /// </summary>
        public double? InPoint
        {
            get => _inPoint;
            set
            {
                if (Nullable.Equals(_inPoint, value))
                {
                    return;
                }

                _inPoint = value;
                RepaintBands();
                InOutChanged?.Invoke();
            }
        }

        /// <summary>End of the marked range, or null when unset.</summary>
        public double? OutPoint
        {
            get => _outPoint;
            set
            {
                if (Nullable.Equals(_outPoint, value))
                {
                    return;
                }

                _outPoint = value;
                RepaintBands();
                InOutChanged?.Invoke();
            }
        }

        /// <summary>
        /// Whether the In/Out band is drawn at all: both ends set, finite, and In no later than
        /// Out. A reversed pair is simply not drawn — misuse must never throw.
        /// </summary>
        public bool HasInOut =>
            _inPoint.HasValue && _outPoint.HasValue
            && TweeqMath.IsFinite(_inPoint.Value) && TweeqMath.IsFinite(_outPoint.Value)
            && _inPoint.Value <= _outPoint.Value;

        /// <summary>The color theme. Falls back to Dark() when null is passed.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyThemeStyles();

                // Composite parts owe their own children the theme (M7 propagation contract).
                TweeqThemeDistribution.Distribute(_content, _theme);
            }
        }

        /// <summary>The track area. Children added here are candidates for <see cref="PinItem"/>.</summary>
        public override VisualElement contentContainer => _content ?? this;

        /// <summary>
        /// Moves the window the least amount needed to reveal [start, end]. The zoom is untouched,
        /// and a range already on screen does not move it (the original's <c>showRange</c>).
        /// </summary>
        public void ShowRange(double start, double end)
        {
            (double newStart, double _) =
                TimelineLogic.BringIntoView(_visibleStart, this.VisibleEnd, start, end);

            SetVisibleStart(newStart);
        }

        /// <summary>Reveals a single frame. Same as the original's <c>showRange(number)</c>, i.e. [frame, frame+1].</summary>
        public void ShowFrame(double frame)
        {
            this.ShowRange(frame, frame + 1.0);
        }

        /// <summary>Scrolls so the frame sits at the horizontal center, keeping the zoom.</summary>
        public void CenterFrame(double frame)
        {
            SetVisibleStart(frame - this.VisibleFrames * 0.5);
        }

        /// <summary>
        /// Reveals the In/Out span with a 5% margin on each side. Does nothing while
        /// <see cref="HasInOut"/> is false.
        /// </summary>
        public void FocusInOut()
        {
            if (!this.HasInOut)
            {
                return;
            }

            double start = _inPoint.Value;
            double end = _outPoint.Value;
            double margin = (end - start) * FOCUS_MARGIN_RATIO;
            this.ShowRange(start - margin, end + margin);
        }

        /// <summary>The local x of a frame within the track area.</summary>
        public float FrameToLocalX(double frame)
        {
            if (_frameWidth <= 0.0 || !TweeqMath.IsFinite(frame))
            {
                return 0f;
            }

            return (float)((frame - _visibleStart) * _frameWidth);
        }

        /// <summary>The frame at a local x within the track area.</summary>
        public double LocalXToFrame(float x)
        {
            if (_frameWidth <= 0.0 || float.IsNaN(x))
            {
                return _visibleStart;
            }

            return _visibleStart + x / _frameWidth;
        }

        /// <summary>
        /// Declares that <paramref name="item"/> sits at <paramref name="frame"/>, so the timeline
        /// keeps its horizontal offset in step with the visible range. Passing
        /// <paramref name="lengthFrames"/> also drives its width, i.e. a clip.
        /// </summary>
        /// <remarks>
        /// The original's <c>rangeStyle</c> takes an inclusive [start, end] pair and widens it by
        /// one frame. This takes a length instead, so no frame is added; an inclusive pair maps to
        /// <c>PinItem(el, start, end - start + 1)</c>.
        /// </remarks>
        public void PinItem(VisualElement item, double frame, double? lengthFrames = null)
        {
            if (item == null)
            {
                return;
            }

            // The offset is written as a translate, so the element must be out of layout flow;
            // vertical placement and height stay entirely with the host.
            item.style.position = Position.Absolute;
            item.style.left = 0f;

            PinnedItem entry = new PinnedItem
            {
                Element = item,
                Frame = frame,
                Length = lengthFrames ?? 0.0,
                HasLength = lengthFrames.HasValue,
            };

            int index = IndexOfPin(item);
            if (index >= 0)
            {
                _pinned[index] = entry;
            }
            else
            {
                _pinned.Add(entry);
            }

            ApplyPin(in entry);
        }

        /// <summary>Stops tracking the element. Its last written offset and width are left in place.</summary>
        public void UnpinItem(VisualElement item)
        {
            int index = IndexOfPin(item);
            if (index >= 0)
            {
                _pinned.RemoveAt(index);
            }
        }

        /// <summary>
        /// Rebases the frame/pixel mapping on a viewport width. Normally driven by
        /// GeometryChangedEvent; public so a host (or a test) can drive it before layout has run.
        /// </summary>
        public void SetViewportWidth(float width)
        {
            float sanitized = float.IsNaN(width) || width < 0f ? 0f : width;
            if (_viewportWidth == sanitized)
            {
                return;
            }

            _viewportWidth = sanitized;
            Invalidate();
        }

        /// <summary>
        /// Emits a pending <see cref="Confirmed"/> right now instead of waiting out the debounce.
        /// Does nothing when no zoom is pending, so it is safe to call at any time.
        /// </summary>
        public void FlushPendingConfirm()
        {
            _confirmItem?.Pause();

            if (!_confirmPending)
            {
                return;
            }

            _confirmPending = false;
            Confirmed?.Invoke();
        }

        #endregion

        #region Construction

        public TweeqTimeline()
        {
            this.AddToClassList("tweeq-timeline");
            this.style.flexDirection = FlexDirection.Column;
            this.style.height = DEFAULT_HEIGHT;
            this.style.overflow = Overflow.Hidden;

            _container = new VisualElement { name = "tweeq-timeline-container" };
            _container.style.flexGrow = 1f;
            _container.style.overflow = Overflow.Hidden;
            this.hierarchy.Add(_container);

            // Three stacked layers so the lit In/Out band sits under the clips while the dimming
            // of everything outside it sits over them.
            _underlay = CreateLayer("tweeq-timeline-underlay");
            _underlay.generateVisualContent += OnGenerateUnderlay;
            _container.hierarchy.Add(_underlay);

            _content = CreateLayer("tweeq-timeline-content");

            // Ignoring picks only stops the layer itself from being hit; pinned children are
            // still pickable, and wheel / middle-drag on empty space reaches the timeline.
            _content.pickingMode = PickingMode.Ignore;
            _container.hierarchy.Add(_content);

            _overlay = CreateLayer("tweeq-timeline-overlay");
            _overlay.generateVisualContent += OnGenerateOverlay;
            _container.hierarchy.Add(_overlay);

            _scrollbar = new VisualElement { name = "tweeq-timeline-scrollbar" };
            _scrollbar.style.height = SCROLLBAR_HEIGHT;
            _scrollbar.style.flexShrink = 0f;
            this.hierarchy.Add(_scrollbar);

            _knob = new VisualElement { name = "tweeq-timeline-knob" };
            _knob.style.position = Position.Absolute;
            _knob.style.top = 0f;
            _knob.style.bottom = 0f;
            SetBorderRadius(_knob, SCROLLBAR_HEIGHT * 0.5f);
            _scrollbar.hierarchy.Add(_knob);

            ApplyThemeStyles();
            UpdateKnob();

            _container.RegisterCallback<GeometryChangedEvent>(OnContainerGeometryChanged);

            this.RegisterCallback<WheelEvent>(OnWheel);
            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            this.RegisterCallback<PointerUpEvent>(OnPointerUp);
            this.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);

            _knob.RegisterCallback<PointerDownEvent>(OnKnobPointerDown);
            _knob.RegisterCallback<PointerMoveEvent>(OnKnobPointerMove);
            _knob.RegisterCallback<PointerUpEvent>(OnKnobPointerUp);
            _knob.RegisterCallback<PointerCaptureOutEvent>(OnKnobCaptureOut);

            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        static VisualElement CreateLayer(string name)
        {
            VisualElement layer = new VisualElement { name = name };
            layer.style.position = Position.Absolute;
            layer.style.left = 0f;
            layer.style.top = 0f;
            layer.style.right = 0f;
            layer.style.bottom = 0f;
            return layer;
        }

        #endregion

        #region Input

        void OnWheel(WheelEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            Vector3 delta = evt.delta;

            if ((evt.modifiers & EventModifiers.Alt) != 0)
            {
                ApplyZoom(delta.y * _wheelSensitivity, PointerLocalX(evt.mousePosition));
                evt.StopPropagation();
                return;
            }

            // The original only pans from the horizontal axis, since it assumes a trackpad. A mouse
            // produces the vertical axis only, so that falls through to panning as well.
            float raw = delta.x != 0f ? delta.x : delta.y;
            if (raw == 0f)
            {
                return;
            }

            PanByPixels(raw * _wheelSensitivity);
            evt.StopPropagation();
        }

        // Middle-drag panning is not in the original; it is the DCC convention this port targets.
        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != MIDDLE_BUTTON
                || _panPointerId != PointerId.invalidPointerId)
            {
                return;
            }

            _panPointerId = evt.pointerId;
            _panOriginX = evt.position.x;
            _panOriginStart = _visibleStart;

            if (this.panel != null)
            {
                this.CapturePointer(_panPointerId);
            }

            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || _panPointerId == PointerId.invalidPointerId
                || evt.pointerId != _panPointerId || _frameWidth <= 0.0)
            {
                return;
            }

            // The content must follow the pointer, so moving right pulls the start backwards.
            double delta = (evt.position.x - _panOriginX) / _frameWidth;
            SetVisibleStartClamped(_panOriginStart - delta);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt == null || evt.pointerId != _panPointerId)
            {
                return;
            }

            EndPan();
            evt.StopPropagation();
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            EndPan();
        }

        void EndPan()
        {
            if (_panPointerId == PointerId.invalidPointerId)
            {
                return;
            }

            int pointerId = _panPointerId;
            _panPointerId = PointerId.invalidPointerId;

            if (this.panel != null && this.HasPointerCapture(pointerId))
            {
                this.ReleasePointer(pointerId);
            }
        }

        void OnKnobPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _knobPointerId != PointerId.invalidPointerId)
            {
                return;
            }

            _knobPointerId = evt.pointerId;
            _knobOriginX = evt.position.x;
            _knobOriginStart = _visibleStart;

            if (_knob.panel != null)
            {
                _knob.CapturePointer(_knobPointerId);
            }

            evt.StopPropagation();
        }

        void OnKnobPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || _knobPointerId == PointerId.invalidPointerId
                || evt.pointerId != _knobPointerId)
            {
                return;
            }

            float track = _scrollbar.contentRect.width;
            if (float.IsNaN(track) || track <= 0f)
            {
                return;
            }

            // The knob's center spans the full scrollable travel, so a track fraction maps
            // straight onto that travel.
            (double minStart, double maxStart) = TimelineLogic.ScrollBounds(
                _rangeStart, _rangeEnd, this.VisibleFrames, _overscroll);

            double travel = maxStart - minStart;
            if (!TweeqMath.IsFinite(travel) || travel <= 0.0)
            {
                return;
            }

            double delta = (evt.position.x - _knobOriginX) / track * travel;
            SetVisibleStartClamped(_knobOriginStart + delta);
            evt.StopPropagation();
        }

        void OnKnobPointerUp(PointerUpEvent evt)
        {
            if (evt == null || evt.pointerId != _knobPointerId)
            {
                return;
            }

            EndKnobDrag();
            evt.StopPropagation();
        }

        void OnKnobCaptureOut(PointerCaptureOutEvent evt)
        {
            EndKnobDrag();
        }

        void EndKnobDrag()
        {
            if (_knobPointerId == PointerId.invalidPointerId)
            {
                return;
            }

            int pointerId = _knobPointerId;
            _knobPointerId = PointerId.invalidPointerId;

            if (_knob.panel != null && _knob.HasPointerCapture(pointerId))
            {
                _knob.ReleasePointer(pointerId);
            }
        }

        void OnContainerGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt == null || !ReferenceEquals(evt.target, _container))
            {
                return;
            }

            SetViewportWidth(evt.newRect.width);
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (evt != null && !ReferenceEquals(evt.target, this))
            {
                return;
            }

            // A gesture must never survive the panel it started on.
            EndPan();
            EndKnobDrag();
            _confirmItem?.Pause();
        }

        float PointerLocalX(Vector2 panelPosition)
        {
            return _container.WorldToLocal(panelPosition).x;
        }

        #endregion

        #region Pan / zoom

        /// <summary>Pans by a pixel amount, as the original does with a horizontal scroll.</summary>
        public void PanByPixels(double pixels)
        {
            if (_frameWidth <= 0.0 || !TweeqMath.IsFinite(pixels))
            {
                return;
            }

            SetVisibleStartClamped(_visibleStart + pixels / _frameWidth);
        }

        /// <summary>
        /// Applies one zoom step around a viewport-local x, exactly as Alt+scroll does. Raises
        /// <see cref="FrameWidthChanged"/> and restarts the <see cref="Confirmed"/> debounce even
        /// when the zoom was already pinned at a bound, matching the original's unconditional emit.
        /// </summary>
        public void ApplyZoom(double wheelDelta, float anchorX)
        {
            if (!TweeqMath.IsFinite(wheelDelta))
            {
                return;
            }

            // Scrolling up (a negative delta in UI Toolkit) has to zoom in, hence the negated
            // exponent against the original, which is written for the browser's sign convention.
            double factor = Math.Pow(TimelineLogic.ZOOM_BASE, -wheelDelta);
            double newFrameWidth = ClampFrameWidth(_frameWidth * factor);
            if (!TweeqMath.IsFinite(newFrameWidth) || newFrameWidth <= 0.0)
            {
                return;
            }

            double visibleFrames = this.VisibleFrames;
            _frameWidth = newFrameWidth;
            double newVisibleFrames = this.VisibleFrames;

            double anchorT = _viewportWidth > 0f ? anchorX / _viewportWidth : 0.5;
            double start = TimelineLogic.ZoomAroundAnchor(
                _visibleStart, visibleFrames, newVisibleFrames, anchorT);

            (double clamped, double _) = TimelineLogic.ClampRange(
                start, start + newVisibleFrames, _rangeStart, _rangeEnd, _overscroll);

            _visibleStart = clamped;

            FrameWidthChanged?.Invoke(_frameWidth);
            RequestConfirm();
            Invalidate();
        }

        void SetVisibleStartClamped(double start)
        {
            (double clamped, double _) = TimelineLogic.ClampRange(
                start, start + this.VisibleFrames, _rangeStart, _rangeEnd, _overscroll);

            SetVisibleStart(clamped);
        }

        void SetVisibleStart(double start)
        {
            if (!TweeqMath.IsFinite(start) || _visibleStart == start)
            {
                return;
            }

            _visibleStart = start;
            Invalidate();
        }

        double ClampFrameWidth(double value)
        {
            if (!TweeqMath.IsFinite(value))
            {
                return _frameWidth;
            }

            return TweeqMath.Clamp(value, _frameWidthMin, _frameWidthMax);
        }

        void RequestConfirm()
        {
            _confirmPending = true;

            if (_confirmItem == null)
            {
                _confirmItem = this.schedule.Execute(FlushPendingConfirm);
            }

            // ExecuteLater restarts the countdown, so only the last notch of a burst survives.
            _confirmItem.ExecuteLater(TimelineLogic.CONFIRM_DEBOUNCE_MS);
        }

        #endregion

        #region Pinned children

        struct PinnedItem
        {
            public VisualElement Element;
            public double Frame;
            public double Length;
            public bool HasLength;
        }

        int IndexOfPin(VisualElement item)
        {
            for (int index = 0; index < _pinned.Count; index++)
            {
                if (ReferenceEquals(_pinned[index].Element, item))
                {
                    return index;
                }
            }

            return -1;
        }

        void ApplyPinned()
        {
            for (int index = 0; index < _pinned.Count; index++)
            {
                PinnedItem entry = _pinned[index];
                ApplyPin(in entry);
            }
        }

        void ApplyPin(in PinnedItem entry)
        {
            if (entry.Element == null)
            {
                return;
            }

            entry.Element.style.translate = new Translate(FrameToLocalX(entry.Frame), 0f);

            if (entry.HasLength)
            {
                entry.Element.style.width = (float)(entry.Length * _frameWidth);
            }
        }

        #endregion

        #region Presentation

        void Invalidate()
        {
            ApplyPinned();
            UpdateKnob();

            // The band is the only painted thing that moves with the range, so a timeline without
            // In/Out never regenerates a mesh while panning.
            if (this.HasInOut)
            {
                RepaintBands();
            }

            VisibleRangeChanged?.Invoke();
        }

        void RepaintBands()
        {
            _underlay.MarkDirtyRepaint();
            _overlay.MarkDirtyRepaint();
        }

        void UpdateKnob()
        {
            (double leftT, double widthT) = TimelineLogic.ScrollbarKnob(
                _visibleStart, this.VisibleEnd, _rangeStart, _rangeEnd, _overscroll);

            if (!TweeqMath.IsFinite(leftT) || !TweeqMath.IsFinite(widthT))
            {
                return;
            }

            _knob.style.left = Length.Percent((float)(leftT * 100.0));
            _knob.style.width = Length.Percent((float)(widthT * 100.0));
        }

        void ApplyThemeStyles()
        {
            // The original leaves the track transparent, but a widget that draws nothing on its
            // own reads as broken in isolation, so the track carries the input surface color.
            _container.style.backgroundColor = _theme.Input;

            Color knob = _theme.Text;
            knob.a = KNOB_ALPHA;
            _knob.style.backgroundColor = knob;

            RepaintBands();
        }

        static void SetBorderRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        #endregion

        #region Painting

        void OnGenerateUnderlay(MeshGenerationContext context)
        {
            if (!this.HasInOut)
            {
                return;
            }

            Painter2D painter = context?.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _underlay.contentRect;
            if (!IsDrawable(rect))
            {
                return;
            }

            float left = Mathf.Max(FrameToLocalX(_inPoint.Value), 0f);
            float right = Mathf.Min(FrameToLocalX(_outPoint.Value), rect.width);
            if (right <= left)
            {
                return;
            }

            Color fill = _theme.Surface;
            fill.a = IN_OUT_FILL_ALPHA;
            FillRect(painter, fill, left, 0f, right - left, rect.height);
        }

        void OnGenerateOverlay(MeshGenerationContext context)
        {
            if (!this.HasInOut)
            {
                return;
            }

            Painter2D painter = context?.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _overlay.contentRect;
            if (!IsDrawable(rect))
            {
                return;
            }

            float inX = FrameToLocalX(_inPoint.Value);
            float outX = FrameToLocalX(_outPoint.Value);

            Color dim = _theme.Background;
            dim.a = IN_OUT_DIM_ALPHA;

            float leftEdge = Mathf.Min(inX, rect.width);
            if (leftEdge > 0f)
            {
                FillRect(painter, dim, 0f, 0f, leftEdge, rect.height);
            }

            float rightEdge = Mathf.Max(outX, 0f);
            if (rightEdge < rect.width)
            {
                FillRect(painter, dim, rightEdge, 0f, rect.width - rightEdge, rect.height);
            }

            painter.strokeColor = _theme.Accent;
            painter.lineWidth = IN_OUT_LINE_WIDTH;
            painter.lineCap = LineCap.Butt;
            StrokeVerticalLine(painter, inX, rect);
            StrokeVerticalLine(painter, outX, rect);
        }

        static bool IsDrawable(Rect rect)
        {
            return !float.IsNaN(rect.width) && !float.IsNaN(rect.height)
                && rect.width > 0f && rect.height > 0f;
        }

        static void FillRect(Painter2D painter, Color color, float x, float y, float width, float height)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + width, y));
            painter.LineTo(new Vector2(x + width, y + height));
            painter.LineTo(new Vector2(x, y + height));
            painter.ClosePath();
            painter.Fill();
        }

        static void StrokeVerticalLine(Painter2D painter, float x, Rect rect)
        {
            if (x < 0f || x > rect.width)
            {
                return;
            }

            painter.BeginPath();
            painter.MoveTo(new Vector2(x, 0f));
            painter.LineTo(new Vector2(x, rect.height));
            painter.Stroke();
        }

        #endregion
    }
}
