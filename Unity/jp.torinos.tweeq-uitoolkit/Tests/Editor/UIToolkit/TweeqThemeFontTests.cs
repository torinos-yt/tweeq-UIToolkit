using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Default values for TweeqTheme's font tokens. Pins down that the bundled Geist is applied
    /// regardless of the construction path, and that FontUi alone is left "unspecified (i.e. panel default)".
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
            // Existing code has spots that use a plain new TweeqTheme(), so nothing should be missing there either
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

            // FontUi carries over including the overridden value (i.e. it does not revert to the default empty)
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
