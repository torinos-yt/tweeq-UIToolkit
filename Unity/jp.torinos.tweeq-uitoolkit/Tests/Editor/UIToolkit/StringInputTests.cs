using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies StringInput's editing session and two-tier event contract (the StringInput item
    /// of string-color-spec.md's "test contract").
    ///
    /// StringInput has BeginEditing / SetEditingText / CommitEditing / EndEditing / CancelEditing
    /// as a panel-independent logic layer, so keystrokes, confirm, and rollback are fully covered
    /// here. The following require a panel and real focus, so they are the responsibility of the
    /// Play Mode side:
    /// - Caret placement at the click position (that select-all does NOT happen is verified here)
    /// - The actual selection range (the result of TextField.SelectAll)
    /// - Focus movement via Tab itself
    /// </summary>
    public class StringInputTests
    {
        static StringInput Create(string initial = "")
        {
            StringInput input = new StringInput();
            input.SetValueWithoutNotify(initial);
            return input;
        }

        static float Radius(StyleLength length)
        {
            return length.value.value;
        }

        // A "3 characters max" validator, used to verify the rejection path
        static readonly Func<string, bool> MaxThree = text => text != null && text.Length <= 3;

        #region ValueChanged per keystroke

        [Test]
        public void Typing_RaisesValueChangedForEveryKeystroke()
        {
            StringInput input = Create();
            List<string> changed = new List<string>();
            input.ValueChanged += value => changed.Add(value);

            input.BeginEditing(true);
            input.SetEditingText("a");
            input.SetEditingText("ab");
            input.SetEditingText("abc");

            Assert.AreEqual(new[] { "a", "ab", "abc" }, changed.ToArray());
            Assert.AreEqual("abc", input.value);
        }

        [Test]
        public void Typing_SameTextTwiceNotifiesOnce()
        {
            StringInput input = Create();
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.BeginEditing(true);
            input.SetEditingText("a");
            input.SetEditingText("a");

            Assert.AreEqual(1, changed);
        }

        [Test]
        public void Typing_DoesNotConfirm()
        {
            StringInput input = Create();
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginEditing(true);
            input.SetEditingText("a");
            input.SetEditingText("ab");

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Typing_NullBecomesEmpty()
        {
            StringInput input = Create("abc");

            input.BeginEditing(true);
            input.SetEditingText(null);

            Assert.AreEqual(string.Empty, input.value);
            Assert.AreEqual(string.Empty, input.DisplayText);
        }

        [Test]
        public void Typing_WhileDisabledIsIgnored()
        {
            StringInput input = Create("abc");
            input.Disabled = true;
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.SetEditingText("xyz");

            Assert.AreEqual("abc", input.value);
            Assert.AreEqual("abc", input.DisplayText);
            Assert.AreEqual(0, changed);
        }

        #endregion

        #region Validator

        [Test]
        public void Validator_RejectedKeystrokeDoesNotNotify()
        {
            StringInput input = Create();
            input.Validator = MaxThree;
            List<string> changed = new List<string>();
            input.ValueChanged += value => changed.Add(value);

            input.BeginEditing(true);
            input.SetEditingText("abc");
            input.SetEditingText("abcd");

            Assert.AreEqual(new[] { "abc" }, changed.ToArray());
        }

        [Test]
        public void Validator_RejectedTextStaysVisibleWhileValueIsHeld()
        {
            StringInput input = Create();
            input.Validator = MaxThree;

            input.BeginEditing(true);
            input.SetEditingText("abc");
            input.SetEditingText("abcd");

            // The display stays as what was typed, while the value is the last string that passed
            // validation (the Vue original's validLocal approach)
            Assert.AreEqual("abcd", input.DisplayText);
            Assert.AreEqual("abc", input.value);
            Assert.IsTrue(input.IsRejected);
        }

        [Test]
        public void Validator_AcceptingAgainClearsTheRejection()
        {
            StringInput input = Create();
            input.Validator = MaxThree;

            input.BeginEditing(true);
            input.SetEditingText("abcd");
            input.SetEditingText("abc");

            Assert.IsFalse(input.IsRejected);
            Assert.AreEqual("abc", input.value);
        }

        [Test]
        public void Validator_NullAcceptsEverything()
        {
            StringInput input = Create();

            input.BeginEditing(true);
            input.SetEditingText("anything at all");

            Assert.IsFalse(input.IsRejected);
            Assert.AreEqual("anything at all", input.value);
        }

        [Test]
        public void Validator_AssignmentReevaluatesTheCurrentDisplay()
        {
            StringInput input = Create("abcd");

            Assert.IsFalse(input.IsRejected);

            input.Validator = MaxThree;

            // The value does not change; only the invalid display is raised
            Assert.IsTrue(input.IsRejected);
            Assert.AreEqual("abcd", input.value);
        }

        #endregion

        #region Confirm

        [Test]
        public void Confirm_EnterFiresOnceAndKeepsEditing()
        {
            StringInput input = Create();
            int confirmed = 0;
            string last = null;
            input.Confirmed += value =>
            {
                confirmed++;
                last = value;
            };

            input.BeginEditing(true);
            input.SetEditingText("abc");
            input.CommitEditing();

            Assert.AreEqual(1, confirmed);
            Assert.AreEqual("abc", last);
            Assert.IsTrue(input.IsEditing);
        }

        [Test]
        public void Confirm_BlurFiresOnceAndEndsEditing()
        {
            StringInput input = Create();
            int confirmed = 0;
            string last = null;
            input.Confirmed += value =>
            {
                confirmed++;
                last = value;
            };

            input.BeginEditing(true);
            input.SetEditingText("abc");
            input.EndEditing();

            Assert.AreEqual(1, confirmed);
            Assert.AreEqual("abc", last);
            Assert.IsFalse(input.IsEditing);
        }

        [Test]
        public void Confirm_WithoutChangeStillFires()
        {
            StringInput input = Create("abc");
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginEditing(true);
            input.CommitEditing();

            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void Confirm_WhileNotEditingIsIgnored()
        {
            StringInput input = Create("abc");
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.CommitEditing();
            input.EndEditing();

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Confirm_EnterThenBlurFiresOncePerAction()
        {
            StringInput input = Create();
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginEditing(true);
            input.SetEditingText("abc");
            input.CommitEditing();
            input.EndEditing();

            // Both Enter and blur are "confirm" actions, hence 2 total, but exactly once per action
            Assert.AreEqual(2, confirmed);
        }

        [Test]
        public void Confirm_ValueSetterDoesNotConfirm()
        {
            StringInput input = Create();
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.value = "abc";

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Confirm_DisablingWhileEditingDoesNotConfirm()
        {
            StringInput input = Create();
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.BeginEditing(true);
            input.SetEditingText("abc");
            input.Disabled = true;

            Assert.AreEqual(0, confirmed);
            Assert.IsFalse(input.IsEditing);
        }

        #endregion

        #region Rejected display rollback

        [Test]
        public void Rollback_EnterRestoresTheDisplayToValue()
        {
            StringInput input = Create();
            input.Validator = MaxThree;

            input.BeginEditing(true);
            input.SetEditingText("abc");
            input.SetEditingText("abcd");
            input.CommitEditing();

            Assert.AreEqual("abc", input.DisplayText);
            Assert.AreEqual("abc", input.value);
            Assert.IsFalse(input.IsRejected);
        }

        [Test]
        public void Rollback_BlurRestoresTheDisplayToValue()
        {
            StringInput input = Create();
            input.Validator = MaxThree;

            input.BeginEditing(true);
            input.SetEditingText("abc");
            input.SetEditingText("abcd");
            input.EndEditing();

            Assert.AreEqual("abc", input.DisplayText);
            Assert.AreEqual("abc", input.value);
            Assert.IsFalse(input.IsRejected);
        }

        [Test]
        public void Rollback_DoesNotNotifyValueChanged()
        {
            StringInput input = Create();
            input.Validator = MaxThree;
            List<string> changed = new List<string>();
            input.ValueChanged += value => changed.Add(value);

            input.BeginEditing(true);
            input.SetEditingText("abc");
            input.SetEditingText("abcd");
            input.CommitEditing();

            // The rollback is a display-only matter, so the value never changed even once
            Assert.AreEqual(new[] { "abc" }, changed.ToArray());
        }

        [Test]
        public void Rollback_KeepsInvalidWhenTheValueItselfFailsTheValidator()
        {
            StringInput input = Create("abcd");
            input.Validator = MaxThree;

            input.BeginEditing(true);
            input.CommitEditing();

            // An invalid value entered from outside is not silently corrected; only the invalid display remains
            Assert.AreEqual("abcd", input.value);
            Assert.IsTrue(input.IsRejected);
        }

        #endregion

        #region Escape

        [Test]
        public void Escape_RestoresTheValueAtEditStartAndEndsEditing()
        {
            StringInput input = Create("start");

            input.BeginEditing(true);
            input.SetEditingText("edited");
            input.CancelEditing();

            Assert.AreEqual("start", input.value);
            Assert.AreEqual("start", input.DisplayText);
            Assert.IsFalse(input.IsEditing);
        }

        [Test]
        public void Escape_NotifiesTheRollbackAndDoesNotConfirm()
        {
            StringInput input = Create("start");
            List<string> changed = new List<string>();
            int confirmed = 0;
            input.ValueChanged += value => changed.Add(value);
            input.Confirmed += _ => confirmed++;

            input.BeginEditing(true);
            input.SetEditingText("edited");
            input.CancelEditing();

            // 2 notifications: one for the change made, one for the rollback. Because the value
            // notified partway through gets rolled back, the rollback is also notified
            Assert.AreEqual(new[] { "edited", "start" }, changed.ToArray());
            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Escape_WithoutChangeDoesNotNotify()
        {
            StringInput input = Create("start");
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.BeginEditing(true);
            input.CancelEditing();

            Assert.AreEqual(0, changed);
            Assert.AreEqual("start", input.value);
        }

        [Test]
        public void Escape_ClearsARejectedDisplayWithoutNotifying()
        {
            StringInput input = Create("abc");
            input.Validator = MaxThree;
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.BeginEditing(true);
            input.SetEditingText("abcd");
            input.CancelEditing();

            Assert.AreEqual("abc", input.DisplayText);
            Assert.IsFalse(input.IsRejected);
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Escape_UsesTheValueAtTheStartOfTheLatestSession()
        {
            StringInput input = Create("first");

            input.BeginEditing(true);
            input.SetEditingText("second");
            input.EndEditing();

            input.BeginEditing(true);
            input.SetEditingText("third");
            input.CancelEditing();

            Assert.AreEqual("second", input.value);
        }

        [Test]
        public void Escape_WhileNotEditingIsIgnored()
        {
            StringInput input = Create("abc");
            input.SetValueWithoutNotify("xyz");

            input.CancelEditing();

            Assert.AreEqual("xyz", input.value);
        }

        #endregion

        #region Focus / select all

        [Test]
        public void Focus_KeyboardFocusSelectsAll()
        {
            StringInput input = Create("abc");

            input.BeginEditing(false);

            Assert.IsTrue(input.IsEditing);
            Assert.IsTrue(input.SelectedAllAtEditStart);
        }

        [Test]
        public void Focus_PointerFocusDoesNotSelectAll()
        {
            StringInput input = Create("abc");

            input.BeginEditing(true);

            Assert.IsTrue(input.IsEditing);
            Assert.IsFalse(input.SelectedAllAtEditStart);
        }

        [Test]
        public void Focus_SelectAllFlagIsPerSession()
        {
            StringInput input = Create("abc");

            input.BeginEditing(false);
            input.EndEditing();
            input.BeginEditing(true);

            Assert.IsFalse(input.SelectedAllAtEditStart);
        }

        [Test]
        public void Focus_BeginEditingTwiceKeepsTheFirstSnapshot()
        {
            StringInput input = Create("start");

            input.BeginEditing(true);
            input.SetEditingText("edited");
            input.BeginEditing(false);

            Assert.AreEqual("start", input.ValueAtEditStart);
            Assert.IsFalse(input.SelectedAllAtEditStart);
        }

        [Test]
        public void Focus_BeginEditingWhileDisabledIsIgnored()
        {
            StringInput input = Create("abc");
            input.Disabled = true;

            input.BeginEditing(false);

            Assert.IsFalse(input.IsEditing);
        }

        #endregion

        #region Value

        [Test]
        public void Value_SetterNotifiesOnce()
        {
            StringInput input = Create();
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.value = "abc";
            input.value = "abc";

            Assert.AreEqual(1, changed);
        }

        [Test]
        public void Value_SetValueWithoutNotifyIsSilent()
        {
            StringInput input = Create();
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.SetValueWithoutNotify("abc");

            Assert.AreEqual("abc", input.value);
            Assert.AreEqual("abc", input.DisplayText);
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Value_NullBecomesEmpty()
        {
            StringInput input = Create("abc");

            input.SetValueWithoutNotify(null);

            Assert.AreEqual(string.Empty, input.value);
            Assert.AreEqual(string.Empty, input.DisplayText);
        }

        [Test]
        public void Value_ExternalUpdateDoesNotClobberTheEditedDisplay()
        {
            StringInput input = Create("abc");

            input.BeginEditing(true);
            input.SetEditingText("typed");
            input.SetValueWithoutNotify("external");

            // An external assignment does not clobber the display while typing (the same condition as the Vue original's display watcher)
            Assert.AreEqual("typed", input.DisplayText);
            Assert.AreEqual("external", input.value);
        }

        [Test]
        public void Value_DefaultsToEmpty()
        {
            StringInput input = new StringInput();

            Assert.AreEqual(string.Empty, input.value);
            Assert.AreEqual(string.Empty, input.DisplayText);
        }

        #endregion

        #region Box

        [Test]
        public void Box_ImplementsInputBox()
        {
            Assert.IsTrue(new StringInput() is ITweeqInputBox);
        }

        [Test]
        public void Box_ImplementsNotifyValueChanged()
        {
            Assert.IsTrue(new StringInput() is INotifyValueChanged<string>);
        }

        [Test]
        public void Box_StandaloneKeepsEveryCorner()
        {
            StringInput input = Create();
            float radius = input.Theme.InputRadius;

            Assert.AreEqual(radius, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(radius, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(radius, Radius(input.style.borderBottomLeftRadius));
            Assert.AreEqual(radius, Radius(input.style.borderBottomRightRadius));
        }

        [Test]
        public void Box_InlineStartFlattensTrailingCorners()
        {
            StringInput input = new StringInput
            {
                InlinePosition = TweeqBoxPosition.Start,
            };
            float radius = input.Theme.InputRadius;

            Assert.AreEqual(radius, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(radius, Radius(input.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomRightRadius));
        }

        [Test]
        public void Box_BlockMiddleFlattensEveryCorner()
        {
            StringInput input = new StringInput
            {
                BlockPosition = TweeqBoxPosition.Middle,
            };

            Assert.AreEqual(0f, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomRightRadius));
        }

        [Test]
        public void Box_DisabledBlocksPicking()
        {
            StringInput input = Create();

            input.Disabled = true;
            Assert.AreEqual(PickingMode.Ignore, input.pickingMode);

            input.Disabled = false;
            Assert.AreEqual(PickingMode.Position, input.pickingMode);
        }

        [Test]
        public void Box_ThemeNullFallsBackToDark()
        {
            StringInput input = Create();

            input.Theme = null;

            Assert.IsNotNull(input.Theme);
            Assert.AreEqual(ColorMode.Dark, input.Theme.Mode);
        }

        #endregion

        #region Align

        [Test]
        public void Align_DefaultsToLeft()
        {
            Assert.AreEqual(TweeqTextAlign.Left, new StringInput().Align);
        }

        [Test]
        public void Align_IsSettable()
        {
            StringInput input = Create();

            input.Align = TweeqTextAlign.Center;

            Assert.AreEqual(TweeqTextAlign.Center, input.Align);
        }

        #endregion

        #region Invalid

        [Test]
        public void Invalid_ExternalFlagIsIndependentOfTheValidator()
        {
            StringInput input = Create("abc");

            input.Invalid = true;

            // An external invalid flag does not taint the validator's own judgment (the display composition is an OR)
            Assert.IsTrue(input.Invalid);
            Assert.IsFalse(input.IsRejected);
        }

        #endregion
    }
}
