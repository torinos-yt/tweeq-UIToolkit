using NUnit.Framework;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// The contract of StringDropdownInput (a string-specialized wrapper for UXML use).
    /// Opening/closing and filtering are covered by the base DropdownInput's own tests, so here
    /// we only cover the comma-separated split/join that underlies the UXML attribute, and the
    /// connection to the base class.
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

            // Whitespace inside an element is preserved ("Ease In" is a single option)
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

        #region Connection to the base class

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
