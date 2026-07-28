using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>The text's horizontal alignment (equivalent to Vue's InputAlign).</summary>
    public enum TweeqTextAlign
    {
        Left,
        Center,
        Right,
    }

    /// <summary>
    /// A string input field (string-color-spec.md, the "StringInput" section).
    ///
    /// Unlike NumberInput, there's no gesture (scrub) that would interfere with text editing, so
    /// TextField is kept in front at all times. The reason it doesn't switch between a display overlay
    /// and an editing TextField the way NumberInput does is to satisfy the spec's "pointer-originated
    /// focus doesn't select all; the caret goes to the click position" (with a scheme that only shows
    /// TextField after a click, the caret at the clicked coordinate would be lost).
    ///
    /// The edit session's state machine (<see cref="BeginEditing" /> / <see cref="SetEditingText" /> /
    /// <see cref="CommitEditing" /> / <see cref="EndEditing" /> / <see cref="CancelEditing" />) is kept
    /// panel-independent. The real UI's focus and key input just drive this layer, so even without a
    /// panel attached, state still advances without throwing (EditMode tests drive this layer).
    /// </summary>
    [UxmlElement]
    public partial class StringInput
        : VisualElement, INotifyValueChanged<string>, ITweeqInputBox, ITweeqThemed
    {
        #region Constants

        // Spec: 0.5em left/right padding. 6px based on a 12px fontSize.
        const float TEXT_PADDING = 6f;

        // TextField's inner element (touched from here since only the alignment is widget-specific).
        const string TEXT_INPUT_NAME = "unity-text-input";

        #endregion

        #region Fields

        string _value = string.Empty;

        // The raw text currently displayed. Diverges from _value while rejected (equivalent to Vue's display ref).
        string _display = string.Empty;

        // The value at the start of editing. What Escape restores to.
        string _valueAtEditStart = string.Empty;

        Func<string, bool> _validator;

        // Whether _display hasn't passed the validator. Only changes the text color to Error; the display itself is left as-is.
        bool _rejected;

        TweeqTextAlign _align = TweeqTextAlign.Left;
        bool _disabled;
        bool _invalid;
        bool _editing;
        bool _hovered;

        // Whether the most recent focus originated from a pointer (same approach as NumberInput C-2).
        // If it originated from Tab, select all; if from a click, respect the caret position instead.
        bool _focusFromPointer;

        // Whether the current edit session started with select-all. Kept so "Tab select-all" can be verified panel-independently.
        bool _selectedAllAtEditStart;

        TweeqBoxPosition _inlinePosition = TweeqBoxPosition.None;
        TweeqBoxPosition _blockPosition = TweeqBoxPosition.None;

        TweeqTheme _theme = TweeqTheme.Dark();

        TextField _textField;
        VisualElement _textInput;
        TextElement _textElement;
        TweeqFocusRing _focusRing;

        #endregion

        #region Public API

        /// <summary>
        /// Fires for each keystroke that passes the validator (only when the value actually changes).
        /// Also fires on a rollback via Escape.
        /// </summary>
        public event Action<string> ValueChanged;

        /// <summary>Fires only on blur / Enter. Does not fire on keystrokes or Escape.</summary>
        public event Action<string> Confirmed;

        /// <summary>The validated output value.</summary>
        [UxmlAttribute]
        public string value
        {
            get => _value;
            set
            {
                string next = value ?? string.Empty;
                if (_value == next)
                {
                    return;
                }

                string previous = _value;
                SetValueWithoutNotify(next);
                ValueChanged?.Invoke(_value);
                NotifyValueChanged(previous, _value);
            }
        }

        /// <summary>
        /// Decides whether input is accepted. Always allowed when null.
        /// Input that returns false is left displayed as-is, while <see cref="value" /> stays unchanged (Vue's validLocal approach).
        /// </summary>
        public Func<string, bool> Validator
        {
            get => _validator;
            set
            {
                _validator = value;

                // The acceptance criteria changed, so re-evaluate what's currently displayed.
                _rejected = !IsAccepted(_display);
                Refresh();
            }
        }

        /// <summary>The text's alignment. Defaults to left (unlike Number, since mid-text editing is expected).</summary>
        [UxmlAttribute]
        public TweeqTextAlign Align
        {
            get => _align;
            set
            {
                if (_align == value)
                {
                    return;
                }

                _align = value;
                ApplyAlign();
            }
        }

        /// <summary>Non-interactive state.</summary>
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

                if (_disabled && _editing)
                {
                    // If editing is still active at the moment of disabling, there would be no way left to confirm it.
                    // However, "Confirmed firing without any actual operation" would be a mistake, so it isn't confirmed here
                    // (same treatment as NumberInput's Disabled -> SetEditing(false)).
                    FinishEditing(false);
                }

                ApplyInteractivity();
                Refresh();
            }
        }

        /// <summary>Externally supplied invalid-value display. Combined via OR with rejection by the validator.</summary>
        [UxmlAttribute]
        public bool Invalid
        {
            get => _invalid;
            set
            {
                _invalid = value;
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

        /// <summary>Position within a horizontal group. Setting this collapses the corner radius.</summary>
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

        /// <summary>The raw text currently displayed. Diverges from <see cref="value" /> while rejected.</summary>
        public string DisplayText => _display;

        /// <summary>Whether an edit session is in progress.</summary>
        public bool IsEditing => _editing;

        /// <summary>Whether the current display is being rejected by the validator.</summary>
        public bool IsRejected => _rejected;

        /// <summary>The value at the start of editing (what Escape restores to).</summary>
        public string ValueAtEditStart => _valueAtEditStart;

        /// <summary>Whether the current edit session started with select-all (Tab focus).</summary>
        public bool SelectedAllAtEditStart => _selectedAllAtEditStart;

        /// <summary>Sets the value without firing ChangeEvent / ValueChanged.</summary>
        public void SetValueWithoutNotify(string newValue)
        {
            _value = newValue ?? string.Empty;

            // An external set doesn't disturb an in-progress keystroke (same condition as Vue's display watcher).
            if (!_editing)
            {
                _display = _value;
                _rejected = !IsAccepted(_display);
                SyncTextField();
            }

            Refresh();
        }

        #endregion

        #region Editing session

        /// <summary>
        /// Begins an edit session. Selects all when fromPointer=false (originating from Tab or another keyboard source).
        /// Does nothing if already editing.
        /// </summary>
        /// <param name="fromPointer">Whether the focus originated from a pointer. If true, doesn't select all.</param>
        public void BeginEditing(bool fromPointer = false)
        {
            if (_disabled || _editing)
            {
                return;
            }

            _focusFromPointer = fromPointer;
            BeginEditingInternal();

            if (this.panel != null && _textField != null)
            {
                _textField.Focus();
            }

            if (!fromPointer)
            {
                SelectAll();
            }
        }

        /// <summary>
        /// Updates the display for a single keystroke. If it passes the validator, this reflects into the
        /// value and fires <see cref="ValueChanged" />; if rejected, only the display is left updated.
        /// </summary>
        /// <param name="text">The field's new display text. null is treated as an empty string.</param>
        public void SetEditingText(string text)
        {
            if (_disabled)
            {
                return;
            }

            string next = text ?? string.Empty;
            _display = next;
            SyncTextField();

            if (IsAccepted(next))
            {
                _rejected = false;

                // ValueChanged only fires when the value actually changes (the equality guard on the setter side).
                this.value = next;
            }
            else
            {
                _rejected = true;
            }

            Refresh();
        }

        /// <summary>
        /// Confirms on Enter. Rolls the rejected display back to <see cref="value" /> and
        /// fires <see cref="Confirmed" /> exactly once. The edit session continues (Enter doesn't blur).
        /// </summary>
        public void CommitEditing()
        {
            if (!_editing)
            {
                return;
            }

            RollbackDisplayToValue();
            Refresh();
            Confirmed?.Invoke(_value);
        }

        /// <summary>Confirms on blur. Performs the same confirmation as <see cref="CommitEditing" />, then ends editing.</summary>
        public void EndEditing()
        {
            if (!_editing)
            {
                return;
            }

            FinishEditing(true);
        }

        /// <summary>
        /// Escape. Restores the value at the start of editing and ends editing (<see cref="Confirmed" /> does not fire).
        ///
        /// The original has no cancel behavior for Escape, but this is an intentional deviation that
        /// prioritizes consistency with the "Escape = restore starting value" behavior already adopted
        /// in Number / Rotary (string-color-spec.md).
        /// </summary>
        public void CancelEditing()
        {
            if (!_editing)
            {
                return;
            }

            // The session is torn down first so the blur path (OnFocusOut -> EndEditing) doesn't also confirm.
            _editing = false;

            // Just like canceling a drag, this also fires a notification rolling back the value notified mid-way.
            this.value = _valueAtEditStart;

            // This also aligns the display for the case where the value never actually changed (it was only a rejected display).
            RollbackDisplayToValue();
            Refresh();
            BlurTextField();
        }

        /// <summary>Selects all the display text. Only records the intent if no panel is attached.</summary>
        public void SelectAll()
        {
            _selectedAllAtEditStart = true;

            if (_textField == null || this.panel == null)
            {
                return;
            }

            // The selection range gets overwritten unless this waits until the frame after focus is settled (same as NumberInput).
            this.schedule.Execute(() =>
            {
                if (_textField != null && _editing)
                {
                    _textField.SelectAll();
                }
            }).StartingIn(0);
        }

        void FinishEditing(bool confirm)
        {
            RollbackDisplayToValue();
            _editing = false;
            Refresh();

            if (confirm)
            {
                Confirmed?.Invoke(_value);
            }
        }

        void BeginEditingInternal()
        {
            if (_editing)
            {
                return;
            }

            _editing = true;
            _valueAtEditStart = _value;
            _selectedAllAtEditStart = false;
            Refresh();
        }

        // Rollback on confirm. Same as Vue's confirm() resetting display = local = model.
        void RollbackDisplayToValue()
        {
            _display = _value;
            _rejected = !IsAccepted(_display);
            SyncTextField();
        }

        bool IsAccepted(string text)
        {
            return _validator == null || _validator(text);
        }

        #endregion

        #region Construction

        public StringInput()
        {
            this.AddToClassList("tweeq-string-input");

            // The root itself never takes focus. The TextField it contains is the sole tab stop
            // (making the root focusable too would make Tab stop twice on a single field).
            this.focusable = false;
            this.style.height = _theme.InputHeight;
            this.style.minWidth = _theme.InputHeight;
            this.style.flexShrink = 0f;
            this.style.overflow = Overflow.Hidden;

            BuildChildren();
            ApplyStaticStyles();
            ApplyInteractivity();

            // Registered with TrickleDown to intercept Enter / Escape before TextField does.
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // TrickleDown so "whether it started from a pointer" gets set before focus moves.
            this.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            this.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            this.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);

            this.RegisterCallback<FocusInEvent>(OnFocusIn);
            this.RegisterCallback<FocusOutEvent>(OnFocusOut);

            Refresh();
        }

        void BuildChildren()
        {
            _textField = new TextField
            {
                name = "tweeq-string-text",

                // ValueChanged needs to fire on every character (the spec's two-tier event contract).
                // With isDelayed = true, ChangeEvent wouldn't arrive until Enter / blur.
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
            _textField.RegisterValueChangedCallback(OnTextChanged);
            this.hierarchy.Add(_textField);

            _textInput = _textField.Q(TEXT_INPUT_NAME);

            // The character actually gets drawn by the TextElement inside unity-text-input.
            // Vertical squashing persists even if only the input side is fixed, so the same setting is applied here too (NumberInput A-6).
            _textElement = _textInput != null ? _textInput.Q<TextElement>() : null;

            // The focus ring is drawn using a separate layer's border. Adding a border on the root side
            // would shift the absolutely-positioned children 1px inward.
            _focusRing = TweeqFocusRing.Attach(this);
            _focusRing.name = "tweeq-string-focus-ring";
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

            // Background only, 0.15s / cubic-bezier(0.4,0,0.2,1). UI Toolkit has no identical curve,
            // so EaseInOutCubic is used as an approximation (same judgment as NumberInput / RotaryInput).
            TweeqInputBoxStyles.ApplyBackgroundTransition(this, _theme);

            // Normalization of height, padding, and caret color was moved into the shared public helper (EXT-03-A).
            TweeqInputBoxStyles.ApplyTextField(_textField, _theme);

            if (_textInput != null)
            {
                // The helper resets left/right to 0, so the spec's 0.5em is reapplied here.
                _textInput.style.paddingLeft = TEXT_PADDING;
                _textInput.style.paddingRight = TEXT_PADDING;
            }

            ApplyAlign();
        }

        void ApplyAlign()
        {
            TextAnchor anchor;
            switch (_align)
            {
                case TweeqTextAlign.Center:
                    anchor = TextAnchor.MiddleCenter;
                    break;

                case TweeqTextAlign.Right:
                    anchor = TextAnchor.MiddleRight;
                    break;

                default:
                    anchor = TextAnchor.MiddleLeft;
                    break;
            }

            if (_textField != null)
            {
                _textField.style.unityTextAlign = anchor;
            }

            if (_textInput != null)
            {
                _textInput.style.unityTextAlign = anchor;
            }

            if (_textElement != null)
            {
                _textElement.style.unityTextAlign = anchor;
            }
        }

        void ApplyInteractivity()
        {
            this.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;

            if (_textField != null)
            {
                _textField.SetEnabled(!_disabled);
                _textField.pickingMode = _disabled ? PickingMode.Ignore : PickingMode.Position;
            }
        }

        void ApplyCornerRadius()
        {
            TweeqInputBoxStyles.ApplyCornerRadius(this, _theme, _inlinePosition, _blockPosition);

            // The focus ring is a separate layer, so the same corner radius is reapplied to it.
            if (_focusRing != null)
            {
                _focusRing.Apply(_theme, _inlinePosition, _blockPosition);
            }
        }

        #endregion

        #region Events

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt == null || _disabled)
            {
                return;
            }

            // Only cleared on FocusOut (i.e. it means "the current focus began from a pointer").
            _focusFromPointer = true;
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

        void OnFocusIn(FocusInEvent evt)
        {
            if (evt == null || _disabled || !IsTextTarget(evt.target))
            {
                return;
            }

            BeginEditingInternal();
            ScheduleKeyboardSelectAll();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            if (evt == null || !IsTextTarget(evt.target))
            {
                return;
            }

            // The next FocusIn is re-evaluated as "the start of a new focus session."
            _focusFromPointer = false;

            // The Escape path already tears down the session first, so this doesn't confirm here.
            EndEditing();
        }

        // Whether it originated from a pointer isn't settled until "after this frame's PointerDown finishes processing."
        // schedule runs only after all of this frame's event processing is done, so the check happens there.
        void ScheduleKeyboardSelectAll()
        {
            if (this.panel == null)
            {
                return;
            }

            this.schedule.Execute(() =>
            {
                if (_focusFromPointer || _disabled || !_editing || _selectedAllAtEditStart)
                {
                    return;
                }

                SelectAll();
            }).StartingIn(0);
        }

        void OnTextChanged(ChangeEvent<string> evt)
        {
            if (evt == null)
            {
                return;
            }

            SetEditingText(evt.newValue);
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
                    if (_editing)
                    {
                        CommitEditing();
                        evt.StopPropagation();
                    }

                    break;

                case KeyCode.Escape:
                    if (_editing)
                    {
                        CancelEditing();
                        evt.StopPropagation();
                    }

                    break;
            }
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

        // TextField is delegatesFocus, so the element that actually holds focus is the inner one.
        // _textField.Blur() sometimes fails to release it, so this clears it from the focusedElement side instead.
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

        #region Refresh

        // This runs on every keystroke, so it avoids building a string and only does a comparison.
        void SyncTextField()
        {
            if (_textField == null || _textField.value == _display)
            {
                return;
            }

            _textField.SetValueWithoutNotify(_display);
        }

        void Refresh()
        {
            if (_theme == null)
            {
                return;
            }

            UpdateBackground();
            UpdateTextColor();

            if (_focusRing != null)
            {
                _focusRing.Visible = _editing && !_disabled;
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

        // Spec: invalid only changes the text color to Error (the border and icon are left unchanged).
        void UpdateTextColor()
        {
            Color color = ShowInvalid ? _theme.Error : _theme.Text;

            if (_textField != null)
            {
                _textField.style.color = color;
            }

            if (_textInput != null)
            {
                _textInput.style.color = color;
            }
        }

        bool ShowInvalid => _invalid || _rejected;

        #endregion
    }
}
