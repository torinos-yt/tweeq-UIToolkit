using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies the panel-independent parts of CheckboxInput / SwitchInput.
    ///
    /// Swipe (pointer events) and keyboard input aren't handled here, since SendEvent itself
    /// can't be delivered without a panel. The following should be verified via runtime E2E:
    /// - Click = toggle + Confirmed fires once
    /// - Drag starts at 3px (mouse) / 5px (touch) or a 0.2s long press
    /// - The preview reflects the dx sign immediately while dragging, and release fires Confirmed once
    /// - T/Y/1/P -> true, F/N/0/M -> false, Space -> toggle (each fires change + Confirmed)
    /// - The preview overlay appearing/expanding while dragging
    /// </summary>
    public class BoolInputTests
    {
        #region Checkbox

        [Test]
        public void Checkbox_DefaultsToFalseAndIsFocusable()
        {
            CheckboxInput checkbox = new CheckboxInput();

            Assert.IsFalse(checkbox.value);
            Assert.IsTrue(checkbox.focusable);
            Assert.IsFalse(checkbox.Disabled);
        }

        [Test]
        public void Checkbox_ValueSetterUpdatesValue()
        {
            CheckboxInput checkbox = new CheckboxInput();

            checkbox.value = true;

            Assert.IsTrue(checkbox.value);
        }

        [Test]
        public void Checkbox_ValueSetterDoesNotFireConfirmed()
        {
            CheckboxInput checkbox = new CheckboxInput();
            int calls = 0;
            checkbox.Confirmed += _ => calls++;

            checkbox.value = true;
            checkbox.value = false;

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Checkbox_SetValueWithoutNotifyUpdatesValueSilently()
        {
            CheckboxInput checkbox = new CheckboxInput();
            int calls = 0;
            checkbox.Confirmed += _ => calls++;

            checkbox.SetValueWithoutNotify(true);

            Assert.IsTrue(checkbox.value);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Checkbox_DisabledBlocksPicking()
        {
            CheckboxInput checkbox = new CheckboxInput();

            checkbox.Disabled = true;
            Assert.AreEqual(PickingMode.Ignore, checkbox.pickingMode);

            checkbox.Disabled = false;
            Assert.AreEqual(PickingMode.Position, checkbox.pickingMode);
        }

        [Test]
        public void Checkbox_LabelNullBecomesEmptyAndHidesLabel()
        {
            CheckboxInput checkbox = new CheckboxInput { Label = "Enabled" };
            Label label = checkbox.Q<Label>("tweeq-checkbox-label");

            Assert.IsNotNull(label);
            Assert.AreEqual("Enabled", label.text);
            Assert.AreEqual(DisplayStyle.Flex, label.style.display.value);

            checkbox.Label = null;

            Assert.AreEqual(string.Empty, checkbox.Label);
            Assert.AreEqual(DisplayStyle.None, label.style.display.value);
        }

        [Test]
        public void Checkbox_ThemeNullFallsBackToDark()
        {
            CheckboxInput checkbox = new CheckboxInput();

            checkbox.Theme = null;

            Assert.IsNotNull(checkbox.Theme);
            Assert.AreEqual(ColorMode.Dark, checkbox.Theme.Mode);
        }

        #endregion

        #region Corner fusion

        [Test]
        public void Checkbox_ImplementsInputBox()
        {
            Assert.IsTrue(new CheckboxInput() is ITweeqInputBox);
        }

        [Test]
        public void Checkbox_StandaloneKeepsEveryCorner()
        {
            CheckboxInput checkbox = new CheckboxInput();
            VisualElement box = Box(checkbox);
            float radius = checkbox.Theme.InputRadius;

            Assert.AreEqual(radius, Radius(box.style.borderTopLeftRadius));
            Assert.AreEqual(radius, Radius(box.style.borderTopRightRadius));
            Assert.AreEqual(radius, Radius(box.style.borderBottomLeftRadius));
            Assert.AreEqual(radius, Radius(box.style.borderBottomRightRadius));
        }

        [Test]
        public void Checkbox_InlineStartFlattensTrailingCorners()
        {
            CheckboxInput checkbox = new CheckboxInput { InlinePosition = TweeqBoxPosition.Start };
            VisualElement box = Box(checkbox);
            float radius = checkbox.Theme.InputRadius;

            Assert.AreEqual(radius, Radius(box.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(box.style.borderTopRightRadius));
            Assert.AreEqual(radius, Radius(box.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(box.style.borderBottomRightRadius));
        }

        [Test]
        public void Checkbox_BlockEndFlattensTopCorners()
        {
            CheckboxInput checkbox = new CheckboxInput { BlockPosition = TweeqBoxPosition.End };
            VisualElement box = Box(checkbox);
            float radius = checkbox.Theme.InputRadius;

            Assert.AreEqual(0f, Radius(box.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(box.style.borderTopRightRadius));
            Assert.AreEqual(radius, Radius(box.style.borderBottomLeftRadius));
            Assert.AreEqual(radius, Radius(box.style.borderBottomRightRadius));
        }

        [Test]
        public void Checkbox_MiddleFlattensEveryCorner()
        {
            CheckboxInput checkbox = new CheckboxInput { InlinePosition = TweeqBoxPosition.Middle };
            VisualElement box = Box(checkbox);

            Assert.AreEqual(0f, Radius(box.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(box.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(box.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(box.style.borderBottomRightRadius));
        }

        [Test]
        public void Checkbox_AxesAreComposedWithOr()
        {
            // Composing Inline=Start (flattens the right) with Block=Start (flattens the bottom) doubly flattens only the bottom-right
            CheckboxInput checkbox = new CheckboxInput
            {
                InlinePosition = TweeqBoxPosition.Start,
                BlockPosition = TweeqBoxPosition.Start,
            };
            VisualElement box = Box(checkbox);
            float radius = checkbox.Theme.InputRadius;

            Assert.AreEqual(radius, Radius(box.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(box.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(box.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(box.style.borderBottomRightRadius));
        }

        #endregion

        #region Switch

        [Test]
        public void Switch_DefaultsToFalseAndIsFocusable()
        {
            SwitchInput toggle = new SwitchInput();

            Assert.IsFalse(toggle.value);
            Assert.IsTrue(toggle.focusable);
        }

        [Test]
        public void Switch_ValueSetterUpdatesValue()
        {
            SwitchInput toggle = new SwitchInput();

            toggle.value = true;

            Assert.IsTrue(toggle.value);
        }

        [Test]
        public void Switch_ValueSetterDoesNotFireConfirmed()
        {
            SwitchInput toggle = new SwitchInput();
            int calls = 0;
            toggle.Confirmed += _ => calls++;

            toggle.value = true;
            toggle.value = false;

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Switch_SetValueWithoutNotifyUpdatesValueSilently()
        {
            SwitchInput toggle = new SwitchInput();
            int calls = 0;
            toggle.Confirmed += _ => calls++;

            toggle.SetValueWithoutNotify(true);

            Assert.IsTrue(toggle.value);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Switch_HandleMovesToTheOnPosition()
        {
            SwitchInput toggle = new SwitchInput();
            VisualElement handle = toggle.Q<VisualElement>("tweeq-switch-handle");
            Assert.IsNotNull(handle);

            // off: inset 4px / width 16px
            Assert.AreEqual(4f, Radius(handle.style.left));
            Assert.AreEqual(16f, Radius(handle.style.width));

            toggle.value = true;

            // on: 48 - 4 - 16 = 28
            Assert.AreEqual(28f, Radius(handle.style.left));
            Assert.AreEqual(16f, Radius(handle.style.width));
        }

        [Test]
        public void Switch_ThemeNullFallsBackToDark()
        {
            SwitchInput toggle = new SwitchInput();

            toggle.Theme = null;

            Assert.IsNotNull(toggle.Theme);
            Assert.AreEqual(ColorMode.Dark, toggle.Theme.Mode);
        }

        [Test]
        public void Switch_DoesNotJoinCornerFusion()
        {
            Assert.IsFalse(new SwitchInput() is ITweeqInputBox);
        }

        [Test]
        public void Switch_HasNoDisabledMember()
        {
            // Spec Unity-facing decision 3: Switch having no disabled matches the Vue original.
            // Keep this as a test recording the compile-time intent, so accidentally adding it back gets noticed
            Assert.IsNull(typeof(SwitchInput).GetProperty("Disabled", BindingFlags.Public | BindingFlags.Instance));
            Assert.IsNull(typeof(SwitchInput).GetField("Disabled", BindingFlags.Public | BindingFlags.Instance));
        }

        #endregion

        #region Helpers

        static VisualElement Box(CheckboxInput checkbox)
        {
            VisualElement box = checkbox.Q<VisualElement>("tweeq-checkbox-box");
            Assert.IsNotNull(box);
            return box;
        }

        static float Radius(StyleLength length)
        {
            return length.value.value;
        }

        #endregion
    }
}
