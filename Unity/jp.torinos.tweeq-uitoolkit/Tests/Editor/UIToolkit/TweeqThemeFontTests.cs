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
            Assert.IsTrue(TweeqFonts.IsEmpty(theme.FontUi), $"{path}: FontUi is empty by default");
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

        [Test]
        public void PlainConstructor_HasDefaultMetricKnobs()
        {
            TweeqTheme theme = new TweeqTheme();

            Assert.AreEqual(24f, theme.InputHeight);
            Assert.AreEqual(12f, theme.FontSizeInput);
            Assert.AreEqual(11f, theme.FontSizeLabel);
            Assert.AreEqual(14f, theme.FontSizeHeading);
            Assert.AreEqual(9f, theme.FontSizeRuler);
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
        public void Copy_PreservesMetricKnobs()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            theme.InputHeight = 20f;
            theme.FontSizeInput = 11f;
            theme.FontSizeLabel = 10f;
            theme.FontSizeHeading = 13f;
            theme.FontSizeRuler = 8f;

            TweeqTheme copy = theme.Copy();

            Assert.AreEqual(theme.InputHeight, copy.InputHeight);
            Assert.AreEqual(theme.FontSizeInput, copy.FontSizeInput);
            Assert.AreEqual(theme.FontSizeLabel, copy.FontSizeLabel);
            Assert.AreEqual(theme.FontSizeHeading, copy.FontSizeHeading);
            Assert.AreEqual(theme.FontSizeRuler, copy.FontSizeRuler);
        }

        [Test]
        public void MetricKnobs_ApplyToNativeInputLabelsAndRuler()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            theme.InputHeight = 20f;
            theme.FontSizeInput = 11f;
            theme.FontSizeLabel = 10f;
            theme.FontSizeHeading = 13f;
            theme.FontSizeRuler = 8f;

            NumberInput input = new NumberInput { Theme = theme };
            Assert.AreEqual(20f, input.style.height.value.value);
            TextField textField = input.Q<TextField>();
            Assert.IsNotNull(textField);
            Assert.AreEqual(11f, textField.style.fontSize.value.value);

            Parameter parameter = new Parameter("Label") { Theme = theme };
            Label label = parameter.Q<Label>(className: Parameter.LABEL_USS_CLASS_NAME);
            Assert.IsNotNull(label);
            Assert.AreEqual(10f, label.style.fontSize.value.value);

            ParameterHeading heading = new ParameterHeading("Heading") { Theme = theme };
            Assert.AreEqual(13f, heading.TextElement.style.fontSize.value.value);

            TweeqRuler ruler = new TweeqRuler { Theme = theme };
            ruler.Scales = new[] { new RulerScale(0.0, "0") };
            Label rulerLabel = ruler.Q<Label>();
            Assert.IsNotNull(rulerLabel);
            Assert.AreEqual(8f, rulerLabel.style.fontSize.value.value);
        }

        [Test]
        public void WithAccent_PreservesFontTokens()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            theme.FontCode = default;

            TweeqTheme derived = theme.WithAccent(TweeqTheme.DEFAULT_GRAY);

            Assert.IsTrue(TweeqFonts.IsEmpty(derived.FontCode), "an explicitly cleared setting must also carry over");
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
