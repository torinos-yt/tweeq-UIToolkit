using NUnit.Framework;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies InputGroup's position assignment and the resulting corner rounding on the NumberInput side (the table in spec §1).
    /// A VisualElement can be created and styled without a panel, so this is fully covered in EditMode.
    /// </summary>
    public class InputGroupTests
    {
        const float RADIUS = 4f;

        static InputGroup CreateGroup(int count, FlexDirection direction)
        {
            InputGroup group = new InputGroup { Direction = direction };

            for (int i = 0; i < count; i++)
            {
                group.Add(new NumberInput());
            }

            group.RefreshPositions();
            return group;
        }

        static NumberInput AxisAt(InputGroup group, int index)
        {
            return group.ElementAt(index) as NumberInput;
        }

        static float Radius(StyleLength style)
        {
            return style.value.value;
        }

        [Test]
        public void Horizontal_Three_AssignsStartMiddleEnd()
        {
            InputGroup group = CreateGroup(3, FlexDirection.Row);

            Assert.AreEqual(TweeqBoxPosition.Start, AxisAt(group, 0).InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.Middle, AxisAt(group, 1).InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.End, AxisAt(group, 2).InlinePosition);

            // Does not touch the other axis
            Assert.AreEqual(TweeqBoxPosition.None, AxisAt(group, 0).BlockPosition);
            Assert.AreEqual(TweeqBoxPosition.None, AxisAt(group, 1).BlockPosition);
            Assert.AreEqual(TweeqBoxPosition.None, AxisAt(group, 2).BlockPosition);
        }

        [Test]
        public void Vertical_Three_AssignsBlockPositions()
        {
            InputGroup group = CreateGroup(3, FlexDirection.Column);

            Assert.AreEqual(TweeqBoxPosition.Start, AxisAt(group, 0).BlockPosition);
            Assert.AreEqual(TweeqBoxPosition.Middle, AxisAt(group, 1).BlockPosition);
            Assert.AreEqual(TweeqBoxPosition.End, AxisAt(group, 2).BlockPosition);

            Assert.AreEqual(TweeqBoxPosition.None, AxisAt(group, 0).InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.None, AxisAt(group, 2).InlinePosition);
        }

        [Test]
        public void SingleChild_StaysNone()
        {
            InputGroup group = CreateGroup(1, FlexDirection.Row);

            Assert.AreEqual(TweeqBoxPosition.None, AxisAt(group, 0).InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.None, AxisAt(group, 0).BlockPosition);
        }

        [Test]
        public void EmptyGroup_RefreshDoesNotThrow()
        {
            InputGroup group = new InputGroup();
            Assert.DoesNotThrow(() => group.RefreshPositions());
        }

        [Test]
        public void NonBoxChildren_AreSkippedForPositions()
        {
            InputGroup group = new InputGroup();
            group.Add(new Label("head"));
            NumberInput first = new NumberInput();
            NumberInput second = new NumberInput();
            group.Add(first);
            group.Add(second);

            // The label does not count, so the two NumberInputs become Start / End
            Assert.AreEqual(TweeqBoxPosition.Start, first.InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.End, second.InlinePosition);
        }

        [Test]
        public void DirectionChange_ClearsOtherAxis()
        {
            InputGroup group = CreateGroup(3, FlexDirection.Row);
            group.Direction = FlexDirection.Column;

            Assert.AreEqual(TweeqBoxPosition.None, AxisAt(group, 0).InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.Start, AxisAt(group, 0).BlockPosition);
        }

        [Test]
        public void Gap_AppliedToAllButLastChild()
        {
            InputGroup group = CreateGroup(3, FlexDirection.Row);
            float gap = new TweeqTheme().GapGroup;

            Assert.AreEqual(gap, AxisAt(group, 0).style.marginRight.value.value);
            Assert.AreEqual(gap, AxisAt(group, 1).style.marginRight.value.value);
            Assert.AreEqual(0f, AxisAt(group, 2).style.marginRight.value.value);
        }

        [Test]
        public void Gap_VerticalUsesMarginBottom()
        {
            InputGroup group = CreateGroup(2, FlexDirection.Column);
            float gap = new TweeqTheme().GapGroup;

            Assert.AreEqual(gap, AxisAt(group, 0).style.marginBottom.value.value);
            Assert.AreEqual(0f, AxisAt(group, 0).style.marginRight.value.value);
            Assert.AreEqual(0f, AxisAt(group, 1).style.marginBottom.value.value);
        }

        [Test]
        public void InlineStart_SquaresRightCorners()
        {
            NumberInput input = new NumberInput { InlinePosition = TweeqBoxPosition.Start };

            Assert.AreEqual(RADIUS, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(RADIUS, Radius(input.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomRightRadius));
        }

        [Test]
        public void InlineEnd_SquaresLeftCorners()
        {
            NumberInput input = new NumberInput { InlinePosition = TweeqBoxPosition.End };

            Assert.AreEqual(0f, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomLeftRadius));
            Assert.AreEqual(RADIUS, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(RADIUS, Radius(input.style.borderBottomRightRadius));
        }

        [Test]
        public void Middle_SquaresAllCorners()
        {
            NumberInput input = new NumberInput { InlinePosition = TweeqBoxPosition.Middle };

            Assert.AreEqual(0f, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomRightRadius));
        }

        [Test]
        public void BlockStart_SquaresBottomCorners()
        {
            NumberInput input = new NumberInput { BlockPosition = TweeqBoxPosition.Start };

            Assert.AreEqual(RADIUS, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(RADIUS, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderBottomRightRadius));
        }

        [Test]
        public void BlockEnd_SquaresTopCorners()
        {
            NumberInput input = new NumberInput { BlockPosition = TweeqBoxPosition.End };

            Assert.AreEqual(0f, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(0f, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(RADIUS, Radius(input.style.borderBottomLeftRadius));
            Assert.AreEqual(RADIUS, Radius(input.style.borderBottomRightRadius));
        }

        [Test]
        public void None_KeepsAllCornersRounded()
        {
            NumberInput input = new NumberInput();

            Assert.AreEqual(RADIUS, Radius(input.style.borderTopLeftRadius));
            Assert.AreEqual(RADIUS, Radius(input.style.borderTopRightRadius));
            Assert.AreEqual(RADIUS, Radius(input.style.borderBottomLeftRadius));
            Assert.AreEqual(RADIUS, Radius(input.style.borderBottomRightRadius));
        }

        [Test]
        public void RotaryInput_PositionIsStoredOnly()
        {
            RotaryInput rotary = new RotaryInput { InlinePosition = TweeqBoxPosition.Middle };

            // Being circular, it doesn't touch corner rounding. Only verifies the value is retained
            Assert.AreEqual(TweeqBoxPosition.Middle, rotary.InlinePosition);
            Assert.AreEqual(TweeqBoxPosition.None, rotary.BlockPosition);
        }
    }
}
