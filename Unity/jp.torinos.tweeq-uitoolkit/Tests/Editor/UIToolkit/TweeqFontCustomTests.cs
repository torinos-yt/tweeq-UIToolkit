using System.Collections.Generic;
using NUnit.Framework;
using Tweeq.UIToolkit.TestSupport;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Contract for user-supplied fonts (font-custom-spec.md): the FontUi token actually reaches
    /// every piece of text that isn't already claimed by FontNumeric / FontHeading / FontCode, and
    /// TweeqRoot's USS font seeds resolve into the theme.
    ///
    /// The default FontUi is empty, so these also pin down that widgets leave
    /// unityFontDefinition unspecified when nothing was asked for — that is what makes the
    /// no-custom-font case identical to before the wiring existed.
    /// </summary>
    public class TweeqFontCustomTests
    {
        #region Helpers

        /// <summary>
        /// A stand-in for a resolved USS block. Only the Object-typed lookups matter here; the rest
        /// exist because ICustomStyle demands them.
        /// </summary>
        sealed class FakeCustomStyle : ICustomStyle
        {
            readonly Dictionary<string, Object> _objects = new Dictionary<string, Object>();

            public void Set(string propertyName, Object value)
            {
                _objects[propertyName] = value;
            }

            // The asset-typed lookup TweeqRoot uses goes through this generic overload
            public bool TryGetValue<T>(CustomStyleProperty<T> property, out T value)
                where T : Object
            {
                if (_objects.TryGetValue(property.name, out Object found) && found is T typed)
                {
                    value = typed;
                    return true;
                }

                value = null;
                return false;
            }

            public bool TryGetValue(CustomStyleProperty<float> property, out float value)
            {
                value = default;
                return false;
            }

            public bool TryGetValue(CustomStyleProperty<int> property, out int value)
            {
                value = default;
                return false;
            }

            public bool TryGetValue(CustomStyleProperty<bool> property, out bool value)
            {
                value = default;
                return false;
            }

            public bool TryGetValue(CustomStyleProperty<Color> property, out Color value)
            {
                value = default;
                return false;
            }

            public bool TryGetValue(CustomStyleProperty<Texture2D> property, out Texture2D value)
            {
                value = null;
                return false;
            }

            public bool TryGetValue(CustomStyleProperty<Sprite> property, out Sprite value)
            {
                value = null;
                return false;
            }

            public bool TryGetValue(CustomStyleProperty<VectorImage> property, out VectorImage value)
            {
                value = null;
                return false;
            }

            public bool TryGetValue(CustomStyleProperty<string> property, out string value)
            {
                value = null;
                return false;
            }
        }

        /// <summary>
        /// A font that is definitely not the panel default, so "was it applied" is observable.
        /// The bundled Geist is reused rather than generating one, since a runtime-built Font has
        /// no glyphs and some import paths return null.
        /// </summary>
        static FontDefinition CustomFont()
        {
            FontDefinition definition = TweeqFonts.GeistMonoRegular;
            if (TweeqFonts.IsEmpty(definition))
            {
                Assert.Ignore("the bundled Geist is unavailable, so there is no font to swap in");
            }

            return definition;
        }

        static TweeqTheme ThemeWithUiFont()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            theme.FontUi = CustomFont();
            return theme;
        }

        static void AssertFont(VisualElement element, FontDefinition expected, string what)
        {
            Assert.IsNotNull(element, $"{what}: the element under test was not found");
            Assert.AreEqual(
                expected.font, element.style.unityFontDefinition.value.font, $"{what}: font");
            Assert.AreEqual(
                expected.fontAsset,
                element.style.unityFontDefinition.value.fontAsset,
                $"{what}: fontAsset");
        }

        static void AssertNoFont(VisualElement element, string what)
        {
            Assert.IsNotNull(element, $"{what}: the element under test was not found");
            Assert.IsTrue(
                TweeqFonts.IsEmpty(element.style.unityFontDefinition.value),
                $"{what}: must stay unspecified so the panel default applies");
        }

        #endregion

        #region FontUi wiring

        [Test]
        public void ButtonInput_AppliesFontUiToItsLabel()
        {
            ButtonInput button = new ButtonInput { Label = "Render" };

            button.Theme = ThemeWithUiFont();

            AssertFont(button.Q<Label>(), CustomFont(), "ButtonInput label");
        }

        [Test]
        public void ButtonInput_DefaultTheme_LeavesTheLabelOnThePanelDefault()
        {
            ButtonInput button = new ButtonInput { Label = "Render" };

            button.Theme = TweeqTheme.Dark();

            AssertNoFont(button.Q<Label>(), "ButtonInput label");
        }

        [Test]
        public void ButtonInput_RevertingToAnEmptyFontUiClearsTheOverride()
        {
            ButtonInput button = new ButtonInput { Label = "Render" };
            button.Theme = ThemeWithUiFont();

            // Assigning a theme whose FontUi went back to empty has to remove the override,
            // otherwise the previous custom font would be stuck on the element forever
            button.Theme = TweeqTheme.Dark();

            AssertNoFont(button.Q<Label>(), "ButtonInput label after revert");
        }

        [Test]
        public void ButtonToggleInput_AppliesFontUiToItsLabel()
        {
            ButtonToggleInput toggle = new ButtonToggleInput { Label = "Solo" };

            toggle.Theme = ThemeWithUiFont();

            AssertFont(toggle.Q<Label>(), CustomFont(), "ButtonToggleInput label");
        }

        [Test]
        public void CheckboxInput_AppliesFontUiToItsLabel()
        {
            CheckboxInput checkbox = new CheckboxInput { Label = "Visible" };

            checkbox.Theme = ThemeWithUiFont();

            AssertFont(checkbox.Q<Label>("tweeq-checkbox-label"), CustomFont(), "CheckboxInput label");
        }

        [Test]
        public void SwitchInput_AppliesFontUiToItsLabel()
        {
            SwitchInput toggle = new SwitchInput { Label = "Loop" };

            toggle.Theme = ThemeWithUiFont();

            AssertFont(toggle.Q<Label>("tweeq-switch-label"), CustomFont(), "SwitchInput label");
        }

        [Test]
        public void RadioInput_AppliesFontUiToEverySegment()
        {
            RadioInput radio = new RadioInput { Options = new[] { "Low", "Mid", "High" } };

            radio.Theme = ThemeWithUiFont();

            List<Label> segments = new List<Label>(radio.Query<Label>().ToList());
            Assert.AreEqual(3, segments.Count, "one label per option");
            for (int index = 0; index < segments.Count; index++)
            {
                AssertFont(segments[index], CustomFont(), $"RadioInput segment {index}");
            }
        }

        [Test]
        public void Parameter_AppliesFontUiToItsLabel()
        {
            Parameter parameter = new Parameter { Label = "Opacity" };

            parameter.Theme = ThemeWithUiFont();

            AssertFont(
                parameter.Q<Label>(className: Parameter.LABEL_USS_CLASS_NAME),
                CustomFont(),
                "Parameter label");
        }

        [Test]
        public void StringInput_AppliesFontUiToTheFieldRoot()
        {
            StringInput field = new StringInput();

            field.Theme = ThemeWithUiFont();

            // unityFontDefinition is inherited, so the root is what the inner TextField reads
            AssertFont(field, CustomFont(), "StringInput root");
        }

        [Test]
        public void StringInput_DefaultTheme_LeavesTheFieldOnThePanelDefault()
        {
            StringInput field = new StringInput();

            field.Theme = TweeqTheme.Dark();

            AssertNoFont(field, "StringInput root");
        }

        [Test]
        public void DropdownInput_AppliesFontUiToTheFieldLabelAndTheOptionRows()
        {
            using (TweeqRuntimeTestPanel panel = TweeqRuntimeTestPanel.Create())
            {
                StringDropdownInput dropdown = new StringDropdownInput
                {
                    Options = new[] { "Linear", "Ease", "Step" },
                };
                panel.Root.Add(dropdown);
                dropdown.Open();

                dropdown.Theme = ThemeWithUiFont();

                AssertFont(
                    dropdown.Q<Label>("tweeq-dropdown-label"), CustomFont(), "Dropdown field label");

                // Popups live on the overlay layer, which hangs off the panel's own root rather than
                // the document root the widget was added to
                List<Label> rows = new List<Label>(
                    panel.Root.panel.visualTree.Query<Label>("tweeq-dropdown-option").ToList());
                Assert.IsNotEmpty(rows, "the popup rows could not be reached");
                for (int index = 0; index < rows.Count; index++)
                {
                    AssertFont(rows[index], CustomFont(), $"Dropdown row {index}");
                }
            }
        }

        [Test]
        public void DropdownInput_AppliesFontUiToTheFilterField()
        {
            StringDropdownInput dropdown = new StringDropdownInput
            {
                Options = new[] { "Linear", "Ease", "Step" },
            };
            dropdown.Open();
            dropdown.BeginFilter("ea");

            dropdown.Theme = ThemeWithUiFont();

            AssertFont(
                dropdown.Q<TextField>("tweeq-dropdown-filter"), CustomFont(), "Dropdown filter field");
        }

        [Test]
        public void TweeqTabs_AppliesFontUiToTheHeaderLabels()
        {
            TweeqTabs tabs = new TweeqTabs("tweeq.tests.fontCustom.tabs");
            TweeqTab first = new TweeqTab("First");
            tabs.Add(first);
            first.ConnectToTabs();

            tabs.Theme = ThemeWithUiFont();

            AssertFont(
                tabs.GetHeader(0).Q<Label>("tweeq-tabs-header-label"),
                CustomFont(),
                "TweeqTabs header label");
        }

        [Test]
        public void NumberInput_AppliesFontUiToTheUnitWordsAndTheLeftLabel()
        {
            NumberInput number = new NumberInput { Prefix = "x", Suffix = "px", LeftLabel = "X" };

            number.Theme = ThemeWithUiFont();

            VisualElement overlay = number.Q("tweeq-number-display");
            Assert.IsNotNull(overlay, "the display overlay could not be reached");

            AssertFont(overlay.hierarchy.ElementAt(0), CustomFont(), "NumberInput prefix");
            AssertFont(overlay.hierarchy.ElementAt(2), CustomFont(), "NumberInput suffix");
            AssertFont(
                number.Q<Label>("tweeq-number-left-label"), CustomFont(), "NumberInput left label");
        }

        #endregion

        #region Numeric / code boundary

        [Test]
        public void NumberInput_FontUiDoesNotReachTheDigits()
        {
            TweeqTheme theme = ThemeWithUiFont();
            NumberInput number = new NumberInput();

            number.Theme = theme;

            VisualElement overlay = number.Q("tweeq-number-display");
            AssertFont(overlay.hierarchy.ElementAt(1), theme.FontNumeric, "NumberInput value label");
            AssertFont(number.Q<TextField>(), theme.FontNumeric, "NumberInput text field");
        }

        [Test]
        public void ParameterHeading_FontUiDoesNotReachTheHeadingText()
        {
            TweeqTheme theme = ThemeWithUiFont();
            ParameterHeading heading = new ParameterHeading { Text = "Transform" };

            heading.Theme = theme;

            AssertFont(heading.TextElement, theme.FontHeading, "ParameterHeading text");
        }

        [Test]
        public void ColorInput_FontUiDoesNotReachTheHexField()
        {
            using (TweeqRuntimeTestPanel panel = TweeqRuntimeTestPanel.Create())
            {
                TweeqTheme theme = ThemeWithUiFont();
                ColorInput color = new ColorInput();
                panel.Root.Add(color);
                color.OpenPicker();

                // The HEX field only joins the value row in the hex color space
                color.ColorSpace = ColorInput.COLOR_SPACE_HEX;

                color.Theme = theme;

                // The HEX field is a StringInput, i.e. it is on the FontUi path by default;
                // ColorInput overriding it with FontCode has to survive the wiring
                StringInput hex = panel.Root.panel.visualTree.Q<StringInput>();
                AssertFont(hex, theme.FontCode, "ColorInput HEX field");
            }
        }

        #endregion

        #region USS seeds

        [Test]
        public void TryCreateFontDefinition_LegacyFont_UsesFromFont()
        {
            Font font = TweeqFonts.LoadFont(TweeqFonts.GEIST_REGULAR_PATH);
            if (font == null)
            {
                Assert.Ignore("the bundled Geist is unavailable");
            }

            Assert.IsTrue(TweeqRoot.TryCreateFontDefinition(font, out FontDefinition definition));
            Assert.AreEqual(font, definition.font);
            Assert.IsNull(definition.fontAsset);
        }

        [Test]
        public void TryCreateFontDefinition_FontAsset_UsesFromSdfFont()
        {
            Font font = TweeqFonts.LoadFont(TweeqFonts.GEIST_REGULAR_PATH);
            if (font == null)
            {
                Assert.Ignore("the bundled Geist is unavailable");
            }

            UnityEngine.TextCore.Text.FontAsset asset =
                UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(font);
            if (asset == null)
            {
                Assert.Ignore("a FontAsset could not be generated in this environment");
            }

            try
            {
                Assert.IsTrue(TweeqRoot.TryCreateFontDefinition(asset, out FontDefinition definition));
                Assert.AreEqual(asset, definition.fontAsset);
                Assert.IsNull(definition.font);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void TryCreateFontDefinition_Null_IsIgnored()
        {
            Assert.IsFalse(TweeqRoot.TryCreateFontDefinition(null, out FontDefinition definition));
            Assert.IsTrue(TweeqFonts.IsEmpty(definition));
        }

        [Test]
        public void TryCreateFontDefinition_WrongAssetType_IsIgnored()
        {
            Texture2D texture = new Texture2D(1, 1) { name = "not-a-font" };

            try
            {
                // A warning is logged alongside this; the contract that matters is that the seed is
                // dropped rather than throwing, so the theme's own token survives
                Assert.IsFalse(
                    TweeqRoot.TryCreateFontDefinition(texture, out FontDefinition definition));
                Assert.IsTrue(TweeqFonts.IsEmpty(definition));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ApplyFontSeeds_OverridesOnlyTheTokensThatWereSpecified()
        {
            Font font = TweeqFonts.LoadFont(TweeqFonts.GEIST_MONO_REGULAR_PATH);
            if (font == null)
            {
                Assert.Ignore("the bundled Geist is unavailable");
            }

            TweeqTheme theme = TweeqTheme.Dark();
            FontDefinition headingBefore = theme.FontHeading;

            FakeCustomStyle style = new FakeCustomStyle();
            style.Set(TweeqRoot.FONT_UI_PROPERTY_NAME, font);
            style.Set(TweeqRoot.FONT_CODE_PROPERTY_NAME, font);

            TweeqRoot.ApplyFontSeeds(style, theme);

            Assert.AreEqual(font, theme.FontUi.font, "--tq-font-ui");
            Assert.AreEqual(font, theme.FontCode.font, "--tq-font-code");
            Assert.AreEqual(
                headingBefore.font,
                theme.FontHeading.font,
                "an unspecified token keeps the theme's own value");
            Assert.AreEqual(TweeqFonts.NumericFont.font, theme.FontNumeric.font, "--tq-font-numeric");
        }

        [Test]
        public void ApplyFontSeeds_NoSeeds_LeavesTheDefaultsAlone()
        {
            TweeqTheme theme = TweeqTheme.Dark();

            TweeqRoot.ApplyFontSeeds(new FakeCustomStyle(), theme);

            Assert.IsTrue(TweeqFonts.IsEmpty(theme.FontUi), "FontUi stays empty (panel default)");
            Assert.AreEqual(TweeqFonts.NumericFont.font, theme.FontNumeric.font);
            Assert.AreEqual(TweeqFonts.HeadingFont.font, theme.FontHeading.font);
            Assert.AreEqual(TweeqFonts.CodeFont.font, theme.FontCode.font);
        }

        [Test]
        public void ApplyFontSeeds_SeededThemeReachesTheWidgets()
        {
            Font font = TweeqFonts.LoadFont(TweeqFonts.GEIST_MONO_REGULAR_PATH);
            if (font == null)
            {
                Assert.Ignore("the bundled Geist is unavailable");
            }

            TweeqTheme theme = TweeqTheme.Dark();
            FakeCustomStyle style = new FakeCustomStyle();
            style.Set(TweeqRoot.FONT_UI_PROPERTY_NAME, font);
            TweeqRoot.ApplyFontSeeds(style, theme);

            TweeqRoot root = new TweeqRoot();
            ButtonInput button = new ButtonInput { Label = "Render" };
            root.Add(button);

            root.Theme = theme;

            AssertFont(button.Q<Label>(), FontDefinition.FromFont(font), "ButtonInput under a seeded root");
        }

        [Test]
        public void ApplyFontSeeds_NullTheme_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => TweeqRoot.ApplyFontSeeds(new FakeCustomStyle(), null));
            Assert.DoesNotThrow(() => TweeqRoot.ApplyFontSeeds(null, TweeqTheme.Dark()));
            Assert.DoesNotThrow(() => TweeqRoot.ApplyFontSeeds(null, null, null, null, null));
        }

        [Test]
        public void FontSeedPropertyNames_MatchTheDocumentedTokens()
        {
            Assert.AreEqual("--tq-font-ui", TweeqRoot.FONT_UI_PROPERTY_NAME);
            Assert.AreEqual("--tq-font-numeric", TweeqRoot.FONT_NUMERIC_PROPERTY_NAME);
            Assert.AreEqual("--tq-font-heading", TweeqRoot.FONT_HEADING_PROPERTY_NAME);
            Assert.AreEqual("--tq-font-code", TweeqRoot.FONT_CODE_PROPERTY_NAME);
        }

        #endregion
    }
}
