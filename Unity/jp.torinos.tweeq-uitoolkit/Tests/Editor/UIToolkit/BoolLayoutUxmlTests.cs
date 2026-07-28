using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Bool 系（Checkbox / Switch / Button / ButtonToggle / Radio）と
    /// レイアウト系（InputGroup / Parameter 系 / Popover / Balloon）の UXML 対応を検証する。
    ///
    /// UxmlSerializedData を直接叩くと生成コードの内部命名に依存するため、
    /// 実際に .uxml をインポートして Instantiate する経路で確かめる。
    /// VisualTreeAsset を文字列から作る公開 API が無いので、Assets 配下に一時ファイルを
    /// 書いて AssetDatabase 経由でインポートし、TearDown で消す。
    /// </summary>
    public class BoolLayoutUxmlTests
    {
        const string TEMP_FOLDER = "Assets/TweeqUxmlTests";
        const string TEMP_ASSET = TEMP_FOLDER + "/tweeq-uxml-test.uxml";

        // ParameterGroup の group-name を書くテストがあるので、PlayerPrefs を汚さないよう専用キーを使う
        const string TEST_GROUP_NAME = "tweeq.tests.boolLayoutUxmlTests.group";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TEMP_FOLDER))
            {
                AssetDatabase.DeleteAsset(TEMP_FOLDER);
            }

            PlayerPrefs.DeleteKey(ParameterGroup.PrefsKey(TEST_GROUP_NAME));
            PlayerPrefs.Save();
        }

        /// <summary>UXML の中身（要素だけ）を渡すと、実体化したツリーのルートを返す。</summary>
        static VisualElement Instantiate(string body)
        {
            if (!AssetDatabase.IsValidFolder(TEMP_FOLDER))
            {
                AssetDatabase.CreateFolder("Assets", "TweeqUxmlTests");
            }

            string document =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:tq=\"Tweeq.UIToolkit\">"
                + body
                + "</ui:UXML>";

            File.WriteAllText(TEMP_ASSET, document);
            AssetDatabase.ImportAsset(TEMP_ASSET, ImportAssetOptions.ForceSynchronousImport);

            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TEMP_ASSET);
            Assert.That(asset, Is.Not.Null, "一時 UXML をインポートできない");

            VisualElement root = asset.Instantiate();
            Assert.That(root, Is.Not.Null, "UXML を実体化できない");
            return root;
        }

        #region Bool inputs

        [Test]
        public void CheckboxAttributesAreAppliedFromUxml()
        {
            VisualElement root = Instantiate(
                "<tq:CheckboxInput label=\"Visible\" value=\"true\" disabled=\"true\" />");

            CheckboxInput checkbox = root.Q<CheckboxInput>();
            Assert.That(checkbox, Is.Not.Null, "CheckboxInput が UXML から解決できていない");
            Assert.That(checkbox.Label, Is.EqualTo("Visible"));
            Assert.That(checkbox.value, Is.True);
            Assert.That(checkbox.Disabled, Is.True);
            Assert.That(checkbox.pickingMode, Is.EqualTo(PickingMode.Ignore));
        }

        [Test]
        public void SwitchValueIsAppliedFromUxml()
        {
            VisualElement root = Instantiate("<tq:SwitchInput value=\"true\" label=\"Loop\" />");

            SwitchInput toggle = root.Q<SwitchInput>();
            Assert.That(toggle, Is.Not.Null, "SwitchInput が UXML から解決できていない");
            Assert.That(toggle.value, Is.True);
            Assert.That(toggle.Label, Is.EqualTo("Loop"));
        }

        [Test]
        public void ButtonAttributesAreAppliedFromUxml()
        {
            VisualElement root = Instantiate(
                "<tq:ButtonInput text=\"Render\" subtle=\"true\" narrow=\"true\" chevron=\"true\" />");

            ButtonInput button = root.Q<ButtonInput>();
            Assert.That(button, Is.Not.Null, "ButtonInput が UXML から解決できていない");

            // C# 側は Label、UXML 側は Vue の prop 名に合わせた text
            Assert.That(button.Label, Is.EqualTo("Render"));
            Assert.That(button.Subtle, Is.True);
            Assert.That(button.Narrow, Is.True);
            Assert.That(button.Chevron, Is.True);
            Assert.That(button.Disabled, Is.False);
        }

        [Test]
        public void ButtonToggleAttributesAreAppliedFromUxml()
        {
            VisualElement root = Instantiate(
                "<tq:ButtonToggleInput text=\"Solo\" value=\"true\" disabled=\"true\" />");

            ButtonToggleInput toggle = root.Q<ButtonToggleInput>();
            Assert.That(toggle, Is.Not.Null, "ButtonToggleInput が UXML から解決できていない");
            Assert.That(toggle.Label, Is.EqualTo("Solo"));
            Assert.That(toggle.value, Is.True);
            Assert.That(toggle.Disabled, Is.True);
        }

        [Test]
        public void RadioOptionsAreAppliedBeforeValue()
        {
            VisualElement root = Instantiate(
                "<tq:RadioInput options=\"Low,Mid,High\" value=\"2\" />");

            RadioInput radio = root.Q<RadioInput>();
            Assert.That(radio, Is.Not.Null, "RadioInput が UXML から解決できていない");

            // options が string[] としてカンマ区切りで読めること（読めなければ CSV 専用プロパティが必要）
            Assert.That(radio.Options, Is.EqualTo(new[] { "Low", "Mid", "High" }));

            // value は options の後に適用されないと範囲外として捨てられる
            Assert.That(radio.value, Is.EqualTo(2));
        }

        #endregion

        #region Layout

        [Test]
        public void InputGroupDirectionIsAppliedFromUxml()
        {
            VisualElement root = Instantiate(
                "<tq:InputGroup direction=\"Column\">"
                + "<tq:ButtonToggleInput text=\"A\" />"
                + "<tq:ButtonToggleInput text=\"B\" />"
                + "</tq:InputGroup>");

            InputGroup group = root.Q<InputGroup>();
            Assert.That(group, Is.Not.Null, "InputGroup が UXML から解決できていない");
            Assert.That(group.Direction, Is.EqualTo(FlexDirection.Column));
            Assert.That(group.childCount, Is.EqualTo(2));
        }

        [Test]
        public void ParameterLabelIsAppliedFromUxml()
        {
            VisualElement root = Instantiate("<tq:Parameter label=\"Opacity\" />");

            Parameter parameter = root.Q<Parameter>();
            Assert.That(parameter, Is.Not.Null, "Parameter が UXML から解決できていない");
            Assert.That(parameter.Label, Is.EqualTo("Opacity"));
        }

        [Test]
        public void ParameterGroupAttributesAreAppliedFromUxml()
        {
            VisualElement root = Instantiate(
                "<tq:ParameterGrid>"
                + "<tq:ParameterHeading text=\"Transform\" />"
                + "<tq:ParameterGroup group-name=\"" + TEST_GROUP_NAME
                + "\" heading-text=\"Vector\" expanded=\"false\" />"
                + "</tq:ParameterGrid>");

            Assert.That(root.Q<ParameterGrid>(), Is.Not.Null, "ParameterGrid が UXML から解決できていない");

            ParameterHeading heading = root.Q<ParameterHeading>();
            Assert.That(heading, Is.Not.Null, "ParameterHeading が UXML から解決できていない");
            Assert.That(heading.Text, Is.EqualTo("Transform"));

            ParameterGroup group = root.Q<ParameterGroup>();
            Assert.That(group, Is.Not.Null, "ParameterGroup が UXML から解決できていない");
            Assert.That(group.Name, Is.EqualTo(TEST_GROUP_NAME));
            Assert.That(group.Label, Is.EqualTo("Vector"));
            Assert.That(group.Expanded, Is.False);
        }

        [Test]
        public void PopoverAttributesAreAppliedFromUxml()
        {
            VisualElement root = Instantiate(
                "<tq:TweeqPopover placement=\"Top\" arrow=\"false\" light-dismiss=\"false\""
                + " chrome=\"false\" />");

            TweeqPopover popover = root.Q<TweeqPopover>();
            Assert.That(popover, Is.Not.Null, "TweeqPopover が UXML から解決できていない");
            Assert.That(popover.Placement, Is.EqualTo(Tweeq.Core.PopoverPlacement.Top));
            Assert.That(popover.Arrow, Is.False);
            Assert.That(popover.LightDismiss, Is.False);
            Assert.That(popover.Chrome, Is.False);
        }

        [Test]
        public void BalloonAttributesAreAppliedFromUxml()
        {
            // TweeqPopover は内部に TweeqBalloon を持つので、混ぜて実体化すると Q が取り違える
            VisualElement root = Instantiate(
                "<tq:TweeqBalloon arrow-side=\"Bottom\" arrow-offset=\"12\" />");

            TweeqBalloon balloon = root.Q<TweeqBalloon>();
            Assert.That(balloon, Is.Not.Null, "TweeqBalloon が UXML から解決できていない");
            Assert.That(balloon.ArrowSide, Is.EqualTo(TweeqArrowSide.Bottom));
            Assert.That(balloon.ArrowOffset, Is.EqualTo(12f).Within(0.01f));
        }

        #endregion
    }
}
