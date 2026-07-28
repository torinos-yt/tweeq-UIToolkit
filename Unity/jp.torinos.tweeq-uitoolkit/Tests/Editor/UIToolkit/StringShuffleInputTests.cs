using NUnit.Framework;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Contract for StringShuffleInput (a string-specialized wrapper for UXML).
    /// The behavior of the base ShuffleInput is covered by ShuffleInputTests, so here
    /// we only cover the "default Generate = draw from Options" behavior and the Options copy boundary.
    /// </summary>
    public class StringShuffleInputTests
    {
        static StringShuffleInput Create(string initial, params string[] options)
        {
            StringShuffleInput input = new StringShuffleInput(options);
            input.SetValueWithoutNotify(initial);
            return input;
        }

        #region Default Generate

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

            // With 2 options, the "shift to the neighbor if the same one is drawn" effect guarantees a swap on every click
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
