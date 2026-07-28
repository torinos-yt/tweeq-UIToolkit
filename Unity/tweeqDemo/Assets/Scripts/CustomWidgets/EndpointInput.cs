using System;
using System.Globalization;
using System.Text;
using Tweeq.Core;
using Tweeq.UIToolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TweeqDemo.CustomWidgets
{
    #region Address

    /// <summary>
    /// The parsed result of an IPv4 address (+ optional port).
    /// </summary>
    /// <remarks>
    /// The 4 octets are held as fields rather than an array so that the normalization that
    /// runs on every keystroke doesn't touch the heap. Range clamping happens while
    /// <see cref="TryParse"/> accumulates digits, so a long never overflows no matter how
    /// many decimal digits arrive.
    /// </remarks>
    public readonly struct EndpointAddress
    {
        /// <summary>Number of octets.</summary>
        public const int OCTET_COUNT = 4;

        /// <summary>Upper bound of an octet.</summary>
        public const int OCTET_MAX = 255;

        /// <summary>Upper bound of the port (16 bit).</summary>
        public const int PORT_MAX = 65535;

        readonly int _octet0;
        readonly int _octet1;
        readonly int _octet2;
        readonly int _octet3;

        /// <summary>Port number. 0 when <see cref="HasPort"/> is false.</summary>
        public readonly int Port;

        /// <summary>Whether the original string contained ":port".</summary>
        public readonly bool HasPort;

        public EndpointAddress(int octet0, int octet1, int octet2, int octet3, int port, bool hasPort)
        {
            _octet0 = octet0;
            _octet1 = octet1;
            _octet2 = octet2;
            _octet3 = octet3;
            Port = port;
            HasPort = hasPort;
        }

        /// <summary>The octet at a 0-based index. Returns 0 out of range (a policy of not throwing at boundaries).</summary>
        public int GetOctet(int index)
        {
            switch (index)
            {
                case 0: return _octet0;
                case 1: return _octet1;
                case 2: return _octet2;
                case 3: return _octet3;
                default: return 0;
            }
        }

        /// <summary>Writes out a normalized string.</summary>
        public string Format(bool includePort)
        {
            StringBuilder builder = new StringBuilder(21);
            for (int index = 0; index < OCTET_COUNT; index++)
            {
                if (index > 0)
                {
                    builder.Append('.');
                }

                builder.Append(GetOctet(index).ToString(CultureInfo.InvariantCulture));
            }

            if (includePort)
            {
                builder.Append(':');
                builder.Append(Port.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Parses "1.2.3.4" / "1.2.3.4:8080". Redundant leading zeros are allowed, and values
        /// exceeding the range are rounded down to the upper bound. Fails as soon as a
        /// non-digit character appears.
        /// </summary>
        public static bool TryParse(string text, out EndpointAddress address)
        {
            address = default;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            string host = trimmed;
            int port = 0;
            bool hasPort = false;

            int colon = trimmed.IndexOf(':');
            if (colon >= 0)
            {
                // A string with two or more ":" is either IPv6 or a typo. We handle neither.
                if (trimmed.IndexOf(':', colon + 1) >= 0)
                {
                    return false;
                }

                host = trimmed.Substring(0, colon);
                if (!TryParseClamped(trimmed.Substring(colon + 1), PORT_MAX, out port))
                {
                    return false;
                }

                hasPort = true;
            }

            string[] parts = host.Split('.');
            if (parts.Length != OCTET_COUNT)
            {
                return false;
            }

            int octet0;
            int octet1;
            int octet2;
            int octet3;
            if (!TryParseClamped(parts[0], OCTET_MAX, out octet0)
                || !TryParseClamped(parts[1], OCTET_MAX, out octet1)
                || !TryParseClamped(parts[2], OCTET_MAX, out octet2)
                || !TryParseClamped(parts[3], OCTET_MAX, out octet3))
            {
                return false;
            }

            address = new EndpointAddress(octet0, octet1, octet2, octet3, port, hasPort);
            return true;
        }

        // Digits are accumulated while capping at the upper bound, so even
        // "99999999999999999999" never overflows.
        static bool TryParseClamped(string text, int max, out int result)
        {
            result = 0;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            long accumulated = 0;
            for (int index = 0; index < text.Length; index++)
            {
                char c = text[index];
                if (c < '0' || c > '9')
                {
                    return false;
                }

                accumulated = accumulated * 10 + (c - '0');
                if (accumulated > max)
                {
                    accumulated = max;
                }
            }

            result = (int)accumulated;
            return true;
        }
    }

    #endregion

    /// <summary>
    /// An endpoint (IPv4[:port]) input with tweeq's input-box chrome and feel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A proof-of-concept sample showing that a custom widget can be built from an external
    /// asmdef (<c>Tweeq.Demo.CustomWidgets</c>) using only the public API of <c>Tweeq.Core</c> /
    /// <c>Tweeq.UIToolkit</c> (ext-custom-widgets-spec.md EXT-02). It depends on none of the
    /// package's internals.
    /// </para>
    /// <para>
    /// The state machine (session, segment editing, scrub) is kept panel-independent; the
    /// real UI's focus/pointer wiring only calls into that layer. EditMode tests can therefore
    /// verify the contract without a panel (same design as StringInput).
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class EndpointInput
        : VisualElement, INotifyValueChanged<string>, ITweeqThemed, ITweeqInputBox,
          ITweeqConfirmable<string>
    {
        #region Constants

        /// <summary>Number of octets.</summary>
        public const int OCTET_COUNT = EndpointAddress.OCTET_COUNT;

        /// <summary>Maximum number of segments including the port.</summary>
        public const int MAX_SEGMENT_COUNT = OCTET_COUNT + 1;

        /// <summary>Scrub sensitivity. Spec EXT-02: 1 step per 4px (crosses 0-255 over roughly 1000px).</summary>
        public const float PIXELS_PER_STEP = 4f;

        /// <summary>Shift (fast) multiplier.</summary>
        public const double FAST_MULTIPLIER = 10.0;

        const float TEXT_FONT_SIZE = 12f;
        const float BOX_PADDING = 6f;
        const float OCTET_WIDTH = 26f;
        const float PORT_WIDTH = 38f;
        const float DISABLED_OPACITY = 0.4f;

        #endregion

        #region Fields

        readonly EndpointSegment[] _segments = new EndpointSegment[MAX_SEGMENT_COUNT];
        readonly Label[] _separators = new Label[OCTET_COUNT];

        TweeqFocusRing _focusRing;

        TweeqTheme _theme = TweeqTheme.Dark();
        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        bool _portEnabled;
        bool _disabled;
        bool _invalid;
        bool _hovered;

        bool _sessionActive;
        string _valueAtSessionStart = string.Empty;

        int _focusedSegment;

        // A guard against writing back the mid-keystroke display (e.g. blank or "00") during
        // the ComposeValue → value setter → ApplyAddress round trip.
        bool _composing;

        #endregion

        #region Public API

        /// <summary>Fires once when the editing session ends, only if the value actually changed.</summary>
        public event Action<string> Confirmed;

        /// <summary>
        /// The normalized endpoint string. ":port" is appended only when <see cref="PortEnabled"/> is true.
        /// </summary>
        /// <remarks>
        /// The setter silently discards unparsable strings (so programmatic misuse never throws during a live show).
        /// </remarks>
        [UxmlAttribute]
        public string value
        {
            get { return ComposeValue(); }
            set
            {
                string previous = ComposeValue();
                SetValueWithoutNotify(value);

                string current = ComposeValue();
                if (previous == current)
                {
                    return;
                }

                NotifyValueChanged(previous, current);
            }
        }

        /// <summary>Whether to show the port segment (0-65535).</summary>
        [UxmlAttribute]
        public bool PortEnabled
        {
            get { return _portEnabled; }
            set
            {
                if (_portEnabled == value)
                {
                    return;
                }

                string previous = ComposeValue();
                _portEnabled = value;

                // If focus stays on a hidden segment, the Tab order skips around
                if (!_portEnabled && _focusedSegment >= SegmentCount)
                {
                    _focusedSegment = SegmentCount - 1;
                }

                ApplyPortVisibility();

                string current = ComposeValue();
                if (previous != current)
                {
                    NotifyValueChanged(previous, current);
                }
            }
        }

        /// <summary>Inoperable state. Lowers opacity and blocks pointer/focus.</summary>
        [UxmlAttribute]
        public bool Disabled
        {
            get { return _disabled; }
            set
            {
                if (_disabled == value)
                {
                    return;
                }

                _disabled = value;

                // Firing a confirm at the moment of disabling would raise Confirmed without any actual operation, so avoid it
                if (_disabled && _sessionActive)
                {
                    EndSession(false);
                }

                ApplyInteractivity();
                Refresh();
            }
        }

        /// <summary>Externally supplied invalid-value display. Following NumberInput's convention, only the text color turns Error.</summary>
        [UxmlAttribute]
        public bool Invalid
        {
            get { return _invalid; }
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

        /// <summary>Color theme. Falls back to Dark() when null is passed.</summary>
        public TweeqTheme Theme
        {
            get { return _theme; }
            set
            {
                _theme = value ?? TweeqTheme.Dark();
                ApplyStaticStyles();
                Refresh();
            }
        }

        /// <summary>Position within a horizontal group. Setting this squares off the corner radius.</summary>
        public TweeqBoxPosition InlinePosition
        {
            get { return _inlinePosition; }
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
            get { return _blockPosition; }
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

        /// <summary>Number of segments currently shown (5 when the port is enabled).</summary>
        public int SegmentCount
        {
            get { return _portEnabled ? MAX_SEGMENT_COUNT : OCTET_COUNT; }
        }

        /// <summary>Index of the segment that currently receives key input.</summary>
        public int FocusedSegment
        {
            get { return _focusedSegment; }
        }

        /// <summary>Whether an editing session is active.</summary>
        public bool IsSessionActive
        {
            get { return _sessionActive; }
        }

        /// <summary>The value at session start (what Escape restores to).</summary>
        public string ValueAtSessionStart
        {
            get { return _valueAtSessionStart; }
        }

        /// <summary>Sets the value without firing a ChangeEvent. Keeps the current value if unparsable.</summary>
        public void SetValueWithoutNotify(string newValue)
        {
            EndpointAddress address;
            if (!EndpointAddress.TryParse(newValue, out address))
            {
                return;
            }

            ApplyAddress(address);
        }

        /// <summary>The segment's current value. Out-of-range indices return 0.</summary>
        public int GetSegment(int index)
        {
            return IsValidSegment(index) ? _segments[index].Value : 0;
        }

        /// <summary>Writes a value into the segment and fires a ChangeEvent if the value actually moved.</summary>
        public void SetSegment(int index, int segmentValue)
        {
            if (!IsValidSegment(index))
            {
                return;
            }

            string previous = ComposeValue();
            _segments[index].SetValue(segmentValue, true);

            string current = ComposeValue();
            if (previous != current)
            {
                NotifyValueChanged(previous, current);
            }
        }

        /// <summary>The raw text displayed in the segment (can be blank while editing).</summary>
        public string GetSegmentText(int index)
        {
            return IsValidSegment(index) ? _segments[index].Text : string.Empty;
        }

        /// <summary>
        /// One keystroke applied to a segment. Drops non-digit characters, and if "." / ":"
        /// is present, moves to the next segment and selects it all (the same feel as
        /// Windows' IP input fields).
        /// </summary>
        public void SetSegmentText(int index, string text)
        {
            if (_disabled || !IsValidSegment(index))
            {
                return;
            }

            _segments[index].ApplyUserText(text);
        }

        /// <summary>Moves focus to a segment (also moves the real focus if a panel exists).</summary>
        public void FocusSegment(int index)
        {
            if (_disabled || !IsValidSegment(index))
            {
                return;
            }

            _focusedSegment = index;
            BeginSession();
            _segments[index].FocusAndSelectAll();
        }

        /// <summary>Moves relative to the current segment. Doesn't move past either end.</summary>
        public void MoveSegment(int delta)
        {
            int next = _focusedSegment + delta;
            if (next < 0 || next >= SegmentCount)
            {
                return;
            }

            FocusSegment(next);
        }

        #endregion

        #region Editing session

        /// <summary>Begins an editing session. Does nothing if one is already active.</summary>
        public void BeginSession()
        {
            if (_disabled || _sessionActive)
            {
                return;
            }

            _sessionActive = true;
            _valueAtSessionStart = ComposeValue();
            Refresh();
        }

        /// <summary>
        /// Commits on Enter. Normalizes the display (blank → 0), then fires
        /// <see cref="Confirmed"/> once if the value changed. The session keeps going.
        /// </summary>
        public void CommitEditing()
        {
            if (!_sessionActive)
            {
                return;
            }

            NormalizeSegmentText();
            FireConfirmedIfChanged();
        }

        /// <summary>Escape. Restores the session-start value and closes the session without confirming.</summary>
        public void CancelEditing()
        {
            if (!_sessionActive)
            {
                return;
            }

            // Close the session first so the blur path (OnFocusOut → EndSession) doesn't confirm it
            _sessionActive = false;

            this.value = _valueAtSessionStart;
            NormalizeSegmentText();
            Refresh();
        }

        /// <summary>Focus left the component. Confirms and closes the session.</summary>
        public void EndSession()
        {
            EndSession(true);
        }

        void EndSession(bool confirm)
        {
            if (!_sessionActive)
            {
                return;
            }

            NormalizeSegmentText();
            _sessionActive = false;

            if (confirm)
            {
                FireConfirmedIfChanged();
            }

            Refresh();
        }

        void FireConfirmedIfChanged()
        {
            string current = ComposeValue();
            if (current == _valueAtSessionStart)
            {
                return;
            }

            // Without advancing the baseline, the blur after Enter would fire once more
            _valueAtSessionStart = current;

            Action<string> confirmed = Confirmed;
            if (confirmed != null)
            {
                confirmed(current);
            }
        }

        void NormalizeSegmentText()
        {
            for (int index = 0; index < MAX_SEGMENT_COUNT; index++)
            {
                _segments[index].NormalizeText();
            }
        }

        #endregion

        #region Scrub (entry point of the state machine; the real UI calls it via TweeqScrubManipulator)

        /// <summary>Begins scrubbing a segment.</summary>
        public void BeginSegmentScrub(int index)
        {
            if (_disabled || !IsValidSegment(index))
            {
                return;
            }

            _focusedSegment = index;
            BeginSession();
            _segments[index].BeginScrub();
        }

        /// <summary>One scrub sample. Movement amount is in px.</summary>
        public void UpdateSegmentScrub(int index, float deltaX, float deltaY, bool shift, bool alt)
        {
            if (_disabled || !IsValidSegment(index))
            {
                return;
            }

            _segments[index].UpdateScrub(new ScrubUpdate(deltaX, deltaY, shift, alt));
        }

        /// <summary>Ends scrubbing (commit). Confirmation happens at session end, so it isn't fired here.</summary>
        public void EndSegmentScrub(int index)
        {
            if (!IsValidSegment(index))
            {
                return;
            }

            _segments[index].EndScrub();
        }

        /// <summary>Cancels scrubbing. Reverts to the value at the start.</summary>
        public void CancelSegmentScrub(int index)
        {
            if (!IsValidSegment(index))
            {
                return;
            }

            _segments[index].CancelScrub();
        }

        #endregion

        #region Construction

        public EndpointInput()
        {
            this.AddToClassList("tweeq-endpoint-input");

            // The root itself doesn't take focus. Tab stops belong to each embedded TextField
            this.focusable = false;
            this.style.flexDirection = FlexDirection.Row;
            this.style.alignItems = Align.Center;
            this.style.justifyContent = Justify.Center;
            this.style.flexShrink = 0f;
            this.style.overflow = Overflow.Hidden;
            this.style.paddingLeft = BOX_PADDING;
            this.style.paddingRight = BOX_PADDING;

            BuildChildren();
            ApplyStaticStyles();
            ApplyPortVisibility();
            ApplyInteractivity();

            // Registered with TrickleDown to intercept Enter / Escape before the TextField sees them
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);

            Refresh();
        }

        void BuildChildren()
        {
            for (int index = 0; index < MAX_SEGMENT_COUNT; index++)
            {
                bool isPort = index == OCTET_COUNT;

                EndpointSegment segment = new EndpointSegment(
                    index,
                    isPort ? EndpointAddress.PORT_MAX : EndpointAddress.OCTET_MAX,
                    isPort ? PORT_WIDTH : OCTET_WIDTH);

                segment.Changed += OnSegmentChanged;
                segment.Clicked += OnSegmentClicked;
                segment.MoveRequested += OnSegmentMoveRequested;
                segment.FocusGained += OnSegmentFocusGained;

                _segments[index] = segment;

                if (index > 0)
                {
                    Label separator = new Label(index == OCTET_COUNT ? ":" : ".")
                    {
                        name = "tweeq-endpoint-separator",
                        pickingMode = PickingMode.Ignore,
                    };
                    separator.style.fontSize = TEXT_FONT_SIZE;
                    separator.style.unityTextAlign = TextAnchor.MiddleCenter;
                    separator.style.marginLeft = 0f;
                    separator.style.marginRight = 0f;
                    separator.style.marginTop = 0f;
                    separator.style.marginBottom = 0f;
                    separator.style.paddingLeft = 0f;
                    separator.style.paddingRight = 0f;
                    separator.style.flexShrink = 0f;

                    _separators[index - 1] = separator;
                    this.hierarchy.Add(separator);
                }

                this.hierarchy.Add(segment);
            }

            // The focus ring is drawn with a border on a separate layer. Adding a border to the
            // root itself would shift the contents by 1px (same reason as StringInput)
            _focusRing = TweeqFocusRing.Attach(this);
            _focusRing.name = "tweeq-endpoint-focus-ring";
        }

        void ApplyStaticStyles()
        {
            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;

            ApplyCornerRadius();
            TweeqInputBoxStyles.SetBorderColor(this, _theme.Border);
            TweeqInputBoxStyles.ApplyBackgroundTransition(this, _theme);

            for (int index = 0; index < _separators.Length; index++)
            {
                Label separator = _separators[index];
                if (separator == null)
                {
                    continue;
                }

                separator.style.color = _theme.TextSubtle;
                TweeqFonts.Apply(separator, TweeqFonts.NumericFont);
            }

            for (int index = 0; index < MAX_SEGMENT_COUNT; index++)
            {
                _segments[index].ApplyTheme(_theme);
            }
        }

        void ApplyCornerRadius()
        {
            TweeqInputBoxStyles.ApplyCornerRadius(this, _theme, _inlinePosition, _blockPosition);

            // The focus ring is a separate layer, so reapply the same corner radius to it
            if (_focusRing != null)
            {
                _focusRing.Apply(_theme, _inlinePosition, _blockPosition);
            }
        }

        void ApplyPortVisibility()
        {
            DisplayStyle display = _portEnabled ? DisplayStyle.Flex : DisplayStyle.None;

            _segments[OCTET_COUNT].style.display = display;
            _segments[OCTET_COUNT].SetInteractive(_portEnabled && !_disabled);

            Label separator = _separators[OCTET_COUNT - 1];
            if (separator != null)
            {
                separator.style.display = display;
            }
        }

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            this.style.opacity = _disabled ? DISABLED_OPACITY : 1f;

            for (int index = 0; index < MAX_SEGMENT_COUNT; index++)
            {
                bool live = !_disabled && (index < OCTET_COUNT || _portEnabled);
                _segments[index].SetInteractive(live);
            }
        }

        #endregion

        #region Composition

        bool IsValidSegment(int index)
        {
            return index >= 0 && index < SegmentCount;
        }

        string ComposeValue()
        {
            EndpointAddress address = new EndpointAddress(
                _segments[0].Value,
                _segments[1].Value,
                _segments[2].Value,
                _segments[3].Value,
                _segments[OCTET_COUNT].Value,
                _portEnabled);

            return address.Format(_portEnabled);
        }

        void ApplyAddress(EndpointAddress address)
        {
            // Don't write the display back on the round trip from the keystroke path (_composing),
            // so the user can keep typing while it's "" or "00"
            bool syncDisplay = !_composing;

            for (int index = 0; index < OCTET_COUNT; index++)
            {
                _segments[index].SetValue(address.GetOctet(index), syncDisplay);
            }

            // A string with no port specified means "port 0". Since the value string represents
            // the entire state, feeding the same string back in always returns the same state
            _segments[OCTET_COUNT].SetValue(address.HasPort ? address.Port : 0, syncDisplay);
        }

        void NotifyValueChanged(string previous, string current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<string> changeEvent = ChangeEvent<string>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion

        #region Segment callbacks

        void OnSegmentChanged(EndpointSegment segment)
        {
            _composing = true;
            try
            {
                this.value = ComposeValue();
            }
            finally
            {
                _composing = false;
            }

            Refresh();
        }

        void OnSegmentClicked(EndpointSegment segment)
        {
            // Release below the threshold = a click. Select all so the segment can be retyped
            FocusSegment(segment.Index);
        }

        void OnSegmentMoveRequested(EndpointSegment segment, int delta)
        {
            _focusedSegment = segment.Index;

            // Don't leave the segment being left blank (prevents it staying "" right after exiting via ".")
            segment.NormalizeText();

            MoveSegment(delta);
        }

        void OnSegmentFocusGained(EndpointSegment segment)
        {
            _focusedSegment = segment.Index;
            BeginSession();
            Refresh();
        }

        #endregion

        #region Events

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

        void OnFocusIn(FocusInEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            BeginSession();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            // Don't close the session when moving between segments (1 session = until leaving the component)
            VisualElement related = evt.relatedTarget as VisualElement;
            if (related != null && this.Contains(related))
            {
                return;
            }

            EndSession();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_sessionActive)
                    {
                        CommitEditing();
                        evt.StopPropagation();
                    }

                    break;

                case KeyCode.Escape:
                    if (_sessionActive)
                    {
                        CancelEditing();
                        evt.StopPropagation();
                    }

                    break;
            }
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            UpdateBackground();
            UpdateTextColor();

            if (_focusRing != null)
            {
                _focusRing.Visible = _sessionActive && !_disabled;
            }
        }

        void UpdateBackground()
        {
            TweeqInputBoxStyles.ApplyDisabledChrome(this, _theme, _disabled);

            if (_disabled)
            {
                return;
            }

            this.style.backgroundColor = TweeqInputBoxStyles.ResolveBackground(_theme, _hovered);
        }

        void UpdateTextColor()
        {
            Color color = _invalid ? _theme.Error : _theme.Text;

            for (int index = 0; index < MAX_SEGMENT_COUNT; index++)
            {
                _segments[index].ApplyTextColor(color);
            }
        }

        #endregion
    }

    #region Segment

    /// <summary>
    /// The view + state for one endpoint segment. Receives both scrub and text editing.
    /// </summary>
    /// <remarks>
    /// <see cref="TweeqScrubManipulator"/> doesn't swallow PointerDown, so it can coexist with the
    /// focus and caret of the TextField that's always on top.
    /// A click (below the threshold) comes down through Clicked, where it switches to select-all.
    /// </remarks>
    sealed class EndpointSegment : VisualElement
    {
        #region Constants

        const string TEXT_INPUT_NAME = "unity-text-input";

        #endregion

        #region Fields

        readonly TextField _field;
        readonly VisualElement _textInput;
        readonly TextElement _textElement;
        readonly TweeqScrubManipulator _scrub = new TweeqScrubManipulator();
        readonly TweakGesture _gesture = new TweakGesture();

        readonly int _max;

        int _value;
        int _valueAtScrubStart;
        double _scrubLocal;
        bool _scrubbing;

        #endregion

        #region Public API

        /// <summary>0-based order. The port is <see cref="EndpointInput.OCTET_COUNT"/>.</summary>
        public int Index { get; }

        /// <summary>The clamped current value.</summary>
        public int Value
        {
            get { return _value; }
        }

        /// <summary>The raw text being displayed. Can be blank mid-keystroke.</summary>
        public string Text
        {
            get { return _field.value ?? string.Empty; }
        }

        /// <summary>The value moved due to user operation.</summary>
        public event Action<EndpointSegment> Changed;

        /// <summary>Pointer release below the threshold (= a click).</summary>
        public event Action<EndpointSegment> Clicked;

        /// <summary>A request to move between segments (+1 / -1).</summary>
        public event Action<EndpointSegment, int> MoveRequested;

        /// <summary>This segment's subtree gained focus.</summary>
        public event Action<EndpointSegment> FocusGained;

        public EndpointSegment(int index, int max, float width)
        {
            Index = index;
            _max = max;

            this.AddToClassList("tweeq-endpoint-segment");

            // The order is kept in the name so tests and the UI Builder can grab each one individually
            this.name = "tweeq-endpoint-segment-" + index.ToString(CultureInfo.InvariantCulture);
            this.style.width = width;
            this.style.height = Length.Percent(100f);
            this.style.flexShrink = 0f;

            _field = new TextField
            {
                name = "tweeq-endpoint-segment-text",

                // Not delayed, since the value needs to be rebuilt on every keystroke
                isDelayed = false,
                multiline = false,
                maxLength = max.ToString(CultureInfo.InvariantCulture).Length,
            };
            _field.style.position = Position.Absolute;
            _field.style.left = 0f;
            _field.style.top = 0f;
            _field.style.right = 0f;
            _field.style.bottom = 0f;
            _field.RegisterValueChangedCallback(OnTextChanged);
            this.hierarchy.Add(_field);

            _textInput = _field.Q(TEXT_INPUT_NAME);
            _textElement = _textInput != null ? _textInput.Q<TextElement>() : null;

            _scrub.ScrubBegan += BeginScrub;
            _scrub.ScrubUpdated += UpdateScrub;
            _scrub.ScrubEnded += EndScrub;
            _scrub.ScrubCancelled += CancelScrub;
            _scrub.Clicked += OnScrubClicked;
            this.AddManipulator(_scrub);

            this.RegisterCallback<KeyDownEvent>(OnKeyDown);
            this.RegisterCallback<FocusInEvent>(OnFocusIn);

            SyncText();
        }

        /// <summary>Writes the value. Leaves the display untouched if <paramref name="syncDisplay"/> is false.</summary>
        public void SetValue(int newValue, bool syncDisplay)
        {
            int clamped = Mathf.Clamp(newValue, 0, _max);
            if (_value == clamped)
            {
                if (syncDisplay)
                {
                    SyncText();
                }

                return;
            }

            _value = clamped;

            if (syncDisplay)
            {
                SyncText();
            }
        }

        /// <summary>Corrects the display to match the value (blank → "0").</summary>
        public void NormalizeText()
        {
            SyncText();
        }

        /// <summary>
        /// Applies one keystroke's worth of input. Discards non-digits, and requests a move
        /// to the next segment if "." / ":" is present.
        /// </summary>
        public void ApplyUserText(string raw)
        {
            string source = raw ?? string.Empty;

            bool advance = false;
            StringBuilder digits = new StringBuilder(source.Length);
            for (int index = 0; index < source.Length; index++)
            {
                char c = source[index];
                if (c >= '0' && c <= '9')
                {
                    if (digits.Length < _field.maxLength)
                    {
                        digits.Append(c);
                    }

                    continue;
                }

                if (c == '.' || c == ':')
                {
                    advance = true;
                }
            }

            string filtered = digits.ToString();
            int parsed = 0;
            if (filtered.Length > 0)
            {
                // Digit count is bounded by maxLength, so int never overflows
                parsed = int.Parse(filtered, CultureInfo.InvariantCulture);
                if (parsed > _max)
                {
                    parsed = _max;
                    filtered = _max.ToString(CultureInfo.InvariantCulture);
                }
            }

            SetText(filtered);
            _value = parsed;

            Action<EndpointSegment> changed = Changed;
            if (changed != null)
            {
                changed(this);
            }

            if (!advance)
            {
                return;
            }

            Action<EndpointSegment, int> move = MoveRequested;
            if (move != null)
            {
                move(this, 1);
            }
        }

        /// <summary>Focuses and selects all. Does nothing without a panel.</summary>
        public void FocusAndSelectAll()
        {
            if (this.panel == null)
            {
                return;
            }

            _field.Focus();

            // The selection gets overwritten unless this waits for the frame after focus settles
            this.schedule.Execute(() =>
            {
                if (_field != null)
                {
                    _field.SelectAll();
                }
            }).StartingIn(0);
        }

        /// <summary>Toggles interactive/non-interactive.</summary>
        public void SetInteractive(bool interactive)
        {
            _field.SetEnabled(interactive);
            _field.focusable = interactive;
            _field.pickingMode = interactive ? PickingMode.Position : PickingMode.Ignore;
            this.pickingMode = interactive ? PickingMode.Position : PickingMode.Ignore;
        }

        /// <summary>Applies theme-derived static styles.</summary>
        public void ApplyTheme(TweeqTheme theme)
        {
            if (theme == null)
            {
                return;
            }

            // The normalization that fits the TextField into a 24px box, and the caret color,
            // are left to the public helper (EXT-03-A)
            TweeqInputBoxStyles.ApplyTextField(_field, theme);

            // Segment digits are center-aligned. Alignment is widget-specific, so add it after the helper
            if (_textInput != null)
            {
                _textInput.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            if (_textElement != null)
            {
                _textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            _field.style.unityTextAlign = TextAnchor.MiddleCenter;

            // A numeric field, so apply Geist (fontNumeric)
            TweeqFonts.Apply(_field, TweeqFonts.NumericFont);
            TweeqFonts.Apply(_textInput, TweeqFonts.NumericFont);
            TweeqFonts.Apply(_textElement, TweeqFonts.NumericFont);
        }

        /// <summary>Swaps the text color (Invalid display).</summary>
        public void ApplyTextColor(Color color)
        {
            _field.style.color = color;

            if (_textInput != null)
            {
                _textInput.style.color = color;
            }

            if (_textElement != null)
            {
                _textElement.style.color = color;
            }
        }

        #endregion

        #region Scrub

        /// <summary>Begins a scrub. Sensitivity is fixed, so TweakGesture's speed range is also pinned to 1.</summary>
        public void BeginScrub()
        {
            _scrubbing = true;
            _valueAtScrubStart = _value;
            _scrubLocal = _value;
            _gesture.Reset();
        }

        /// <summary>One scrub sample.</summary>
        public void UpdateScrub(ScrubUpdate update)
        {
            if (!_scrubbing)
            {
                // Don't miss paths that bypass the manipulator (tests, keyboard) either
                BeginScrub();
            }

            GestureModifiers modifiers = new GestureModifiers(update.Alt, update.Shift, false);

            // Spec EXT-02: sensitivity fixed at 4px = 1. Vertical-drag sensitivity change isn't
            // used, so collapse both min/max speed to 1
            GestureUpdate gesture = _gesture.Update(
                update.DeltaX, update.DeltaY,
                1.0 / EndpointInput.PIXELS_PER_STEP,
                modifiers, EndpointInput.FAST_MULTIPLIER, 1.0, 1.0);

            // Clamp on every raw value. Letting an out-of-range number show through invites bugs
            // (same judgment as NumberInput D-3)
            _scrubLocal = TweeqMath.Clamp(_scrubLocal + gesture.Delta, 0.0, _max);

            NumberValidation validation = NumberValidator.Validate(
                _scrubLocal, 0.0, _max, 1.0, 1.0, false);

            int next = (int)Math.Round(validation.Value, MidpointRounding.AwayFromZero);
            if (next == _value)
            {
                return;
            }

            _value = next;
            SyncText();

            Action<EndpointSegment> changed = Changed;
            if (changed != null)
            {
                changed(this);
            }
        }

        /// <summary>Ends the scrub.</summary>
        public void EndScrub()
        {
            _scrubbing = false;
        }

        /// <summary>Cancels the scrub. Reverts to the value at the start.</summary>
        public void CancelScrub()
        {
            if (!_scrubbing)
            {
                return;
            }

            _scrubbing = false;

            if (_value == _valueAtScrubStart)
            {
                return;
            }

            _value = _valueAtScrubStart;
            SyncText();

            Action<EndpointSegment> changed = Changed;
            if (changed != null)
            {
                changed(this);
            }
        }

        void OnScrubClicked()
        {
            Action<EndpointSegment> clicked = Clicked;
            if (clicked != null)
            {
                clicked(this);
            }
        }

        #endregion

        #region Events

        void OnTextChanged(ChangeEvent<string> evt)
        {
            if (evt == null)
            {
                return;
            }

            // Writing the display back uses SetValueWithoutNotify, so only a user keystroke arrives here
            ApplyUserText(evt.newValue);
        }

        void OnFocusIn(FocusInEvent evt)
        {
            Action<EndpointSegment> gained = FocusGained;
            if (gained != null)
            {
                gained(this);
            }
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            int delta;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow:
                    delta = -1;
                    break;

                case KeyCode.RightArrow:
                    delta = 1;
                    break;

                default:
                    return;
            }

            // Only crosses into another segment when the caret is at the edge. Normal caret movement mid-text
            int caret = _field.textSelection.cursorIndex;
            int length = Text.Length;
            bool atEdge = delta < 0 ? caret <= 0 : caret >= length;
            if (!atEdge)
            {
                return;
            }

            Action<EndpointSegment, int> move = MoveRequested;
            if (move != null)
            {
                move(this, delta);
            }

            evt.StopPropagation();
        }

        #endregion

        #region Text

        void SyncText()
        {
            SetText(_value.ToString(CultureInfo.InvariantCulture));
        }

        void SetText(string text)
        {
            if (_field.value == text)
            {
                return;
            }

            _field.SetValueWithoutNotify(text);
        }

        #endregion
    }

    #endregion
}
