using System;
using System.Collections.Generic;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

// Build option rows with Label. Aliased to keep naming consistent with other Inputs
using UILabel = UnityEngine.UIElements.Label;

namespace Tweeq.UIToolkit
{
    /// <summary>Vertical popup policy. Auto preserves the macOS-style placement used by default.</summary>
    public enum DropdownPopupDirection
    {
        Auto,
        Upward,
        Downward,
    }

    /// <summary>
    /// Dropdown selection (popover-spec.md "DropdownInput&lt;T&gt;").
    /// The closed state is a single input row; the open state pops up macOS-style, positioned so the
    /// selected option overlaps the field.
    ///
    /// The open/close state machine (<see cref="Open"/> / <see cref="Close"/> / <see cref="Commit"/> /
    /// <see cref="Cancel"/> / <see cref="MoveSelection"/> / <see cref="PerformPointerUp"/>) is
    /// panel-independent. The popup display just rides on top of it, so even without a panel attached
    /// the state still advances without throwing (EditMode tests exercise this layer directly).
    /// </summary>
    public class DropdownInput<T> : VisualElement, INotifyValueChanged<T>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // Constants from Vue InputDropdown.vue 53-55. SELECT_CHROME (=2) is a measured
        // margin+border value from Vue's .select, so here we measure and pass the actual padding+border instead
        const float VIEWPORT_MARGIN = 6f;
        const float AUTO_SCROLL_SPEED = 8f;

        // Vue uses requestAnimationFrame. UI Toolkit's scheduler has no "every frame" option, so
        // approximate it with a ~60fps interval (the Every item is reused, so it's only allocated once)
        const long AUTO_SCROLL_INTERVAL_MS = 16;

        // Vue onPointerupWhileOpen: a pointerup within this time of opening is treated as
        // "still mid-drag-select" and ignored. Past this time, it commits and closes
        const long CONFIRM_GRACE_MS = 500;

        // Vue's $chevron-width = .7 * inputHeight
        const float CHEVRON_WIDTH_RATIO = 0.7f;
        const float CHEVRON_IDLE_OPACITY = 0.4f;
        const float CHEVRON_TRIANGLE_WIDTH = 8f;
        const float CHEVRON_TRIANGLE_HEIGHT = 4f;
        const float CHEVRON_TRIANGLE_GAP = 3f;

        const float FOCUS_RING_WIDTH = 1f;
        const float DISABLED_BORDER_WIDTH = 1f;
        const float POPUP_BORDER_WIDTH = 1f;

        // Left/right padding for option rows (Vue uses left .5em / right chevron width, but here it's centered so kept symmetric)
        const float OPTION_PADDING = 6f;

        const float TEXT_FONT_SIZE = 12f;

        // There's no box-shadow, so approximate "0 0 20px" by stacking several translucent rounded layers (popover-spec.md)
        const float SHADOW_SPREAD = 20f;
        const int SHADOW_LAYERS = 5;

        // Slack used to judge scroll-edge state (the 0.5px from Vue's updateScrollArrows)
        const float SCROLL_EPSILON = 0.5f;

        const float SCROLL_ARROW_RATIO = 0.7f;

        // Inner element of the filter TextField (touched to strip its background/border and use up the full height)
        const string TEXT_INPUT_NAME = "unity-text-input";

        // Left/right padding of the filter input. Matches the option row's 6px so text position doesn't jump while typing
        const float FILTER_PADDING = OPTION_PADDING;

        #endregion

        #region Fields

        static readonly EqualityComparer<T> Comparer = EqualityComparer<T>.Default;

        TweeqTheme _theme = TweeqTheme.Dark();

        T _value;
        T[] _options = Array.Empty<T>();
        string[] _labelCache = Array.Empty<string>();
        Func<T, string> _labelizer;
        string[] _labels;
        string _prefix = string.Empty;
        string _suffix = string.Empty;
        string _displayText = string.Empty;

        // Position of _value within options. -1 if not found. Cached so it isn't searched for every frame
        int _valueIndex = -1;

        bool _disabled;
        bool _invalid;
        bool _hovered;
        bool _focused;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        // Field side
        UILabel _fieldLabel;
        VisualElement _chevron;
        VisualElement _focusRing;

        // Filter (fuzzy search). The TextField isn't created until the first filter.
        // This keeps a dropdown that's never had a keystroke from carrying a TextField's internal hierarchy
        TextField _filterField;
        VisualElement _filterInput;
        TextElement _filterText;

        bool _filtering;
        string _filterQuery = string.Empty;

        // Filtered results (a list of indices into options). Re-packed on every keystroke, so the List is reused
        readonly List<int> _filtered = new List<int>();

        // Popup side (rebuilt only when options change; reused across open/close)
        TweeqPopover _popover;
        VisualElement _surface;
        VisualElement _shadowLayer;
        VisualElement _viewport;
        VisualElement _list;
        VisualElement _arrowUp;
        VisualElement _arrowDown;
        readonly List<UILabel> _rows = new List<UILabel>();

        bool _open;
        T _valueAtStart;

        // Position of the value at the moment it opened. Looked up once in Open() so it isn't linearly searched every current-display
        int _valueAtStartIndex = -1;
        long _openTimeMs;

        // Remembers "has it already been added" without depending on popover's internal structure
        bool _popupAttached;

        float _scrollOffset;
        float _visibleHeight;
        float _listHeight;

        // Only one scheduled item for auto-scroll is created; it's reused via Resume/Pause
        IVisualElementScheduledItem _autoScrollItem;
        int _autoScrollDirection;

        // Don't commit when released over a scroll arrow (equivalent to Vue's @pointerup.stop)
        bool _pointerOverArrow;

        DropdownPopupDirection _popupDirection = DropdownPopupDirection.Auto;

        // Outside-click/release detection attached to the panel root only while open
        VisualElement _dismissRoot;

        #endregion

        #region Public API

        /// <summary>Fires every time the value changes. Also fires for arrow-key and hover selection.</summary>
        public event Action<T> ValueChanged;

        /// <summary>Fires only once per operation, on click-confirm or Enter-confirm.</summary>
        public event Action<T> Confirmed;

        /// <summary>The currently selected value.</summary>
        public T value
        {
            get => _value;
            set
            {
                if (Comparer.Equals(_value, value))
                {
                    return;
                }

                T previous = _value;
                SetValueWithoutNotify(value);
                ValueChanged?.Invoke(_value);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>
        /// The options. Both get and set go through a copy (decoupling the caller's array from internal state).
        /// The value is left untouched even if the current value isn't among the new options (to avoid firing an unwanted notification).
        /// The field only retains the display produced via <see cref="Labelizer"/>.
        /// </summary>
        public T[] Options
        {
            get
            {
                T[] copy = new T[_options.Length];
                Array.Copy(_options, copy, _options.Length);
                return copy;
            }

            set
            {
                if (value == null)
                {
                    _options = Array.Empty<T>();
                }
                else
                {
                    _options = new T[value.Length];
                    Array.Copy(value, _options, value.Length);
                }

                RebuildLabelCache();

                // The filtered results are "indices into the old options", so re-derive them before rebuilding rows.
                // Entering RebuildRows first would reference out-of-range indices
                RefreshFilterResults();

                RebuildRows();
                _valueIndex = IndexOf(_value);

                // If the options disappear, the popup's contents become meaningless too
                if (_options.Length == 0 && _open)
                {
                    Close();
                }

                RefreshDisplayText();
                Refresh();
            }
        }

        /// <summary>
        /// A function that builds a label from a value. Takes priority over <see cref="Labels"/>.
        /// Results are generated in bulk and cached when options change, so this isn't called every frame.
        /// </summary>
        public Func<T, string> Labelizer
        {
            get => _labelizer;
            set
            {
                _labelizer = value;
                RebuildLabelCache();
                RefreshFilterResults();
                ApplyRowTexts();
                RefreshDisplayText();
                Refresh();
            }
        }

        /// <summary>A label array corresponding to options by index. Used when <see cref="Labelizer"/> isn't set.</summary>
        public string[] Labels
        {
            get => _labels;
            set
            {
                _labels = value;
                RebuildLabelCache();
                RefreshFilterResults();
                ApplyRowTexts();
                RefreshDisplayText();
                Refresh();
            }
        }

        /// <summary>String prepended to the field display. Not applied to option rows (that's Vue's InputString's responsibility).</summary>
        public string Prefix
        {
            get => _prefix;
            set
            {
                _prefix = value ?? string.Empty;
                RefreshDisplayText();
                Refresh();
            }
        }

        /// <summary>String appended to the field display.</summary>
        public string Suffix
        {
            get => _suffix;
            set
            {
                _suffix = value ?? string.Empty;
                RefreshDisplayText();
                Refresh();
            }
        }

        /// <summary>Non-interactive state.</summary>
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
                    // If it stays open at the moment it's disabled, there'd be no way left to close it
                    Close();
                }

                this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
                Refresh();
            }
        }

        /// <summary>
        /// External invalid-value display. Only changes the text color to Error; leaves the border and chevron alone.
        /// </summary>
        /// <remarks>
        /// Vue's InputDropdown delegates display to an internal InputString, so it carries invalid too
        /// (m7-disabled-invalid-spec.md). There's no delegate here, so the same styling as
        /// <see cref="StringInput"/> is applied to both the field's label and the filter TextField.
        /// </remarks>
        public bool Invalid
        {
            get => _invalid;
            set
            {
                _invalid = value;
                Refresh();
            }
        }

        /// <summary>Color theme. Falls back to Dark() when null is passed.</summary>
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

        /// <summary>Whether the popup is open (logical state; advances even without a panel).</summary>
        public bool IsOpen => _open;

        /// <summary>Controls the popup's vertical expansion while open. Auto keeps the existing placement algorithm.</summary>
        public DropdownPopupDirection PopupDirection
        {
            get => _popupDirection;
            set
            {
                if (_popupDirection == value)
                {
                    return;
                }

                _popupDirection = value;
                RelayoutPopup();
            }
        }

        /// <summary>The string shown in the field (Prefix + label + Suffix). Hidden while filtering.</summary>
        public string DisplayText => _displayText;

        /// <summary>Whether currently narrowing down via fuzzy search.</summary>
        public bool IsFiltering => _filtering;

        /// <summary>The query being typed while filtering. Empty string when not filtering.</summary>
        public string FilterQuery => _filterQuery;

        /// <summary>Number of candidates shown in the popup. The total option count when not filtering.</summary>
        public int VisibleCount => _filtering ? _filtered.Count : _options.Length;

        /// <summary>Which index into options the visibleIndex-th displayed candidate corresponds to. -1 if out of range.</summary>
        public int OptionIndexAt(int visibleIndex)
        {
            if (visibleIndex < 0)
            {
                return -1;
            }

            if (!_filtering)
            {
                return visibleIndex < _options.Length ? visibleIndex : -1;
            }

            if (visibleIndex >= _filtered.Count)
            {
                return -1;
            }

            int index = _filtered[visibleIndex];
            return index >= 0 && index < _options.Length ? index : -1;
        }

        /// <summary>The value at the moment it opened. The rollback target for Escape.</summary>
        public T ValueAtStart => _valueAtStart;

        /// <summary>
        /// Source of the millisecond timestamp. Defaults to Time.realtimeSinceStartup.
        /// Made replaceable so the 500ms rule can be verified in EditMode.
        /// </summary>
        public Func<long> TimeSource { get; set; }

        /// <summary>Sets the value without firing ChangeEvent / ValueChanged.</summary>
        public void SetValueWithoutNotify(T newValue)
        {
            _value = newValue;
            _valueIndex = IndexOf(newValue);
            RefreshDisplayText();
            Refresh();
        }

        /// <summary>
        /// Opens the popup. Records the value at this moment into <see cref="ValueAtStart"/> and
        /// marks the starting point of the 500ms rule. Does nothing when options are empty or while disabled.
        /// </summary>
        public void Open()
        {
            if (_open || _disabled || _options.Length == 0)
            {
                return;
            }

            _open = true;
            _valueAtStart = _value;
            _valueAtStartIndex = _valueIndex;
            _openTimeMs = Now();
            _pointerOverArrow = false;

            ShowPopup();
            Refresh();
        }

        /// <summary>Closes without committing. The current value is left as-is (equivalent to Vue's outside click).</summary>
        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _pointerOverArrow = false;
            StopAutoScroll();

            // Spec §B: regardless of which path it closes through, clear the filter and revert the display to the label.
            // Restore row display before collapsing the popup (so it starts with the full list next time it opens)
            EndFilter();

            HidePopup();
            Refresh();
        }

        /// <summary>Commits the current value and closes. Confirmed fires exactly once per operation, only here.</summary>
        public void Commit()
        {
            if (!_open)
            {
                return;
            }

            Close();
            Confirmed?.Invoke(_value);
        }

        /// <summary>Rolls back to the value at open time and closes (Escape). Confirmed does not fire.</summary>
        public void Cancel()
        {
            if (!_open)
            {
                return;
            }

            // If already changed, also fire ValueChanged for the reverting direction (same handling as canceling a drag)
            this.value = _valueAtStart;
            Close();
        }

        /// <summary>
        /// Moves to an adjacent candidate with wraparound (direction: -1=previous / +1=next).
        /// The value moves whether open or closed (same as Vue's onPressArrow, where active == the current value).
        /// While filtering, wraps only within the filtered results.
        /// </summary>
        public void MoveSelection(int direction)
        {
            if (_disabled || direction == 0 || _options.Length == 0)
            {
                return;
            }

            int count = VisibleCount;
            if (count == 0)
            {
                return;
            }

            int current = VisibleIndexOfValue();

            // The current value isn't among the candidates (outside options, or dropped by filtering). Start from the top regardless of direction
            int next = current < 0 ? 0 : WrapIndex(current + (direction > 0 ? 1 : -1), count);

            int option = OptionIndexAt(next);
            if (option < 0)
            {
                return;
            }

            this.value = _options[option];

            if (_open)
            {
                ScrollActiveIntoView();
            }
        }

        /// <summary>
        /// Common path for pointer release. Within 500ms of opening it's treated as
        /// still mid-drag-select and ignored; past that it commits and closes (Vue onPointerupWhileOpen).
        /// </summary>
        public void PerformPointerUp()
        {
            if (!_open || _pointerOverArrow)
            {
                return;
            }

            if (Now() - _openTimeMs <= CONFIRM_GRACE_MS)
            {
                return;
            }

            Commit();
        }

        #endregion

        #region Filter session

        /// <summary>
        /// Enters fuzzy-search mode and replaces the query with query. Opens if closed (matches Vue).
        /// Does nothing when options are empty or while disabled.
        /// </summary>
        public void BeginFilter(string query)
        {
            if (_disabled || _options.Length == 0)
            {
                return;
            }

            if (!_filtering)
            {
                // If filtering moves the value before Open() records valueAtStart, Escape's rollback target gets thrown off.
                // The order "open first, then filter" is fixed here
                _filtering = true;
                ShowFilterField();
                Open();
            }

            SetFilterQuery(query);
        }

        /// <summary>
        /// Swaps the query while filtering and re-filters. Does nothing when not filtering.
        /// An empty query means "no filtering (all items)".
        /// </summary>
        public void SetFilterQuery(string query)
        {
            if (!_filtering)
            {
                return;
            }

            _filterQuery = query ?? string.Empty;
            SyncFilterField();
            ApplyFilter();
        }

        /// <summary>
        /// Clears the filter and reverts the field display to the label. Candidates also revert to the full list.
        /// Doesn't touch popup open/close (closing is <see cref="Close"/>'s responsibility).
        /// </summary>
        public void EndFilter()
        {
            if (!_filtering)
            {
                return;
            }

            _filtering = false;
            _filterQuery = string.Empty;
            _filtered.Clear();

            HideFilterField();
            ApplyRowTexts();
            RefreshDisplayText();
            Refresh();
        }

        // Path taken on every keystroke. Doesn't allocate strings or a List — just re-packs the reused buffer
        void ApplyFilter()
        {
            RefreshFilterResults();

            // Vue: if the current value falls out of the filtered results, snap to the top. This keeps the up/down starting point always within the candidates
            if (_filtered.Count > 0 && VisibleIndexOfValue() < 0)
            {
                this.value = _options[_filtered[0]];
            }

            ApplyRowTexts();

            // Right after filtering, always show from the top (Vue's scrollTop = 0)
            SetScroll(0f);

            RelayoutPopup();
            Refresh();
        }

        void RefreshFilterResults()
        {
            if (!_filtering)
            {
                return;
            }

            FuzzySearch.Filter(_filterQuery, _labelCache, _filtered);
        }

        // The current value's position "in display order". When not filtering, this is just the options index itself
        int VisibleIndexOfValue()
        {
            if (_valueIndex < 0)
            {
                return -1;
            }

            if (!_filtering)
            {
                return _valueIndex;
            }

            for (int i = 0; i < _filtered.Count; i++)
            {
                if (_filtered[i] == _valueIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion

        #region Construction

        public DropdownInput()
        {
            this.AddToClassList("tweeq-dropdown-input");

            // The root itself holds focus so it can receive arrow keys, Enter, and Escape
            this.focusable = true;
            this.style.flexShrink = 0f;
            this.style.overflow = Overflow.Hidden;

            BuildField();
            ApplyStaticStyles();

            this.RegisterCallback<PointerDownEvent>(OnPointerDown);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            // While filtering, focus is on the inner TextField, so Enter / Escape / up-down
            // need to be intercepted before the TextField (same TrickleDown registration as NumberInput)
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // Arrow keys also fire a NavigationMoveEvent separate from KeyDown, and that event
            // moves focus on its own (feedback-fixes-01.md A-5 / same fix as NumberInput)
            this.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            Refresh();
        }

        public DropdownInput(T[] options)
            : this()
        {
            this.Options = options;
        }

        void BuildField()
        {
            _fieldLabel = new UILabel(string.Empty)
            {
                name = "tweeq-dropdown-label",
                pickingMode = PickingMode.Ignore,
            };
            _fieldLabel.style.position = Position.Absolute;
            _fieldLabel.style.left = 0f;
            _fieldLabel.style.top = 0f;
            _fieldLabel.style.right = 0f;
            _fieldLabel.style.bottom = 0f;
            _fieldLabel.style.marginLeft = 0f;
            _fieldLabel.style.marginRight = 0f;
            _fieldLabel.style.marginTop = 0f;
            _fieldLabel.style.marginBottom = 0f;
            _fieldLabel.style.paddingTop = 0f;
            _fieldLabel.style.paddingBottom = 0f;
            _fieldLabel.style.fontSize = _theme != null ? _theme.FontSizeInput : TEXT_FONT_SIZE;
            _fieldLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _fieldLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _fieldLabel.style.overflow = Overflow.Hidden;
            _fieldLabel.style.textOverflow = TextOverflow.Ellipsis;
            this.hierarchy.Add(_fieldLabel);

            _chevron = new VisualElement
            {
                name = "tweeq-dropdown-chevron",
                pickingMode = PickingMode.Ignore,
            };
            _chevron.style.position = Position.Absolute;
            _chevron.style.top = 0f;
            _chevron.style.bottom = 0f;
            _chevron.style.right = 0f;
            _chevron.generateVisualContent += OnGenerateChevron;
            this.hierarchy.Add(_chevron);

            // The focus ring is drawn on a separate layer rather than the root's border (same reason as NumberInput)
            _focusRing = new VisualElement
            {
                name = "tweeq-dropdown-focus-ring",
                pickingMode = PickingMode.Ignore,
            };
            _focusRing.style.position = Position.Absolute;
            _focusRing.style.left = 0f;
            _focusRing.style.top = 0f;
            _focusRing.style.right = 0f;
            _focusRing.style.bottom = 0f;
            _focusRing.style.display = DisplayStyle.None;
            SetBorderWidth(_focusRing, FOCUS_RING_WIDTH);
            this.hierarchy.Add(_focusRing);
        }

        void ApplyStaticStyles()
        {
            if (_theme == null)
            {
                return;
            }

            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;
            ApplyCornerRadius();
            SetBorderColor(this, _theme.Border);
            ApplyTransition(this, _theme.HoverTransitionDuration, "background-color");

            float chevronWidth = _theme.InputHeight * CHEVRON_WIDTH_RATIO;

            if (_chevron != null)
            {
                _chevron.style.width = chevronWidth;
                ApplyTransition(_chevron, _theme.HoverTransitionDuration, "opacity");
            }

            if (_fieldLabel != null)
            {
                // Inset from left and right by the chevron's width, keeping the text's center aligned with the box's center
                _fieldLabel.style.paddingLeft = chevronWidth;
                _fieldLabel.style.paddingRight = chevronWidth;
                _fieldLabel.style.fontSize = _theme.FontSizeInput;

                TweeqFonts.Apply(_fieldLabel, _theme.FontUi);
            }

            if (_focusRing != null)
            {
                SetBorderColor(_focusRing, _theme.Accent);
            }

            ApplyFilterFieldStyles();
            ApplyPopupStyles();
        }

        // The corner-radius table from Spec §1. Settings on both axes are combined with OR
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

            if (_focusRing != null)
            {
                SetCornerRadius(_focusRing, radius, topLeft, topRight, bottomLeft, bottomRight);
            }
        }

        #endregion

        #region Filter field

        // Same two-tier setup as NumberInput's edit-mode switch (display is a Label, TextField only while typing).
        // If a TextField were kept permanently up front like StringInput, the Dropdown's primary
        // operation — pressing the field opens the popup — and the interception of up/down would get absorbed by the TextField.
        // Dropdown doesn't need a click-position caret (typing always starts from an empty query),
        // so the reason StringInput avoids the two-tier setup doesn't apply here
        void EnsureFilterField()
        {
            if (_filterField != null)
            {
                return;
            }

            _filterField = new TextField
            {
                name = "tweeq-dropdown-filter",

                // Needs to filter on every keystroke. With isDelayed = true, the change wouldn't arrive until Enter
                isDelayed = false,
                multiline = false,
            };
            _filterField.style.position = Position.Absolute;
            _filterField.style.left = 0f;
            _filterField.style.top = 0f;
            _filterField.style.right = 0f;
            _filterField.style.bottom = 0f;
            _filterField.style.marginLeft = 0f;
            _filterField.style.marginRight = 0f;
            _filterField.style.marginTop = 0f;
            _filterField.style.marginBottom = 0f;
            _filterField.style.display = DisplayStyle.None;
            _filterField.pickingMode = PickingMode.Ignore;
            _filterField.RegisterValueChangedCallback(OnFilterTextChanged);
            this.hierarchy.Add(_filterField);

            _filterInput = _filterField.Q(TEXT_INPUT_NAME);

            // The actual glyph drawing happens in the TextElement inside unity-text-input.
            // Vertical squashing (feedback-fixes-01.md A-6) persists even if only the input side is fixed
            _filterText = _filterInput != null ? _filterInput.Q<TextElement>() : null;

            ApplyFilterFieldStyles();
        }

        void ApplyFilterFieldStyles()
        {
            if (_theme == null || _filterField == null)
            {
                return;
            }

            float chevronWidth = _theme.InputHeight * CHEVRON_WIDTH_RATIO;

            if (_filterInput != null)
            {
                _filterInput.style.backgroundColor = Color.clear;
                SetBorderWidth(_filterInput, 0f);
                SetBorderColor(_filterInput, Color.clear);

                // Inset by the chevron width just like the closed-state label, so text doesn't jump sideways when switching modes
                _filterInput.style.paddingLeft = chevronWidth + FILTER_PADDING;
                _filterInput.style.paddingRight = chevronWidth + FILTER_PADDING;
                _filterInput.style.marginLeft = 0f;
                _filterInput.style.marginRight = 0f;
                _filterInput.style.unityTextAlign = TextAnchor.MiddleCenter;

                // A-6: with default USS top/bottom padding and auto height left as-is, the row collapses within a 24px box
                _filterInput.style.height = Length.Percent(100f);
                _filterInput.style.minHeight = 0f;
                _filterInput.style.paddingTop = 0f;
                _filterInput.style.paddingBottom = 0f;
                _filterInput.style.marginTop = 0f;
                _filterInput.style.marginBottom = 0f;
                _filterInput.style.fontSize = _theme.FontSizeInput;
                _filterInput.style.whiteSpace = WhiteSpace.NoWrap;
            }

            if (_filterText != null)
            {
                _filterText.style.height = Length.Percent(100f);
                _filterText.style.minHeight = 0f;
                _filterText.style.paddingTop = 0f;
                _filterText.style.paddingBottom = 0f;
                _filterText.style.marginTop = 0f;
                _filterText.style.marginBottom = 0f;
                _filterText.style.fontSize = _theme.FontSizeInput;
                _filterText.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            _filterField.style.unityTextAlign = TextAnchor.MiddleCenter;
            _filterField.style.fontSize = _theme.FontSizeInput;

            // TextField's internals declare their own fontSize, so inheritance alone can't be trusted here;
            // the font is pushed down to input / TextElement the same way NumberInput does it.
            TweeqFonts.Apply(_filterField, _theme.FontUi);
            TweeqFonts.Apply(_filterInput, _theme.FontUi);
            TweeqFonts.Apply(_filterText, _theme.FontUi);

            // The caret/selection colors default to USS black, which is invisible on a dark background.
            // selectionColor is obsolete, but the recommended --unity-selection-color can't be
            // set per-instance from C# (same judgment call as NumberInput / StringInput)
#pragma warning disable 618
            _filterField.textSelection.cursorColor = _theme.Text;
            _filterField.textSelection.selectionColor = _theme.AccentSoft;
#pragma warning restore 618

            _filterField.style.paddingTop = 0f;
            _filterField.style.paddingBottom = 0f;
            _filterField.style.paddingLeft = 0f;
            _filterField.style.paddingRight = 0f;
            _filterField.style.minHeight = 0f;
            _filterField.style.alignItems = Align.Stretch;

            // The first filter creation passes through here directly (bypassing Refresh), so the text color is reapplied
            UpdateFilterTextColor();
        }

        void ShowFilterField()
        {
            EnsureFilterField();

            if (_filterField == null)
            {
                return;
            }

            if (_fieldLabel != null)
            {
                _fieldLabel.style.display = DisplayStyle.None;
            }

            // Focus() won't take effect while display:none, so the display must be switched first
            _filterField.style.display = DisplayStyle.Flex;
            _filterField.pickingMode = PickingMode.Position;

            if (this.panel != null)
            {
                _filterField.Focus();
                ScheduleCaretToEnd();
            }
        }

        void HideFilterField()
        {
            if (_fieldLabel != null)
            {
                _fieldLabel.style.display = DisplayStyle.Flex;
            }

            if (_filterField == null)
            {
                return;
            }

            bool hadFocus = HasFilterFocus();

            _filterField.SetValueWithoutNotify(string.Empty);
            _filterField.style.display = DisplayStyle.None;
            _filterField.pickingMode = PickingMode.Ignore;

            // Focus while filtering sits on the TextField side. Return it to the root so
            // Enter / up-down keep being received even after collapsing
            if (hadFocus && this.panel != null)
            {
                this.Focus();
            }
        }

        // Since this side feeds in the first character, send the caret to the end right after focus.
        // The selection gets overwritten unless it's the frame after focus is confirmed (same as NumberInput)
        void ScheduleCaretToEnd()
        {
            if (this.panel == null)
            {
                return;
            }

            this.schedule.Execute(() =>
            {
                if (_filterField == null || !_filtering)
                {
                    return;
                }

                int caret = _filterQuery.Length;
                _filterField.SelectRange(caret, caret);
            }).StartingIn(0);
        }

        void SyncFilterField()
        {
            if (_filterField == null || _filterField.value == _filterQuery)
            {
                return;
            }

            _filterField.SetValueWithoutNotify(_filterQuery);
        }

        void OnFilterTextChanged(ChangeEvent<string> evt)
        {
            if (evt == null || !_filtering)
            {
                return;
            }

            SetFilterQuery(evt.newValue);
        }

        bool HasFilterFocus()
        {
            if (_filterField == null || this.focusController == null)
            {
                return false;
            }

            VisualElement focused = this.focusController.focusedElement as VisualElement;
            return focused != null && (focused == _filterField || _filterField.Contains(focused));
        }

        // While filtering, the caret-holding TextField receives key input; otherwise the root does
        void FocusSelf()
        {
            if (this.panel == null)
            {
                return;
            }

            if (_filtering && _filterField != null)
            {
                _filterField.Focus();
                return;
            }

            this.Focus();
        }

        #endregion

        #region Labels

        // Runs only when options / labelizer / labels changes. Generated in bulk here so
        // subsequent access is just an array reference (no per-frame Format)
        void RebuildLabelCache()
        {
            if (_labelCache.Length != _options.Length)
            {
                _labelCache = _options.Length == 0
                    ? Array.Empty<string>()
                    : new string[_options.Length];
            }

            for (int i = 0; i < _options.Length; i++)
            {
                _labelCache[i] = ComposeLabel(_options[i], i);
            }
        }

        // Priority order is Labelizer > Labels > value.ToString() (popover-spec.md)
        string ComposeLabel(T option, int index)
        {
            if (_labelizer != null)
            {
                return _labelizer(option) ?? string.Empty;
            }

            if (_labels != null && index >= 0 && index < _labels.Length && _labels[index] != null)
            {
                return _labels[index];
            }

            return option == null ? string.Empty : option.ToString();
        }

        void RefreshDisplayText()
        {
            string label = _valueIndex >= 0 && _valueIndex < _labelCache.Length
                ? _labelCache[_valueIndex]
                : ComposeLabel(_value, -1);

            // If prefix and suffix are both empty, use the cache as-is instead of concatenating (avoids allocating a string on every value change)
            _displayText = _prefix.Length == 0 && _suffix.Length == 0
                ? label
                : _prefix + label + _suffix;

            if (_fieldLabel != null)
            {
                _fieldLabel.text = _displayText;
            }
        }

        int IndexOf(T target)
        {
            for (int i = 0; i < _options.Length; i++)
            {
                if (Comparer.Equals(_options[i], target))
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion

        #region Popup construction

        // Option rows are only rebuilt when options changes. Reused across open/close, so nothing is allocated on every open
        void RebuildRows()
        {
            EnsurePopupElements();

            for (int i = _rows.Count - 1; i >= _options.Length; i--)
            {
                _list.Remove(_rows[i]);
                _rows.RemoveAt(i);
            }

            while (_rows.Count < _options.Length)
            {
                UILabel row = new UILabel(string.Empty)
                {
                    name = "tweeq-dropdown-option",

                    // Hit testing is derived from the layout's y on the _list side (rows don't carry per-row callbacks)
                    pickingMode = PickingMode.Ignore,
                };
                ApplyRowStyles(row);
                _list.Add(row);
                _rows.Add(row);
            }

            ApplyRowTexts();
        }

        // The row pool stays sized to the option count; only rows left over from filtering are collapsed.
        // Elements aren't recreated on every keystroke, so allocation while filtering is zero
        void ApplyRowTexts()
        {
            int visible = VisibleCount;

            for (int i = 0; i < _rows.Count; i++)
            {
                UILabel row = _rows[i];

                if (i >= visible)
                {
                    row.style.display = DisplayStyle.None;
                    continue;
                }

                row.style.display = DisplayStyle.Flex;

                int option = OptionIndexAt(i);
                row.text = option >= 0 && option < _labelCache.Length
                    ? _labelCache[option]
                    : string.Empty;
            }
        }

        void EnsurePopupElements()
        {
            if (_surface != null)
            {
                return;
            }

            // Positioning is the popover's responsibility, so no absolute positioning here
            _surface = new VisualElement { name = "tweeq-dropdown-popup" };

            // The shadow is drawn overflowing outside surface, so no clipping is applied
            _surface.style.overflow = Overflow.Visible;

            _shadowLayer = new VisualElement
            {
                name = "tweeq-dropdown-shadow",
                pickingMode = PickingMode.Ignore,
            };
            _shadowLayer.style.position = Position.Absolute;
            _shadowLayer.style.left = -SHADOW_SPREAD;
            _shadowLayer.style.right = -SHADOW_SPREAD;
            _shadowLayer.style.top = -SHADOW_SPREAD;
            _shadowLayer.style.bottom = -SHADOW_SPREAD;
            _shadowLayer.generateVisualContent += OnGenerateShadow;
            _surface.Add(_shadowLayer);

            _viewport = new VisualElement { name = "tweeq-dropdown-viewport" };
            _viewport.style.overflow = Overflow.Hidden;
            _viewport.style.position = Position.Relative;
            _viewport.RegisterCallback<PointerMoveEvent>(OnListPointerMove);
            _viewport.RegisterCallback<PointerDownEvent>(OnListPointerDown);
            _viewport.RegisterCallback<WheelEvent>(OnListWheel);
            _surface.Add(_viewport);

            _list = new VisualElement
            {
                name = "tweeq-dropdown-list",
                pickingMode = PickingMode.Ignore,
            };
            _list.style.position = Position.Absolute;
            _list.style.left = 0f;
            _list.style.right = 0f;
            _list.style.top = 0f;
            _viewport.Add(_list);

            // Arrows are added after list, meaning they layer on top
            _arrowUp = new VisualElement { name = "tweeq-dropdown-scroll-up" };
            SetupScrollArrow(_arrowUp, true);
            _arrowUp.generateVisualContent += OnGenerateArrowUp;
            _arrowUp.RegisterCallback<PointerEnterEvent>(OnArrowEnterUp);
            _viewport.Add(_arrowUp);

            _arrowDown = new VisualElement { name = "tweeq-dropdown-scroll-down" };
            SetupScrollArrow(_arrowDown, false);
            _arrowDown.generateVisualContent += OnGenerateArrowDown;
            _arrowDown.RegisterCallback<PointerEnterEvent>(OnArrowEnterDown);
            _viewport.Add(_arrowDown);

            ApplyPopupStyles();
        }

        void SetupScrollArrow(VisualElement arrow, bool up)
        {
            arrow.style.position = Position.Absolute;
            arrow.style.left = 0f;
            arrow.style.right = 0f;
            arrow.style.display = DisplayStyle.None;

            if (up)
            {
                arrow.style.top = 0f;
            }
            else
            {
                arrow.style.bottom = 0f;
            }

            arrow.RegisterCallback<PointerLeaveEvent>(OnArrowLeave);
        }

        void ApplyRowStyles(UILabel row)
        {
            if (_theme == null || row == null)
            {
                return;
            }

            row.style.height = _theme.InputHeight;
            row.style.paddingLeft = OPTION_PADDING;
            row.style.paddingRight = OPTION_PADDING;
            row.style.paddingTop = 0f;
            row.style.paddingBottom = 0f;
            row.style.marginLeft = 0f;
            row.style.marginRight = 0f;
            row.style.marginTop = 0f;
            row.style.marginBottom = 0f;
            row.style.fontSize = _theme.FontSizeInput;
            row.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.style.whiteSpace = WhiteSpace.NoWrap;
            row.style.overflow = Overflow.Hidden;
            row.style.textOverflow = TextOverflow.Ellipsis;
            SetCornerRadius(row, _theme.InputRadius, true, true, true, true);

            TweeqFonts.Apply(row, _theme.FontUi);
        }

        void ApplyPopupStyles()
        {
            if (_theme == null || _surface == null)
            {
                return;
            }

            // UI Toolkit has no blur, so a translucent Surface would let rows behind it show through → composite opaque instead
            _surface.style.backgroundColor = _theme.SurfaceOpaque;
            SetBorderWidth(_surface, POPUP_BORDER_WIDTH);
            SetBorderColor(_surface, _theme.Border);
            SetCornerRadius(_surface, _theme.RadiusPopup, true, true, true, true);

            float padding = _theme.PopupPadding;
            _surface.style.paddingLeft = padding;
            _surface.style.paddingRight = padding;
            _surface.style.paddingTop = padding;
            _surface.style.paddingBottom = padding;

            float arrowHeight = _theme.InputHeight * SCROLL_ARROW_RATIO;

            if (_arrowUp != null)
            {
                _arrowUp.style.height = arrowHeight;
            }

            if (_arrowDown != null)
            {
                _arrowDown.style.height = arrowHeight;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                ApplyRowStyles(_rows[i]);
            }
        }

        #endregion

        #region Popup placement

        void ShowPopup()
        {
            if (this.panel == null || _theme == null)
            {
                // Without a panel attached, nothing visual can be shown. Just advance the logical state without throwing
                return;
            }

            EnsurePopupElements();
            ApplyPopupStyles();

            if (_popover == null)
            {
                // popover-spec.md: Dropdown uses LightDismiss=false. Closing is the owner's (i.e. here) responsibility.
                // The chrome is drawn by us to align row width with the field, so use a pass-through host with Chrome=false
                _popover = new TweeqPopover { Context = this, LightDismiss = false, Chrome = false };
                _popover.Closed += OnPopoverClosed;
            }

            if (!_popupAttached)
            {
                _popover.Add(_surface);
                _popupAttached = true;
            }

            Vector2 position = Layout();
            _popover.Open(position);

            AttachDismissHandlers();

            // Layout isn't settled on the first pass, so reapply on the next frame once size is determined
            this.schedule.Execute(RelayoutPopup).StartingIn(0);
        }

        void HidePopup()
        {
            DetachDismissHandlers();

            if (_popover == null)
            {
                return;
            }

            _popover.Close();
        }

        void OnPopoverClosed()
        {
            // Keep state in sync even when closed from the popover side (shouldn't normally happen with LightDismiss=false)
            if (!_open)
            {
                return;
            }

            Close();
        }

        void RelayoutPopup()
        {
            if (!_open || _popover == null)
            {
                return;
            }

            Vector2 position = Layout();
            _popover.Open(position);
        }

        // Has Core back-calculate the position where the selected option overlaps the field,
        // and shifts whatever doesn't fit into internal scrolling (popover-spec.md "macOS-style placement")
        Vector2 Layout()
        {
            float itemHeight = _theme.InputHeight;
            float padding = _theme.PopupPadding;

            Rect field = this.worldBound;
            float fieldTop = float.IsNaN(field.yMin) ? 0f : field.yMin;
            float fieldLeft = float.IsNaN(field.xMin) ? 0f : field.xMin;
            float fieldWidth = float.IsNaN(field.width) || field.width <= 0f
                ? _theme.InputHeight
                : field.width;

            float fieldHeight = float.IsNaN(field.height) || field.height <= 0f
                ? _theme.InputHeight
                : field.height;

            float viewportHeight = ViewportHeight();
            float chromeTop = padding + POPUP_BORDER_WIDTH;
            int index = _valueIndex < 0 ? 0 : _valueIndex;

            _listHeight = VisibleCount * itemHeight;

            double top;
            bool forceUpward = _popupDirection == DropdownPopupDirection.Upward;
            bool forceDownward = _popupDirection == DropdownPopupDirection.Downward;
            if (forceUpward)
            {
                top = DropdownLogic.GetDropdownTopUpward(
                    fieldTop,
                    itemHeight,
                    VIEWPORT_MARGIN,
                    chromeTop,
                    _listHeight);
            }
            else if (_filtering || forceDownward)
            {
                // Filtering and explicit downward placement keep the list anchored below the field.
                top = fieldTop + fieldHeight;
            }
            else
            {
                // A pure function from Core. selectChrome is passed the popup's own top-edge chrome (border + padding).
                // Omitting listHeight makes the bottom-edge clamp fall back to the safe side of "unmeasured = list too tall", so it's always passed
                // fieldInset=0: the UI Toolkit version of the field doesn't carry Vue's border+outline 2px.
                // The E2E verification condition is that a row's worldBound exactly matches the field
                top = DropdownLogic.GetDropdownTop(
                    fieldTop,
                    index,
                    itemHeight,
                    viewportHeight,
                    VIEWPORT_MARGIN,
                    chromeTop,
                    _listHeight,
                    0.0);
            }

            float available = forceUpward
                ? fieldTop - (float)top - chromeTop * 2f
                : viewportHeight - (float)top - VIEWPORT_MARGIN - chromeTop * 2f;
            _visibleHeight = Mathf.Max(itemHeight, Mathf.Min(_listHeight, available));

            // Row width should match the field, so widen outward by exactly the padding and border
            _surface.style.width = fieldWidth + chromeTop * 2f;
            _viewport.style.height = _visibleHeight;

            if (_filtering)
            {
                // Always show filtered results from the top (Vue's scrollTop = 0)
                SetScroll(0f);
            }
            else
            {
                AlignActiveToField(fieldTop, (float)top + chromeTop, itemHeight);
            }

            return new Vector2(fieldLeft - chromeTop, (float)top);
        }

        float ViewportHeight()
        {
            if (this.panel != null && this.panel.visualTree != null)
            {
                float height = this.panel.visualTree.layout.height;
                if (!float.IsNaN(height) && height > 0f)
                {
                    return height;
                }
            }

            return Screen.height > 0 ? Screen.height : 0f;
        }

        // Vue alignCurrentToTrigger: aligns scroll so the selected row lands over the field
        void AlignActiveToField(float fieldTop, float listTop, float itemHeight)
        {
            int index = _valueIndex < 0 ? 0 : _valueIndex;
            SetScroll(listTop + index * itemHeight - fieldTop);
        }

        #endregion

        #region Scrolling

        void SetScroll(float offset)
        {
            float max = Mathf.Max(0f, _listHeight - _visibleHeight);
            _scrollOffset = Mathf.Clamp(offset, 0f, max);

            if (_list != null)
            {
                _list.style.top = -_scrollOffset;
            }

            UpdateScrollArrows();
        }

        void UpdateScrollArrows()
        {
            if (_arrowUp == null || _arrowDown == null)
            {
                return;
            }

            bool canUp = _scrollOffset > SCROLL_EPSILON;
            bool canDown = _scrollOffset + _visibleHeight < _listHeight - SCROLL_EPSILON;

            _arrowUp.style.display = canUp ? DisplayStyle.Flex : DisplayStyle.None;
            _arrowDown.style.display = canDown ? DisplayStyle.Flex : DisplayStyle.None;

            if (_autoScrollDirection < 0 && !canUp)
            {
                StopAutoScroll();
            }
            else if (_autoScrollDirection > 0 && !canDown)
            {
                StopAutoScroll();
            }
        }

        void ScrollActiveIntoView()
        {
            int visibleIndex = VisibleIndexOfValue();
            if (_theme == null || visibleIndex < 0)
            {
                return;
            }

            float itemHeight = _theme.InputHeight;
            float rowTop = visibleIndex * itemHeight;
            float rowBottom = rowTop + itemHeight;

            if (rowTop < _scrollOffset)
            {
                SetScroll(rowTop);
                return;
            }

            if (rowBottom > _scrollOffset + _visibleHeight)
            {
                SetScroll(rowBottom - _visibleHeight);
            }
        }

        void StartAutoScroll(int direction)
        {
            if (!_open || direction == 0)
            {
                return;
            }

            _autoScrollDirection = direction;

            if (this.panel == null)
            {
                return;
            }

            if (_autoScrollItem == null)
            {
                // Only one scheduled item is created and reused (avoids allocating a closure every frame)
                _autoScrollItem = this.schedule
                    .Execute(OnAutoScrollTick)
                    .Every(AUTO_SCROLL_INTERVAL_MS);
            }

            _autoScrollItem.Resume();
        }

        void StopAutoScroll()
        {
            _autoScrollDirection = 0;
            _autoScrollItem?.Pause();
        }

        void OnAutoScrollTick()
        {
            if (!_open || _autoScrollDirection == 0)
            {
                StopAutoScroll();
                return;
            }

            SetScroll(_scrollOffset + _autoScrollDirection * AUTO_SCROLL_SPEED);
        }

        #endregion

        #region Popup interaction

        // No per-row callback; derived from the local y within the list instead (same trick as RadioInput).
        // Returns "position in display order", so convert to an options index via OptionIndexAt
        int RowIndexAt(float localY)
        {
            int visible = VisibleCount;
            if (_theme == null || visible == 0)
            {
                return -1;
            }

            float itemHeight = _theme.InputHeight;
            if (itemHeight <= 0f)
            {
                return -1;
            }

            int index = Mathf.FloorToInt((localY + _scrollOffset) / itemHeight);
            return index < 0 || index >= visible ? -1 : index;
        }

        void OnListPointerMove(PointerMoveEvent evt)
        {
            if (evt == null || !_open || _viewport == null)
            {
                return;
            }

            SelectRowAt(evt);
        }

        void OnListPointerDown(PointerDownEvent evt)
        {
            if (evt == null || !_open || _viewport == null)
            {
                return;
            }

            // A press inside the popup isn't treated as an outside click.
            // Also return focus to the field so Enter / Escape keep working
            SelectRowAt(evt);

            // While filtering, return to the caret-holding TextField; otherwise return to the root
            FocusSelf();

            evt.StopPropagation();
        }

        void SelectRowAt(IPointerEvent evt)
        {
            // Events over the arrows also bubble up to viewport. Don't grab a row hidden behind the band
            if (_pointerOverArrow)
            {
                return;
            }

            Vector3 position = evt.position;
            Vector2 local = _viewport.WorldToLocal(new Vector2(position.x, position.y));
            int option = OptionIndexAt(RowIndexAt(local.y));
            if (option < 0)
            {
                return;
            }

            // Vue updates the model on an option's pointerenter (hover becomes an instant preview)
            this.value = _options[option];
        }

        void OnListWheel(WheelEvent evt)
        {
            if (evt == null || !_open)
            {
                return;
            }

            SetScroll(_scrollOffset + evt.delta.y * (_theme != null ? _theme.InputHeight : 1f));
            evt.StopPropagation();
        }

        void OnArrowEnterUp(PointerEnterEvent evt)
        {
            _pointerOverArrow = true;
            StartAutoScroll(-1);
        }

        void OnArrowEnterDown(PointerEnterEvent evt)
        {
            _pointerOverArrow = true;
            StartAutoScroll(1);
        }

        void OnArrowLeave(PointerLeaveEvent evt)
        {
            _pointerOverArrow = false;
            StopAutoScroll();
        }

        #endregion

        #region Dismiss handling

        // Since popover uses LightDismiss=false, outside clicks and releases are caught here.
        // pointerup is received via BubbleUp so "ignore only when released over an arrow" can be enforced
        void AttachDismissHandlers()
        {
            if (this.panel == null || _dismissRoot != null)
            {
                return;
            }

            _dismissRoot = this.panel.visualTree;
            if (_dismissRoot == null)
            {
                return;
            }

            _dismissRoot.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
            _dismissRoot.RegisterCallback<PointerUpEvent>(OnRootPointerUp);
        }

        void DetachDismissHandlers()
        {
            if (_dismissRoot == null)
            {
                return;
            }

            _dismissRoot.UnregisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
            _dismissRoot.UnregisterCallback<PointerUpEvent>(OnRootPointerUp);
            _dismissRoot = null;
        }

        void OnRootPointerDown(PointerDownEvent evt)
        {
            if (evt == null || !_open)
            {
                return;
            }

            VisualElement target = evt.target as VisualElement;
            if (target != null && (IsInsideField(target) || IsInsidePopup(target)))
            {
                return;
            }

            // An outside click rolls back to valueAtStart (matches Vue's onPopoverUpdateOpen).
            // Because hover / up-down / filter input move the value live, closing by anything
            // other than a commit operation (Enter / option click) reverts to the value at open time
            // (changed from M5's "keep the current value" by user decision on 2026-07-27)
            Cancel();
        }

        void OnRootPointerUp(PointerUpEvent evt)
        {
            if (evt == null || !_open)
            {
                return;
            }

            PerformPointerUp();
        }

        bool IsInsideField(VisualElement element)
        {
            return element == this || this.Contains(element);
        }

        bool IsInsidePopup(VisualElement element)
        {
            return _surface != null && (element == _surface || _surface.Contains(element));
        }

        #endregion

        #region Field interaction

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled)
            {
                return;
            }

            if (_filtering)
            {
                // A field press while filtering is a caret operation. Passed straight through to the TextField
                return;
            }

            if (this.panel != null)
            {
                // Don't capture the pointer. Capturing it would stop popup rows from receiving PointerMove
                this.Focus();
            }

            Open();
            evt.StopPropagation();
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

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    evt.StopPropagation();
                    break;

                case KeyCode.DownArrow:
                    MoveSelection(1);
                    evt.StopPropagation();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_open)
                    {
                        Commit();
                    }
                    else
                    {
                        Open();
                    }

                    evt.StopPropagation();
                    break;

                case KeyCode.Escape:
                    if (_open)
                    {
                        Cancel();
                        evt.StopPropagation();
                    }

                    break;

                default:
                    TryBeginFilterFromKey(evt);
                    break;
            }
        }

        // Spec §B: a printable character while focused enters filter mode, opening even if closed.
        // Unity fires two events per keystroke — one with a keyCode (character = '\0') and one with a
        // character (keyCode = None) — so only the character-side event is picked up.
        // Keystrokes after filtering starts are received directly by the TextField, so only the first character is handled here
        void TryBeginFilterFromKey(KeyDownEvent evt)
        {
            if (_filtering || _options.Length == 0)
            {
                return;
            }

            // A Ctrl/Cmd combo is a shortcut, not character input
            const EventModifiers commandKeys = EventModifiers.Control | EventModifiers.Command;
            if ((evt.modifiers & commandKeys) != 0)
            {
                return;
            }

            if (!IsPrintable(evt.character))
            {
                return;
            }

            BeginFilter(evt.character.ToString());
            evt.StopPropagation();
        }

        static bool IsPrintable(char character)
        {
            // Rejects '\0' (the keyCode-side event) and Enter / Tab / Backspace / Escape / DEL
            return character >= ' ' && character != (char)127;
        }

        // feedback-fixes-01.md A-5: up/down only change the selection. Focus doesn't move
        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Up:
                case NavigationMoveEvent.Direction.Down:
                    break;

                default:
                    return;
            }

            evt.StopPropagation();

            // In Unity 6, this is what actually stops "the focus move itself" (PreventDefault is deprecated)
            this.focusController?.IgnoreEvent(evt);
        }

        void OnFocusIn(FocusInEvent evt)
        {
            _focused = true;
            Refresh();
        }

        // Elements inside the popup aren't focusable, so this fires the instant an option is clicked.
        // Closing here would prevent the click-commit from taking effect, so closing is left to only
        // three paths — outside click, Escape, and detach (Vue doesn't close on blur either)
        void OnFocusOut(FocusOutEvent evt)
        {
            _focused = false;
            Refresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            StopAutoScroll();
            _autoScrollItem = null;
            DetachDismissHandlers();

            if (_open)
            {
                Close();
            }

            // The filter shouldn't be left active while closed, but
            // collapse it anyway so a detached element doesn't carry over keystroke state
            EndFilter();

            _hovered = false;
            _focused = false;
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            if (_disabled)
            {
                // Spec §5: transparent background + a 1px inset border
                this.style.backgroundColor = Color.clear;
                SetBorderWidth(this, DISABLED_BORDER_WIDTH);
                SetBorderColor(this, _theme.Border);
            }
            else
            {
                SetBorderWidth(this, 0f);
                this.style.backgroundColor = _hovered || _open ? _theme.InputHover : _theme.Input;
            }

            if (_fieldLabel != null)
            {
                _fieldLabel.style.color = TextColor;
            }

            UpdateFilterTextColor();

            if (_chevron != null)
            {
                _chevron.style.opacity = _hovered || _focused || _open ? 1f : CHEVRON_IDLE_OPACITY;
                _chevron.MarkDirtyRepaint();
            }

            if (_focusRing != null)
            {
                _focusRing.style.display = _focused && !_disabled
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            RefreshRows();
        }

        // disabled overrides invalid (red text on a disabled field could be misread as "an interactable invalid value")
        Color TextColor
        {
            get
            {
                if (_disabled)
                {
                    return _theme.TextSubtle;
                }

                return _invalid ? _theme.Error : _theme.Text;
            }
        }

        void UpdateFilterTextColor()
        {
            if (_filterField == null)
            {
                return;
            }

            Color color = TextColor;
            _filterField.style.color = color;

            if (_filterInput != null)
            {
                _filterInput.style.color = color;
            }
        }

        void RefreshRows()
        {
            if (_rows.Count == 0)
            {
                return;
            }

            Color onAccent = TweeqTheme.ContrastText(_theme.Accent);
            int currentIndex = _open ? _valueAtStartIndex : -1;
            int visible = VisibleCount;

            for (int i = 0; i < _rows.Count; i++)
            {
                if (i >= visible)
                {
                    // No point repainting a row that's collapsed
                    continue;
                }

                UILabel row = _rows[i];
                int option = OptionIndexAt(i);
                bool active = option >= 0 && option == _valueIndex;
                bool current = option >= 0 && option == currentIndex;

                row.style.backgroundColor = active
                    ? _theme.Accent
                    : current ? _theme.AccentSoft : Color.clear;
                row.style.color = active ? onAccent : _theme.Text;
            }
        }

        void NotifyValueChanged(T previous, T current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<T> changeEvent = ChangeEvent<T>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Painting

        // Equivalent to mdi:unfold-more-horizontal. Drawn as small up/down triangles to avoid depending on a font
        void OnGenerateChevron(MeshGenerationContext context)
        {
            if (context == null || _theme == null || _chevron == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _chevron.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float centerX = rect.width * 0.5f;
            float centerY = rect.height * 0.5f;
            float half = CHEVRON_TRIANGLE_WIDTH * 0.5f;

            painter.fillColor = _theme.TextSubtle;

            painter.BeginPath();
            painter.MoveTo(new Vector2(centerX, centerY - CHEVRON_TRIANGLE_GAP - CHEVRON_TRIANGLE_HEIGHT));
            painter.LineTo(new Vector2(centerX + half, centerY - CHEVRON_TRIANGLE_GAP));
            painter.LineTo(new Vector2(centerX - half, centerY - CHEVRON_TRIANGLE_GAP));
            painter.ClosePath();
            painter.Fill();

            painter.BeginPath();
            painter.MoveTo(new Vector2(centerX, centerY + CHEVRON_TRIANGLE_GAP + CHEVRON_TRIANGLE_HEIGHT));
            painter.LineTo(new Vector2(centerX + half, centerY + CHEVRON_TRIANGLE_GAP));
            painter.LineTo(new Vector2(centerX - half, centerY + CHEVRON_TRIANGLE_GAP));
            painter.ClosePath();
            painter.Fill();
        }

        void OnGenerateArrowUp(MeshGenerationContext context)
        {
            PaintScrollArrow(context, _arrowUp, true);
        }

        void OnGenerateArrowDown(MeshGenerationContext context)
        {
            PaintScrollArrow(context, _arrowDown, false);
        }

        // A band + triangle covering the cut-off edge. Vue uses a linear-gradient, but UI Toolkit's
        // inline styles have no gradient support, so a flat Surface fill substitutes for it
        void PaintScrollArrow(MeshGenerationContext context, VisualElement arrow, bool up)
        {
            if (context == null || _theme == null || arrow == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = arrow.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            // Must match the popup face's color (SurfaceOpaque), or a seam shows under the arrow
            painter.fillColor = _theme.SurfaceOpaque;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, 0f));
            painter.LineTo(new Vector2(rect.width, 0f));
            painter.LineTo(new Vector2(rect.width, rect.height));
            painter.LineTo(new Vector2(0f, rect.height));
            painter.ClosePath();
            painter.Fill();

            float centerX = rect.width * 0.5f;
            float centerY = rect.height * 0.5f;
            float half = CHEVRON_TRIANGLE_WIDTH * 0.5f;
            float halfHeight = CHEVRON_TRIANGLE_HEIGHT * 0.5f;
            float tipY = up ? centerY - halfHeight : centerY + halfHeight;
            float baseY = up ? centerY + halfHeight : centerY - halfHeight;

            painter.fillColor = _theme.Text;
            painter.BeginPath();
            painter.MoveTo(new Vector2(centerX, tipY));
            painter.LineTo(new Vector2(centerX + half, baseY));
            painter.LineTo(new Vector2(centerX - half, baseY));
            painter.ClosePath();
            painter.Fill();
        }

        // There's no box-shadow, so approximate "0 0 20px" by stacking several outward-growing rounded layers
        void OnGenerateShadow(MeshGenerationContext context)
        {
            if (context == null || _theme == null || _shadowLayer == null)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _shadowLayer.contentRect;
            if (float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= SHADOW_SPREAD * 2f || rect.height <= SHADOW_SPREAD * 2f)
            {
                return;
            }

            Color shadow = _theme.Shadow;
            float radius = _theme.RadiusPopup;

            for (int i = SHADOW_LAYERS; i >= 1; i--)
            {
                float grow = SHADOW_SPREAD * i / SHADOW_LAYERS;
                Color layer = shadow;

                // Thinner toward the outside. Stacking them approximates a Gaussian-like falloff
                layer.a = shadow.a / SHADOW_LAYERS;
                painter.fillColor = layer;

                TraceRoundedRect(
                    painter,
                    SHADOW_SPREAD - grow,
                    SHADOW_SPREAD - grow,
                    rect.width - (SHADOW_SPREAD - grow) * 2f,
                    rect.height - (SHADOW_SPREAD - grow) * 2f,
                    radius + grow);
                painter.Fill();
            }
        }

        static void TraceRoundedRect(
            Painter2D painter, float x, float y, float width, float height, float radius)
        {
            float limit = Mathf.Min(width, height) * 0.5f;
            float r = Mathf.Clamp(radius, 0f, limit);

            painter.BeginPath();
            painter.MoveTo(new Vector2(x + r, y));
            painter.ArcTo(new Vector2(x + width, y), new Vector2(x + width, y + height), r);
            painter.ArcTo(new Vector2(x + width, y + height), new Vector2(x, y + height), r);
            painter.ArcTo(new Vector2(x, y + height), new Vector2(x, y), r);
            painter.ArcTo(new Vector2(x, y), new Vector2(x + width, y), r);
            painter.ClosePath();
        }

        #endregion

        #region Helpers

        long Now()
        {
            Func<long> source = TimeSource;
            if (source != null)
            {
                return source();
            }

            return (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
        }

        // C#'s % returns negative for negative values, so the sign is normalized
        static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        static void ApplyTransition(VisualElement element, float duration, string property)
        {
            if (element == null)
            {
                return;
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName(property) });
            element.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(duration, TimeUnit.Second) });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.EaseInOutCubic) });
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
    }
}
