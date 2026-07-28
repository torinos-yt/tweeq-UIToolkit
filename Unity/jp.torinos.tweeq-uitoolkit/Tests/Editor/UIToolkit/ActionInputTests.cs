using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// ButtonInput / ButtonToggleInput / RadioInput のうち panel 非依存の部分
    /// （値の受け入れ規則・Options のコピー意味論・Disabled のゲート）を検証する。
    ///
    /// ChangeEvent は panel が無いと送れないので、ここでは value プロパティと
    /// Confirmed / Clicked（ただの C# イベント）で確認する。ポインタ操作・遷移・
    /// Blink / Flash の見た目は panel と描画が要るので Play Mode 側の担当。
    /// </summary>
    public class ActionInputTests
    {
        const float RADIUS = 4f;

        static float Radius(StyleLength style)
        {
            return style.value.value;
        }

        #region RadioInput

        [Test]
        public void Radio_ValueBelowRangeIsIgnored()
        {
            RadioInput radio = new RadioInput(new[] { "A", "B", "C" });
            radio.SetValueWithoutNotify(1);

            radio.value = -1;

            Assert.AreEqual(1, radio.value);
        }

        [Test]
        public void Radio_ValueAboveRangeIsIgnored()
        {
            RadioInput radio = new RadioInput(new[] { "A", "B", "C" });
            radio.SetValueWithoutNotify(1);

            radio.value = 3;

            Assert.AreEqual(1, radio.value);
        }

        [Test]
        public void Radio_ValueWithoutOptionsIsIgnored()
        {
            RadioInput radio = new RadioInput();

            radio.value = 0;

            Assert.AreEqual(0, radio.value);
            Assert.AreEqual(0, radio.Options.Length);
        }

        [Test]
        public void Radio_SetValueWithoutNotifyRejectsOutOfRange()
        {
            RadioInput radio = new RadioInput(new[] { "A", "B" });
            radio.SetValueWithoutNotify(1);

            radio.SetValueWithoutNotify(5);

            Assert.AreEqual(1, radio.value);
        }

        [Test]
        public void Radio_OptionsGetReturnsCopy()
        {
            RadioInput radio = new RadioInput(new[] { "A", "B" });

            string[] snapshot = radio.Options;
            snapshot[0] = "Z";

            Assert.AreEqual("A", radio.Options[0]);
        }

        [Test]
        public void Radio_OptionsSetCopiesInput()
        {
            string[] source = { "A", "B" };
            RadioInput radio = new RadioInput(source);

            source[1] = "Z";

            Assert.AreEqual("B", radio.Options[1]);
        }

        [Test]
        public void Radio_OptionsNullBecomesEmpty()
        {
            RadioInput radio = new RadioInput(new[] { "A", "B" });

            radio.Options = null;

            Assert.AreEqual(0, radio.Options.Length);
        }

        [Test]
        public void Radio_OptionsShrinkClampsValue()
        {
            RadioInput radio = new RadioInput(new[] { "A", "B", "C" });
            radio.SetValueWithoutNotify(2);

            radio.Options = new[] { "A" };

            Assert.AreEqual(0, radio.value);
        }

        [Test]
        public void Radio_WrapIndexWrapsBothDirections()
        {
            Assert.AreEqual(2, RadioInput.WrapIndex(-1, 3));
            Assert.AreEqual(0, RadioInput.WrapIndex(3, 3));
            Assert.AreEqual(1, RadioInput.WrapIndex(1, 3));
            Assert.AreEqual(2, RadioInput.WrapIndex(-4, 3));
        }

        [Test]
        public void Radio_WrapIndexWithoutOptionsIsZero()
        {
            Assert.AreEqual(0, RadioInput.WrapIndex(3, 0));
            Assert.AreEqual(0, RadioInput.WrapIndex(-3, -1));
        }

        [Test]
        public void Radio_ThemeNullFallsBackToDark()
        {
            RadioInput radio = new RadioInput { Theme = null };

            Assert.IsNotNull(radio.Theme);
            Assert.AreEqual(ColorMode.Dark, radio.Theme.Mode);
        }

        #endregion

        #region ButtonToggleInput

        [Test]
        public void ButtonToggle_ValueSetterKeepsValue()
        {
            ButtonToggleInput toggle = new ButtonToggleInput("Mute");

            toggle.value = true;

            Assert.IsTrue(toggle.value);
        }

        [Test]
        public void ButtonToggle_SetValueWithoutNotifyDoesNotConfirm()
        {
            ButtonToggleInput toggle = new ButtonToggleInput();
            int calls = 0;
            toggle.Confirmed += _ => calls++;

            toggle.SetValueWithoutNotify(true);

            Assert.IsTrue(toggle.value);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void ButtonToggle_PerformClickTogglesAndConfirms()
        {
            ButtonToggleInput toggle = new ButtonToggleInput();
            int calls = 0;
            bool last = false;
            toggle.Confirmed += value =>
            {
                calls++;
                last = value;
            };

            toggle.PerformClick();

            Assert.IsTrue(toggle.value);
            Assert.AreEqual(1, calls);
            Assert.IsTrue(last);

            toggle.PerformClick();

            Assert.IsFalse(toggle.value);
            Assert.AreEqual(2, calls);
            Assert.IsFalse(last);
        }

        [Test]
        public void ButtonToggle_DisabledBlocksPerformClick()
        {
            ButtonToggleInput toggle = new ButtonToggleInput { Disabled = true };
            int calls = 0;
            toggle.Confirmed += _ => calls++;

            toggle.PerformClick();

            Assert.IsFalse(toggle.value);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void ButtonToggle_InlineMiddleSquaresAllCorners()
        {
            ButtonToggleInput toggle = new ButtonToggleInput
            {
                InlinePosition = TweeqBoxPosition.Middle,
            };

            Assert.AreEqual(0f, Radius(toggle.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(toggle.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(toggle.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(toggle.style.borderBottomRightRadius));
        }

        #endregion

        #region ButtonInput

        [Test]
        public void Button_PerformClickRaisesClicked()
        {
            ButtonInput button = new ButtonInput("Go");
            int calls = 0;
            button.Clicked += () => calls++;

            button.PerformClick();

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Button_DisabledBlocksClicked()
        {
            ButtonInput button = new ButtonInput("Go") { Disabled = true };
            int calls = 0;
            button.Clicked += () => calls++;

            button.PerformClick();

            Assert.AreEqual(0, calls);
        }

        [Test]
        public void Button_ReEnablingRestoresClicked()
        {
            ButtonInput button = new ButtonInput("Go") { Disabled = true };
            int calls = 0;
            button.Clicked += () => calls++;

            button.PerformClick();
            button.Disabled = false;
            button.PerformClick();

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Button_LabelNullBecomesEmpty()
        {
            ButtonInput button = new ButtonInput("Go") { Label = null };

            Assert.AreEqual(string.Empty, button.Label);
        }

        [Test]
        public void Button_NarrowDropsMinWidth()
        {
            ButtonInput button = new ButtonInput("+");

            Assert.AreEqual(24f, button.style.minWidth.value.value);

            button.Narrow = true;

            Assert.AreEqual(0f, button.style.minWidth.value.value);
        }

        [Test]
        public void Button_FlashWithoutPanelDoesNotThrow()
        {
            ButtonInput button = new ButtonInput("Go");

            // panel が無いとスケジューラが回らない。例外を出さずに素通りすることだけ保証する
            Assert.DoesNotThrow(() => button.Flash());
            Assert.DoesNotThrow(() => button.Flash());
        }

        [Test]
        public void Button_BlinkTogglesWithoutPanel()
        {
            ButtonInput button = new ButtonInput("Go");

            Assert.DoesNotThrow(() => button.Blink = true);
            Assert.IsTrue(button.Blink);

            Assert.DoesNotThrow(() => button.Blink = false);
            Assert.IsFalse(button.Blink);
        }

        [Test]
        public void Button_InlineStartSquaresRightCorners()
        {
            ButtonInput button = new ButtonInput { InlinePosition = TweeqBoxPosition.Start };

            Assert.AreEqual(RADIUS, Radius(button.style.borderTopLeftRadius));
            Assert.AreEqual(RADIUS, Radius(button.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(button.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(button.style.borderBottomRightRadius));
        }

        [Test]
        public void Button_ThemeNullFallsBackToDark()
        {
            ButtonInput button = new ButtonInput { Theme = null };

            Assert.IsNotNull(button.Theme);
            Assert.AreEqual(ColorMode.Dark, button.Theme.Mode);
        }

        #endregion
    }
}
