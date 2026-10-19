using System.Collections.Generic;
using NUnit.Framework;
using Tweeq.UIToolkit.TestSupport;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Pins ShowMixedValue: what a mixed input hides, and which edits resolve it.
    ///
    /// Key and pointer input go through a runtime panel (events are only delivered on one); the
    /// dropdown's open/commit/cancel layer is panel-independent, so it is driven directly.
    /// </summary>
    public class MixedValueTests
    {
        static readonly string[] Options = { "Linear", "Ease In", "Ease Out" };

        TweeqRuntimeTestPanel _panel;

        [TearDown]
        public void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
        }

        #region Helpers

        T Mount<T>(T element) where T : VisualElement
        {
            _panel = TweeqRuntimeTestPanel.Create();
            _panel.Root.Add(element);

            // An EditMode panel has no "element under the pointer", so PointerDown only arrives through a capture
            element.CapturePointer(PointerId.mousePointerId);
            element.Focus();
            return element;
        }

        static void SendPointer(VisualElement element, EventType type, Vector2 position)
        {
            Event systemEvent = new Event
            {
                type = type,
                mousePosition = position,
                button = 0,
                modifiers = EventModifiers.None,
            };

            if (type == EventType.MouseDown)
            {
                using (PointerDownEvent down = PointerDownEvent.GetPooled(systemEvent))
                {
                    element.SendEvent(down);
                }

                return;
            }

            if (type == EventType.MouseUp)
            {
                using (PointerUpEvent up = PointerUpEvent.GetPooled(systemEvent))
                {
                    element.SendEvent(up);
                }

                return;
            }

            using (PointerMoveEvent move = PointerMoveEvent.GetPooled(systemEvent))
            {
                element.SendEvent(move);
            }
        }

        static void KeyDown(VisualElement element, KeyCode keyCode)
        {
            using (KeyDownEvent evt = KeyDownEvent.GetPooled('\0', keyCode, EventModifiers.None))
            {
                element.SendEvent(evt);
            }
        }

        // The overlay holds prefix / value / suffix labels in that order
        static Label NumberValueLabel(NumberInput input)
        {
            return (Label)input.Q("tweeq-number-display").ElementAt(1);
        }

        #endregion

        [Test]
        public void Default_IsOffForEveryInput()
        {
            Assert.IsFalse(new NumberInput().ShowMixedValue);
            Assert.IsFalse(new RotaryInput().ShowMixedValue);
            Assert.IsFalse(new AngleInput().ShowMixedValue);
            Assert.IsFalse(new SwitchInput().ShowMixedValue);
            Assert.IsFalse(new DropdownInput<string>(Options).ShowMixedValue);
        }

        #region NumberInput

        [Test]
        public void Number_MixedShowsDashUntilCleared()
        {
            NumberInput input = new NumberInput { Suffix = "m" };
            input.SetValueWithoutNotify(0.5f);
            string numeric = NumberValueLabel(input).text;

            input.ShowMixedValue = true;

            Assert.AreEqual(TweeqInputBoxStyles.MIXED_VALUE_TEXT, NumberValueLabel(input).text);
            Assert.AreEqual(DisplayStyle.None, input.Q("tweeq-number-display").ElementAt(2).style.display.value);
            Assert.AreEqual(numeric, input.DisplayText, "DisplayText keeps the value's text");

            input.SetValueWithoutNotify(0.25f);
            Assert.IsTrue(input.ShowMixedValue, "SetValueWithoutNotify leaves the mixed state alone");

            input.ShowMixedValue = false;
            Assert.AreEqual(input.DisplayText, NumberValueLabel(input).text);
        }

        [Test]
        public void Number_EnterWhileMixedConfirmsNothingAndArrowResolves()
        {
            NumberInput input = Mount(new NumberInput { Step = 1.0 });
            input.SetValueWithoutNotify(5f);
            input.ShowMixedValue = true;
            List<float> confirmed = new List<float>();
            List<float> changed = new List<float>();
            input.Confirmed += value => confirmed.Add(value);
            input.RegisterValueChangedCallback(evt => changed.Add(evt.newValue));

            KeyDown(input, KeyCode.Return);
            Assert.AreEqual(0, confirmed.Count);
            Assert.IsTrue(input.ShowMixedValue);

            KeyDown(input, KeyCode.UpArrow);
            Assert.IsFalse(input.ShowMixedValue);
            Assert.AreEqual(new[] { 6f }, changed.ToArray());

            KeyDown(input, KeyCode.Return);
            Assert.AreEqual(new[] { 6f }, confirmed.ToArray());
        }

        [Test]
        public void Number_DragResolvesMixedFromTheCurrentValue()
        {
            NumberInput input = Mount(new NumberInput());
            input.SetValueWithoutNotify(5f);
            input.ShowMixedValue = true;
            List<float> confirmed = new List<float>();
            input.Confirmed += value => confirmed.Add(value);

            SendPointer(input, EventType.MouseDown, new Vector2(10f, 5f));
            SendPointer(input, EventType.MouseDrag, new Vector2(20f, 5f));
            Assert.IsTrue(input.IsScrubbing);
            Assert.IsFalse(input.ShowMixedValue);

            // The move that crosses the threshold only starts the scrub; the next one changes the value
            SendPointer(input, EventType.MouseDrag, new Vector2(40f, 5f));
            SendPointer(input, EventType.MouseUp, new Vector2(40f, 5f));

            Assert.AreEqual(1, confirmed.Count);
            Assert.Greater(confirmed[0], 5f, "the drag continues from the current value");
            Assert.IsFalse(input.ShowMixedValue);
        }

        #endregion

        #region SwitchInput

        [Test]
        public void Switch_MixedDrawsTheOffTrackEvenWhenOn()
        {
            SwitchInput input = new SwitchInput();
            input.SetValueWithoutNotify(true);
            VisualElement track = input.Q("tweeq-switch-track");

            input.ShowMixedValue = true;
            Assert.AreEqual(input.Theme.Input, track.style.backgroundColor.value);

            input.ShowMixedValue = false;
            Assert.AreEqual(input.Theme.Accent, track.style.backgroundColor.value);
        }

        [Test]
        public void Switch_ClickWhileMixedTurnsOn()
        {
            // Already on underneath: a plain toggle would have turned it off
            SwitchInput input = Mount(new SwitchInput());
            input.SetValueWithoutNotify(true);
            input.ShowMixedValue = true;
            List<bool> confirmed = new List<bool>();
            List<bool> changed = new List<bool>();
            input.Confirmed += value => confirmed.Add(value);
            input.RegisterValueChangedCallback(evt => changed.Add(evt.newValue));

            SendPointer(input, EventType.MouseDown, new Vector2(4f, 4f));
            SendPointer(input, EventType.MouseUp, new Vector2(4f, 4f));

            Assert.IsTrue(input.value);
            Assert.IsFalse(input.ShowMixedValue);
            Assert.AreEqual(new[] { true }, confirmed.ToArray());
            Assert.AreEqual(new[] { true }, changed.ToArray(), "resolving the mixed state notifies even for the same value");
        }

        #endregion

        #region DropdownInput

        [Test]
        public void Dropdown_CommitWithoutAPickConfirmsNothing()
        {
            DropdownInput<string> dropdown = new DropdownInput<string>(Options);
            dropdown.SetValueWithoutNotify("Ease In");
            dropdown.ShowMixedValue = true;
            List<string> confirmed = new List<string>();
            dropdown.Confirmed += value => confirmed.Add(value);

            Assert.AreEqual(TweeqInputBoxStyles.MIXED_VALUE_TEXT, dropdown.DisplayText);

            dropdown.Open();
            dropdown.Commit();
            Assert.AreEqual(0, confirmed.Count);
            Assert.IsTrue(dropdown.ShowMixedValue);

            // No option is marked, so the arrow starts from the top rather than from the hidden value
            dropdown.Open();
            dropdown.MoveSelection(1);
            Assert.AreEqual("Linear", dropdown.value);
            Assert.IsFalse(dropdown.ShowMixedValue);
            dropdown.Commit();
            Assert.AreEqual(new[] { "Linear" }, confirmed.ToArray());
        }

        [Test]
        public void Dropdown_CancelAfterAHoverRestoresMixed()
        {
            DropdownInput<string> dropdown = new DropdownInput<string>(Options);
            dropdown.SetValueWithoutNotify("Ease In");
            dropdown.ShowMixedValue = true;

            dropdown.Open();
            dropdown.PerformPointerHover(2);
            Assert.AreEqual("Ease Out", dropdown.value);
            Assert.IsFalse(dropdown.ShowMixedValue);

            dropdown.Cancel();
            Assert.AreEqual("Ease In", dropdown.value);
            Assert.IsTrue(dropdown.ShowMixedValue);
            Assert.AreEqual(TweeqInputBoxStyles.MIXED_VALUE_TEXT, dropdown.DisplayText);
        }

        #endregion

        #region AngleInput

        [Test]
        public void Angle_MixedCoversBothHalvesAndANumberEditResolvesBoth()
        {
            AngleInput input = new AngleInput();
            input.SetValueWithoutNotify(30f);

            input.ShowMixedValue = true;
            Assert.IsTrue(input.Rotary.ShowMixedValue);
            Assert.IsTrue(input.Number.ShowMixedValue);

            input.PerformNumberEdit(45f);
            Assert.AreEqual(45f, input.value);
            Assert.IsFalse(input.Rotary.ShowMixedValue);
            Assert.IsFalse(input.Number.ShowMixedValue);
        }

        #endregion
    }
}
