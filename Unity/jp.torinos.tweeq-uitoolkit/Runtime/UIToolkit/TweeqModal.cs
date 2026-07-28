using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A modal that covers the entire screen (m8-modal-tabs-spec.md §A, equivalent to the Vue
    /// original's PaneModal). It sits in the user's tree but draws nothing itself (size 0), and
    /// only while <see cref="Open"/> is true does it place its internal backdrop onto the
    /// <see cref="TweeqOverlayLayer"/>. Content can simply be Add-ed normally; it lives inside the
    /// internal <see cref="TweeqBalloon"/> so it is not destroyed by opening/closing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Closing is the owner's responsibility. The base TweeqModal <b>does not handle any keys at
    /// all</b> (matching the Vue original's PaneModal). It does not close on a backdrop click either;
    /// it only returns the <see cref="Emphasize"/> bounce and <see cref="OutsideClicked"/> (a
    /// "modal that doesn't close on its own").
    /// </para>
    /// <para>
    /// There are 2 deliberate deviations from the Vue original (backed by the spec doc): since
    /// there is no backdrop-filter, dimming is done via <see cref="TweeqTheme.Background"/> at 50%
    /// alpha, and since accidental interaction with background UI is a real incident risk, the
    /// backdrop blocks pointer events.
    /// </para>
    /// <para>
    /// <see cref="Open"/> is a pure reflector just like in the Vue original — this element never
    /// writes back to it. If opened while not attached to a panel, no exception is thrown; it is
    /// simply left "not yet placed" and gets placed once attached to a panel (UXML's open="true"
    /// goes through this path).
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqModal : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>Margin reserved at the edge of the layer (px). The Vue original's pane-margin. Max size is this value doubled and subtracted.</summary>
        public const float PANE_MARGIN = 48f;

        /// <summary>Corner radius of the outer shell (px). The Vue original's radius-popup.</summary>
        public const float PANE_RADIUS = 13f;

        /// <summary>Inner padding of the outer shell (px). The Vue original's pane-padding (overrides the balloon's default of 9).</summary>
        public const float PANE_PADDING = 12f;

        // The same "wide and soft" shadow as the arrowless popup
        const float PANE_SHADOW_BLUR = 20f;
        const float PANE_SHADOW_OFFSET_Y = 0f;

        // Strength of the dimming. A substitute for UI Toolkit's lack of backdrop-filter (deliberate deviation)
        const float BACKDROP_ALPHA = 0.5f;

        // Amount lifted up from below on appearance (px). Equivalent to the translateY(-6px) used
        // in another reference implementation's style sheet
        const float ENTER_TRANSLATE_Y = -6f;

        // emphasize: scale 1 -> 1.03(35%) -> 1 over 0.2s
        const long EMPHASIZE_DURATION_MS = 200L;
        const float EMPHASIZE_PEAK_SCALE = 1.03f;
        const float EMPHASIZE_PEAK_PHASE = 0.35f;

        // The minimum schedule tick. Equivalent to 60fps, which looks smooth enough
        const long TICK_MS = 16L;

        #endregion

        #region Fields

        // The transition definitions are immutable, so create just one and share it across all instances
        // (style.transition* requires a List every time; a new one on each open would generate garbage)
        static readonly StyleList<StylePropertyName> PaneProperties =
            new StyleList<StylePropertyName>(new List<StylePropertyName>
            {
                new StylePropertyName("opacity"),
                new StylePropertyName("translate"),
            });

        static readonly StyleList<StylePropertyName> BackdropProperties =
            new StyleList<StylePropertyName>(new List<StylePropertyName>
            {
                new StylePropertyName("background-color"),
            });

        // Keep the count aligned with the properties list (don't rely on CSS's cyclic fill-in; keep it explicit and readable)
        static readonly StyleList<EasingFunction> PaneEase =
            new StyleList<EasingFunction>(new List<EasingFunction>
            {
                new EasingFunction(EasingMode.EaseOutCubic),
                new EasingFunction(EasingMode.EaseOutCubic),
            });

        static readonly StyleList<EasingFunction> BackdropEase =
            new StyleList<EasingFunction>(new List<EasingFunction>
            {
                new EasingFunction(EasingMode.EaseOutCubic),
            });

        static readonly StyleList<TimeValue> PaneInstant =
            new StyleList<TimeValue>(new List<TimeValue>
            {
                new TimeValue(0f, TimeUnit.Second),
                new TimeValue(0f, TimeUnit.Second),
            });

        static readonly StyleList<TimeValue> BackdropInstant =
            new StyleList<TimeValue>(new List<TimeValue>
            {
                new TimeValue(0f, TimeUnit.Second),
            });

        TweeqTheme _theme = TweeqTheme.Dark();

        readonly VisualElement _backdrop;
        readonly TweeqBalloon _pane;

        // Durations derived from the theme. Rebuilt only when the theme is swapped
        StyleList<TimeValue> _paneDuration;
        StyleList<TimeValue> _backdropDuration;

        bool _open;

        // Whether it's placed on the layer. _open is the request; _mounted is the actual placement
        // state (they diverge when not attached to a panel)
        bool _mounted;

        TweeqOverlayLayer _layer;

        // A method-group conversion allocates a delegate every time, so keep a single instance and
        // reuse it for register/unregister
        readonly EventCallback<GeometryChangedEvent> _onLayerGeometryChanged;

        // Reuse a single item for "transition from the start value to the target value one frame later"
        IVisualElementScheduledItem _settleItem;

        IVisualElementScheduledItem _emphasizeItem;
        long _emphasizeStartMs = -1L;
        bool _emphasizing;

        #endregion

        #region Public API

        /// <summary>Fires once when <see cref="Open"/> transitions from false to true.</summary>
        public event Action Opened;

        /// <summary>Fires once when <see cref="Open"/> transitions from true to false.</summary>
        public event Action Closed;

        /// <summary>Fires when the backdrop (i.e. outside the modal) is pressed. Whether to close is up to the owner.</summary>
        public event Action OutsideClicked;

        /// <summary>
        /// Open/closed state. A pure reflector just like in the Vue original — this element never
        /// writes back to it (it does not become false from a backdrop click or Escape).
        /// </summary>
        [UxmlAttribute("open")]
        public bool Open
        {
            get => _open;
            set
            {
                if (_open == value)
                {
                    return;
                }

                _open = value;

                if (_open)
                {
                    Mount();
                    Opened?.Invoke();
                }
                else
                {
                    Unmount();
                    Closed?.Invoke();
                }
            }
        }

        /// <summary>The background layer covering the whole screen. Handles dimming and pointer blocking.</summary>
        public VisualElement Backdrop => _backdrop;

        /// <summary>The outer shell balloon. Touch this when you want to tweak the radius, padding, or shadow individually.</summary>
        public TweeqBalloon Pane => _pane;

        /// <summary>Whether the <see cref="Emphasize"/> bounce is currently playing.</summary>
        public bool IsEmphasizing => _emphasizing;

        /// <summary>The color theme. Distributed to the backdrop / balloon / content's <see cref="ITweeqThemed"/> descendants.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                _theme = value ?? TweeqTheme.Dark();

                _pane.Theme = _theme;

                // The balloon re-applies its own transition (scale) every time Theme is assigned,
                // so the modal's opacity / translate settings need to be re-applied afterward
                ApplyTransitions();
                ApplyBackdropColor(_mounted ? 1f : 0f);
                DistributeTheme(_pane.contentContainer);
                OnThemeApplied();
            }
        }

        /// <summary>Content goes into the balloon's content layer (the parent doesn't change on open/close, so it isn't destroyed).</summary>
        public override VisualElement contentContainer => _pane != null ? _pane.contentContainer : this;

        /// <summary>
        /// A bounce to draw attention (scale 1 -> 1.03 -> 1 over 0.2s).
        /// Calling it again while playing restarts from the beginning. Does nothing while not
        /// attached to a panel, since the scheduler doesn't run.
        /// </summary>
        public void Emphasize()
        {
            // From the beginning. TimerState.now advances every tick, so the start time is tracked manually
            _emphasizeStartMs = -1L;
            ApplyEmphasizeScale(0f);

            if (_pane.panel == null)
            {
                _emphasizing = false;
                return;
            }

            _emphasizing = true;

            if (_emphasizeItem == null)
            {
                _emphasizeItem = _pane.schedule.Execute(OnEmphasizeTick).Every(TICK_MS);
                return;
            }

            _emphasizeItem.Resume();
        }

        /// <summary>
        /// Fires the processing equivalent to a backdrop click (bounce then <see cref="OutsideClicked"/>).
        /// Without a panel, pointer events can't be synthesized, so this is exposed for tests and external drivers.
        /// </summary>
        public void PerformOutsideClick()
        {
            Emphasize();
            OutsideClicked?.Invoke();
        }

        #endregion

        #region Construction

        public TweeqModal()
        {
            this.name = "tweeq-modal";

            // Takes no space in the user's tree. The actual content lives on the overlay layer
            this.style.display = DisplayStyle.None;
            this.pickingMode = PickingMode.Ignore;

            _backdrop = new VisualElement
            {
                name = "tweeq-modal-backdrop",

                // The Vue original's popover="manual" leaves the background operable, but here
                // accidental interaction during a live performance is a real incident risk, so
                // the pointer is blocked (deliberate deviation)
                pickingMode = PickingMode.Position,
            };
            _backdrop.style.position = Position.Absolute;
            _backdrop.style.left = 0f;
            _backdrop.style.top = 0f;
            _backdrop.style.right = 0f;
            _backdrop.style.bottom = 0f;
            _backdrop.style.justifyContent = Justify.Center;
            _backdrop.style.alignItems = Align.Center;

            _pane = new TweeqBalloon
            {
                name = "tweeq-modal-pane",
                Theme = _theme,
                ArrowSide = TweeqArrowSide.None,
                Radius = PANE_RADIUS,
                PaddingVertical = PANE_PADDING,
                PaddingHorizontal = PANE_PADDING,
                ShadowBlur = PANE_SHADOW_BLUR,
                ShadowOffsetY = PANE_SHADOW_OFFSET_Y,
            };

            // The balloon defaults to alignSelf: FlexStart (a speech-bubble sticks to the left at its content width).
            // The modal wants it to follow the backdrop's centering instead, so reset it to the parent's alignItems
            _pane.style.alignSelf = Align.Auto;
            _backdrop.hierarchy.Add(_pane);

            _onLayerGeometryChanged = OnLayerGeometryChanged;

            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            _backdrop.RegisterCallback<PointerDownEvent>(OnBackdropPointerDown);

            ApplyTransitions();
            ApplyBackdropColor(0f);
        }

        #endregion

        #region Mounting

        /// <summary>Non-null only while placed on the layer. Used by derived classes for things like key wiring.</summary>
        protected TweeqOverlayLayer Layer => _layer;

        /// <summary>Called right after being placed on the layer. Does nothing by default.</summary>
        protected virtual void OnMounted(TweeqOverlayLayer layer)
        {
        }

        /// <summary>Called right before being taken off the layer. Any registered handlers must be unregistered here.</summary>
        protected virtual void OnUnmounted()
        {
        }

        /// <summary>Called at the end of a <see cref="Theme"/> assignment. A hook for redistributing to a derived class's own parts.</summary>
        protected virtual void OnThemeApplied()
        {
        }

        void Mount()
        {
            if (_mounted)
            {
                return;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(this);
            if (layer == null)
            {
                // No place to put it while not attached to a panel. Don't throw; place it once attached
                return;
            }

            _layer = layer;

            if (_backdrop.hierarchy.parent != layer)
            {
                _backdrop.RemoveFromHierarchy();
                layer.Add(_backdrop);
            }

            _mounted = true;

            // A size change of the layer (i.e. a viewport change) doesn't always move the content, so watch it separately
            _layer.RegisterCallback(_onLayerGeometryChanged);

            ApplyMaxSize();
            BeginEnterAnimation();

            OnMounted(_layer);
        }

        void Unmount()
        {
            StopEmphasize();

            if (_mounted)
            {
                _mounted = false;
                _settleItem?.Pause();

                if (_layer != null)
                {
                    _layer.UnregisterCallback(_onLayerGeometryChanged);
                }

                // Unregister derived handlers before removing from the tree (no leaks allowed)
                OnUnmounted();
            }

            _layer = null;

            // The backdrop is kept parentless while closed. Its content stays in the balloon, so it isn't destroyed.
            // Absorbing a pointer for even one frame after closing is a real incident risk, so it is
            // taken down immediately without waiting for a fade-out
            // (the test contract "removed on Close" also demands this immediacy)
            _backdrop.RemoveFromHierarchy();
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // UXML's open="true" is set at attribute-apply time (before attaching to a panel), so pick it back up here
            if (_open && !_mounted)
            {
                Mount();
            }
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // Don't leave it stranded on the layer when the owner is removed from the tree along with it.
            // The Open request itself is preserved, so re-mounting reopens it
            Unmount();
        }

        #endregion

        #region Presentation

        void OnLayerGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyMaxSize();
        }

        // A ceiling that only kicks in when the content doesn't fit the layer. Below this it centers at its content size
        void ApplyMaxSize()
        {
            if (_layer == null)
            {
                return;
            }

            float width = _layer.layout.width;
            float height = _layer.layout.height;
            if (!IsUsableSize(width) || !IsUsableSize(height))
            {
                // Layout hasn't landed yet. Will be called again by GeometryChanged
                return;
            }

            _pane.style.maxWidth = Mathf.Max(0f, width - PANE_MARGIN * 2f);
            _pane.style.maxHeight = Mathf.Max(0f, height - PANE_MARGIN * 2f);
        }

        // Transitions are set once at init (and on theme swap). Touching them every frame would allocate a StyleList
        void ApplyTransitions()
        {
            float duration = _theme != null ? _theme.ActiveTransitionDuration : 0.064f;

            _paneDuration = new StyleList<TimeValue>(new List<TimeValue>
            {
                new TimeValue(duration, TimeUnit.Second),
                new TimeValue(duration, TimeUnit.Second),
            });

            _backdropDuration = new StyleList<TimeValue>(new List<TimeValue>
            {
                new TimeValue(duration, TimeUnit.Second),
            });

            // Don't include scale in the transition targets. Emphasize writes it every frame via
            // schedule, and a transition riding on top of that would break the intended waveform
            _pane.style.transitionProperty = PaneProperties;
            _pane.style.transitionTimingFunction = PaneEase;
            _pane.style.transitionDuration = _paneDuration;

            _backdrop.style.transitionProperty = BackdropProperties;
            _backdrop.style.transitionTimingFunction = BackdropEase;
            _backdrop.style.transitionDuration = _backdropDuration;
        }

        void ApplyBackdropColor(float weight)
        {
            Color color = _theme != null ? _theme.Background : Color.black;
            color.a = BACKDROP_ALPHA * Mathf.Clamp01(weight);
            _backdrop.style.backgroundColor = color;
        }

        void BeginEnterAnimation()
        {
            // From the second time onward the previous end value (opacity 1) is still in place, so
            // simply setting 0 would first play a "disappearing" animation. Apply the start value with duration 0
            // (this plays the role the Vue original's @starting-style served. Same trick as Popover / Balloon)
            _pane.style.transitionDuration = PaneInstant;
            _pane.style.opacity = 0f;
            _pane.style.translate = new StyleTranslate(
                new Translate(new Length(0f), new Length(ENTER_TRANSLATE_Y), 0f));

            _backdrop.style.transitionDuration = BackdropInstant;
            ApplyBackdropColor(0f);

            if (_backdrop.panel == null)
            {
                // The scheduler doesn't run, so jump straight to the end value to avoid getting stuck transparent
                Settle();
                return;
            }

            if (_settleItem == null)
            {
                _settleItem = _backdrop.schedule.Execute(Settle);
            }

            _settleItem.ExecuteLater(0L);
        }

        void Settle()
        {
            if (!_mounted)
            {
                return;
            }

            _pane.style.transitionDuration = _paneDuration;
            _pane.style.opacity = 1f;
            _pane.style.translate = new StyleTranslate(
                new Translate(new Length(0f), new Length(0f), 0f));

            _backdrop.style.transitionDuration = _backdropDuration;
            ApplyBackdropColor(1f);
        }

        // TweeqRoot stops traversal when it hits an ITweeqThemed. Since the modal itself is an
        // ITweeqThemed, the theme won't reach the content unless it's redistributed from here
        // (the composite part's forwarding responsibility). Opaquing the outer shell is now shared
        // across all popups since TweeqBalloon uses Theme.SurfaceOpaque
        void DistributeTheme(VisualElement parent)
        {
            TweeqThemeDistribution.Distribute(parent, _theme);
        }

        #endregion

        #region Emphasize

        void OnEmphasizeTick(TimerState state)
        {
            if (_emphasizeStartMs < 0L)
            {
                _emphasizeStartMs = state.now;
            }

            long elapsed = state.now - _emphasizeStartMs;
            if (elapsed >= EMPHASIZE_DURATION_MS)
            {
                StopEmphasize();
                return;
            }

            // A piecewise-linear 1 -> 1.03(35%) -> 1 curve. Passed through smoothstep (ease-equivalent) to avoid sharp corners
            float phase = elapsed / (float)EMPHASIZE_DURATION_MS;
            float ramp = phase <= EMPHASIZE_PEAK_PHASE
                ? phase / EMPHASIZE_PEAK_PHASE
                : (1f - phase) / (1f - EMPHASIZE_PEAK_PHASE);

            ApplyEmphasizeScale(ramp * ramp * (3f - 2f * ramp));
        }

        void StopEmphasize()
        {
            _emphasizing = false;
            _emphasizeStartMs = -1L;
            _emphasizeItem?.Pause();
            ApplyEmphasizeScale(0f);
        }

        // Per-frame path. Scale / StyleScale are structs, so there's no allocation here
        void ApplyEmphasizeScale(float weight)
        {
            float scale = Mathf.Lerp(1f, EMPHASIZE_PEAK_SCALE, Mathf.Clamp01(weight));
            _pane.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }

        #endregion

        #region Events

        void OnBackdropPointerDown(PointerDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            // The frontmost element picks up input within the panel's content, so it never reaches
            // background UI in the first place. Stopping it here is a safeguard against the path
            // that would otherwise leak "outside the layer"
            if (!(evt.target is VisualElement target) || target != _backdrop)
            {
                // Let clicks on the pane's content pass through untouched (the inner part handles them itself)
                return;
            }

            evt.StopPropagation();
            PerformOutsideClick();
        }

        #endregion

        #region Helpers

        static bool IsUsableSize(float value)
        {
            return !float.IsNaN(value) && value > 0f;
        }

        #endregion
    }
}
