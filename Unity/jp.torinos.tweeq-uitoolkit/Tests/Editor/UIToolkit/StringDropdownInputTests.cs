using NUnit.Framework;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// StringDropdownInput（UXML 用の string 特化ラッパ）の契約。
    /// 開閉やフィルタは基底 DropdownInput 側のテストが見るので、ここでは
    /// UXML 属性の土台になるカンマ区切りの分解・合成と、基底との接続だけを見る。
    /// </summary>
    public class StringDropdownInputTests
    {
        #region Csv

        [Test]
        public void Csv_SplitsOnCommasAndTrimsSpaces()
        {
            string[] options = StringDropdownInput.Split("Linear, Ease In ,Ease Out");

            Assert.AreEqual(3, options.Length);
            Assert.AreEqual("Linear", options[0]);

            // 要素内の空白は残す（"Ease In" は 1 つの選択肢）
            Assert.AreEqual("Ease In", options[1]);
            Assert.AreEqual("Ease Out", options[2]);
        }

        [Test]
        public void Csv_SplitDropsEmptyEntries()
        {
            string[] options = StringDropdownInput.Split("a,,b,");

            Assert.AreEqual(2, options.Length);
            Assert.AreEqual("a", options[0]);
            Assert.AreEqual("b", options[1]);
        }

        [Test]
        public void Csv_SplitOfNullOrEmptyIsEmpty()
        {
            Assert.AreEqual(0, StringDropdownInput.Split(null).Length);
            Assert.AreEqual(0, StringDropdownInput.Split(string.Empty).Length);
        }

        [Test]
        public void Csv_JoinOfNullIsEmptyString()
        {
            Assert.AreEqual(string.Empty, StringDropdownInput.Join(null));
        }

        [Test]
        public void Csv_RoundTrips()
        {
            string[] options = { "a", "b", "c" };

            Assert.AreEqual("a,b,c", StringDropdownInput.Join(options));
            Assert.AreEqual(options, StringDropdownInput.Split(StringDropdownInput.Join(options)));
        }

        #endregion

        #region 基底との接続

        [Test]
        public void Base_OptionsConstructorFillsTheDropdown()
        {
            StringDropdownInput dropdown = new StringDropdownInput(new[] { "a", "b" });

            Assert.AreEqual(2, dropdown.VisibleCount);
            Assert.AreEqual("a", dropdown.Options[0]);
        }

        [Test]
        public void Base_ArrowKeysStillWork()
        {
            StringDropdownInput dropdown = new StringDropdownInput(new[] { "a", "b" });
            dropdown.SetValueWithoutNotify("a");

            dropdown.MoveSelection(1);

            Assert.AreEqual("b", dropdown.value);
        }

        [Test]
        public void Base_InvalidIsInherited()
        {
            StringDropdownInput dropdown = new StringDropdownInput(new[] { "a", "b" });

            dropdown.Invalid = true;

            Assert.IsTrue(dropdown.Invalid);
        }

        #endregion
    }
}
