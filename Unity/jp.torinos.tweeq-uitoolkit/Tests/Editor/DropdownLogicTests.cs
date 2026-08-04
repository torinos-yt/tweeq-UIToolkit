using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class DropdownLogicTests
    {
        const double TOLERANCE = 1e-9;

        // theme.inputHeight = 24 / SELECT_CHROME = 2 / 5 options
        const double ITEM_HEIGHT = 24.0;
        const double LIST_HEIGHT = 5.0 * ITEM_HEIGHT + 2.0 * 2.0;
        const double VIEWPORT_HEIGHT = 800.0;
        const double MARGIN = DropdownLogic.DEFAULT_VIEWPORT_MARGIN;
        const double CHROME = DropdownLogic.DEFAULT_SELECT_CHROME;

        static double Top(double fieldWorldY, int index, double listHeight = LIST_HEIGHT,
            double viewportHeight = VIEWPORT_HEIGHT)
        {
            return DropdownLogic.GetDropdownTop(
                fieldWorldY, index, ITEM_HEIGHT, viewportHeight, MARGIN, CHROME, listHeight);
        }

        #region GetDropdownTop

        [Test]
        public void SelectedOptionLandsOnTheField()
        {
            // fieldWorldY - border/outline 2 - chrome 2 - index * itemHeight
            Assert.That(Top(300.0, 0), Is.EqualTo(296.0).Within(TOLERANCE));
            Assert.That(Top(300.0, 2), Is.EqualTo(248.0).Within(TOLERANCE));
        }

        [Test]
        public void EachIndexRaisesTheTopByExactlyOneRow()
        {
            Assert.That(Top(300.0, 1) - Top(300.0, 2), Is.EqualTo(ITEM_HEIGHT).Within(TOLERANCE));
        }

        [Test]
        public void NegativeIndexIsTreatedAsFirstOption()
        {
            // The case where the current value isn't in options (indexOf = -1)
            Assert.That(Top(300.0, -1), Is.EqualTo(Top(300.0, 0)).Within(TOLERANCE));
        }

        [Test]
        public void BottomIsClampedSoTheWholeListStaysVisible()
        {
            double top = Top(780.0, 0);

            Assert.That(top, Is.EqualTo(VIEWPORT_HEIGHT - MARGIN - LIST_HEIGHT).Within(TOLERANCE));
            Assert.That(top + LIST_HEIGHT, Is.EqualTo(VIEWPORT_HEIGHT - MARGIN).Within(TOLERANCE));
        }

        [Test]
        public void TopIsClampedToTheViewportMargin()
        {
            Assert.That(Top(10.0, 5), Is.EqualTo(MARGIN).Within(TOLERANCE));
        }

        [Test]
        public void OversizedListKeepsAtLeastOneRowVisible()
        {
            const double tallList = 2000.0;

            // Since a list that doesn't fit is assumed to extend to the bottom edge, maxTop may be lowered as far as "one row's worth"
            Assert.That(Top(780.0, 0, tallList),
                Is.EqualTo(VIEWPORT_HEIGHT - MARGIN - ITEM_HEIGHT).Within(TOLERANCE));
            // If the selection is further down, it goes up as far as the ideal value and stops at margin
            Assert.That(Top(400.0, 30, tallList), Is.EqualTo(MARGIN).Within(TOLERANCE));
            Assert.That(Top(400.0, 0, tallList), Is.EqualTo(396.0).Within(TOLERANCE));
        }

        [Test]
        public void UnmeasuredListIsTreatedAsOversized()
        {
            // Before measurement, the bottom-edge clamp is tilted toward the stricter side (one row's worth)
            Assert.That(Top(780.0, 0, 0.0),
                Is.EqualTo(VIEWPORT_HEIGHT - MARGIN - ITEM_HEIGHT).Within(TOLERANCE));
            // If the ideal value is sufficiently high, it's used as-is
            Assert.That(Top(300.0, 2, 0.0), Is.EqualTo(248.0).Within(TOLERANCE));
        }

        [Test]
        public void UpwardPlacementKeepsMeasuredListAboveField()
        {
            double top = DropdownLogic.GetDropdownTopUpward(780.0, ITEM_HEIGHT, MARGIN, CHROME, LIST_HEIGHT);

            Assert.That(top + LIST_HEIGHT + CHROME * 2.0,
                Is.EqualTo(780.0).Within(TOLERANCE));
        }

        [Test]
        public void UpwardPlacementUsesViewportMarginForTallList()
        {
            double top = DropdownLogic.GetDropdownTopUpward(780.0, ITEM_HEIGHT, MARGIN, CHROME, 2000.0);

            Assert.That(top, Is.EqualTo(MARGIN).Within(TOLERANCE));
        }

        [Test]
        public void MarginWinsWhenTheViewportIsShorterThanTheList()
        {
            // Preserves the top margin even in the extreme case where maxTop falls below margin (never returns a negative top)
            Assert.That(Top(10.0, 0, LIST_HEIGHT, 20.0), Is.EqualTo(MARGIN).Within(TOLERANCE));
        }

        [Test]
        public void OptionalArgumentsDefaultToVueConstants()
        {
            Assert.That(
                DropdownLogic.GetDropdownTop(300.0, 2, ITEM_HEIGHT, VIEWPORT_HEIGHT),
                Is.EqualTo(DropdownLogic.GetDropdownTop(300.0, 2, ITEM_HEIGHT, VIEWPORT_HEIGHT, 6.0, 2.0))
                    .Within(TOLERANCE));
            Assert.That(DropdownLogic.DEFAULT_VIEWPORT_MARGIN, Is.EqualTo(6.0));
            Assert.That(DropdownLogic.DEFAULT_SELECT_CHROME, Is.EqualTo(2.0));
            // Making chrome thicker raises the ideal value by that same amount
            Assert.That(
                DropdownLogic.GetDropdownTop(300.0, 2, ITEM_HEIGHT, VIEWPORT_HEIGHT, MARGIN, 10.0, LIST_HEIGHT),
                Is.EqualTo(240.0).Within(TOLERANCE));
        }

        #endregion

        #region GetDropdownMaxHeight

        [Test]
        public void MaxHeightNeverExceedsTheList()
        {
            double top = Top(300.0, 2);

            Assert.That(
                DropdownLogic.GetDropdownMaxHeight(top, LIST_HEIGHT, VIEWPORT_HEIGHT),
                Is.EqualTo(LIST_HEIGHT).Within(TOLERANCE));
        }

        [Test]
        public void OversizedListScrollsByTheOverflowingAmount()
        {
            const double tallList = 2000.0;

            double top = Top(400.0, 30, tallList);
            double maxHeight = DropdownLogic.GetDropdownMaxHeight(top, tallList, VIEWPORT_HEIGHT);

            // Extends to the full height minus the top/bottom 6px margin; the rest is shown via internal scrolling
            Assert.That(maxHeight, Is.EqualTo(788.0).Within(TOLERANCE));
            Assert.That(tallList - maxHeight, Is.EqualTo(1212.0).Within(TOLERANCE));
        }

        [Test]
        public void UnmeasuredListFillsDownToTheViewportEdge()
        {
            Assert.That(
                DropdownLogic.GetDropdownMaxHeight(100.0, 0.0, VIEWPORT_HEIGHT),
                Is.EqualTo(694.0).Within(TOLERANCE));
        }

        [Test]
        public void MaxHeightNeverGoesNegative()
        {
            Assert.That(
                DropdownLogic.GetDropdownMaxHeight(900.0, LIST_HEIGHT, VIEWPORT_HEIGHT),
                Is.EqualTo(0.0).Within(TOLERANCE));
        }

        #endregion
    }
}
