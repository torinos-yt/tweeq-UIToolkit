using NUnit.Framework;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// StringShuffleInput（UXML 用の string 特化ラッパ）の契約。
    /// 基底 ShuffleInput の挙動は ShuffleInputTests が見るので、ここでは
    /// 「既定 Generate = Options からの抽選」と Options のコピー境界だけを見る。
    /// </summary>
    public class StringShuffleInputTests
    {
        static StringShuffleInput Create(string initial, params string[] options)
        {
            StringShuffleInput input = new StringShuffleInput(options);
            input.SetValueWithoutNotify(initial);
            return input;
        }

        #region 既定 Generate

        [Test]
        public void Default_ClickPicksFromOptions()
        {
            StringShuffleInput input = Create("a", "a", "b", "c");

            input.PerformClick();

            Assert.Contains(input.value, new[] { "a", "b", "c" });
        }

        [Test]
        public void Default_ClickAlwaysMovesAwayFromTheCurrentValue()
        {
            StringShuffleInput input = Create("a", "a", "b");

            // 2 択なら「同じものを引いたら隣へずらす」の効果でクリックごとに必ず入れ替わる
            for (int i = 0; i < 20; i++)
            {
                string before = input.value;

                input.PerformClick();

                Assert.AreNotEqual(before, input.value);
            }
        }

        [Test]
        public void Default_SingleOptionKeepsTheValue()
        {
            StringShuffleInput input = Create("a", "a");

            input.PerformClick();

            Assert.AreEqual("a", input.value);
        }

        [Test]
        public void Default_EmptyOptionsKeepTheValue()
        {
            StringShuffleInput input = Create("a");

            input.PerformClick();

            Assert.AreEqual("a", input.value);
        }

        [Test]
        public void Default_ValueOutsideOptionsIsPulledIntoTheSet()
        {
            StringShuffleInput input = Create("z", "a", "b");

            input.PerformClick();

            Assert.Contains(input.value, new[] { "a", "b" });
        }

        [Test]
        public void Default_GenerateCanStillBeReplaced()
        {
            StringShuffleInput input = Create("a", "a", "b");

            input.Generate = previous => previous + "!";
            input.PerformClick();

            Assert.AreEqual("a!", input.value);
        }

        #endregion

        #region Options

        [Test]
        public void Options_RoundTripsThroughACopy()
        {
            StringShuffleInput input = Create("a", "a", "b");

            string[] read = input.Options;
            read[0] = "mutated";

            Assert.AreEqual("a", input.Options[0]);
        }

        [Test]
        public void Options_NullBecomesEmpty()
        {
            StringShuffleInput input = Create("a", "a", "b");

            input.Options = null;

            Assert.AreEqual(0, input.Options.Length);
        }

        #endregion

        #region Box

        [Test]
        public void Box_ThemeNullFallsBackToDark()
        {
            StringShuffleInput input = Create("a", "a");

            input.Theme = null;

            Assert.IsNotNull(input.Theme);
            Assert.AreEqual(ColorMode.Dark, input.Theme.Mode);
        }

        #endregion
    }
}
