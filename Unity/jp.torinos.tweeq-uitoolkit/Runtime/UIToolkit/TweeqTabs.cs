using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// The display-name label is built with a Label element. Aliased so its usage matches the other Inputs.
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Tab switching (M8 spec §D, "TweeqTabs"). Has a header (tab list) and a body (panels); a
    /// <see cref="TweeqTab"/> placed as a child registers itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Vue original (`ref/tweeq/src/Tabs`) is authoritative, but for confirmed bugs the fixed-up
    /// version from another reference implementation's port is adopted instead:
    /// (1) making <see cref="SelectTab"/>'s disabled guard unconditional, (2) excluding disabled at every
    /// stage of active-tab resolution, (3) a guard against double-registering the same id plus an
    /// index &lt; 0 guard on update, (4) keyboard navigation (arrow wrap, Home/End, roving tabIndex),
    /// (5) <see cref="StorageKey"/> (present in Vue's type but left unused there).
    /// </para>
    /// <para>
    /// <c>contentContainer</c> is routed to the panel layer, so <see cref="TweeqTab"/> can be added as a
    /// child directly whether from UXML or C#. The header is an internal element outside of that, so it
    /// never mixes with the caller's children.
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqTabs : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>The USS class attached to this element.</summary>
        public const string USS_CLASS_NAME = "tweeq-tabs";

        /// <summary>The persistence key prefix (corresponds to Vue's appConfig store's appId).</summary>
        public const string PREFS_PREFIX = "tweeq.";

        /// <summary>The suffix appended to <see cref="TabsName"/> when <see cref="StorageKey"/> is omitted.</summary>
        public const string PREFS_SUFFIX = ".active";

        // The following are the actual dimensions of Vue's style block, converted at rem=12px (m8-modal-tabs-spec.md §D "Appearance").

        // Horizontal: gap between the tab list and the panels (0.5rem).
        const float ROOT_GAP_HORIZONTAL = 6f;

        // Vertical: gap between the tab list and the panels (1rem).
        const float ROOT_GAP_VERTICAL = 12f;

        // Gap between horizontal tab-list items (0.2rem).
        const float TABLIST_GAP_HORIZONTAL = 2.4f;

        // Gap between vertical tab-list items.
        const float TABLIST_GAP_VERTICAL = 2f;

        // An item's line-height (2rem). UI Toolkit has no line-height, so a fixed height substitutes for it.
        const float HEADER_LINE_HEIGHT = 24f;

        const float HEADER_PADDING_TOP = 2f;

        // Left/right padding on a horizontal item (0.4rem).
        const float HEADER_PADDING_INLINE = 4.8f;

        // Vertical label padding (0.2rem / 0.6rem).
        const float VERTICAL_LABEL_PADDING_BLOCK = 2.4f;

        const float VERTICAL_LABEL_PADDING_INLINE = 7.2f;

        /// <summary>The thickness of the line indicating the active tab (an underline horizontally, a left line vertically).</summary>
        public const float INDICATOR_WIDTH = 3f;

        // The inactive label. Vue's .tablist-link opacity.
        const float INACTIVE_OPACITY = 0.4f;

        // The vertical layout's panel-side divider line and margin (1rem).
        const float PANELS_PADDING_LEFT = 12f;

        const float PANELS_BORDER_WIDTH = 1f;

        // ScrollView's viewport mercilessly clips anything drawn outside its bounds (like SwitchInput's
        // focus ring at inset -3px). This is the safety margin kept inside the clip boundary (ring 3px + AA 1px).
        const float CLIP_SAFE_PADDING = 4f;

        #endregion

        #region Storage

        static ITweeqStorage _storage = TweeqMemoryStorage.Instance;

        /// <summary>
        /// Where the active tab id is persisted (shared across all <see cref="TweeqTabs"/>).
        /// Defaults to the session-only <see cref="TweeqMemoryStorage.Instance"/> so nothing
        /// hits disk unless the host opts in (assign
        /// <see cref="TweeqPlayerPrefsStorage.Instance"/> for cross-run persistence).
        /// Assigning null reverts to the default.
        /// </summary>
        public static ITweeqStorage Storage
        {
            get => _storage;
            set => _storage = value ?? TweeqMemoryStorage.Instance;
        }

        /// <summary>
        /// Builds the persistence key. <paramref name="storageKey"/> takes priority; failing that,
        /// <paramref name="tabsName"/> + ".active" is used. If both are empty, an empty string is returned (i.e. no persistence).
        /// </summary>
        public static string PrefsKey(string tabsName, string storageKey)
        {
            if (!string.IsNullOrEmpty(storageKey))
            {
                return PREFS_PREFIX + storageKey;
            }

            if (!string.IsNullOrEmpty(tabsName))
            {
                return PREFS_PREFIX + tabsName + PREFS_SUFFIX;
            }

            return string.Empty;
        }

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        readonly VisualElement _tabList;
        readonly VisualElement _panels;

        // Only wraps the panels when vertical. Never created for horizontal (ScrollView clips its
        // viewport, so wrapping content in a layout that isn't meant to scroll would cut it off).
        ScrollView _scroll;

        readonly List<TweeqTab> _tabs = new List<TweeqTab>();
        readonly List<VisualElement> _headers = new List<VisualElement>();
        readonly List<UILabel> _headerLabels = new List<UILabel>();

        string _tabsName = string.Empty;
        string _storageKey = string.Empty;
        string _defaultTabId = string.Empty;
        string _activeId = string.Empty;
        bool _vertical;

        // Whether the current _activeId is "what the user chose" or merely "where resolution landed."
        // Tabs get registered one at a time, so if the tentative pick made while only the first one has
        // registered were treated as final, it would become impossible to switch to a saved tab that shows up later.
        bool _activeIdIsExplicit;

        TweeqTab _hoveredTab;

        #endregion

        #region Public API

        /// <summary>Fires when switching to a different tab.</summary>
        public event Action<TweeqTab> Changed;

        /// <summary>
        /// Fires when an already-active tab is clicked again.
        /// Mutually exclusive with <see cref="Changed"/> (never both for the same operation).
        /// </summary>
        public event Action<TweeqTab> Clicked;

        /// <summary>The name the persistence key is derived from. Used when <see cref="StorageKey"/> is absent.</summary>
        [UxmlAttribute("tabs-name")]
        public string TabsName
        {
            get => _tabsName;
            set
            {
                string next = value ?? string.Empty;
                if (_tabsName == next)
                {
                    return;
                }

                _tabsName = next;
                EnsureActiveTab();
            }
        }

        /// <summary>
        /// An explicit persistence key. Since Vue has this in its type but leaves it unused (a bug),
        /// the fixed-up version from another reference implementation (`StorageKey ?? $"{TabsName}.active"`) is adopted here.
        /// </summary>
        [UxmlAttribute("storage-key")]
        public string StorageKey
        {
            get => _storageKey;
            set
            {
                string next = value ?? string.Empty;
                if (_storageKey == next)
                {
                    return;
                }

                _storageKey = next;
                EnsureActiveTab();
            }
        }

        /// <summary>The initial tab id. The second choice when there's no saved value (or it's invalid).</summary>
        [UxmlAttribute("default-tab-id")]
        public string DefaultTabId
        {
            get => _defaultTabId;
            set
            {
                string next = value ?? string.Empty;
                if (_defaultTabId == next)
                {
                    return;
                }

                _defaultTabId = next;
                EnsureActiveTab();
            }
        }

        /// <summary>AE / Resolve-style: places the tab list on the left and the panels on the right.</summary>
        [UxmlAttribute("vertical")]
        public bool Vertical
        {
            get => _vertical;
            set
            {
                if (_vertical == value)
                {
                    return;
                }

                _vertical = value;
                ApplyLayout();
                ApplyHeaderStaticStyles();
                RefreshHeaderStyles();
            }
        }

        /// <summary>The current tab id. Change it via <see cref="SelectTab"/>.</summary>
        public string ActiveId => _activeId;

        /// <summary>The current tab. null if there isn't a single selectable tab.</summary>
        public TweeqTab ActiveTab => FindTab(_activeId);

        /// <summary>The registered tabs (in header order).</summary>
        public IReadOnlyList<TweeqTab> Tabs => _tabs;

        /// <summary>The persistence key this <see cref="TweeqTabs"/> uses. Never persists when empty.</summary>
        public string ResolvedStorageKey => PrefsKey(_tabsName, _storageKey);

        /// <summary>
        /// Routes UXML children and plain Add() calls into the panel layer (internal construction is safe
        /// since it goes through hierarchy.Add). Null-guarded because this can be called during the
        /// constructor before _panels is created.
        /// </summary>
        public override VisualElement contentContainer => _panels ?? this;

        /// <summary>The color theme. Applied to the header and forwarded to <see cref="ITweeqThemed"/> descendants within the panels.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                // Not short-circuited even for the same instance, so it still reaches tabs added after the theme was set.
                _theme = value ?? TweeqTheme.Dark();
                ApplyLayout();
                ApplyHeaderStaticStyles();
                RefreshHeaderStyles();
                TweeqThemeDistribution.Distribute(_panels, _theme);
            }
        }

        /// <summary>
        /// Registers a tab. Rejects a duplicate registration of the same id on the same instance (another reference implementation's fix 3).
        /// Normally called from the <see cref="TweeqTab"/> side.
        /// </summary>
        public void RegisterTab(TweeqTab tab)
        {
            if (tab == null || _tabs.Contains(tab))
            {
                return;
            }

            // Nameless tabs (empty id) are treated as never colliding with each other (FindTab never picks up an empty id).
            if (FindTab(tab.Id) != null)
            {
                // Leaving the rejected panel displayed would show two panels overlapping.
                // A warning is logged so the cause is discoverable, and it's folded away without throwing an exception.
                Debug.LogWarning(
                    $"{nameof(TweeqTabs)}: duplicate tab id '{tab.Id}'; ignoring the later one");
                tab.SetActive(false);
                return;
            }

            _tabs.Add(tab);
            tab.SetOwner(this);

            RebuildHeaders();
            EnsureActiveTab();
            ApplyActive();
        }

        /// <summary>Unregisters a tab. If it was active, the selection is re-resolved.</summary>
        public void UnregisterTab(TweeqTab tab)
        {
            if (tab == null || !_tabs.Remove(tab))
            {
                return;
            }

            if (ReferenceEquals(_hoveredTab, tab))
            {
                _hoveredTab = null;
            }

            tab.SetOwner(null);

            RebuildHeaders();
            EnsureActiveTab();
            ApplyActive();
        }

        /// <summary>
        /// Pulls in a property change on an already-registered tab (equivalent to Vue's watch -> updateTab).
        /// Does nothing if given an unregistered tab (in Vue this is a bug that throws a TypeError via `tabs[-1]`).
        /// </summary>
        public void UpdateTab(TweeqTab tab)
        {
            if (tab == null || _tabs.IndexOf(tab) < 0)
            {
                return;
            }

            RebuildHeaders();
            EnsureActiveTab();
            ApplyActive();
        }

        /// <summary>
        /// Scans the panel layer and picks up any <see cref="TweeqTab"/> not yet registered.
        /// Normally unnecessary since each tab registers itself via its own <see cref="AttachToPanelEvent"/>,
        /// but this plugs the gap for a tree built without ever being attached to a panel (tests, editor extensions).
        /// </summary>
        public void SyncTabsFromHierarchy()
        {
            CollectAndRegister(_panels);
        }

        /// <summary>Selects a tab programmatically. Unconditionally rejects a disabled tab (another reference implementation's fix 1).</summary>
        public void SelectTab(string id)
        {
            TweeqTab selected = FindTab(id);
            if (selected == null)
            {
                return;
            }

            // Vue only rejected it when an event argument was present, so a disabled tab could still be
            // selected via keyboard or programmatic selection.
            if (selected.IsDisabled)
            {
                return;
            }

            if (_activeId == selected.Id)
            {
                // A re-selection is also recorded as "the user chose this tab." It isn't left as a tentative selection.
                _activeIdIsExplicit = true;
                Persist(_activeId);
                Clicked?.Invoke(selected);
                return;
            }

            ApplySelection(selected, true);
        }

        /// <summary>
        /// Moves the selection the way arrow keys do. Skips over disabled entries and wraps, and the selection tracks focus.
        /// <paramref name="direction"/> is negative for previous, positive for next.
        /// </summary>
        public void MoveSelection(int direction)
        {
            if (direction == 0)
            {
                return;
            }

            int count = CountEnabled();
            if (count == 0)
            {
                return;
            }

            int current = EnabledIndexOf(_activeId);

            // When the current value isn't among the enabled entries (unselected/disabled), the start point is the first one (same as another reference implementation).
            int start = current < 0 ? 0 : current;
            int delta = direction < 0 ? -1 : 1;
            int next = ((start + delta) % count + count) % count;

            SelectAndFocus(EnabledAt(next));
        }

        /// <summary>Equivalent to the Home key. Selects the first enabled tab.</summary>
        public void SelectFirstEnabled()
        {
            SelectAndFocus(EnabledAt(0));
        }

        /// <summary>Equivalent to the End key. Selects the last enabled tab.</summary>
        public void SelectLastEnabled()
        {
            SelectAndFocus(EnabledAt(CountEnabled() - 1));
        }

        /// <summary>
        /// Deletes the saved active tab and reverts to the default (unset).
        /// Corresponds to Vue's appConfig behavior of "delete the key once it reverts to the default value."
        /// </summary>
        public void ClearPersistedActiveTab()
        {
            string key = ResolvedStorageKey;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            Storage.Delete(key);
        }

        /// <summary>
        /// The header element at <paramref name="index"/>. null if out of range.
        /// Used to check tabIndex / focus, or to add decoration from outside.
        /// </summary>
        public VisualElement GetHeader(int index)
        {
            if (index < 0 || index >= _headers.Count)
            {
                return null;
            }

            return _headers[index];
        }

        /// <summary>Looks up a tab by id. null / empty id always returns null (so nameless tabs are never picked up).</summary>
        public TweeqTab FindTab(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab != null && tab.Id == id)
                {
                    return tab;
                }
            }

            return null;
        }

        #endregion

        #region Construction

        public TweeqTabs()
        {
            this.AddToClassList(USS_CLASS_NAME);

            _tabList = new VisualElement { name = "tweeq-tabs-tablist" };
            _tabList.style.flexShrink = 0f;
            this.hierarchy.Add(_tabList);

            _panels = new VisualElement { name = "tweeq-tabs-panels" };
            _panels.style.flexDirection = FlexDirection.Column;
            this.hierarchy.Add(_panels);

            ApplyLayout();

            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        public TweeqTabs(string tabsName)
            : this()
        {
            this.TabsName = tabsName;
        }

        #endregion

        #region Layout

        void ApplyLayout()
        {
            this.style.flexDirection = _vertical ? FlexDirection.Row : FlexDirection.Column;

            _tabList.style.flexDirection = _vertical ? FlexDirection.Column : FlexDirection.Row;
            TweeqGap.Apply(
                _tabList,
                _vertical ? TABLIST_GAP_VERTICAL : TABLIST_GAP_HORIZONTAL,
                _tabList.style.flexDirection.value);

            // Before re-parenting, the previous mode's margin/border is cleared (leftovers after switching would look doubled up).
            _panels.style.marginTop = 0f;
            _panels.style.marginLeft = 0f;
            _panels.style.borderLeftWidth = 0f;
            _panels.style.paddingLeft = 0f;

            if (_vertical)
            {
                EnsureScrollView();

                if (!ReferenceEquals(_panels.hierarchy.parent, _scroll.contentContainer))
                {
                    _panels.RemoveFromHierarchy();
                    _scroll.Add(_panels);
                }

                if (!ReferenceEquals(_scroll.hierarchy.parent, this))
                {
                    this.hierarchy.Add(_scroll);
                }

                _panels.style.flexGrow = 0f;

                _scroll.style.flexGrow = 1f;
                _scroll.style.minHeight = 0f;
                _scroll.style.marginLeft = ROOT_GAP_VERTICAL;
                _scroll.style.borderLeftWidth = PANELS_BORDER_WIDTH;
                _scroll.style.borderLeftColor = _theme.Border;
                _scroll.style.paddingLeft = PANELS_PADDING_LEFT;
                return;
            }

            if (_scroll != null && ReferenceEquals(_scroll.hierarchy.parent, this))
            {
                this.hierarchy.Remove(_scroll);
            }

            if (!ReferenceEquals(_panels.hierarchy.parent, this))
            {
                _panels.RemoveFromHierarchy();
                this.hierarchy.Add(_panels);
            }

            _panels.style.flexGrow = 1f;
            _panels.style.minHeight = 0f;
            _panels.style.marginTop = ROOT_GAP_HORIZONTAL;
        }

        void EnsureScrollView()
        {
            if (_scroll != null)
            {
                return;
            }

            _scroll = new ScrollView(ScrollViewMode.Vertical) { name = "tweeq-tabs-scroll" };
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            // This only means anything if placed "inside" the clip boundary (ScrollView's own padding
            // attaches outside the viewport, so the content would still hug the viewport edge and get clipped).
            VisualElement content = _scroll.contentContainer;
            if (content != null)
            {
                content.style.paddingTop = CLIP_SAFE_PADDING;
                content.style.paddingBottom = CLIP_SAFE_PADDING;
                content.style.paddingLeft = CLIP_SAFE_PADDING;
                content.style.paddingRight = CLIP_SAFE_PADDING;
            }
        }

        #endregion

        #region Headers

        // The headers are only ever rebuilt on a registration change or property change (never on a per-frame path).
        void RebuildHeaders()
        {
            for (int i = 0; i < _headers.Count; i++)
            {
                _headers[i].RemoveFromHierarchy();
            }

            _headers.Clear();
            _headerLabels.Clear();
            _hoveredTab = null;

            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab == null)
                {
                    continue;
                }

                VisualElement header = new VisualElement { name = "tweeq-tabs-header" };

                // The header itself takes focus (roving tabIndex). The actual 0 / -1 assignment is
                // handled by RefreshHeaderStyles based on active state.
                header.focusable = true;

                UILabel label = new UILabel(tab.TabName)
                {
                    name = "tweeq-tabs-header-label",

                    // Hit testing happens on the header side (when vertical, the entire row is the click target).
                    pickingMode = PickingMode.Ignore,
                };
                header.Add(label);

                // Rebuilt every time registration changes, so the entity is captured directly rather than by index.
                TweeqTab captured = tab;
                header.RegisterCallback<ClickEvent>(_ => SelectTab(captured.Id));
                header.RegisterCallback<PointerEnterEvent>(_ => SetHovered(captured));
                header.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(null));
                header.RegisterCallback<KeyDownEvent>(OnHeaderKeyDown);
                header.RegisterCallback<NavigationMoveEvent>(OnHeaderNavigationMove);

                _tabList.Add(header);
                _headers.Add(header);
                _headerLabels.Add(label);
            }

            TweeqGap.Apply(
                _tabList,
                _vertical ? TABLIST_GAP_VERTICAL : TABLIST_GAP_HORIZONTAL,
                _tabList.style.flexDirection.value);

            ApplyHeaderStaticStyles();
            RefreshHeaderStyles();
        }

        void ApplyHeaderStaticStyles()
        {
            float duration = _theme.HoverTransitionDuration;

            for (int i = 0; i < _headers.Count; i++)
            {
                VisualElement header = _headers[i];
                UILabel label = _headerLabels[i];

                header.style.flexShrink = 0f;
                header.style.flexDirection = FlexDirection.Column;

                label.style.marginTop = 0f;
                label.style.marginBottom = 0f;
                label.style.marginLeft = 0f;
                label.style.marginRight = 0f;
                label.style.whiteSpace = WhiteSpace.NoWrap;

                // Vue's font-weight: bold. FontHeading isn't used here, so this follows the general UI font.
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                TweeqFonts.Apply(label, _theme.FontUi);
                label.style.height = HEADER_LINE_HEIGHT;

                if (_vertical)
                {
                    // The gap between items is assigned by TweeqGap on the main-axis side (marginTop when vertical).
                    // A leftover from the previous mode on the cross axis would misalign the column, so it's always cleared.
                    header.style.marginLeft = 0f;

                    header.style.borderBottomWidth = 0f;
                    header.style.borderLeftWidth = INDICATOR_WIDTH;
                    header.style.paddingTop = 0f;
                    header.style.paddingBottom = 0f;
                    header.style.paddingLeft = 0f;
                    header.style.paddingRight = 0f;

                    // Padding is placed on the label rather than the item, i.e. the full column width becomes the hover/click target.
                    label.style.paddingTop = VERTICAL_LABEL_PADDING_BLOCK;
                    label.style.paddingBottom = VERTICAL_LABEL_PADDING_BLOCK;
                    label.style.paddingLeft = VERTICAL_LABEL_PADDING_INLINE;
                    label.style.paddingRight = VERTICAL_LABEL_PADDING_INLINE;
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                }
                else
                {
                    header.style.marginTop = 0f;

                    header.style.borderLeftWidth = 0f;
                    header.style.borderBottomWidth = INDICATOR_WIDTH;
                    header.style.paddingTop = HEADER_PADDING_TOP;
                    header.style.paddingBottom = 0f;
                    header.style.paddingLeft = HEADER_PADDING_INLINE;
                    header.style.paddingRight = HEADER_PADDING_INLINE;

                    label.style.paddingTop = 0f;
                    label.style.paddingBottom = 0f;
                    label.style.paddingLeft = 0f;
                    label.style.paddingRight = 0f;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                }

                // Only the color transitions, not the line thickness (to avoid shaking the layout).
                // Applying it to both the horizontal and vertical line means it doesn't need to be reapplied on a Vertical switch.
                ApplyTransition(
                    header, duration, EasingMode.Ease, "border-bottom-color", "border-left-color");

                // Without also transitioning the color, the instant hover is released the opacity would
                // still be high while only the color snaps back to Text, flashing white before it has a
                // chance to darken (the same reasoning as the comment in Vue's style block).
                ApplyTransition(label, duration, EasingMode.Ease, "opacity", "color");
            }
        }

        void RefreshHeaderStyles()
        {
            for (int i = 0; i < _headers.Count && i < _tabs.Count; i++)
            {
                VisualElement header = _headers[i];
                UILabel label = _headerLabels[i];
                TweeqTab tab = _tabs[i];
                if (header == null || label == null || tab == null)
                {
                    continue;
                }

                bool active = !string.IsNullOrEmpty(_activeId) && tab.Id == _activeId;
                bool disabled = tab.IsDisabled;
                bool hovered = !disabled && ReferenceEquals(_hoveredTab, tab);

                label.text = tab.TabName;

                // The active indicator line is Text; hovering it becomes Accent (Vue's .active:hover).
                Color indicator = active
                    ? (hovered ? _theme.Accent : _theme.Text)
                    : Color.clear;

                if (_vertical)
                {
                    header.style.borderLeftColor = indicator;
                }
                else
                {
                    header.style.borderBottomColor = indicator;
                }

                label.style.opacity = active || hovered ? 1f : INACTIVE_OPACITY;
                label.style.color = hovered ? _theme.Accent : _theme.Text;

                // A disabled tab doesn't react to hover (dropping pickingMode also stops Enter/Leave from arriving).
                header.pickingMode = disabled ? PickingMode.Ignore : PickingMode.Position;
                header.focusable = !disabled;
                header.tabIndex = active ? 0 : -1;
            }
        }

        void SetHovered(TweeqTab tab)
        {
            if (ReferenceEquals(_hoveredTab, tab))
            {
                return;
            }

            _hoveredTab = tab;
            RefreshHeaderStyles();
        }

        #endregion

        #region Selection

        // Re-evaluates the selection (equivalent to Vue's watch(tabs) -> ensureActiveTab).
        // Excludes disabled at every stage: persisted value -> DefaultTabId -> first (another reference implementation's fix 2).
        void EnsureActiveTab()
        {
            if (_tabs.Count == 0)
            {
                return;
            }

            string next = ResolveActiveId();

            if (!string.IsNullOrEmpty(next))
            {
                if (next != _activeId)
                {
                    TweeqTab tab = FindTab(next);
                    if (tab != null)
                    {
                        ApplySelection(tab, false);
                    }
                }

                return;
            }

            if (string.IsNullOrEmpty(_activeId))
            {
                return;
            }

            // There isn't a single selectable tab left. Only the selection is dropped (the saved value is
            // the user's own setting, so it's never erased).
            _activeId = string.Empty;
            _activeIdIsExplicit = false;
            ApplyActive();
        }

        // The one and only place that actually switches the selection.
        // With explicitChoice=false (a tentative selection from resolution), nothing is persisted. Tabs
        // register one at a time, so writing here would, during the brief moment when "the saved tab
        // hasn't registered yet," overwrite the saved value with the first tab, breaking restoration every time.
        void ApplySelection(TweeqTab selected, bool explicitChoice)
        {
            _activeId = selected.Id;
            _activeIdIsExplicit = explicitChoice;

            if (explicitChoice)
            {
                Persist(_activeId);
            }

            ApplyActive();
            Changed?.Invoke(selected);
        }

        string ResolveActiveId()
        {
            // What the user chose is preserved with top priority.
            if (_activeIdIsExplicit)
            {
                string chosen = Selectable(_activeId);
                if (chosen != null)
                {
                    return chosen;
                }
            }

            string persisted = Selectable(LoadPersisted());
            if (persisted != null)
            {
                return persisted;
            }

            string preferred = Selectable(_defaultTabId);
            if (preferred != null)
            {
                return preferred;
            }

            // Neither a saved value nor a default exists (yet). If the current tentative selection is still valid, it's left as-is.
            string current = Selectable(_activeId);
            if (current != null)
            {
                return current;
            }

            // The final fallback is also "the first enabled tab." Vue uses tabs[0], so if the first tab
            // was disabled, nothing at all could ever get selected.
            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab != null && !tab.IsDisabled && !string.IsNullOrEmpty(tab.Id))
                {
                    return tab.Id;
                }
            }

            return string.Empty;
        }

        // Returns the id as-is if selectable, or null if not (so the caller can chain ?? operators).
        string Selectable(string id)
        {
            TweeqTab tab = FindTab(id);
            return tab != null && !tab.IsDisabled ? tab.Id : null;
        }

        void ApplyActive()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab == null)
                {
                    continue;
                }

                tab.SetActive(!string.IsNullOrEmpty(_activeId) && tab.Id == _activeId);
            }

            RefreshHeaderStyles();
        }

        void SelectAndFocus(TweeqTab tab)
        {
            if (tab == null)
            {
                return;
            }

            // Clicked doesn't fire when keyboard movement lands on "the same tab" (same as another reference implementation).
            // A change in selection and a click re-selection carry different meanings, so they're never mixed.
            if (tab.Id != _activeId)
            {
                SelectTab(tab.Id);
            }

            FocusHeader(tab);
        }

        void FocusHeader(TweeqTab tab)
        {
            if (this.panel == null || tab == null)
            {
                return;
            }

            int index = _tabs.IndexOf(tab);
            if (index < 0 || index >= _headers.Count)
            {
                return;
            }

            _headers[index].Focus();
        }

        int CountEnabled()
        {
            int count = 0;
            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab != null && !tab.IsDisabled)
                {
                    count++;
                }
            }

            return count;
        }

        // An index into the virtual column packed with only the enabled entries. Counts without building a List, so no allocation.
        int EnabledIndexOf(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return -1;
            }

            int index = 0;
            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab == null || tab.IsDisabled)
                {
                    continue;
                }

                if (tab.Id == id)
                {
                    return index;
                }

                index++;
            }

            return -1;
        }

        TweeqTab EnabledAt(int index)
        {
            if (index < 0)
            {
                return null;
            }

            int current = 0;
            for (int i = 0; i < _tabs.Count; i++)
            {
                TweeqTab tab = _tabs[i];
                if (tab == null || tab.IsDisabled)
                {
                    continue;
                }

                if (current == index)
                {
                    return tab;
                }

                current++;
            }

            return null;
        }

        #endregion

        #region Persistence

        string LoadPersisted()
        {
            string key = ResolvedStorageKey;
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return Storage.Get(key, string.Empty) ?? string.Empty;
        }

        void Persist(string id)
        {
            string key = ResolvedStorageKey;
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (string.IsNullOrEmpty(id))
            {
                // Reverting to the default (unset) means deleting the key. Leaving an empty string written
                // would cause an extra step of falling through as "a saved but invalid id" on the next restore.
                Storage.Delete(key);
                return;
            }

            Storage.Set(key, id);
        }

        #endregion

        #region Keyboard

        void OnHeaderKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Home:
                    SelectFirstEnabled();
                    break;

                case KeyCode.End:
                    SelectLastEnabled();
                    break;

                case KeyCode.LeftArrow:
                    if (_vertical)
                    {
                        return;
                    }

                    MoveSelection(-1);
                    break;

                case KeyCode.RightArrow:
                    if (_vertical)
                    {
                        return;
                    }

                    MoveSelection(1);
                    break;

                case KeyCode.UpArrow:
                    if (!_vertical)
                    {
                        return;
                    }

                    MoveSelection(-1);
                    break;

                case KeyCode.DownArrow:
                    if (!_vertical)
                    {
                        return;
                    }

                    MoveSelection(1);
                    break;

                default:
                    return;
            }

            evt.StopPropagation();
        }

        // Arrow keys also fire a NavigationMoveEvent separately from KeyDown, and that one moves focus on
        // its own (same handling as RadioInput). Since the destination is decided here, that event is swallowed.
        void OnHeaderNavigationMove(NavigationMoveEvent evt)
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
            this.focusController?.IgnoreEvent(evt);
        }

        #endregion

        #region Events

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // Each TweeqTab also registers itself on its own Attach, but this adds a safety net so it stays consistent regardless of order.
            SyncTabsFromHierarchy();
        }

        void CollectAndRegister(VisualElement parent)
        {
            if (parent == null)
            {
                return;
            }

            int count = parent.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                VisualElement child = parent.hierarchy.ElementAt(i);
                if (child == null)
                {
                    continue;
                }

                // Nested tab groups are that group's own responsibility. This never dives inside them.
                if (child is TweeqTabs)
                {
                    continue;
                }

                if (child is TweeqTab tab)
                {
                    RegisterTab(tab);
                    continue;
                }

                CollectAndRegister(child);
            }
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

        #endregion
    }
}
