using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// 外部 asmdef 向けに公開したクローム API（ext-custom-widgets-spec.md EXT-01-A）の検証。
    ///
    /// 抽出元は NumberInput なので、角丸表そのものに加えて
    /// 「NumberInput / StringInput の見た目が抽出前後で変わっていないこと」も併せて確かめる。
    /// VisualElement は panel が無くても style を設定できるので EditMode で完結する。
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
            // 横 Start（右を潰す）と縦 End（上を潰す）の合成は左下だけが丸く残る
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
            // クロームの抽出はこの 2 つにだけ入れたので、両者がずれていないことを固定する
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
    }
}
