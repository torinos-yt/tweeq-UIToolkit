using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies the TweeqFonts loading path.
    ///
    /// Since this runs in the Editor, actual assets can be read from Resources. The two things to
    /// confirm here are: "the bundled TTF is imported as a Font and can be retrieved via Resources.Load"
    /// and "a missing font falls back to an empty FontDefinition instead of throwing".
    /// Actual glyph shapes and kerning are the responsibility of visual inspection (the requester's uloop screenshots).
    /// </summary>
    public class TweeqFontsTests
    {
        [SetUp]
        public void SetUp()
        {
            // Reset every time, because the resolved flag would otherwise carry over if other tests rely on ResetCache having run
            TweeqFonts.ResetCache();
        }

        #region Load success

        [Test]
        public void GeistRegular_LoadsFromResources()
        {
            FontDefinition definition = TweeqFonts.GeistRegular;

            Assert.IsFalse(TweeqFonts.IsEmpty(definition), "Geist-Regular が Resources から読めていない");
            Assert.IsNotNull(definition.font);
            Assert.AreEqual("Geist-Regular", definition.font.name);
        }

        [Test]
        public void GeistSemiBold_LoadsFromResources()
        {
            FontDefinition definition = TweeqFonts.GeistSemiBold;

            Assert.IsFalse(TweeqFonts.IsEmpty(definition), "Geist-SemiBold が Resources から読めていない");
            Assert.AreEqual("Geist-SemiBold", definition.font.name);
        }

        [Test]
        public void GeistMonoRegular_LoadsFromResources()
        {
            FontDefinition definition = TweeqFonts.GeistMonoRegular;

            Assert.IsFalse(TweeqFonts.IsEmpty(definition), "GeistMono-Regular が Resources から読めていない");
            Assert.AreEqual("GeistMono-Regular", definition.font.name);
        }

        [Test]
        public void IsAvailable_IsTrueWhenAllWeightsBundled()
        {
            Assert.IsTrue(TweeqFonts.IsAvailable);
            Assert.IsTrue(TweeqFonts.IsNumericFontAvailable);
            Assert.IsTrue(TweeqFonts.IsHeadingFontAvailable);
            Assert.IsTrue(TweeqFonts.IsCodeFontAvailable);
        }

        [Test]
        public void SecondAccess_ReturnsCachedInstance()
        {
            Font first = TweeqFonts.GeistRegular.font;
            Font second = TweeqFonts.GeistRegular.font;

            Assert.AreSame(first, second);
        }

        [Test]
        public void Preload_DoesNotThrowAndResolvesAll()
        {
            Assert.DoesNotThrow(TweeqFonts.Preload);
            Assert.IsTrue(TweeqFonts.IsAvailable);
        }

        [Test]
        public void ResetCache_ReloadsWithoutError()
        {
            Assert.IsTrue(TweeqFonts.IsAvailable);
            TweeqFonts.ResetCache();
            Assert.IsTrue(TweeqFonts.IsAvailable, "ResetCache 後も再ロードできること");
        }

        #endregion

        #region Semantic mapping

        [Test]
        public void SemanticFonts_MapToExpectedWeights()
        {
            Assert.AreEqual(TweeqFonts.GeistRegular.font, TweeqFonts.NumericFont.font);
            Assert.AreEqual(TweeqFonts.GeistSemiBold.font, TweeqFonts.HeadingFont.font);
            Assert.AreEqual(TweeqFonts.GeistMonoRegular.font, TweeqFonts.CodeFont.font);
        }

        [Test]
        public void UiFont_IsEmptyBecauseUpstreamUsesSystemUi()
        {
            // In the Vue original, fontUi=system-ui. In UI Toolkit, "not specifying" corresponds to adopting the default font
            Assert.IsTrue(TweeqFonts.IsEmpty(TweeqFonts.UiFont));
        }

        #endregion

        #region Fallback

        [Test]
        public void LoadFontDefinition_MissingPath_ReturnsDefaultWithoutThrowing()
        {
            FontDefinition definition = default;

            Assert.DoesNotThrow(() =>
                definition = TweeqFonts.LoadFontDefinition("Tweeq/ThisFontDoesNotExist"));

            Assert.IsTrue(TweeqFonts.IsEmpty(definition));
            Assert.AreEqual(default(FontDefinition), definition);
        }

        [Test]
        public void LoadFont_NullOrEmptyPath_ReturnsNullWithoutThrowing()
        {
            Assert.IsNull(TweeqFonts.LoadFont(null));
            Assert.IsNull(TweeqFonts.LoadFont(string.Empty));
        }

        [Test]
        public void IsEmpty_DefaultDefinition_IsTrue()
        {
            Assert.IsTrue(TweeqFonts.IsEmpty(default));
        }

        #endregion

        #region Apply

        [Test]
        public void Apply_SetsInlineFontDefinition()
        {
            VisualElement element = new VisualElement();

            TweeqFonts.Apply(element, TweeqFonts.CodeFont);

            Assert.AreEqual(TweeqFonts.CodeFont.font, element.style.unityFontDefinition.value.font);
        }

        [Test]
        public void Apply_EmptyDefinition_ClearsInlineOverride()
        {
            VisualElement element = new VisualElement();
            TweeqFonts.Apply(element, TweeqFonts.CodeFont);

            TweeqFonts.Apply(element, default);

            Assert.IsTrue(TweeqFonts.IsEmpty(element.style.unityFontDefinition.value),
                "空の定義を渡したらインライン指定が外れて既定へ戻ること");
        }

        [Test]
        public void Apply_NullElement_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => TweeqFonts.Apply(null, TweeqFonts.CodeFont));
        }

        #endregion
    }
}
