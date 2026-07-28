using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Tweeq.Core is noEngineReferences, so it keeps Rect / Vector2 as its own double-based structs.
// `using Tweeq.Core;` would collide entirely with the same-named UnityEngine types, so pull them in only via aliases
using CorePlacement = Tweeq.Core.PopoverPlacement;
using CoreRect = Tweeq.Core.TweeqRect;
using CoreVector2 = Tweeq.Core.TweeqVec2;
using PopoverLogic = Tweeq.Core.PopoverLogic;
using PopoverResult = Tweeq.Core.PopoverResult;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A display-only popover that follows an anchor and floats above <see cref="TweeqOverlayLayer"/>.
    /// It has no trigger of its own (open is controlled externally, same as the Vue version). Content can be added
    /// normally with Add, and it goes into the internal <see cref="TweeqBalloon"/>.
    /// </summary>
    [UxmlElement]
    public partial class TweeqPopover : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>Margin reserved at the viewport edge (px). Corresponds to Popover.vue's VIEWPORT_MARGIN.</summary>
        public const float DEFAULT_VIEWPORT_MARGIN = 8f;

        // Shadow for arrow-less popups (Dropdown, etc). Equivalent to common.styl's box-shadow 0 0 20px
        const float POPUP_SHADOW_BLUR = 20f;
        const float POPUP_SHADOW_OFFSET_Y = 0f;

        #endregion

        #region Fields

        // The transition definition is immutable, so create just one and share it across all instances
        // (style.transition* requires a List every time, so allocating new each open would generate garbage on every open)
        static readonly StyleList<StylePropertyName> OpacityProperty =
            new StyleList<StylePropertyName>(new List<StylePropertyName> { new StylePropertyName("opacity") });

        static readonly StyleList<EasingFunction> EaseOut =
            new StyleList<EasingFunction>(new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) });

        static readonly StyleList<TimeValue> InstantDuration =
            new StyleList<TimeValue>(new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) });

        TweeqTheme _theme = TweeqTheme.Dark();
        TweeqBalloon _balloon;

        // Duration derived from the theme. Rebuilt only when the theme is swapped
        StyleList<TimeValue> _fadeDuration;

        TweeqOverlayLayer _layer;
        VisualElement _root;
        VisualElement _anchor;

        bool _isOpen;
        bool _useFixedPosition;
        Vector2 _fixedPosition;
        bool _arrow = true;
        bool _chrome = true;

        CorePlacement _placement = CorePlacement.BottomStart;
        double _offsetMain;
        double _offsetCross;
        float _viewportMargin = DEFAULT_VIEWPORT_MARGIN;

        // Method group conversion allocates a delegate every time, so hold onto instances reused across register/unregister
        // The same handler is reused for both the anchor and the layer (whichever moves, the request is just "reposition")
        readonly EventCallback<GeometryChangedEvent> _onWatchedGeometryChanged;
        readonly EventCallback<DetachFromPanelEvent> _onAnchorDetached;
        readonly EventCallback<PointerDownEvent> _onRootPointerDown;
        readonly EventCallback<KeyDownEvent> _onRootKeyDown;

        // The layer being watched. Kept separate from _layer so it can reliably be unregistered on close
        TweeqOverlayLayer _watchedLayer;

        // Reuses a single scheduled item for "wait one frame for size to settle, then reposition + fade in"
        IVisualElementScheduledItem _settleItem;

        #endregion

        #region Public API

        /// <summary>Fires once when Close() actually closes the popover.</summary>
        public event Action Closed;

        /// <summary>Whether the popover is open.</summary>
        public bool IsOpen => _isOpen;

        /// <summary>The balloon body itself. Touch this when you want to tune radius, padding, or shadow individually.</summary>
        public TweeqBalloon Balloon => _balloon;

        /// <summary>
        /// The owner element used to resolve the panel. Since <see cref="Open(Vector2)"/> has no anchor,
        /// the overlay layer is traced from this or from the previous anchor.
        /// </summary>
        public VisualElement Context { get; set; }

        /// <summary>
        /// Whether the popover side draws the balloon chrome (surface, border, padding, shadow) (default true).
        /// false is a pass-through mode that only hosts and opens/closes on the overlay layer; drawing the chrome becomes
        /// the content's own responsibility (Dropdown draws its own chrome to align row width and field position).
        /// Since the content's parent changes, set this before Add.
        /// </summary>
        [UxmlAttribute("chrome")]
        public bool Chrome
        {
            get => _chrome;
            set
            {
                if (_chrome == value)
                {
                    return;
                }

                _chrome = value;

                if (_balloon != null)
                {
                    _balloon.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
        }

        /// <summary>Whether to show the arrow. false makes it a rounded-rect popup (for Dropdown).</summary>
        [UxmlAttribute("arrow")]
        public bool Arrow
        {
            get => _arrow;
            set
            {
                if (_arrow == value)
                {
                    return;
                }

                _arrow = value;
                ApplyShadowStyle();

                if (!_arrow && _balloon != null)
                {
                    _balloon.ArrowSide = TweeqArrowSide.None;
                }

                Reposition();
            }
        }

        /// <summary>The desired placement. Defaults to BottomStart. Automatically flips / shifts at screen edges.</summary>
        [UxmlAttribute("placement")]
        public CorePlacement Placement
        {
            get => _placement;
            set
            {
                _placement = value;
                Reposition();
            }
        }

        /// <summary>Additional offset along the main axis (the direction away from the anchor).</summary>
        public double OffsetMain
        {
            get => _offsetMain;
            set
            {
                _offsetMain = value;
                Reposition();
            }
        }

        /// <summary>Additional offset along the cross axis (the direction along the edge).</summary>
        public double OffsetCross
        {
            get => _offsetCross;
            set
            {
                _offsetCross = value;
                Reposition();
            }
        }

        /// <summary>Margin reserved at the viewport edge (px).</summary>
        public float ViewportMargin
        {
            get => _viewportMargin;
            set
            {
                _viewportMargin = value;
                Reposition();
            }
        }

        /// <summary>
        /// Whether to close automatically on outside click / Escape (default true).
        /// When false, closing becomes the owner's responsibility (for nesting / Dropdown).
        /// </summary>
        [UxmlAttribute("light-dismiss")]
        public bool LightDismiss { get; set; } = true;

        /// <summary>The color theme. Falls back to Dark() when null is passed.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();

                if (_balloon != null)
                {
                    _balloon.Theme = _theme;
                }

                ApplyFadeTransition();
            }
        }

        /// <summary>Content goes into the balloon's content layer (directly under the popover when Chrome=false).</summary>
        public override VisualElement contentContainer
            => _chrome && _balloon != null ? _balloon.contentContainer : this;

        /// <summary>Opens the popover, following the anchor element.</summary>
        public void Open(VisualElement anchor)
        {
            if (anchor == null)
            {
                return;
            }

            UnwatchAnchor();
            _anchor = anchor;
            _useFixedPosition = false;

            if (Context == null)
            {
                Context = anchor;
            }

            OpenInternal(anchor);
        }

        /// <summary>Opens at an explicit panel-space position (e.g. Dropdown's macOS-style placement).</summary>
        public void Open(Vector2 position)
        {
            Open(position, Context ?? _anchor);
        }

        /// <summary>Opens at an explicit panel-space position. context is used only to trace the overlay layer.</summary>
        public void Open(Vector2 position, VisualElement context)
        {
            if (context == null)
            {
                return;
            }

            UnwatchAnchor();
            _anchor = null;
            _useFixedPosition = true;
            _fixedPosition = position;
            Context = context;

            OpenInternal(context);
        }

        /// <summary>Closes the popover. Does nothing if it isn't open (Closed doesn't fire either).</summary>
        public void Close()
        {
            if (!_isOpen)
            {
                return;
            }

            // RemoveFromHierarchy triggers DetachFromPanel, so flip this down first to prevent reentrancy
            _isOpen = false;

            _settleItem?.Pause();
            UnwatchRoot();
            UnwatchAnchor();
            this.RemoveFromHierarchy();

            _layer = null;
            _anchor = null;

            Closed?.Invoke();
        }

        /// <summary>
        /// Whether a pointer operation on that element counts as an "outside click" (i.e. a target for light-dismiss closing).
        /// </summary>
        /// <remarks>
        /// Only its own content and <b>nested popovers</b> (e.g. a Dropdown's list inside a picker,
        /// which opens as a sibling on the overlay layer) are not outside. Even inside the layer,
        /// <see cref="TweeqModal"/>'s backdrop / pane are not popovers, so they count as outside,
        /// which lets a click inside a modal correctly close a nested popover.
        /// </remarks>
        public bool IsOutsideClick(VisualElement target)
        {
            if (target == null)
            {
                return true;
            }

            if (target == this || this.Contains(target))
            {
                return false;
            }

            // Exempt only when another TweeqPopover exists while walking up from target to the layer
            // (even within the layer, a path with only backdrop / pane counts as outside = closes).
            // A target outside the layer is traced all the way to the root, but since a popover
            // is only on the layer while open, this doesn't cause false positives
            for (VisualElement node = target; node != null && node != _layer; node = node.hierarchy.parent)
            {
                if (node is TweeqPopover)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Reposition to call when the anchor or size changes. Does nothing if not open.</summary>
        public void Reposition()
        {
            if (!_isOpen || _layer == null || _balloon == null)
            {
                return;
            }

            float width = this.layout.width;
            float height = this.layout.height;
            if (!IsUsableSize(width) || !IsUsableSize(height))
            {
                // Layout hasn't settled yet. Will be called again by GeometryChanged
                return;
            }

            if (_useFixedPosition)
            {
                _balloon.ArrowSide = TweeqArrowSide.None;
                this.style.left = _fixedPosition.x;
                this.style.top = _fixedPosition.y;
                return;
            }

            // Don't leave it open after the anchor is destroyed / detached
            if (_anchor == null || _anchor.panel == null)
            {
                return;
            }

            Rect anchorRect = _anchor.worldBound;
            Rect viewport = _layer.layout;
            if (!IsUsableSize(viewport.width) || !IsUsableSize(viewport.height))
            {
                return;
            }

            PopoverResult result = PopoverLogic.Resolve(
                new CoreRect(anchorRect.x, anchorRect.y, anchorRect.width, anchorRect.height),
                new CoreVector2(width, height),
                new CoreVector2(viewport.width, viewport.height),
                _placement,
                _offsetMain,
                _offsetCross,
                _viewportMargin);

            this.style.left = (float)result.X;
            this.style.top = (float)result.Y;

            _balloon.ArrowSide = _arrow ? ToArrowSide(result.ArrowSide) : TweeqArrowSide.None;
            _balloon.ArrowOffset = (float)result.ArrowOffset;
        }

        #endregion

        #region Construction

        public TweeqPopover()
        {
            this.name = "tweeq-popover";
            this.style.position = Position.Absolute;
            this.style.left = 0f;
            this.style.top = 0f;
            this.style.overflow = Overflow.Visible;

            // Width follows content so the shadow and arrow don't get clipped
            this.style.alignItems = Align.FlexStart;

            _balloon = new TweeqBalloon { Theme = _theme };
            this.hierarchy.Add(_balloon);

            _onWatchedGeometryChanged = OnWatchedGeometryChanged;
            _onAnchorDetached = OnAnchorDetached;
            _onRootPointerDown = OnRootPointerDown;
            _onRootKeyDown = OnRootKeyDown;

            this.RegisterCallback<GeometryChangedEvent>(OnSelfGeometryChanged);
            this.RegisterCallback<DetachFromPanelEvent>(OnSelfDetached);

            ApplyShadowStyle();
            ApplyFadeTransition();
        }

        #endregion

        #region Open / Close internals

        void OpenInternal(VisualElement context)
        {
            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(context);
            if (layer == null)
            {
                // No place to put it when not attached to a panel. Don't throw, just treat it as "didn't open"
                return;
            }

            // Reopening while already open just swaps the anchor without firing Closed.
            // A tooltip's "handoff" between anchors goes through here, and replaying the appear animation would cause flicker
            bool wasOpen = _isOpen;

            _layer = layer;
            if (this.hierarchy.parent != layer)
            {
                // Flip to closed state first so the Detach from the re-parenting doesn't trigger OnSelfDetached → Close()
                _isOpen = false;
                this.RemoveFromHierarchy();
                layer.Add(this);
            }

            _isOpen = true;

            WatchAnchor();
            WatchRoot();

            // As in Vue, resolve once the instant it opens, then settle again one frame later with the final size.
            // If the arrow position isn't decided on the first frame, the balloon's scale origin ends up off
            if (!wasOpen)
            {
                // From the second time onward, opacity remains at 1, so setting it to 0 directly
                // would run the "disappear" animation first. Apply the starting value with duration 0
                this.style.transitionDuration = InstantDuration;
                this.style.opacity = 0f;
            }

            Reposition();

            if (!wasOpen && _chrome)
            {
                _balloon.PlayIn();
            }

            if (_settleItem == null)
            {
                _settleItem = this.schedule.Execute(Settle);
            }

            _settleItem.ExecuteLater(0L);
        }

        void Settle()
        {
            if (!_isOpen)
            {
                return;
            }

            Reposition();

            this.style.transitionDuration = _fadeDuration;
            this.style.opacity = 1f;
        }

        void WatchAnchor()
        {
            if (_anchor == null)
            {
                return;
            }

            _anchor.RegisterCallback(_onWatchedGeometryChanged);
            _anchor.RegisterCallback(_onAnchorDetached);
        }

        void UnwatchAnchor()
        {
            if (_anchor == null)
            {
                return;
            }

            _anchor.UnregisterCallback(_onWatchedGeometryChanged);
            _anchor.UnregisterCallback(_onAnchorDetached);
        }

        // Light dismiss is caught via TrickleDown on the panel root. Since the popover itself
        // lives on the overlay layer, normal bubbling never delivers an outside click.
        // Layer resize (i.e. viewport changes) can happen without moving the anchor, so watch it separately
        void WatchRoot()
        {
            if (_layer == null)
            {
                return;
            }

            if (_watchedLayer != _layer)
            {
                _watchedLayer?.UnregisterCallback(_onWatchedGeometryChanged);
                _watchedLayer = _layer;
                _watchedLayer.RegisterCallback(_onWatchedGeometryChanged);
            }

            if (_root != null)
            {
                return;
            }

            VisualElement root = _layer.hierarchy.parent;
            if (root == null)
            {
                return;
            }

            _root = root;
            _root.RegisterCallback(_onRootPointerDown, TrickleDown.TrickleDown);
            _root.RegisterCallback(_onRootKeyDown, TrickleDown.TrickleDown);
        }

        void UnwatchRoot()
        {
            if (_watchedLayer != null)
            {
                _watchedLayer.UnregisterCallback(_onWatchedGeometryChanged);
                _watchedLayer = null;
            }

            if (_root == null)
            {
                return;
            }

            _root.UnregisterCallback(_onRootPointerDown, TrickleDown.TrickleDown);
            _root.UnregisterCallback(_onRootKeyDown, TrickleDown.TrickleDown);
            _root = null;
        }

        #endregion

        #region Events

        void OnSelfGeometryChanged(GeometryChangedEvent evt)
        {
            // Rewriting left/top triggers this event again, but next time the value is unchanged so it converges
            Reposition();
        }

        void OnSelfDetached(DetachFromPanelEvent evt)
        {
            // Don't leave watchers registered even when removed as a whole tree from outside
            if (!_isOpen)
            {
                return;
            }

            Close();
        }

        void OnWatchedGeometryChanged(GeometryChangedEvent evt)
        {
            Reposition();
        }

        void OnAnchorDetached(DetachFromPanelEvent evt)
        {
            Close();
        }

        void OnRootPointerDown(PointerDownEvent evt)
        {
            if (!_isOpen || !LightDismiss || evt == null)
            {
                return;
            }

            // "Don't close if inside the layer" would mean that once a modal is on the layer, a click
            // inside the modal would fail to close a nested popover. The decision is centralized in IsOutsideClick
            if (evt.target is VisualElement target && !IsOutsideClick(target))
            {
                return;
            }

            Close();
        }

        void OnRootKeyDown(KeyDownEvent evt)
        {
            if (!_isOpen || !LightDismiss || evt == null || evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            Close();
            evt.StopPropagation();
        }

        #endregion

        #region Presentation

        void ApplyShadowStyle()
        {
            if (_balloon == null)
            {
                return;
            }

            // An arrowed popover is "pointing", so it gets a shallow, close shadow; an arrow-less panel gets a wide, soft shadow
            _balloon.ShadowBlur = _arrow ? TweeqBalloon.DEFAULT_SHADOW_BLUR : POPUP_SHADOW_BLUR;
            _balloon.ShadowOffsetY = _arrow ? TweeqBalloon.DEFAULT_SHADOW_OFFSET_Y : POPUP_SHADOW_OFFSET_Y;
        }

        // Transition is set up only at init time (and on theme swap). Touching it every frame would allocate a StyleList
        void ApplyFadeTransition()
        {
            float duration = _theme != null ? _theme.ActiveTransitionDuration : 0.064f;

            _fadeDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(duration, TimeUnit.Second) });

            this.style.transitionProperty = OpacityProperty;
            this.style.transitionTimingFunction = EaseOut;
            this.style.transitionDuration = _fadeDuration;
        }

        #endregion

        #region Helpers

        // PopoverResult.ArrowSide: 0=Top 1=Bottom 2=Left 3=Right
        static TweeqArrowSide ToArrowSide(int side)
        {
            switch (side)
            {
                case 0:
                    return TweeqArrowSide.Top;
                case 1:
                    return TweeqArrowSide.Bottom;
                case 2:
                    return TweeqArrowSide.Left;
                case 3:
                    return TweeqArrowSide.Right;
                default:
                    return TweeqArrowSide.None;
            }
        }

        static bool IsUsableSize(float value)
        {
            return !float.IsNaN(value) && value > 0f;
        }

        #endregion
    }
}
