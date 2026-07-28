using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// テキスト／カラー系コンポーネントの UXML 実体化（m7-wave2-spec.md「UXML 対応」）。
    /// [UxmlElement] の属性名が UXML 側の綴りと一致していること・値が要素へ届くことを見る。
    /// </summary>
    /// <remarks>
    /// UXML から要素を作るには VisualTreeAsset が必要で、その生成は UXML インポータ
    /// （＝AssetDatabase）しか行えない。そのため一時アセットを書き出してインポートし、
    /// TearDown で消す。Editor 専用テストアセンブリなのでこの手が使える。
    /// </remarks>
    public class UxmlTextColorElementsTests
    {
        // 別班の UXML テストと同じフォルダを掴まないよう、この班専用の名前にする
        const string TEMP_FOLDER_NAME = "TweeqUxmlTextColorTests";
        const string TEMP_FOLDER = "Assets/" + TEMP_FOLDER_NAME;

        [TearDown]
        public void DeleteTempAssets()
        {
            if (AssetDatabase.IsValidFolder(TEMP_FOLDER))
            {
                AssetDatabase.DeleteAsset(TEMP_FOLDER);
            }
        }

        static VisualElement Instantiate(string fileName, string body)
        {
            if (!AssetDatabase.IsValidFolder(TEMP_FOLDER))
            {
                AssetDatabase.CreateFolder("Assets", TEMP_FOLDER_NAME);
            }

            string path = TEMP_FOLDER + "/" + fileName + ".uxml";
            string uxml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:tq=\"Tweeq.UIToolkit\">\n"
                + body
                + "\n</ui:UXML>\n";

            File.WriteAllText(path, uxml);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.IsNotNull(asset, "UXML のインポートに失敗した: " + path);

            return asset.Instantiate();
        }

        #region NumberInput

        [Test]
        public void Number_AttributesReachTheElement()
        {
            VisualElement root = Instantiate(
                "number",
                "  <tq:NumberInput value=\"3.5\" min=\"0\" max=\"10\" step=\"0.5\" precision=\"2\""
                + " clamp-min=\"false\" clamp-max=\"false\" bar-visible=\"false\""
                + " prefix=\"x \" suffix=\" mm\" disabled=\"true\" invalid=\"true\" />");

            NumberInput number = root.Q<NumberInput>();

            Assert.IsNotNull(number);
            Assert.AreEqual(3.5f, number.value);
            Assert.AreEqual(0.0, number.Min);
            Assert.AreEqual(10.0, number.Max);
            Assert.AreEqual(0.5, number.Step);
            Assert.AreEqual(2, number.Precision);
            Assert.IsFalse(number.ClampMin);
            Assert.IsFalse(number.ClampMax);
            Assert.IsFalse(number.Bar);
            Assert.AreEqual("x ", number.Prefix);
            Assert.AreEqual(" mm", number.Suffix);
            Assert.IsTrue(number.Disabled);
            Assert.IsTrue(number.Invalid);
        }

        [Test]
        public void Number_OmittedAttributesKeepTheirDefaults()
        {
            VisualElement root = Instantiate("number-bare", "  <tq:NumberInput />");

            NumberInput number = root.Q<NumberInput>();

            // Min / Max の既定は ±∞。UXML に書かれていない属性で 0 に潰されないこと
            Assert.AreEqual(double.NegativeInfinity, number.Min);
            Assert.AreEqual(double.PositiveInfinity, number.Max);
            Assert.AreEqual(4, number.Precision);
            Assert.IsTrue(number.Bar);
        }

        #endregion

        #region StringInput

        [Test]
        public void String_AttributesReachTheElement()
        {
            VisualElement root = Instantiate(
                "string",
                "  <tq:StringInput value=\"hello\" align=\"Center\""
                + " disabled=\"true\" invalid=\"true\" />");

            StringInput input = root.Q<StringInput>();

            Assert.IsNotNull(input);
            Assert.AreEqual("hello", input.value);
            Assert.AreEqual(TweeqTextAlign.Center, input.Align);
            Assert.IsTrue(input.Disabled);
            Assert.IsTrue(input.Invalid);
        }

        #endregion

        #region ColorInput

        [Test]
        public void Color_AttributesReachTheElement()
        {
            VisualElement root = Instantiate(
                "color",
                "  <tq:ColorInput value=\"#FF0000FF\" color-space=\"rgb\" disabled=\"true\" />");

            ColorInput input = root.Q<ColorInput>();

            Assert.IsNotNull(input);
            Assert.AreEqual(1f, input.value.r, 1e-3f);
            Assert.AreEqual(0f, input.value.g, 1e-3f);
            Assert.AreEqual(0f, input.value.b, 1e-3f);
            Assert.AreEqual(1f, input.value.a, 1e-3f);
            Assert.AreEqual(ColorInput.COLOR_SPACE_RGB, input.ColorSpace);
            Assert.IsTrue(input.Disabled);
        }

        #endregion

        #region StringDropdownInput

        [Test]
        public void Dropdown_OptionsAndLabelsComeFromCommaSeparatedAttributes()
        {
            VisualElement root = Instantiate(
                "dropdown",
                "  <tq:StringDropdownInput options=\"Linear,Ease In,Ease Out\""
                + " labels=\"LIN,IN,OUT\" value=\"Ease In\" invalid=\"true\" />");

            StringDropdownInput dropdown = root.Q<StringDropdownInput>();

            Assert.IsNotNull(dropdown);
            Assert.AreEqual(3, dropdown.Options.Length);
            Assert.AreEqual("Ease In", dropdown.Options[1]);
            Assert.AreEqual("Ease In", dropdown.value);
            Assert.AreEqual("IN", dropdown.DisplayText);
            Assert.IsTrue(dropdown.Invalid);
        }

        #endregion

        #region StringShuffleInput

        [Test]
        public void Shuffle_OptionsAttributeDrivesTheDefaultGenerate()
        {
            VisualElement root = Instantiate(
                "shuffle",
                "  <tq:StringShuffleInput options=\"a,b\" value=\"a\" />");

            StringShuffleInput shuffle = root.Q<StringShuffleInput>();

            Assert.IsNotNull(shuffle);
            Assert.AreEqual(2, shuffle.Options.Length);
            Assert.AreEqual("a", shuffle.value);

            // UXML だけで組んでも押せば値が動く（Generate を配線しなくてよい）
            shuffle.PerformClick();

            Assert.AreEqual("b", shuffle.value);
        }

        #endregion
    }
}
