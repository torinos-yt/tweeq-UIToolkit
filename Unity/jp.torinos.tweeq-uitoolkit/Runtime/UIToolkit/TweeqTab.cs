using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// One panel of <see cref="TweeqTabs"/> (M8 spec §D "TweeqTab").
    /// This is the panel body itself; the header (the tab-list item) is drawn by the parent
    /// <see cref="TweeqTabs"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registration with the parent substitutes <see cref="AttachToPanelEvent"/> for Vue's
    /// provide/inject equivalent. A tree built from UXML "gets attached to the panel only
    /// after its children are all in place", so this timing misses the fewest cases.
    /// </para>
    /// <para>
    /// Vue throws if there is no Tabs ancestor, but in live-performance settings a runtime
    /// exception is an incident, so <b>this does not throw and instead behaves as a
    /// standalone, visible container</b> (intentional deviation — m8-modal-tabs-spec.md §D).
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TweeqTab : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>USS class attached to this element.</summary>
        public const string USS_CLASS_NAME = "tweeq-tab";

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        string _tabName = string.Empty;

        // Explicit id. Falls back to a slug of TabName if empty (same as Vue's computed id)
        string _explicitId = string.Empty;

        bool _isDisabled;

        // Must remain visible even when used standalone (no parent), so the initial value is true
        bool _isActive = true;

        TweeqTabs _owner;

        #endregion

        #region Public API

        /// <summary>Display name shown in the header. If empty, the id is also empty and it's treated as an "unnamed tab".</summary>
        [UxmlAttribute("tab-name")]
        public string TabName
        {
            get => _tabName;
            set
            {
                string next = value ?? string.Empty;
                if (_tabName == next)
                {
                    return;
                }

                _tabName = next;

                // Equivalent to Vue's watch → updateTab. Both the id and the label can change here
                _owner?.UpdateTab(this);
            }
        }

        /// <summary>
        /// Tab id. If not specified explicitly, this is a slug made by lowercasing
        /// <see cref="TabName"/> and replacing spaces with `-`.
        /// </summary>
        [UxmlAttribute("id")]
        public string Id
        {
            get => string.IsNullOrEmpty(_explicitId) ? NormalizeId(_tabName) : _explicitId;
            set
            {
                string next = value ?? string.Empty;
                if (_explicitId == next)
                {
                    return;
                }

                _explicitId = next;
                _owner?.UpdateTab(this);
            }
        }

        /// <summary>Displays the header as disabled and makes it unselectable.</summary>
        [UxmlAttribute("disabled")]
        public bool IsDisabled
        {
            get => _isDisabled;
            set
            {
                if (_isDisabled == value)
                {
                    return;
                }

                _isDisabled = value;
                _owner?.UpdateTab(this);
            }
        }

        /// <summary>The <see cref="TweeqTabs"/> this is registered to. Null if used standalone.</summary>
        public TweeqTabs Owner => _owner;

        /// <summary>Whether this panel is currently displayed. Always true when used standalone.</summary>
        public bool IsActive => _isActive;

        /// <summary>Color theme. Forwarded to <see cref="ITweeqThemed"/> descendants inside.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                // Doesn't short-circuit even for the same instance. Delivers the theme to
                // content added after the theme was set. This is itself a colorless container,
                // but TweeqRoot stops its search at ITweeqThemed, so without distributing here
                // the theme would never reach the content inside.
                _theme = value ?? TweeqTheme.Dark();
                TweeqThemeDistribution.Distribute(this, _theme);
            }
        }

        /// <summary>Builds a tab id from the display name (lowercase, spaces to hyphens).</summary>
        public static string NormalizeId(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            // Culture-dependent ToLower mangles "I" under the Turkish locale, so this is pinned to Invariant
            return name.ToLowerInvariant().Replace(' ', '-');
        }

        /// <summary>
        /// Finds the ancestor <see cref="TweeqTabs"/> and registers with it. Does nothing if
        /// there is no ancestor. Normally called automatically from
        /// <see cref="AttachToPanelEvent"/>, but call this directly when building a tree
        /// without attaching it to a panel (tests, editor extensions).
        /// </summary>
        public void ConnectToTabs()
        {
            TweeqTabs tabs = FindTabs();

            if (_owner != null && !ReferenceEquals(_owner, tabs))
            {
                // Reparented. Must detach from the old parent first, or the header would remain duplicated
                _owner.UnregisterTab(this);
            }

            // Having no parent is not an error (the contract is to behave as a standalone visible element)
            tabs?.RegisterTab(this);
        }

        /// <summary>Unregisters from wherever it was registered. Does nothing when used standalone.</summary>
        public void DisconnectFromTabs()
        {
            _owner?.UnregisterTab(this);
        }

        #endregion

        #region Construction

        public TweeqTab()
        {
            this.AddToClassList(USS_CLASS_NAME);
            this.style.flexDirection = FlexDirection.Column;

            // Equivalent to Vue's `.TqTab { height: 100% }`. Expands to fill the panel area
            this.style.flexGrow = 1f;
            this.style.minHeight = 0f;

            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        public TweeqTab(string tabName)
            : this()
        {
            this.TabName = tabName;
        }

        public TweeqTab(string tabName, string id)
            : this()
        {
            this.TabName = tabName;
            this.Id = id;
        }

        #endregion

        #region Tabs interop

        // Called only by the parent. The entry point that keeps a single owner for the
        // registry and display state
        internal void SetOwner(TweeqTabs owner)
        {
            _owner = owner;

            if (_owner == null)
            {
                // Always return to a visible state once reverted to standalone (prevents
                // becoming orphaned while still display:none)
                SetActive(true);
            }
        }

        internal void SetActive(bool active)
        {
            if (_isActive == active)
            {
                return;
            }

            _isActive = active;

            // Inactive is display:none (intentional deviation — m8-modal-tabs-spec.md §D).
            // Vue keeps the height at the tallest tab by stacking grid cells in the same cell
            // plus opacity, but UI Toolkit has no equivalent layout, and the Monaco workaround
            // that was the reason for choosing opacity doesn't apply in Unity either.
            this.style.display = _isActive ? DisplayStyle.Flex : DisplayStyle.None;
        }

        #endregion

        #region Events

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            ConnectToTabs();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            DisconnectFromTabs();
        }

        // The same manual ancestor search as ParameterGrid.Find. UQuery can't take an
        // interface due to type constraints, and this needs to cross parts that swapped out
        // contentContainer, so the hierarchy is walked manually.
        TweeqTabs FindTabs()
        {
            VisualElement current = this.hierarchy.parent;
            while (current != null)
            {
                if (current is TweeqTabs tabs)
                {
                    return tabs;
                }

                current = current.hierarchy.parent;
            }

            return null;
        }

        #endregion
    }
}
