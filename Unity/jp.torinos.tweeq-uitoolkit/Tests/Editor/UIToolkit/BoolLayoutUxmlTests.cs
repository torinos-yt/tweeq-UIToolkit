using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies UXML support for the Bool family (Checkbox / Switch / Button / ButtonToggle /
    /// Radio) and the layout family (InputGroup / Parameter family / Popover / Balloon).
    ///
    /// Hitting UxmlSerializedData directly would depend on the internal naming of generated code,
    /// so this verifies via the path of actually importing and instantiating a .uxml file.
    /// There's no public API to build a VisualTreeAsset from a string, so this writes a temp file
    /// under Assets, imports it via AssetDatabase, and deletes it in TearDown.
    /// </summary>
    public class BoolLayoutUxmlTests
    {
        const string TEMP_FOLDER = "Assets/TweeqUxmlTests";
        const string TEMP_ASSET = TEMP_FOLDER + "/tweeq-uxml-test.uxml";

        // There's a test that writes ParameterGroup's group-name, so use a dedicated key to avoid polluting PlayerPrefs
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

        /// <summary>Given the contents of a UXML document (elements only), returns the root of the instantiated tree.</summary>
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
            Assert.That(asset, Is.Not.Null, "failed to import the temp UXML");

            VisualElement root = asset.Instantiate();
            Assert.That(root, Is.Not.Null, "failed to instantiate the UXML");
            return root;
        }

        #region Bool inputs

        [Test]
        public void CheckboxAttributesAreAppliedFromUxml()
        {
            VisualElement root = Instantiate(
                "<tq:CheckboxInput label=\"Visible\" value=\"true\" disabled=\"true\" />");

            CheckboxInput checkbox = root.Q<CheckboxInput>();
            Assert.That(checkbox, Is.Not.Null, "CheckboxInput could not be resolved from UXML");
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
            Assert.That(toggle, Is.Not.Null, "SwitchInput could not be resolved from UXML");
            Assert.That(toggle.value, Is.True);
            Assert.That(toggle.Label, Is.EqualTo("Loop"));
        }

        [Test]
        public void ButtonAttributesAreAppliedFromUxml()
        {
            VisualElement root = Instantiate(
                "<tq:ButtonInput text=\"Render\" subtle=\"true\" narrow=\"true\" chevron=\"true\" />");

            ButtonInput button = root.Q<ButtonInput>();
            Assert.That(button, Is.Not.Null, "ButtonInput could not be resolved from UXML");

            // The C# side uses Label, while the UXML side uses text to match the Vue original's prop name
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
                "<tq:ButtonToggleInput text=\"Solo\" value=\"true\" disabled=\"true\" font-size=\"12\" />");

            ButtonToggleInput toggle = root.Q<ButtonToggleInput>();
            Assert.That(toggle, Is.Not.Null, "ButtonToggleInput could not be resolved from UXML");
            Assert.That(toggle.Label, Is.EqualTo("Solo"));
            Assert.That(toggle.value, Is.True);
            Assert.That(toggle.Disabled, Is.True);
            Assert.That(toggle.FontSize, Is.EqualTo(12f));
        }

        [Test]
        public void RadioOptionsAreAppliedBeforeValue()
        {
            VisualElement root = Instantiate(
                "<tq:RadioInput options=\"Low,Mid,High\" value=\"2\" />");

            RadioInput radio = root.Q<RadioInput>();
            Assert.That(radio, Is.Not.Null, "RadioInput could not be resolved from UXML");

            // options must be readable as a comma-separated string[] (a dedicated CSV property would be needed if it can't be read)
            Assert.That(radio.Options, Is.EqualTo(new[] { "Low", "Mid", "High" }));

            // value must be applied after options, or it gets discarded as out of range
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
            Assert.That(group, Is.Not.Null, "InputGroup could not be resolved from UXML");
            Assert.That(group.Direction, Is.EqualTo(FlexDirection.Column));
            Assert.That(group.childCount, Is.EqualTo(2));
        }

        [Test]
        public void ParameterLabelIsAppliedFromUxml()
        {
            VisualElement root = Instantiate("<tq:Parameter label=\"Opacity\" />");

            Parameter parameter = root.Q<Parameter>();
            Assert.That(parameter, Is.Not.Null, "Parameter could not be resolved from UXML");
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

            Assert.That(root.Q<ParameterGrid>(), Is.Not.Null, "ParameterGrid could not be resolved from UXML");

            ParameterHeading heading = root.Q<ParameterHeading>();
            Assert.That(heading, Is.Not.Null, "ParameterHeading could not be resolved from UXML");
            Assert.That(heading.Text, Is.EqualTo("Transform"));

            ParameterGroup group = root.Q<ParameterGroup>();
            Assert.That(group, Is.Not.Null, "ParameterGroup could not be resolved from UXML");
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
            Assert.That(popover, Is.Not.Null, "TweeqPopover could not be resolved from UXML");
            Assert.That(popover.Placement, Is.EqualTo(Tweeq.Core.PopoverPlacement.Top));
            Assert.That(popover.Arrow, Is.False);
            Assert.That(popover.LightDismiss, Is.False);
            Assert.That(popover.Chrome, Is.False);
        }

        [Test]
        public void BalloonAttributesAreAppliedFromUxml()
        {
            // TweeqPopover holds a TweeqBalloon internally, so instantiating them mixed together would make Q pick the wrong one
            VisualElement root = Instantiate(
                "<tq:TweeqBalloon arrow-side=\"Bottom\" arrow-offset=\"12\" />");

            TweeqBalloon balloon = root.Q<TweeqBalloon>();
            Assert.That(balloon, Is.Not.Null, "TweeqBalloon could not be resolved from UXML");
            Assert.That(balloon.ArrowSide, Is.EqualTo(TweeqArrowSide.Bottom));
            Assert.That(balloon.ArrowOffset, Is.EqualTo(12f).Within(0.01f));
        }

        #endregion
    }
}
