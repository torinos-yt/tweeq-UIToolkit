using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// The contract of AngleInput (equivalent to the Vue original's InputAngle). Covers two-way
    /// sync between the knob and the number field, unifying notifications, collapsing the number
    /// field based on width, and corner-radius fusion.
    /// </summary>
    /// <remarks>
    /// Value changes on RotaryInput / NumberInput only surface via ChangeEvent, which isn't
    /// delivered without a panel, so notifications originating from the children are substituted
    /// via AngleInput's own entry points (PerformRotaryEdit / PerformNumberEdit / PerformConfirm).
    /// Actual pointer interaction and real layout are covered on the Play Mode side.
    /// </remarks>
    public class AngleInputTests
    {
        const float EPSILON = 1e-4f;

        // The default theme's InputHeight is 24px, so the threshold at which the number field appears is 96px
        const float THRESHOLD = 96f;

        static AngleInput Create(float initial)
        {
            AngleInput input = new AngleInput();
            input.SetValueWithoutNotify(initial);
            return input;
        }

        #region Structure

        [Test]
        public void Rotary_PaintsAboveTheNumberField()
        {
            AngleInput input = Create(45f);

            // Paint order is hierarchy order, and the knob scales to 1.8x on hover/drag, so it
            // must be the later sibling; RowReverse keeps it visually on the left.
            VisualElement group = input.Rotary.hierarchy.parent;
            Assert.AreEqual(FlexDirection.RowReverse, ((InputGroup)group).Direction);
            Assert.Greater(
                group.hierarchy.IndexOf(input.Rotary),
                group.hierarchy.IndexOf(input.Number),
                "the knob must come later in the hierarchy to paint over the field");
        }

        [Test]
        public void Rotary_KeepsTheVisualStartCorners()
        {
            AngleInput input = Create(45f);

            // Wide enough for the number field to show; the fusion override assigns against the
            // visual order regardless of hierarchy order.
            input.PerformResize(THRESHOLD * 2f);

            Assert.AreEqual(TweeqBoxPosition.Start, input.Rotary.InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.End, input.Number.InlinePosition);
        }

        #endregion

        #region Synchronization

        [Test]
        public void SetValueWithoutNotify_WritesBothChildren()
        {
            AngleInput input = Create(45f);

            Assert.AreEqual(45f, input.Rotary.value, EPSILON);
            Assert.AreEqual(45f, input.Number.value, EPSILON);
        }

        [Test]
        public void RotaryEdit_UpdatesTheNumberAndNotifiesOnce()
        {
            AngleInput input = Create(0f);
            int changed = 0;
            float received = 0f;
            input.ValueChanged += value =>
            {
                changed++;
                received = value;
            };

            input.PerformRotaryEdit(30f);

            Assert.AreEqual(1, changed);
            Assert.AreEqual(30f, received, EPSILON);
            Assert.AreEqual(30f, input.value, EPSILON);
            Assert.AreEqual(30f, input.Number.value, EPSILON);
        }

        [Test]
        public void NumberEdit_UpdatesTheRotary()
        {
            AngleInput input = Create(0f);

            input.PerformNumberEdit(-120f);

            Assert.AreEqual(-120f, input.value, EPSILON);
            Assert.AreEqual(-120f, input.Rotary.value, EPSILON);
        }

        [Test]
        public void ValueSetter_NotifiesOnceAndWritesBothChildren()
        {
            AngleInput input = Create(0f);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.value = 90f;

            Assert.AreEqual(1, changed);
            Assert.AreEqual(90f, input.Rotary.value, EPSILON);
            Assert.AreEqual(90f, input.Number.value, EPSILON);
        }

        [Test]
        public void ValueSetter_SameValueIsSilent()
        {
            AngleInput input = Create(45f);
            int changed = 0;
            input.ValueChanged += _ => changed++;

            input.value = 45f;

            Assert.AreEqual(0, changed);
        }

        [Test]
        public void SetValueWithoutNotify_IsSilent()
        {
            AngleInput input = Create(0f);
            int changed = 0;
            int confirmed = 0;
            input.ValueChanged += _ => changed++;
            input.Confirmed += _ => confirmed++;

            input.SetValueWithoutNotify(15f);

            Assert.AreEqual(0, changed);
            Assert.AreEqual(0, confirmed);
        }

        #endregion

        #region Confirmation

        [Test]
        public void Confirm_RaisesConfirmedOnceWithTheFinalValue()
        {
            AngleInput input = Create(0f);
            int confirmed = 0;
            float received = 0f;
            input.Confirmed += value =>
            {
                confirmed++;
                received = value;
            };

            // One gesture = move over several frames, then release
            input.PerformRotaryEdit(10f);
            input.PerformRotaryEdit(20f);
            input.PerformConfirm();

            Assert.AreEqual(1, confirmed);
            Assert.AreEqual(20f, received, EPSILON);
        }

        [Test]
        public void Edit_DoesNotRaiseConfirmed()
        {
            AngleInput input = Create(0f);
            int confirmed = 0;
            input.Confirmed += _ => confirmed++;

            input.PerformRotaryEdit(10f);
            input.PerformNumberEdit(20f);

            Assert.AreEqual(0, confirmed);
        }

        #endregion

        #region Collapsing by width

        [Test]
        public void Number_IsHiddenBeforeTheFirstLayout()
        {
            AngleInput input = Create(0f);

            Assert.IsFalse(input.ShowsNumber);
            Assert.AreEqual(DisplayStyle.None, input.Number.style.display.value);
        }

        [Test]
        public void Number_AppearsAboveFourInputHeights()
        {
            AngleInput input = Create(0f);

            input.PerformResize(THRESHOLD + 1f);

            Assert.IsTrue(input.ShowsNumber);
            Assert.AreEqual(DisplayStyle.Flex, input.Number.style.display.value);
        }

        [Test]
        public void Number_StaysHiddenAtExactlyFourInputHeights()
        {
            AngleInput input = Create(0f);

            input.PerformResize(THRESHOLD);

            Assert.IsFalse(input.ShowsNumber);
            Assert.AreEqual(DisplayStyle.None, input.Number.style.display.value);
        }

        [Test]
        public void Number_CollapsesAgainWhenTheWidthShrinks()
        {
            AngleInput input = Create(0f);

            input.PerformResize(200f);
            input.PerformResize(60f);

            Assert.IsFalse(input.ShowsNumber);
            Assert.AreEqual(DisplayStyle.None, input.Number.style.display.value);
        }

        #endregion

        #region Group fusion

        [Test]
        public void BoxFusion_JoinsRotaryAndNumberWhenBothAreVisible()
        {
            AngleInput input = Create(0f);

            input.PerformResize(200f);

            Assert.AreEqual(TweeqBoxPosition.Start, input.Rotary.InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.End, input.Number.InlinePosition);
        }

        [Test]
        public void BoxFusion_RotaryStandsAloneWhenTheNumberIsHidden()
        {
            AngleInput input = Create(0f);

            input.PerformResize(60f);

            Assert.AreEqual(TweeqBoxPosition.None, input.Rotary.InlinePosition);
        }

        [Test]
        public void BoxFusion_OuterPositionIsSplitAcrossBothChildren()
        {
            AngleInput input = Create(0f);
            input.PerformResize(200f);

            input.InlinePosition = TweeqBoxPosition.Start;

            // Being first = only the left is rounded. The right edge continues into a neighbor, so the number field flattens down to Middle
            Assert.AreEqual(TweeqBoxPosition.Start, input.Rotary.InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.Middle, input.Number.InlinePosition);
        }

        [Test]
        public void BoxFusion_BlockPositionGoesToBothChildrenAsIs()
        {
            AngleInput input = Create(0f);
            input.PerformResize(200f);

            input.BlockPosition = TweeqBoxPosition.End;

            Assert.AreEqual(TweeqBoxPosition.End, input.Rotary.BlockPosition);
            Assert.AreEqual(TweeqBoxPosition.End, input.Number.BlockPosition);
        }

        #endregion

        #region Forwarded properties

        [Test]
        public void Snap_GoesToTheRotaryOnly()
        {
            AngleInput input = Create(0f);
            double numberSnap = input.Number.SnapStep;

            input.Snap = 15.0;

            Assert.AreEqual(15.0, input.Rotary.Snap);
            Assert.AreEqual(15.0, input.Snap);
            Assert.AreEqual(numberSnap, input.Number.SnapStep);
        }

        [Test]
        public void AngleOffset_GoesToTheRotary()
        {
            AngleInput input = Create(0f);

            input.AngleOffset = -90.0;

            Assert.AreEqual(-90.0, input.Rotary.AngleOffset);
            Assert.AreEqual(-90.0, input.AngleOffset);
        }

        [Test]
        public void Step_GoesToBothChildren()
        {
            AngleInput input = Create(0f);

            input.Step = 0.1;

            Assert.AreEqual(0.1, input.Rotary.Step);
            Assert.AreEqual(0.1, input.Number.Step);
            Assert.AreEqual(0.1, input.Step);
        }

        [Test]
        public void Range_GoesToTheNumber()
        {
            AngleInput input = Create(0f);

            input.Min = -180.0;
            input.Max = 180.0;
            input.Precision = 1;

            Assert.AreEqual(-180.0, input.Number.Min);
            Assert.AreEqual(180.0, input.Number.Max);
            Assert.AreEqual(1, input.Number.Precision);
        }

        [Test]
        public void Suffix_IsDegreeSign()
        {
            AngleInput input = Create(0f);

            Assert.AreEqual("°", input.Number.Suffix);
        }

        [Test]
        public void Theme_PropagatesToBothChildren()
        {
            AngleInput input = Create(0f);
            TweeqTheme light = TweeqTheme.Light();

            input.Theme = light;

            Assert.AreSame(light, input.Rotary.Theme);
            Assert.AreSame(light, input.Number.Theme);
        }

        #endregion

        #region disabled / invalid

        [Test]
        public void Disabled_PropagatesToKnobAndNumber()
        {
            AngleInput input = Create(0f);

            Assert.IsFalse(input.Disabled);

            input.Disabled = true;

            Assert.IsTrue(input.Rotary.Disabled);
            Assert.IsTrue(input.Number.Disabled);

            input.Disabled = false;

            Assert.IsFalse(input.Rotary.Disabled);
            Assert.IsFalse(input.Number.Disabled);
        }

        [Test]
        public void Invalid_GoesToTheNumberOnly()
        {
            // The knob has no invalid representation in the Vue original either, so this is only routed to the number side
            AngleInput input = Create(0f);

            input.Invalid = true;

            Assert.IsTrue(input.Number.Invalid);
            Assert.IsFalse(input.Rotary.Disabled);
        }

        [Test]
        public void Disabled_BlocksTheChildEditPorts()
        {
            AngleInput input = Create(30f);
            int changed = 0;
            int confirmed = 0;
            input.ValueChanged += _ => changed++;
            input.Confirmed += _ => confirmed++;

            input.Disabled = true;
            input.PerformRotaryEdit(90f);
            input.PerformNumberEdit(120f);
            input.PerformConfirm();

            Assert.AreEqual(0, changed);
            Assert.AreEqual(0, confirmed);
            Assert.AreEqual(30f, input.value, EPSILON);
        }

        [Test]
        public void Disabled_BlocksTheKnobDrag()
        {
            AngleInput input = Create(0f);

            input.Disabled = true;
            input.Rotary.BeginRotaryDrag();
            input.Rotary.UpdateRotaryDrag(45.0);
            input.Rotary.EndRotaryDrag();

            Assert.IsFalse(input.Rotary.Dragging);
            Assert.AreEqual(0f, input.value, EPSILON);
        }

        [Test]
        public void Disabled_DoesNotBlockTheProgrammaticValue()
        {
            AngleInput input = Create(0f);
            input.Disabled = true;

            input.value = 45f;

            Assert.AreEqual(45f, input.value, EPSILON);
            Assert.AreEqual(45f, input.Rotary.value, EPSILON);
            Assert.AreEqual(45f, input.Number.value, EPSILON);
        }

        #endregion
    }
}
