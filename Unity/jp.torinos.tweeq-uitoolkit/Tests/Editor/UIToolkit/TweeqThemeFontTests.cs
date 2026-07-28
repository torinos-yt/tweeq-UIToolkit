using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// TweeqTheme のフォントトークン既定値。どの生成経路でも同梱 Geist が乗ること、
    /// FontUi だけは「指定しない（＝パネル既定）」であることを固定する。
    /// </summary>
    public class TweeqThemeFontTests
    {
        static void AssertBundledDefaults(TweeqTheme theme, string path)
        {
            Assert.IsTrue(TweeqFonts.IsEmpty(theme.FontUi), $"{path}: FontUi は既定で空");
            Assert.AreEqual(TweeqFonts.NumericFont.font, theme.FontNumeric.font, $"{path}: FontNumeric");
            Assert.AreEqual(TweeqFonts.HeadingFont.font, theme.FontHeading.font, $"{path}: FontHeading");
            Assert.AreEqual(TweeqFonts.CodeFont.font, theme.FontCode.font, $"{path}: FontCode");
        }

        #region Defaults

        [Test]
        public void Dark_HasBundledFontDefaults()
        {
            AssertBundledDefaults(TweeqTheme.Dark(), "Dark()");
        }

        [Test]
        public void Light_HasBundledFontDefaults()
        {
            AssertBundledDefaults(TweeqTheme.Light(), "Light()");
        }

        [Test]
        public void FromSeeds_HasBundledFontDefaults()
        {
            TweeqTheme theme = TweeqTheme.FromSeeds(
                ColorMode.Light,
                TweeqTheme.DEFAULT_LIGHT_BACKGROUND,
                TweeqTheme.DEFAULT_ACCENT,
                TweeqTheme.DEFAULT_GRAY);

            AssertBundledDefaults(theme, "FromSeeds()");
        }

        [Test]
        public void PlainConstructor_HasBundledFontDefaults()
        {
            // 既存コードが素の new TweeqTheme() を使う箇所があるので、そこでも欠けないこと
            AssertBundledDefaults(new TweeqTheme(), "new TweeqTheme()");
        }

        #endregion

        #region Propagation

        [Test]
        public void Copy_PreservesFontTokens()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            theme.FontUi = TweeqFonts.GeistRegular;

            TweeqTheme copy = theme.Copy();

            // FontUi は上書きした値ごと引き継ぐ（＝既定の空へ戻らない）
            Assert.AreEqual(TweeqFonts.GeistRegular.font, copy.FontUi.font);
            Assert.AreEqual(TweeqFonts.NumericFont.font, copy.FontNumeric.font);
            Assert.AreEqual(TweeqFonts.HeadingFont.font, copy.FontHeading.font);
            Assert.AreEqual(TweeqFonts.CodeFont.font, copy.FontCode.font);
        }

        [Test]
        public void WithAccent_PreservesFontTokens()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            theme.FontCode = default;

            TweeqTheme derived = theme.WithAccent(TweeqTheme.DEFAULT_GRAY);

            Assert.IsTrue(TweeqFonts.IsEmpty(derived.FontCode), "明示的に外した指定も引き継ぐこと");
            Assert.AreEqual(TweeqFonts.NumericFont.font, derived.FontNumeric.font);
        }

        [Test]
        public void FontToken_CanBeClearedToFallBackToPanelDefault()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            theme.FontNumeric = default;

            VisualElement element = new VisualElement();
            TweeqFonts.Apply(element, theme.FontNumeric);

            Assert.IsTrue(TweeqFonts.IsEmpty(element.style.unityFontDefinition.value));
        }

        #endregion
    }
}
