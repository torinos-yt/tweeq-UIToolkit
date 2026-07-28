using System;
using Tweeq.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>How a time field prints its frame count.</summary>
    public enum TimeDisplayMode
    {
        /// <summary>"mm:ss:ff" ("h:mm:ss:ff" past one hour). Each group is hoverable.</summary>
        Timecode,

        /// <summary>"{n}F". The whole field is one frames-scale scrub surface.</summary>
        Frames,
    }

    /// <summary>
    /// Timecode field. The value is a frame count (double inside, float on the API) and the
    /// frame rate can be swapped at runtime: the value never moves, only the printed form does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In timecode mode the digit groups (ff / ss / mm / h) are separate elements so the hovered
    /// group can become the drag scale (Vue digit hover). Modifiers offset that scale while held
    /// (Shift = +1 / Alt = -1) and H / M / S / F pin it, ignoring the hover.
    /// </para>
    /// <para>
    /// The editing session (<see cref="BeginEditing"/> / <see cref="CommitEditing"/> /
    /// <see cref="CancelEditing"/> / <see cref="EndEditing"/>) and the scrub session
    /// (<c>PerformScrub*</c>) are panel independent: the real pointer and focus wiring only
    /// drives that layer, so the state machine still runs with no panel attached.
    /// </para>
    /// </remarks>
    [UxmlElement]
    public partial class TimeInput
        : VisualElement, INotifyValueChanged<float>, ITweeqThemed, ITweeqInputBox,
          ITweeqConfirmable<float>
    {
        #region Constants

        /// <summary>Default frame rate (same as the Vue frameRate prop).</summary>
        public const double DEFAULT_FRAME_RATE = 24.0;

        /// <summary>Lowest tweak scale (frames).</summary>
        public const int MIN_SCALE = TimecodeLogic.SCALE_FRAMES;

        /// <summary>Highest tweak scale (hours).</summary>
        public const int MAX_SCALE = TimecodeLogic.SCALE_HOURS;

        // "h:mm:ss:ff" is the widest form; the hour group is not zero padded so only its
        // character count grows
        const int MAX_DIGIT_GROUPS = 4;

        const char DIGIT_SEPARATOR = ':';
        const string SEPARATOR_TEXT = ":";
        const string FRAMES_SUFFIX = "F";

        // Frame counts are normally integral, but an expression may commit a fraction
        const int DISPLAY_PRECISION = 4;

        // Vue .digit padding is .1em .2em, which is 1.2 / 2.4px against the 12px font
        const float DIGIT_PADDING_X = 2.4f;
        const float DIGIT_PADDING_Y = 1.2f;

        // Vue: .TqInputTime:hover &.tweak { background: set-alpha(text-subtle, .3) }
        const float DIGIT_HIGHLIGHT_ALPHA = 0.3f;

        const float TEXT_PADDING = 4f;

        // Inner element of TextField; only the centring is widget specific so it is set here
        const string TEXT_INPUT_NAME = "unity-text-input";

        /// <summary>Name prefix of the digit group elements; the suffix is the scale (0 = frames).</summary>
        public const string DIGIT_NAME_PREFIX = "tweeq-time-digit-";

        #endregion

        #region Fields

        float _value;

        // Raw value while scrubbing. Snapping is applied to the output only and never lands here
        double _local;

        // Built as "value at drag start + accumulated delta". Adding into _value every frame
        // would feed the clamped and snapped output back into the input and make the drag slip
        double _scrubAccum;
        float _valueAtScrubStart;

        double _frameRate = DEFAULT_FRAME_RATE;

        // Vue persists frames as the default format; that deliberately differs from
        // default(TimeDisplayMode)
        TimeDisplayMode _displayMode = TimeDisplayMode.Frames;

        double _min = double.NegativeInfinity;
        double _max = double.PositiveInfinity;
        double _defaultValue;

        bool _disabled;
        bool _invalid;
        bool _hovered;
        bool _editing;
        bool _scrubbing;

        // Scale coming from the hover; frozen at its drag-start value while scrubbing
        int _hoverScale;

        bool _shiftHeld;
        bool _altHeld;
        bool _snapKeyHeld;

        // H / M / S / F pin the scale while held down; they are not toggles
        bool _frameKeyHeld;
        bool _secondKeyHeld;
        bool _minuteKeyHeld;
        bool _hourKeyHeld;

        float _valueAtEditStart;
        float _confirmBaseline;
        bool _confirmedInSession;

        // Last composed display string. It is rebuilt only when its key (value, fps, mode) moves
        string _display = string.Empty;
        string _displayCache;
        double _displayCacheValue;
        double _displayCacheFrameRate;
        TimeDisplayMode _displayCacheMode;
        bool _hasDisplayCache;

        readonly string[] _digitTexts = new string[MAX_DIGIT_GROUPS];
        int _digitCount;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        TweeqTheme _theme = TweeqTheme.Dark();

        VisualElement _digitsRow;
        readonly Label[] _digitLabels = new Label[MAX_DIGIT_GROUPS];
        readonly Label[] _separators = new Label[MAX_DIGIT_GROUPS - 1];
        TextField _textField;
        VisualElement _textInput;
        TextElement _textElement;
        TweeqFocusRing _focusRing;
        TimeTweakOverlay _overlay;

        readonly TweeqScrubManipulator _scrub = new TweeqScrubManipulator();
        bool _scrubAttached;

        // Scale currently painted (-1 = nothing painted)
        int _highlightedScale = -1;

        #endregion

        #region Public API

        /// <summary>Fires on scrub commit, Enter, blur and arrow steps (once per session).</summary>
        public event Action<float> Confirmed;

        /// <summary>Frame count.</summary>
        [UxmlAttribute]
        public float value
        {
            get { return _value; }
            set
            {
                if (_value == value)
                {
                    return;
                }

                float previous = _value;
                SetValueWithoutNotify(value);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>
        /// Frames per second. Changing it at runtime leaves the value (a frame count) alone and
        /// only reformats the display. Non-positive and non-finite rates are ignored so the
        /// display cannot break on a division by zero.
        /// </summary>
        [UxmlAttribute("frame-rate")]
        public double FrameRate
        {
            get { return _frameRate; }
            set
            {
                if (!TweeqMath.IsFinite(value) || value <= 0.0 || _frameRate == value)
                {
                    return;
                }

                _frameRate = value;
                SyncDisplayText(true);
                Refresh();
            }
        }

        /// <summary>Printed form. Defaults to frames, matching the Vue persisted setting.</summary>
        [UxmlAttribute("display-mode")]
        public TimeDisplayMode DisplayMode
        {
            get { return _displayMode; }
            set
            {
                if (_displayMode == value)
                {
                    return;
                }

                _displayMode = value;
                SyncDisplayText(true);
                Refresh();
            }
        }

        /// <summary>Lower bound in frames. Defaults to -infinity.</summary>
        [UxmlAttribute]
        public double Min
        {
            get { return _min; }
            set
            {
                _min = value;
                Refresh();
            }
        }

        /// <summary>Upper bound in frames. Defaults to +infinity.</summary>
        [UxmlAttribute]
        public double Max
        {
            get { return _max; }
            set
            {
                _max = value;
                Refresh();
            }
        }

        /// <summary>Non-interactive state.</summary>
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

                if (_disabled)
                {
                    // Being disabled mid gesture would leave no way to release the drag or to
                    // commit the edit. Confirming here would report a change nobody made, so the
                    // gesture is dropped instead
                    PerformScrubCancel();
                    SetEditing(false);
                    ReleaseHeldKeys();
                }

                ApplyInteractivity();
                Refresh();
            }
        }

        /// <summary>Externally driven invalid state. Only the text colour changes.</summary>
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

        /// <summary>
        /// Target of <see cref="ResetToDefault"/> (the Vue default prop). Vue offers it through
        /// the right-click menu; that menu is not ported, so the host wires the entry point.
        /// </summary>
        public double DefaultValue
        {
            get { return _defaultValue; }
            set { _defaultValue = value; }
        }

        /// <summary>Colour theme. Passing null falls back to Dark().</summary>
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

        /// <summary>Position inside a horizontal group; flattens the touching corners.</summary>
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

        /// <summary>Position inside a vertical group.</summary>
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

        /// <summary>
        /// Active tweak scale (0 = frames / 1 = seconds / 2 = minutes / 3 = hours). Pinning keys
        /// win over the hover plus modifier offset.
        /// </summary>
        public int TweakScale
        {
            get
            {
                if (_frameKeyHeld)
                {
                    return TimecodeLogic.SCALE_FRAMES;
                }

                if (_secondKeyHeld)
                {
                    return TimecodeLogic.SCALE_SECONDS;
                }

                if (_minuteKeyHeld)
                {
                    return TimecodeLogic.SCALE_MINUTES;
                }

                if (_hourKeyHeld)
                {
                    return TimecodeLogic.SCALE_HOURS;
                }

                int offset = _shiftHeld ? 1 : _altHeld ? -1 : 0;
                return ClampScale(_hoverScale + offset);
            }
        }

        /// <summary>Scale coming from the hover, before modifiers and pinning keys.</summary>
        public int HoverScale
        {
            get { return _hoverScale; }
        }

        /// <summary>Whether a scrub session is running.</summary>
        public bool IsScrubbing
        {
            get { return _scrubbing; }
        }

        /// <summary>Whether a text editing session is running.</summary>
        public bool IsEditing
        {
            get { return _editing; }
        }

        /// <summary>String shown while not editing.</summary>
        public string DisplayText
        {
            get { return _display; }
        }

        /// <summary>Number of visible digit groups (1 in frames mode).</summary>
        public int DigitCount
        {
            get { return _digitCount; }
        }

        /// <summary>Text of a digit group; 0 is the frames group on the right. Out of range is empty.</summary>
        public string GetDigitText(int scale)
        {
            return scale >= 0 && scale < _digitCount ? _digitTexts[scale] ?? string.Empty : string.Empty;
        }

        /// <summary>Sets the value without sending a ChangeEvent. The raw value follows.</summary>
        public void SetValueWithoutNotify(float newValue)
        {
            _value = newValue;

            // An external write lands outside any drag or edit session, so reset the accumulator
            _local = newValue;
            _scrubAccum = 0.0;
            SyncDisplayText(true);
            Refresh();
        }

        /// <summary>Restores <see cref="DefaultValue"/> and confirms (the Vue onReset path).</summary>
        public void ResetToDefault()
        {
            if (_disabled)
            {
                return;
            }

            this.value = (float)_defaultValue;
            FireConfirmed();
        }

        #endregion

        #region Scrub session

        /// <summary>
        /// Starts a scrub. <see cref="TweeqScrubManipulator"/> drives this in the real UI; the
        /// entry point stays public for external drivers and tests.
        /// </summary>
        public void PerformScrubBegin()
        {
            if (_disabled || _scrubbing)
            {
                return;
            }

            _scrubbing = true;
            _valueAtScrubStart = _value;
            _local = _value;
            _scrubAccum = 0.0;

            AcquireOverlay();
            Refresh();
        }

        /// <summary>One scrub sample in pixels (horizontal only; vertical carries no sensitivity).</summary>
        public void PerformScrubDelta(float deltaX, bool shift, bool alt)
        {
            if (_disabled)
            {
                return;
            }

            if (!_scrubbing)
            {
                // Paths that bypass the manipulator (tests, external drivers) still open a session
                PerformScrubBegin();
            }

            _shiftHeld = shift;
            _altHeld = alt;

            _scrubAccum += deltaX * TimecodeLogic.ScaleSpeed(TweakScale, _frameRate);

            // The raw value is clamped too (shared deviation of this port). Folding the
            // accumulator as well keeps the return trip immediate after overshooting the range
            double raw = TweeqMath.Clamp(_valueAtScrubStart + _scrubAccum, _min, _max);
            _scrubAccum = raw - _valueAtScrubStart;
            _local = raw;

            ApplyScrubOutput();
        }

        /// <summary>Commits the scrub. <see cref="Confirmed"/> fires once.</summary>
        public void PerformScrubEnd()
        {
            if (!_scrubbing)
            {
                return;
            }

            _scrubbing = false;
            _local = _value;
            _scrubAccum = 0.0;
            ReleaseOverlay();
            SyncDisplayText(true);
            Refresh();
            FireConfirmed();
        }

        /// <summary>Drops the scrub and restores the value at drag start (Escape). No confirm.</summary>
        public void PerformScrubCancel()
        {
            if (!_scrubbing)
            {
                return;
            }

            _scrubbing = false;
            _scrubAccum = 0.0;
            ReleaseOverlay();

            // The drag already notified intermediate values, so the rollback notifies as well
            this.value = _valueAtScrubStart;
            _local = _valueAtScrubStart;
            SyncDisplayText(true);
            Refresh();
        }

        /// <summary>
        /// Sets the hover-driven scale; the digit groups call it on PointerEnter. Ignored while
        /// scrubbing so the drag keeps the scale it started with.
        /// </summary>
        public void PerformDigitHover(int scale)
        {
            if (_scrubbing)
            {
                return;
            }

            int next = ClampScale(scale);
            if (_hoverScale == next)
            {
                return;
            }

            _hoverScale = next;
            Refresh();
        }

        // Raw value -> unit boundary (Q) or whole frames -> clamp -> output
        void ApplyScrubOutput()
        {
            int scale = TweakScale;

            // Q keeps the remainder inside the unit and steps by whole units. The offset source
            // is pinned to the drag-start value; feeding the output back would move the origin
            // every frame
            double snapped = _snapKeyHeld
                ? TimecodeLogic.SnapToScale(_local, scale, _frameRate, _valueAtScrubStart)
                : TimecodeLogic.SnapToScale(_local, TimecodeLogic.SCALE_FRAMES, _frameRate);

            SetOutput(TweeqMath.Clamp(snapped, _min, _max));
        }

        void SetOutput(double next)
        {
            float casted = (float)next;
            if (casted == _value)
            {
                Refresh();
                return;
            }

            float previous = _value;
            _value = casted;
            SyncDisplayText(true);
            Refresh();
            NotifyValueChanged(previous, casted);
        }

        #endregion

        #region Editing session

        /// <summary>Starts text editing and selects everything.</summary>
        /// <remarks>
        /// Vue selects only the clicked digit range; this port always selects all to match its
        /// Tab editing flow (deliberate deviation).
        /// </remarks>
        public void BeginEditing()
        {
            if (_disabled || _editing)
            {
                return;
            }

            _valueAtEditStart = _value;
            _confirmBaseline = _value;
            _confirmedInSession = false;

            SetEditing(true);
            SyncDisplayText(true);

            if (_textField != null && this.panel != null)
            {
                _textField.Focus();
                ScheduleSelectAll();
            }
        }

        /// <summary>
        /// Replaces the editing text without touching the value; the expression is evaluated on
        /// commit. Typing into the TextField is the real-UI equivalent.
        /// </summary>
        public void SetEditingText(string text)
        {
            if (!_editing || _textField == null)
            {
                return;
            }

            _textField.SetValueWithoutNotify(text ?? string.Empty);
        }

        /// <summary>
        /// Enter. Evaluates the expression into the value and fires <see cref="Confirmed"/> once.
        /// The editing session stays open.
        /// </summary>
        public void CommitEditing()
        {
            if (!_editing)
            {
                return;
            }

            ApplyEditingText();
            SyncDisplayText(true);

            // Suppresses the second confirm when a blur follows Enter with nothing typed between
            if (_confirmedInSession && _value == _confirmBaseline)
            {
                return;
            }

            FireConfirmed();
        }

        /// <summary>Blur. Confirms like <see cref="CommitEditing"/> and closes the session.</summary>
        public void EndEditing()
        {
            if (!_editing)
            {
                return;
            }

            CommitEditing();
            SetEditing(false);
        }

        /// <summary>Escape. Restores the value at edit start and closes without confirming.</summary>
        public void CancelEditing()
        {
            if (!_editing)
            {
                return;
            }

            // Close the session first so the blur path (OnFocusOut -> EndEditing) cannot confirm
            SetEditing(false);

            this.value = _valueAtEditStart;
            SyncDisplayText(true);
            BlurTextField();
        }

        /// <summary>
        /// Evaluates editing text as an expression: timecode literals and unit suffixes become
        /// frame counts first, then the arithmetic is solved.
        /// </summary>
        public bool TryParseEditingText(string text, out double frames)
        {
            frames = 0.0;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string replaced = TimecodeLogic.ReplaceTimecodeWithFrames(text, _frameRate);
            if (!TweeqExpression.TryEvaluate(replaced, out double evaluated)
                || !TweeqMath.IsFinite(evaluated))
            {
                return false;
            }

            frames = evaluated;
            return true;
        }

        // Clamp on success, fall back to the value at edit start on failure. Frames are not
        // rounded here because the Vue original stores the expression result as-is
        void ApplyEditingText()
        {
            string text = _textField != null ? _textField.value : _display;

            if (TryParseEditingText(text, out double frames))
            {
                SetOutput(TweeqMath.Clamp(frames, _min, _max));
                return;
            }

            SetOutput(_valueAtEditStart);
        }

        void SetEditing(bool editing)
        {
            if (_editing == editing)
            {
                return;
            }

            _editing = editing;

            if (_textField != null)
            {
                // Focus() does not reach a display:none element, so switch visibility first
                _textField.style.display = editing ? DisplayStyle.Flex : DisplayStyle.None;
                _textField.pickingMode = editing ? PickingMode.Position : PickingMode.Ignore;
            }

            if (_digitsRow != null)
            {
                _digitsRow.style.display = editing ? DisplayStyle.None : DisplayStyle.Flex;
            }

            // Text selection owns the pointer while editing. Leaving the scrub attached would
            // capture the pointer on press and kill drag-selection inside the field
            ApplyScrubManipulator();
            Refresh();
        }

        void ScheduleSelectAll()
        {
            if (_textField == null || this.panel == null)
            {
                return;
            }

            // The selection is overwritten unless it is applied the frame after focus settles
            this.schedule.Execute(() =>
            {
                if (_textField != null && _editing)
                {
                    _textField.SelectAll();
                }
            }).StartingIn(0);
        }

        // TextField delegates focus, so the element actually holding it is one of its children
        void BlurTextField()
        {
            if (_textField == null || this.panel == null)
            {
                return;
            }

            FocusController controller = this.focusController;
            if (controller == null)
            {
                return;
            }

            VisualElement focused = controller.focusedElement as VisualElement;
            if (focused != null && IsTextTarget(focused))
            {
                focused.Blur();
            }
        }

        #endregion

        #region Keyboard

        /// <summary>
        /// Arrow-key step in frames. Moves the value and confirms (the Vue increment calls confirm).
        /// </summary>
        public void Increment(double frames)
        {
            if (_disabled || !TweeqMath.IsFinite(frames))
            {
                return;
            }

            SetOutput(TweeqMath.Clamp(_value + frames, _min, _max));
            _local = _value;
            _scrubAccum = 0.0;
            SyncDisplayText(true);
            FireConfirmed();
        }

        // Vue: up/down = one second, Alt = one frame, Shift = one minute
        double ArrowStep()
        {
            if (_altHeld)
            {
                return TimecodeLogic.UnitFrames(TimecodeLogic.SCALE_FRAMES, _frameRate);
            }

            if (_shiftHeld)
            {
                return TimecodeLogic.UnitFrames(TimecodeLogic.SCALE_MINUTES, _frameRate);
            }

            return TimecodeLogic.UnitFrames(TimecodeLogic.SCALE_SECONDS, _frameRate);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                    Increment(ArrowStep());
                    evt.StopPropagation();
                    break;

                case KeyCode.DownArrow:
                    Increment(-ArrowStep());
                    evt.StopPropagation();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_editing)
                    {
                        CommitEditing();
                        evt.StopPropagation();
                    }

                    break;

                case KeyCode.Escape:
                    if (_scrubbing)
                    {
                        PerformScrubCancel();
                        evt.StopPropagation();
                    }
                    else if (_editing)
                    {
                        CancelEditing();
                        evt.StopPropagation();
                    }

                    break;

                default:
                    // Single letters are literal text while editing, so only claim them outside it
                    if (!_editing && ApplyScaleKey(evt.keyCode, true))
                    {
                        evt.StopPropagation();
                    }

                    break;
            }

            if (_scrubbing)
            {
                ApplyScrubOutput();
            }
            else
            {
                Refresh();
            }
        }

        void OnKeyUp(KeyUpEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            _shiftHeld = (evt.modifiers & EventModifiers.Shift) != 0;
            _altHeld = (evt.modifiers & EventModifiers.Alt) != 0;

            bool wasSnapping = _snapKeyHeld;
            if (ApplyScaleKey(evt.keyCode, false))
            {
                evt.StopPropagation();
            }

            // Vue realigns the raw value onto the snapped one when Q is released, so the next
            // pixel of travel does not jump back to the unsnapped position
            if (wasSnapping && !_snapKeyHeld && _scrubbing)
            {
                _local = _value;
                _scrubAccum = _local - _valueAtScrubStart;
            }

            if (_scrubbing)
            {
                ApplyScrubOutput();
            }
            else
            {
                Refresh();
            }
        }

        bool ApplyScaleKey(KeyCode keyCode, bool held)
        {
            switch (keyCode)
            {
                case KeyCode.F:
                    _frameKeyHeld = held;
                    return true;

                case KeyCode.S:
                    _secondKeyHeld = held;
                    return true;

                case KeyCode.M:
                    _minuteKeyHeld = held;
                    return true;

                case KeyCode.H:
                    _hourKeyHeld = held;
                    return true;

                case KeyCode.Q:
                    _snapKeyHeld = held;
                    return true;

                default:
                    return false;
            }
        }

        void ReleaseHeldKeys()
        {
            _frameKeyHeld = false;
            _secondKeyHeld = false;
            _minuteKeyHeld = false;
            _hourKeyHeld = false;
            _snapKeyHeld = false;
            _shiftHeld = false;
            _altHeld = false;
        }

        // Arrow keys also raise NavigationMoveEvent, which moves focus on its own
        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            bool blocked;
            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Up:
                case NavigationMoveEvent.Direction.Down:
                    blocked = true;
                    break;

                case NavigationMoveEvent.Direction.Left:
                case NavigationMoveEvent.Direction.Right:
                    // While editing, left/right belong to the TextField caret
                    blocked = !_editing;
                    break;

                default:
                    blocked = false;
                    break;
            }

            if (!blocked)
            {
                return;
            }

            evt.StopPropagation();

            // In Unity 6 this is what actually suppresses the focus move (PreventDefault is obsolete)
            this.focusController?.IgnoreEvent(evt);
        }

        #endregion

        #region Construction

        public TimeInput()
        {
            this.AddToClassList("tweeq-time-input");

            // The root takes focus itself so H/M/S/F/Q/Escape land here during a drag
            this.focusable = true;
            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;
            this.style.flexShrink = 0f;
            this.style.overflow = Overflow.Hidden;

            BuildChildren();
            ApplyStaticStyles();
            ApplyInteractivity();

            _scrub.ScrubBegan += PerformScrubBegin;
            _scrub.ScrubUpdated += OnScrubUpdated;
            _scrub.ScrubEnded += PerformScrubEnd;
            _scrub.ScrubCancelled += PerformScrubCancel;
            _scrub.Clicked += OnScrubClicked;
            ApplyScrubManipulator();

            // Registered as trickle-down so arrows, Enter and Escape are claimed before TextField
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            this.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            this.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);

            this.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            SyncDisplayText(true);
            Refresh();
        }

        void BuildChildren()
        {
            // row-reverse makes the insertion order (0 = frames) run right to left on screen
            _digitsRow = new VisualElement
            {
                name = "tweeq-time-digits",
                pickingMode = PickingMode.Ignore,
            };
            _digitsRow.style.position = Position.Absolute;
            _digitsRow.style.left = 0f;
            _digitsRow.style.top = 0f;
            _digitsRow.style.right = 0f;
            _digitsRow.style.bottom = 0f;
            _digitsRow.style.flexDirection = FlexDirection.RowReverse;
            _digitsRow.style.alignItems = Align.Center;
            _digitsRow.style.justifyContent = Justify.Center;
            _digitsRow.style.overflow = Overflow.Hidden;
            this.hierarchy.Add(_digitsRow);

            for (int scale = 0; scale < MAX_DIGIT_GROUPS; scale++)
            {
                Label digit = CreateLabel();
                digit.name = DIGIT_NAME_PREFIX + scale;

                // Only the digit groups take hits; without one the hovered scale cannot be read
                digit.pickingMode = PickingMode.Position;
                digit.style.paddingLeft = DIGIT_PADDING_X;
                digit.style.paddingRight = DIGIT_PADDING_X;
                digit.style.paddingTop = DIGIT_PADDING_Y;
                digit.style.paddingBottom = DIGIT_PADDING_Y;
                digit.style.display = DisplayStyle.None;

                int captured = scale;
                digit.RegisterCallback<PointerEnterEvent>(_ => PerformDigitHover(captured));

                _digitLabels[scale] = digit;
                _digitsRow.Add(digit);

                if (scale >= MAX_DIGIT_GROUPS - 1)
                {
                    continue;
                }

                Label separator = CreateLabel();
                separator.name = "tweeq-time-separator-" + scale;
                separator.text = SEPARATOR_TEXT;
                separator.style.paddingTop = DIGIT_PADDING_Y;
                separator.style.paddingBottom = DIGIT_PADDING_Y;
                separator.style.unityFontStyleAndWeight = FontStyle.Bold;
                separator.style.display = DisplayStyle.None;

                _separators[scale] = separator;
                _digitsRow.Add(separator);
            }

            _textField = new TextField
            {
                name = "tweeq-time-text",

                // The value only lands on Enter or blur, but isDelayed = true would make the
                // arrival of ChangeEvent unpredictable, so it stays false
                isDelayed = false,
                multiline = false,
            };
            _textField.style.position = Position.Absolute;
            _textField.style.left = 0f;
            _textField.style.top = 0f;
            _textField.style.right = 0f;
            _textField.style.bottom = 0f;
            _textField.style.marginLeft = 0f;
            _textField.style.marginRight = 0f;
            _textField.style.marginTop = 0f;
            _textField.style.marginBottom = 0f;
            _textField.style.display = DisplayStyle.None;
            _textField.pickingMode = PickingMode.Ignore;
            this.hierarchy.Add(_textField);

            _textInput = _textField.Q(TEXT_INPUT_NAME);
            _textElement = _textInput != null ? _textInput.Q<TextElement>() : null;

            // The focus ring lives on its own layer: a border on the root would shift every
            // absolutely positioned child inwards by 1px
            _focusRing = TweeqFocusRing.Attach(this);
            _focusRing.name = "tweeq-time-focus-ring";
        }

        static Label CreateLabel()
        {
            Label label = new Label(string.Empty) { pickingMode = PickingMode.Ignore };
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.flexShrink = 0f;
            return label;
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
            TweeqInputBoxStyles.SetBorderColor(this, _theme.Border);
            TweeqInputBoxStyles.ApplyBackgroundTransition(this, _theme);

            // Height, spacing and caret colour normalisation lives in the shared helper
            TweeqInputBoxStyles.ApplyTextField(_textField, _theme);

            if (_textInput != null)
            {
                _textInput.style.paddingLeft = TEXT_PADDING;
                _textInput.style.paddingRight = TEXT_PADDING;
                _textInput.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            if (_textElement != null)
            {
                _textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            if (_textField != null)
            {
                _textField.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            // Vue rounds the digit highlight with --tq-radius-input. It only depends on the
            // theme, so it is written once here and never touched from the per-frame Refresh
            for (int index = 0; index < _digitLabels.Length; index++)
            {
                Label digit = _digitLabels[index];
                if (digit == null)
                {
                    continue;
                }

                digit.style.borderTopLeftRadius = _theme.InputRadius;
                digit.style.borderTopRightRadius = _theme.InputRadius;
                digit.style.borderBottomLeftRadius = _theme.InputRadius;
                digit.style.borderBottomRightRadius = _theme.InputRadius;
            }

            ApplyFonts();
        }

        // Everything on the digit row reads as one number, so the separators take the same font
        void ApplyFonts()
        {
            if (_theme == null)
            {
                return;
            }

            FontDefinition numeric = _theme.FontNumeric;

            TweeqFonts.Apply(_textField, numeric);
            TweeqFonts.Apply(_textInput, numeric);
            TweeqFonts.Apply(_textElement, numeric);

            for (int index = 0; index < _digitLabels.Length; index++)
            {
                TweeqFonts.Apply(_digitLabels[index], numeric);
            }

            for (int index = 0; index < _separators.Length; index++)
            {
                TweeqFonts.Apply(_separators[index], numeric);
            }
        }

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            this.focusable = !_disabled;

            if (_textField != null)
            {
                _textField.SetEnabled(!_disabled);
            }

            for (int index = 0; index < _digitLabels.Length; index++)
            {
                Label digit = _digitLabels[index];
                if (digit != null)
                {
                    digit.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
                }
            }

            ApplyScrubManipulator();
        }

        void ApplyScrubManipulator()
        {
            bool wanted = !_disabled && !_editing;
            if (wanted == _scrubAttached)
            {
                return;
            }

            _scrubAttached = wanted;

            if (wanted)
            {
                this.AddManipulator(_scrub);
            }
            else
            {
                this.RemoveManipulator(_scrub);
            }
        }

        void ApplyCornerRadius()
        {
            TweeqInputBoxStyles.ApplyCornerRadius(this, _theme, _inlinePosition, _blockPosition);

            if (_focusRing != null)
            {
                _focusRing.Apply(_theme, _inlinePosition, _blockPosition);
            }
        }

        #endregion

        #region Pointer

        void OnScrubUpdated(ScrubUpdate update)
        {
            // Vertical travel is unused: the original has no sensitivity gesture
            PerformScrubDelta(update.DeltaX, update.Shift, update.Alt);
        }

        void OnScrubClicked()
        {
            // Released below the threshold, so this is a click and text editing starts here
            BeginEditing();
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0 || _disabled || _editing)
            {
                return;
            }

            // Focus the root so H/M/S/F/Q/Escape are received; this does not enter text editing,
            // so the digit groups stay on screen
            this.Focus();
        }

        void OnPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;

            // Vue @pointerenter="tweakScaleByHover = 0": outside the digits it is the frames scale
            PerformDigitHover(TimecodeLogic.SCALE_FRAMES);
            Refresh();
        }

        void OnPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            Refresh();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            ReleaseHeldKeys();

            if (!IsTextTarget(evt.target))
            {
                Refresh();
                return;
            }

            // The Escape path already closed the session, so nothing is confirmed twice here
            EndEditing();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            PerformScrubCancel();
            ReleaseOverlay();
        }

        bool IsTextTarget(IEventHandler target)
        {
            if (_textField == null)
            {
                return false;
            }

            VisualElement element = target as VisualElement;
            return element != null && _textField.Contains(element);
        }

        #endregion

        #region Display

        // force = false leaves the text being typed alone (same guard as the Vue display watcher)
        void SyncDisplayText(bool force)
        {
            if (_editing && !force)
            {
                return;
            }

            string text = ComposeDisplayText();
            if (!ReferenceEquals(text, _display))
            {
                _display = text;
                SplitDigits(text);
                ApplyDigitTexts();
            }

            if (_textField != null && _textField.value != text)
            {
                _textField.SetValueWithoutNotify(text);
            }
        }

        // Pure in (value, fps, mode), so during a scrub the string is rebuilt only on the frames
        // where the displayed frame count actually changes
        string ComposeDisplayText()
        {
            double source = _value;

            if (_hasDisplayCache
                && _displayCacheMode == _displayMode
                && TweeqFormat.SameValueBits(_displayCacheFrameRate, _frameRate)
                && TweeqFormat.SameValueBits(_displayCacheValue, source))
            {
                return _displayCache;
            }

            _displayCache = _displayMode == TimeDisplayMode.Frames
                ? TweeqFormat.Format(source, DISPLAY_PRECISION, false) + FRAMES_SUFFIX
                : TimecodeLogic.FormatTimecode(source, _frameRate);
            _displayCacheValue = source;
            _displayCacheFrameRate = _frameRate;
            _displayCacheMode = _displayMode;
            _hasDisplayCache = true;
            return _displayCache;
        }

        // Cut from the right so _digitTexts[0] is always the frames group
        void SplitDigits(string text)
        {
            for (int index = 0; index < _digitTexts.Length; index++)
            {
                _digitTexts[index] = null;
            }

            if (_displayMode == TimeDisplayMode.Frames || string.IsNullOrEmpty(text))
            {
                _digitTexts[0] = text ?? string.Empty;
                _digitCount = 1;
                return;
            }

            int count = 0;
            int end = text.Length;
            for (int index = text.Length - 1; index >= 0 && count < MAX_DIGIT_GROUPS - 1; index--)
            {
                if (text[index] != DIGIT_SEPARATOR)
                {
                    continue;
                }

                _digitTexts[count] = text.Substring(index + 1, end - index - 1);
                count++;
                end = index;
            }

            _digitTexts[count] = text.Substring(0, end);
            _digitCount = count + 1;
        }

        void ApplyDigitTexts()
        {
            for (int scale = 0; scale < MAX_DIGIT_GROUPS; scale++)
            {
                Label digit = _digitLabels[scale];
                if (digit == null)
                {
                    continue;
                }

                bool visible = scale < _digitCount;
                digit.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

                if (visible)
                {
                    string text = _digitTexts[scale] ?? string.Empty;
                    if (digit.text != text)
                    {
                        digit.text = text;
                    }
                }

                if (scale >= _separators.Length)
                {
                    continue;
                }

                Label separator = _separators[scale];
                if (separator != null)
                {
                    separator.style.display = scale < _digitCount - 1
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                }
            }
        }

        #endregion

        #region Refresh

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            SyncDisplayText(false);
            UpdateBackground();
            UpdateTextColor();
            UpdateDigitHighlight();
            UpdateOverlay();

            if (_focusRing != null)
            {
                _focusRing.Visible = (_editing || _scrubbing) && !_disabled;
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

            for (int index = 0; index < _digitLabels.Length; index++)
            {
                Label digit = _digitLabels[index];
                if (digit != null)
                {
                    digit.style.color = color;
                }
            }

            for (int index = 0; index < _separators.Length; index++)
            {
                Label separator = _separators[index];
                if (separator != null)
                {
                    separator.style.color = _theme.TextMuted;
                }
            }

            if (_textField != null)
            {
                _textField.style.color = color;
            }

            if (_textInput != null)
            {
                _textInput.style.color = color;
            }
        }

        // Vue paints the .tweak digit while hovering; the fill is kept during a drag so the
        // grabbed group stays visible. This runs every scrub frame, so it only writes styles
        // when the painted group changes
        void UpdateDigitHighlight()
        {
            bool showHighlight = _displayMode == TimeDisplayMode.Timecode
                && !_disabled
                && !_editing
                && (_hovered || _scrubbing);

            int active = showHighlight && TweakScale < _digitCount ? TweakScale : -1;
            if (_highlightedScale == active)
            {
                return;
            }

            _highlightedScale = active;

            Color highlight = _theme.TextSubtle;
            highlight.a *= DIGIT_HIGHLIGHT_ALPHA;

            for (int scale = 0; scale < _digitLabels.Length; scale++)
            {
                Label digit = _digitLabels[scale];
                if (digit != null)
                {
                    digit.style.backgroundColor = scale == active ? highlight : Color.clear;
                }
            }
        }

        #endregion

        #region Overlay

        void AcquireOverlay()
        {
            if (_overlay != null)
            {
                return;
            }

            TweeqOverlayLayer layer = TweeqOverlayLayer.GetOrCreate(this);
            if (layer == null)
            {
                // With no panel the clock face is dropped; the interaction itself still works
                return;
            }

            _overlay = new TimeTweakOverlay();
            layer.Add(_overlay);
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

            if (!_scrubbing || _theme == null)
            {
                ReleaseOverlay();
                return;
            }

            _overlay.Sync(_theme, this.worldBound.center, _value, _frameRate, TweakScale);
        }

        #endregion

        #region Internals

        static int ClampScale(int scale)
        {
            if (scale < MIN_SCALE)
            {
                return MIN_SCALE;
            }

            return scale > MAX_SCALE ? MAX_SCALE : scale;
        }

        void FireConfirmed()
        {
            _confirmBaseline = _value;
            _confirmedInSession = true;

            Action<float> confirmed = Confirmed;
            if (confirmed != null)
            {
                confirmed(_value);
            }
        }

        void NotifyValueChanged(float previous, float current)
        {
            if (this.panel == null)
            {
                return;
            }

            using (ChangeEvent<float> changeEvent = ChangeEvent<float>.GetPooled(previous, current))
            {
                changeEvent.target = this;
                this.SendEvent(changeEvent);
            }
        }

        #endregion
    }
}
