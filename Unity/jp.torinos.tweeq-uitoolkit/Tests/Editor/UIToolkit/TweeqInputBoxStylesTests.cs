using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verification of the chrome API exposed to external asmdefs (ext-custom-widgets-spec.md EXT-01-A).
    ///
    /// The extraction source is NumberInput, so besides the corner-radius table itself, this also confirms
    /// that "the appearance of NumberInput / StringInput has not changed before and after the extraction".
    /// A VisualElement can have its style set without a panel, so this is self-contained within EditMode.
    /// </summary>
    public class TweeqInputBoxStylesTests
    {
        const float RADIUS = 4f;

        static float Radius(StyleLength style)
        {
            return style.value.value;
        }

        static VisualElement Cornered(TweeqBoxPosition inline, TweeqBoxPosition block)
        {
            VisualElement element = new VisualElement();
            TweeqInputBoxStyles.ApplyCornerRadius(element, new TweeqTheme(), inline, block);
            return element;
        }

        static void AssertCorners(
            VisualElement element,
            float topLeft,
            float topRight,
            float bottomLeft,
            float bottomRight)
        {
            Assert.AreEqual(topLeft, Radius(element.style.borderTopLeftRadius), "top-left");
            Assert.AreEqual(topRight, Radius(element.style.borderTopRightRadius), "top-right");
            Assert.AreEqual(bottomLeft, Radius(element.style.borderBottomLeftRadius), "bottom-left");
            Assert.AreEqual(
                bottomRight, Radius(element.style.borderBottomRightRadius), "bottom-right");
        }

        #region Corner radius

        [Test]
        public void ApplyCornerRadius_InlineNone_KeepsAllCorners()
        {
            AssertCorners(
                Cornered(TweeqBoxPosition.None, TweeqBoxPosition.None),
                RADIUS, RADIUS, RADIUS, RADIUS);
        }

        [Test]
        public void ApplyCornerRadius_InlineStart_SquaresRightCorners()
        {
            AssertCorners(
                Cornered(TweeqBoxPosition.Start, TweeqBoxPosition.None),
                RADIUS, 0f, RADIUS, 0f);
        }

        [Test]
        public void ApplyCornerRadius_InlineMiddle_SquaresAllCorners()
        {
            AssertCorners(
                Cornered(TweeqBoxPosition.Middle, TweeqBoxPosition.None),
                0f, 0f, 0f, 0f);
        }

        [Test]
        public void ApplyCornerRadius_InlineEnd_SquaresLeftCorners()
        {
            AssertCorners(
                Cornered(TweeqBoxPosition.End, TweeqBoxPosition.None),
                0f, RADIUS, 0f, RADIUS);
        }

        [Test]
        public void ApplyCornerRadius_BlockStart_SquaresBottomCorners()
        {
            AssertCorners(
                Cornered(TweeqBoxPosition.None, TweeqBoxPosition.Start),
                RADIUS, RADIUS, 0f, 0f);
        }

        [Test]
        public void ApplyCornerRadius_BlockMiddle_SquaresAllCorners()
        {
            AssertCorners(
                Cornered(TweeqBoxPosition.None, TweeqBoxPosition.Middle),
                0f, 0f, 0f, 0f);
        }

        [Test]
        public void ApplyCornerRadius_BlockEnd_SquaresTopCorners()
        {
            AssertCorners(
                Cornered(TweeqBoxPosition.None, TweeqBoxPosition.End),
                0f, 0f, RADIUS, RADIUS);
        }

        [Test]
        public void ApplyCornerRadius_BothAxes_CombineWithOr()
        {
            // Combining a horizontal Start (squares the right side) with a vertical End (squares the top) leaves only the bottom-left rounded
            AssertCorners(
                Cornered(TweeqBoxPosition.Start, TweeqBoxPosition.End),
                0f, 0f, RADIUS, 0f);
        }

        [Test]
        public void ApplyCornerRadius_NullTheme_FallsBackToZeroRadius()
        {
            VisualElement element = new VisualElement();

            TweeqInputBoxStyles.ApplyCornerRadius(
                element, null, TweeqBoxPosition.None, TweeqBoxPosition.None);

            AssertCorners(element, 0f, 0f, 0f, 0f);
        }

        [Test]
        public void ApplyCornerRadius_NullElement_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => TweeqInputBoxStyles.ApplyCornerRadius(
                null, new TweeqTheme(), TweeqBoxPosition.Start, TweeqBoxPosition.End));
        }

        [Test]
        public void SetCornerRadius_AppliesToAllFourCorners()
        {
            VisualElement element = new VisualElement();

            TweeqInputBoxStyles.SetCornerRadius(element, 7f);

            AssertCorners(element, 7f, 7f, 7f, 7f);
        }

        #endregion

        #region Border

        [Test]
        public void SetBorderWidth_AppliesToAllFourEdges()
        {
            VisualElement element = new VisualElement();

            TweeqInputBoxStyles.SetBorderWidth(element, 3f);

            Assert.AreEqual(3f, element.style.borderLeftWidth.value);
            Assert.AreEqual(3f, element.style.borderRightWidth.value);
            Assert.AreEqual(3f, element.style.borderTopWidth.value);
            Assert.AreEqual(3f, element.style.borderBottomWidth.value);
        }

        [Test]
        public void SetBorderColor_AppliesToAllFourEdges()
        {
            VisualElement element = new VisualElement();

            TweeqInputBoxStyles.SetBorderColor(element, Color.red);

            Assert.AreEqual(Color.red, element.style.borderLeftColor.value);
            Assert.AreEqual(Color.red, element.style.borderRightColor.value);
            Assert.AreEqual(Color.red, element.style.borderTopColor.value);
            Assert.AreEqual(Color.red, element.style.borderBottomColor.value);
        }

        [Test]
        public void BorderHelpers_NullElement_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => TweeqInputBoxStyles.SetBorderWidth(null, 1f));
            Assert.DoesNotThrow(() => TweeqInputBoxStyles.SetBorderColor(null, Color.red));
            Assert.DoesNotThrow(() => TweeqInputBoxStyles.SetCornerRadius(null, 1f));
        }

        #endregion

        #region Background

        [Test]
        public void ResolveBackground_UsesInputHoverWhileHovered()
        {
            TweeqTheme theme = TweeqTheme.Dark();

            Assert.AreEqual(theme.Input, TweeqInputBoxStyles.ResolveBackground(theme, false));
            Assert.AreEqual(theme.InputHover, TweeqInputBoxStyles.ResolveBackground(theme, true));
        }

        [Test]
        public void ResolveBackground_LightAndDarkDifferSoThemeIsHonoured()
        {
            Assert.AreNotEqual(
                TweeqInputBoxStyles.ResolveBackground(TweeqTheme.Dark(), true),
                TweeqInputBoxStyles.ResolveBackground(TweeqTheme.Light(), true));
        }

        [Test]
        public void ResolveBackground_NullTheme_ReturnsClear()
        {
            Assert.AreEqual(Color.clear, TweeqInputBoxStyles.ResolveBackground(null, true));
        }

        [Test]
        public void ApplyBackgroundTransition_TransitionsBackgroundColorOnly()
        {
            VisualElement element = new VisualElement();
            TweeqTheme theme = new TweeqTheme();

            TweeqInputBoxStyles.ApplyBackgroundTransition(element, theme);

            List<StylePropertyName> properties = element.style.transitionProperty.value;
            Assert.AreEqual(1, properties.Count);
            Assert.AreEqual("background-color", properties[0].ToString());

            List<TimeValue> durations = element.style.transitionDuration.value;
            Assert.AreEqual(1, durations.Count);
            Assert.AreEqual(theme.HoverTransitionDuration, durations[0].value);
            Assert.AreEqual(TimeUnit.Second, durations[0].unit);

            List<EasingFunction> easings = element.style.transitionTimingFunction.value;
            Assert.AreEqual(1, easings.Count);
            Assert.AreEqual(EasingMode.EaseInOutCubic, easings[0].mode);
        }

        [Test]
        public void ApplyBackgroundTransition_NullArguments_DoNotThrow()
        {
            Assert.DoesNotThrow(
                () => TweeqInputBoxStyles.ApplyBackgroundTransition(null, new TweeqTheme()));
            Assert.DoesNotThrow(
                () => TweeqInputBoxStyles.ApplyBackgroundTransition(new VisualElement(), null));
        }

        #endregion

        #region Adoption

        [Test]
        public void NumberInput_CornersMatchTheExtractedRule()
        {
            NumberInput input = new NumberInput
            {
                InlinePosition = TweeqBoxPosition.Start,
                BlockPosition = TweeqBoxPosition.End,
            };

            AssertCorners(input, 0f, 0f, RADIUS, 0f);
        }

        [Test]
        public void StringInput_CornersMatchTheExtractedRule()
        {
            StringInput input = new StringInput
            {
                InlinePosition = TweeqBoxPosition.Start,
                BlockPosition = TweeqBoxPosition.End,
            };

            AssertCorners(input, 0f, 0f, RADIUS, 0f);
        }

        [Test]
        public void NumberInput_AndStringInput_ShareTheSameChrome()
        {
            // The chrome extraction was only applied to these two, so this pins down that they haven't drifted apart
            NumberInput number = new NumberInput { InlinePosition = TweeqBoxPosition.Middle };
            StringInput text = new StringInput { InlinePosition = TweeqBoxPosition.Middle };

            Assert.AreEqual(
                Radius(number.style.borderTopLeftRadius),
                Radius(text.style.borderTopLeftRadius));
            Assert.AreEqual(
                Radius(number.style.borderBottomRightRadius),
                Radius(text.style.borderBottomRightRadius));
            Assert.AreEqual(
                number.style.transitionDuration.value.Count,
                text.style.transitionDuration.value.Count);
        }

        [Test]
        public void DisabledInputs_KeepTheInsetBorderChrome()
        {
            NumberInput number = new NumberInput { Disabled = true };
            StringInput text = new StringInput { Disabled = true };

            Assert.AreEqual(1f, number.style.borderTopWidth.value);
            Assert.AreEqual(1f, text.style.borderTopWidth.value);
            Assert.AreEqual(Color.clear, number.style.backgroundColor.value);
            Assert.AreEqual(Color.clear, text.style.backgroundColor.value);
        }

        #endregion

        #region Disabled chrome

        [Test]
        public void ApplyDisabledChrome_Disabled_ClearsBackgroundAndDrawsTheInsetBorder()
        {
            VisualElement element = new VisualElement();
            TweeqTheme theme = TweeqTheme.Dark();
            element.style.backgroundColor = Color.red;

            TweeqInputBoxStyles.ApplyDisabledChrome(element, theme, true);

            Assert.AreEqual(Color.clear, element.style.backgroundColor.value);
            Assert.AreEqual(1f, element.style.borderTopWidth.value);
            Assert.AreEqual(1f, element.style.borderLeftWidth.value);
            Assert.AreEqual(1f, element.style.borderRightWidth.value);
            Assert.AreEqual(1f, element.style.borderBottomWidth.value);
            Assert.AreEqual(theme.Border, element.style.borderTopColor.value);
        }

        [Test]
        public void ApplyDisabledChrome_Enabled_DropsTheBorderOnly()
        {
            VisualElement element = new VisualElement();
            TweeqTheme theme = TweeqTheme.Dark();

            TweeqInputBoxStyles.ApplyDisabledChrome(element, theme, true);
            TweeqInputBoxStyles.ApplyDisabledChrome(element, theme, false);

            Assert.AreEqual(0f, element.style.borderTopWidth.value);
            Assert.AreEqual(0f, element.style.borderBottomWidth.value);

            // The normal-state background is the responsibility of the caller, which knows about hover; the helper does not repaint it
            Assert.AreEqual(Color.clear, element.style.backgroundColor.value);
        }

        [Test]
        public void ApplyDisabledChrome_NullTheme_StillClearsTheBackground()
        {
            VisualElement element = new VisualElement();
            element.style.backgroundColor = Color.red;

            TweeqInputBoxStyles.ApplyDisabledChrome(element, null, true);

            Assert.AreEqual(Color.clear, element.style.backgroundColor.value);
            Assert.AreEqual(1f, element.style.borderTopWidth.value);
        }

        [Test]
        public void ApplyDisabledChrome_NullElement_DoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => TweeqInputBoxStyles.ApplyDisabledChrome(null, TweeqTheme.Dark(), true));
            Assert.DoesNotThrow(
                () => TweeqInputBoxStyles.ApplyDisabledChrome(null, TweeqTheme.Dark(), false));
        }

        #endregion

        #region Text field

        static TextField NormalizedField(TweeqTheme theme)
        {
            TextField field = new TextField();
            TweeqInputBoxStyles.ApplyTextField(field, theme);
            return field;
        }

        static VisualElement TextInputOf(TextField field)
        {
            return field.Q("unity-text-input");
        }

        [Test]
        public void ApplyTextField_FlattensTheFieldItself()
        {
            TextField field = NormalizedField(TweeqTheme.Dark());

            Assert.AreEqual(12f, field.style.fontSize.value.value);
            Assert.AreEqual(0f, field.style.paddingLeft.value.value);
            Assert.AreEqual(0f, field.style.paddingRight.value.value);
            Assert.AreEqual(0f, field.style.paddingTop.value.value);
            Assert.AreEqual(0f, field.style.paddingBottom.value.value);
            Assert.AreEqual(0f, field.style.marginLeft.value.value);
            Assert.AreEqual(0f, field.style.marginRight.value.value);
            Assert.AreEqual(0f, field.style.marginTop.value.value);
            Assert.AreEqual(0f, field.style.marginBottom.value.value);
            Assert.AreEqual(0f, field.style.minHeight.value.value);
            Assert.AreEqual(Align.Stretch, field.style.alignItems.value);
        }

        [Test]
        public void ApplyTextField_MakesTheInnerInputFillTheBoxWithoutChrome()
        {
            VisualElement textInput = TextInputOf(NormalizedField(TweeqTheme.Dark()));

            Assert.IsNotNull(textInput);
            Assert.AreEqual(Color.clear, textInput.style.backgroundColor.value);
            Assert.AreEqual(0f, textInput.style.borderTopWidth.value);
            Assert.AreEqual(Color.clear, textInput.style.borderTopColor.value);
            Assert.AreEqual(100f, textInput.style.height.value.value);
            Assert.AreEqual(LengthUnit.Percent, textInput.style.height.value.unit);
            Assert.AreEqual(0f, textInput.style.minHeight.value.value);
            Assert.AreEqual(12f, textInput.style.fontSize.value.value);
            Assert.AreEqual(WhiteSpace.NoWrap, textInput.style.whiteSpace.value);
            Assert.AreEqual(0f, textInput.style.paddingLeft.value.value);
            Assert.AreEqual(0f, textInput.style.paddingRight.value.value);
            Assert.AreEqual(0f, textInput.style.paddingTop.value.value);
            Assert.AreEqual(0f, textInput.style.paddingBottom.value.value);
            Assert.AreEqual(0f, textInput.style.marginTop.value.value);
            Assert.AreEqual(0f, textInput.style.marginBottom.value.value);
        }

        [Test]
        public void ApplyTextField_AlsoUncrushesTheInnerTextElement()
        {
            VisualElement textInput = TextInputOf(NormalizedField(TweeqTheme.Dark()));
            TextElement textElement = textInput.Q<TextElement>();

            Assert.IsNotNull(textElement);
            Assert.AreEqual(100f, textElement.style.height.value.value);
            Assert.AreEqual(LengthUnit.Percent, textElement.style.height.value.unit);
            Assert.AreEqual(0f, textElement.style.minHeight.value.value);
            Assert.AreEqual(0f, textElement.style.paddingTop.value.value);
            Assert.AreEqual(0f, textElement.style.paddingBottom.value.value);
            Assert.AreEqual(12f, textElement.style.fontSize.value.value);
        }

        [Test]
        public void ApplyTextField_TakesTheCaretAndSelectionColoursFromTheTheme()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            TextField field = NormalizedField(theme);

            // The recommended API (--unity-selection-color) cannot be set per-instance from C#,
            // so the verification side also reads the obsolete property
#pragma warning disable 618
            Assert.AreEqual(theme.Text, field.textSelection.cursorColor);
            Assert.AreEqual(theme.AccentSoft, field.textSelection.selectionColor);
#pragma warning restore 618
        }

        [Test]
        public void ApplyTextField_NullTheme_StillNormalizesTheLayout()
        {
            TextField field = NormalizedField(null);

            Assert.AreEqual(12f, field.style.fontSize.value.value);
            Assert.AreEqual(100f, TextInputOf(field).style.height.value.value);
        }

        [Test]
        public void ApplyTextField_NullField_DoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => TweeqInputBoxStyles.ApplyTextField(null, TweeqTheme.Dark()));
        }

        [Test]
        public void AdoptingWidgets_KeepTheirOwnHorizontalPadding()
        {
            // The helper resets left/right padding to 0, so this pins down whether each widget re-applies its own
            // padding after the call (i.e. whether the replacement has left the appearance looking thinner)
            VisualElement numberInput = new NumberInput().Q("unity-text-input");
            VisualElement stringInput = new StringInput().Q("unity-text-input");

            Assert.AreEqual(4f, numberInput.style.paddingLeft.value.value);
            Assert.AreEqual(4f, numberInput.style.paddingRight.value.value);
            Assert.AreEqual(6f, stringInput.style.paddingLeft.value.value);
            Assert.AreEqual(6f, stringInput.style.paddingRight.value.value);
        }

        [Test]
        public void AdoptingWidgets_KeepTheirOwnTextAlignment()
        {
            VisualElement numberInput = new NumberInput().Q("unity-text-input");
            VisualElement stringInput =
                new StringInput { Align = TweeqTextAlign.Right }.Q("unity-text-input");

            Assert.AreEqual(TextAnchor.MiddleCenter, numberInput.style.unityTextAlign.value);
            Assert.AreEqual(TextAnchor.MiddleRight, stringInput.style.unityTextAlign.value);
        }

        #endregion
    }
}
