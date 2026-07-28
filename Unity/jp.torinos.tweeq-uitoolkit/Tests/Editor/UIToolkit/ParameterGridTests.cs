using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Contract verification for the ParameterGrid family (spec §7-5 and others).
    /// A VisualElement can be created and styled without a panel, so this is fully covered in EditMode.
    /// However, actual text width measurement (MeasureTextSize) returns 0 outside a panel,
    /// so shared label width tests only cover distribution down to the 60px minimum.
    /// </summary>
    public class ParameterGridTests
    {
        // Uses a dedicated key so it doesn't collide with other tests or real project settings
        const string TEST_GROUP_NAME = "tweeq.tests.parameterGridTests.group";

        string _prefsKey;

        [SetUp]
        public void SetUp()
        {
            _prefsKey = ParameterGroup.PrefsKey(TEST_GROUP_NAME);
            PlayerPrefs.DeleteKey(_prefsKey);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(_prefsKey);
            PlayerPrefs.Save();
        }

        static Label LabelOf(Parameter parameter)
        {
            return parameter.Q<Label>(className: Parameter.LABEL_USS_CLASS_NAME);
        }

        #region Parameter

        [Test]
        public void ParameterLabelIsReflectedInLabelElement()
        {
            Parameter parameter = new Parameter("Opacity");

            Label label = LabelOf(parameter);
            Assert.That(label, Is.Not.Null);
            Assert.That(label.text, Is.EqualTo("Opacity"));

            parameter.Label = "Rotation";
            Assert.That(label.text, Is.EqualTo("Rotation"));
            Assert.That(parameter.Label, Is.EqualTo("Rotation"));
        }

        [Test]
        public void ParameterLabelNullBecomesEmpty()
        {
            Parameter parameter = new Parameter("Opacity");
            parameter.Label = null;

            Assert.That(parameter.Label, Is.EqualTo(string.Empty));
            Assert.That(LabelOf(parameter).text, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ParameterInputContainerReceivesChildren()
        {
            Parameter parameter = new Parameter("Position");
            NumberInput input = new NumberInput();
            parameter.InputContainer.Add(input);

            Assert.That(parameter.InputContainer.childCount, Is.EqualTo(1));
            Assert.That(input.parent, Is.SameAs(parameter.InputContainer));
        }

        #endregion

        #region ParameterGrid

        [Test]
        public void GridDistributesSharedLabelWidthToEveryRow()
        {
            ParameterGrid grid = new ParameterGrid();
            Parameter first = new Parameter("A");
            Parameter second = new Parameter("Long label");
            grid.Add(first);
            grid.Add(second);

            grid.Refresh();

            // Actual measurement doesn't work outside a panel, so it's enough that the 60px minimum is distributed to every row
            Assert.That(LabelOf(first).style.width.value.value,
                Is.EqualTo(ParameterGrid.MIN_LABEL_WIDTH).Within(0.01f));
            Assert.That(LabelOf(second).style.width.value.value,
                Is.EqualTo(ParameterGrid.MIN_LABEL_WIDTH).Within(0.01f));
        }

        [Test]
        public void GridReachesParametersInsideCollapsibleGroup()
        {
            ParameterGrid grid = new ParameterGrid();
            ParameterGroup group = new ParameterGroup { Label = "Vector" };
            Parameter nested = new Parameter("Position");
            group.Content.Add(nested);
            grid.Add(group);

            grid.Refresh();

            // Spec §5-6: a Parameter inside a group also gets its width from the same Grid
            Assert.That(LabelOf(nested).style.width.value.value,
                Is.EqualTo(ParameterGrid.MIN_LABEL_WIDTH).Within(0.01f));
        }

        [Test]
        public void GridFindReturnsNearestAncestorGrid()
        {
            ParameterGrid grid = new ParameterGrid();
            ParameterGroup group = new ParameterGroup { Label = "Vector" };
            Parameter nested = new Parameter("Position");
            group.Content.Add(nested);
            grid.Add(group);

            grid.Theme = TweeqTheme.Light();

            // Theme propagates down to Parameters inside a group via the Grid's Refresh path
            Assert.That(nested.Theme, Is.SameAs(grid.Theme));
        }

        #endregion

        #region ParameterHeading

        [Test]
        public void HeadingTextIsReflectedInTextElement()
        {
            ParameterHeading heading = new ParameterHeading("InputNumber");

            Label text = heading.Q<Label>(className: ParameterHeading.TEXT_USS_CLASS_NAME);
            Assert.That(text, Is.Not.Null);
            Assert.That(text.text, Is.EqualTo("InputNumber"));

            heading.Text = "Vector";
            Assert.That(text.text, Is.EqualTo("Vector"));
        }

        [Test]
        public void HeadingAppliesThemeHeadingFontAndDropsFauxBold()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            Assume.That(
                TweeqFonts.IsEmpty(theme.FontHeading),
                Is.False,
                "既定テーマの FontHeading が空。同梱フォントが Resources から読めていない");

            ParameterHeading heading = new ParameterHeading("Vector");
            heading.Theme = theme;

            VisualElement text = heading.TextElement;
            Assert.That(
                text.style.unityFontDefinition.value.font,
                Is.SameAs(theme.FontHeading.font));

            // The actual font is SemiBold, so no faux bold is layered on top
            Assert.That(
                text.style.unityFontStyleAndWeight.value,
                Is.EqualTo(FontStyle.Normal));
        }

        [Test]
        public void HeadingKeepsBoldWhenHeadingFontIsUnavailable()
        {
            TweeqTheme theme = TweeqTheme.Dark();
            theme.FontHeading = default;

            ParameterHeading heading = new ParameterHeading("Vector");
            heading.Theme = theme;

            VisualElement text = heading.TextElement;

            // When falling back to the default font, faux bold is the only way to make it look bold
            Assert.That(TweeqFonts.IsEmpty(text.style.unityFontDefinition.value), Is.True);
            Assert.That(
                text.style.unityFontStyleAndWeight.value,
                Is.EqualTo(FontStyle.Bold));
        }

        #endregion

        #region ParameterGroup

        [Test]
        public void GroupDefaultsToExpandedWhenNothingPersisted()
        {
            ParameterGroup group = new ParameterGroup(TEST_GROUP_NAME, "Vector");

            Assert.That(group.Expanded, Is.True);
            Assert.That(PlayerPrefs.HasKey(_prefsKey), Is.False);
        }

        [Test]
        public void GroupExpandedTogglesAndPersists()
        {
            ParameterGroup group = new ParameterGroup(TEST_GROUP_NAME, "Vector");

            group.Expanded = false;
            Assert.That(group.Expanded, Is.False);
            Assert.That(PlayerPrefs.GetInt(_prefsKey, 1), Is.EqualTo(0));

            group.Expanded = true;
            Assert.That(group.Expanded, Is.True);
            Assert.That(PlayerPrefs.GetInt(_prefsKey, 0), Is.EqualTo(1));
        }

        [Test]
        public void GroupRestoresPersistedExpandedState()
        {
            ParameterGroup saved = new ParameterGroup(TEST_GROUP_NAME, "Vector");
            saved.Expanded = false;

            ParameterGroup restored = new ParameterGroup(TEST_GROUP_NAME, "Vector");
            Assert.That(restored.Expanded, Is.False);

            restored.Expanded = true;

            ParameterGroup reRestored = new ParameterGroup(TEST_GROUP_NAME, "Vector");
            Assert.That(reRestored.Expanded, Is.True);
        }

        [Test]
        public void GroupWithoutNameIsNotPersisted()
        {
            ParameterGroup group = new ParameterGroup { Label = "Vector" };
            group.Expanded = false;

            Assert.That(group.Expanded, Is.False);
            Assert.That(ParameterGroup.PrefsKey(string.Empty), Is.EqualTo(string.Empty));
            Assert.That(PlayerPrefs.HasKey(_prefsKey), Is.False);
        }

        [Test]
        public void GroupContentIsClippedOnlyWhileCollapsed()
        {
            ParameterGroup group = new ParameterGroup(TEST_GROUP_NAME, "Vector");
            VisualElement clip = group.Content.parent;
            Assert.That(clip, Is.Not.Null);

            Assert.That(clip.style.overflow.value, Is.EqualTo(Overflow.Visible));

            group.Expanded = false;

            // No animation runs outside a panel, so the closed state is applied immediately
            Assert.That(clip.style.overflow.value, Is.EqualTo(Overflow.Hidden));
            Assert.That(clip.style.maxHeight.value.value, Is.EqualTo(0f).Within(0.01f));
        }

        #endregion
    }
}
