using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class DropdownLogicTests
    {
        const double TOLERANCE = 1e-9;

        // theme.inputHeight = 24 / SELECT_CHROME = 2 / options 5 件
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
            // 現在値が options に無い（indexOf = -1）ケース
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

            // 収まらないリストは下端まで伸びる前提なので、maxTop は「1 行ぶん」まで下げてよい
            Assert.That(Top(780.0, 0, tallList),
                Is.EqualTo(VIEWPORT_HEIGHT - MARGIN - ITEM_HEIGHT).Within(TOLERANCE));
            // 選択が下の方なら理想値どおり上へ抜け、margin で止まる
            Assert.That(Top(400.0, 30, tallList), Is.EqualTo(MARGIN).Within(TOLERANCE));
            Assert.That(Top(400.0, 0, tallList), Is.EqualTo(396.0).Within(TOLERANCE));
        }

        [Test]
        public void UnmeasuredListIsTreatedAsOversized()
        {
            // 実測前は下端クランプを厳しい側（1 行ぶん）に倒す
            Assert.That(Top(780.0, 0, 0.0),
                Is.EqualTo(VIEWPORT_HEIGHT - MARGIN - ITEM_HEIGHT).Within(TOLERANCE));
            // 理想値が十分に上ならそのまま
            Assert.That(Top(300.0, 2, 0.0), Is.EqualTo(248.0).Within(TOLERANCE));
        }

        [Test]
        public void MarginWinsWhenTheViewportIsShorterThanTheList()
        {
            // maxTop が margin を下回る極端なケースでも上端の余白を守る（負の top を返さない）
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
            // chrome を厚くすると理想値がその分だけ上がる
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

            // 上下 margin 6px を除いた全高まで伸び、残りは内部スクロールで見せる
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
