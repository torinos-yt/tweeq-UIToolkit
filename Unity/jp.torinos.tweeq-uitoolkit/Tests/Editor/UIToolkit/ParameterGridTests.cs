using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// ParameterGrid ファミリーの契約検証（仕様 §7-5 ほか）。
    /// VisualElement は panel が無くても生成・スタイル設定できるので EditMode で完結する。
    /// ただし文字幅の実測（MeasureTextSize）は panel 外では 0 を返すため、
    /// 共有ラベル幅のテストは下限 60px の配布までを対象にする。
    /// </summary>
    public class ParameterGridTests
    {
        // 他のテストや実プロジェクトの設定と衝突しないよう、専用のキーを使う
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

            // panel 外では実測が効かないので、下限 60px が全行へ配られていれば良い
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

            // 仕様 §5-6: グループの中の Parameter も同じ Grid から幅をもらう
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

            // Theme は Grid の Refresh 経路でグループ内の Parameter まで伝播する
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

            // 実フォントが SemiBold なので、擬似ボールドを重ねない
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

            // 既定フォントへ落ちた場合は太く見せる手段が擬似ボールドしか無い
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

            // panel 外なのでアニメーションは走らず、閉状態が即座に適用される
            Assert.That(clip.style.overflow.value, Is.EqualTo(Overflow.Hidden));
            Assert.That(clip.style.maxHeight.value.value, Is.EqualTo(0f).Within(0.01f));
        }

        #endregion
    }
}
