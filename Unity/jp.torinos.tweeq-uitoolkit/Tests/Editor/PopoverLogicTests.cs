using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class PopoverLogicTests
    {
        const double TOLERANCE = 1e-9;

        // left=100 top=100 right=160 bottom=120 / center=(130, 110)
        static readonly TweeqRect Anchor = new TweeqRect(100.0, 100.0, 60.0, 20.0);
        static readonly TweeqVec2 Size = new TweeqVec2(40.0, 30.0);

        // A viewport wide enough that neither flip nor shift occurs.
        static readonly TweeqVec2 Viewport = new TweeqVec2(1000.0, 1000.0);

        static PopoverResult ResolveInWideViewport(
            PopoverPlacement placement, double offsetMain = 0.0, double offsetCross = 0.0)
        {
            return PopoverLogic.Resolve(Anchor, Size, Viewport, placement, offsetMain, offsetCross);
        }

        static void AssertPosition(PopoverResult result, double x, double y)
        {
            Assert.That(result.X, Is.EqualTo(x).Within(TOLERANCE));
            Assert.That(result.Y, Is.EqualTo(y).Within(TOLERANCE));
        }

        #region Basic placement

        [Test]
        public void TopPlacementsSitAboveTheAnchor()
        {
            AssertPosition(ResolveInWideViewport(PopoverPlacement.Top), 110.0, 70.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.TopStart), 100.0, 70.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.TopEnd), 120.0, 70.0);
        }

        [Test]
        public void BottomPlacementsSitBelowTheAnchor()
        {
            AssertPosition(ResolveInWideViewport(PopoverPlacement.Bottom), 110.0, 120.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.BottomStart), 100.0, 120.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.BottomEnd), 120.0, 120.0);
        }

        [Test]
        public void LeftPlacementsSitBesideTheAnchor()
        {
            AssertPosition(ResolveInWideViewport(PopoverPlacement.Left), 60.0, 95.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.LeftStart), 60.0, 100.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.LeftEnd), 60.0, 90.0);
        }

        [Test]
        public void RightPlacementsSitBesideTheAnchor()
        {
            AssertPosition(ResolveInWideViewport(PopoverPlacement.Right), 160.0, 95.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.RightStart), 160.0, 100.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.RightEnd), 160.0, 90.0);
        }

        [Test]
        public void PlacementIsKeptWhenItFits()
        {
            Assert.That(ResolveInWideViewport(PopoverPlacement.BottomStart).Effective,
                Is.EqualTo(PopoverPlacement.BottomStart));
            Assert.That(ResolveInWideViewport(PopoverPlacement.Left).Effective,
                Is.EqualTo(PopoverPlacement.Left));
        }

        [Test]
        public void MainOffsetPushesAwayAndCrossOffsetSlidesInward()
        {
            // start applies inward from the start edge, and end applies inward from the end edge (same direction as CSS margin).
            AssertPosition(ResolveInWideViewport(PopoverPlacement.BottomStart, 6.0, 4.0), 104.0, 126.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.TopEnd, 6.0, 4.0), 116.0, 64.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.RightStart, 6.0, 4.0), 166.0, 104.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.LeftEnd, 6.0, 4.0), 54.0, 86.0);
        }

        [Test]
        public void CenteredPlacementsIgnoreCrossOffset()
        {
            AssertPosition(ResolveInWideViewport(PopoverPlacement.Bottom, 0.0, 50.0), 110.0, 120.0);
            AssertPosition(ResolveInWideViewport(PopoverPlacement.Right, 0.0, 50.0), 160.0, 95.0);
        }

        #endregion

        #region Flip

        [Test]
        public void FlipBlockTurnsBottomIntoTop()
        {
            var anchor = new TweeqRect(100.0, 170.0, 60.0, 20.0);
            var size = new TweeqVec2(60.0, 30.0);
            var viewport = new TweeqVec2(1000.0, 200.0);

            PopoverResult result = PopoverLogic.Resolve(
                anchor, size, viewport, PopoverPlacement.BottomStart);

            Assert.That(result.Effective, Is.EqualTo(PopoverPlacement.TopStart));
            AssertPosition(result, 100.0, 140.0);
            Assert.That(result.ArrowSide, Is.EqualTo(PopoverLogic.ARROW_SIDE_BOTTOM));
            Assert.That(result.ArrowOffset, Is.EqualTo(30.0).Within(TOLERANCE));
        }

        [Test]
        public void FlipInlineTurnsRightIntoLeft()
        {
            var anchor = new TweeqRect(150.0, 100.0, 40.0, 20.0);
            var size = new TweeqVec2(60.0, 30.0);
            var viewport = new TweeqVec2(200.0, 1000.0);

            PopoverResult result = PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.Right);

            // flip-block can't change Right, so the second candidate, flip-inline, is adopted.
            Assert.That(result.Effective, Is.EqualTo(PopoverPlacement.Left));
            AssertPosition(result, 90.0, 95.0);
            Assert.That(result.ArrowSide, Is.EqualTo(PopoverLogic.ARROW_SIDE_RIGHT));
        }

        [Test]
        public void FlipBlockSwapsStartAndEndOnVerticalSides()
        {
            var anchor = new TweeqRect(300.0, 150.0, 40.0, 20.0);
            var size = new TweeqVec2(40.0, 60.0);
            var viewport = new TweeqVec2(1000.0, 200.0);

            PopoverResult result = PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.LeftStart);

            // For a left/right placement, the block axis is the cross axis, so flip-block becomes start<->end.
            Assert.That(result.Effective, Is.EqualTo(PopoverPlacement.LeftEnd));
            AssertPosition(result, 260.0, 110.0);
            Assert.That(result.ArrowSide, Is.EqualTo(PopoverLogic.ARROW_SIDE_RIGHT));
        }

        [Test]
        public void BothFlipsAreTriedLastInTheCorner()
        {
            var anchor = new TweeqRect(150.0, 170.0, 40.0, 20.0);
            var size = new TweeqVec2(60.0, 30.0);
            var viewport = new TweeqVec2(200.0, 200.0);

            PopoverResult result = PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.BottomStart);

            // Neither flip-block(TopStart) nor flip-inline(BottomEnd) fits; only TopEnd, with both applied, fits.
            Assert.That(result.Effective, Is.EqualTo(PopoverPlacement.TopEnd));
            AssertPosition(result, 130.0, 140.0);
        }

        [Test]
        public void FlipInlineSwapsStartAndEndOnHorizontalSides()
        {
            var anchor = new TweeqRect(150.0, 100.0, 40.0, 20.0);
            var size = new TweeqVec2(60.0, 30.0);
            var viewport = new TweeqVec2(200.0, 1000.0);

            PopoverResult result = PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.BottomStart);

            // Only the horizontal overflows, so flip-block(TopStart) has no effect; flip-inline's BottomEnd is adopted instead.
            Assert.That(result.Effective, Is.EqualTo(PopoverPlacement.BottomEnd));
            AssertPosition(result, 130.0, 120.0);
            Assert.That(result.ArrowSide, Is.EqualTo(PopoverLogic.ARROW_SIDE_TOP));
            Assert.That(result.ArrowOffset, Is.EqualTo(40.0).Within(TOLERANCE));
        }

        [Test]
        public void OriginalPlacementSurvivesWhenNoCandidateFits()
        {
            var anchor = new TweeqRect(20.0, 20.0, 10.0, 10.0);
            var size = new TweeqVec2(100.0, 100.0);
            var viewport = new TweeqVec2(50.0, 50.0);

            PopoverResult result = PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.TopStart);

            Assert.That(result.Effective, Is.EqualTo(PopoverPlacement.TopStart));
            // Nothing fits anywhere, so it's just pushed to the top-left corner that respects the margin.
            AssertPosition(result, 8.0, 8.0);
        }

        #endregion

        #region Shift and clamp

        [Test]
        public void RightOverflowIsShiftedBackByViewportMargin()
        {
            var anchor = new TweeqRect(180.0, 100.0, 20.0, 20.0);
            var size = new TweeqVec2(60.0, 30.0);
            var viewport = new TweeqVec2(200.0, 1000.0);

            PopoverResult result = PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.Bottom);

            // Flipping to either the left or right still doesn't fit, so it stays Bottom and shifts along the cross axis instead.
            Assert.That(result.Effective, Is.EqualTo(PopoverPlacement.Bottom));
            AssertPosition(result, 132.0, 120.0);
            Assert.That(result.X + size.X, Is.EqualTo(viewport.X - 8.0).Within(TOLERANCE));
        }

        [Test]
        public void LeftOverflowWinsOverRightOverflow()
        {
            var anchor = new TweeqRect(0.0, 100.0, 20.0, 20.0);
            var size = new TweeqVec2(60.0, 30.0);
            var viewport = new TweeqVec2(200.0, 1000.0);

            PopoverResult result = PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.Bottom);

            AssertPosition(result, 8.0, 120.0);
        }

        [Test]
        public void PopoverWiderThanViewportKeepsTheStartEdgeVisible()
        {
            var anchor = new TweeqRect(40.0, 100.0, 20.0, 20.0);
            var size = new TweeqVec2(300.0, 30.0);
            var viewport = new TweeqVec2(200.0, 1000.0);

            PopoverResult result = PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.Bottom);

            // When both edges overflow, the start side (left edge) is prioritized (as noted in Popover.vue's own comment).
            Assert.That(result.X, Is.EqualTo(8.0).Within(TOLERANCE));
        }

        [Test]
        public void ViewportMarginDefaultsToEight()
        {
            var anchor = new TweeqRect(180.0, 100.0, 20.0, 20.0);
            var size = new TweeqVec2(60.0, 30.0);
            var viewport = new TweeqVec2(200.0, 1000.0);

            double defaulted = PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.Bottom).X;
            double explicitEight = PopoverLogic
                .Resolve(anchor, size, viewport, PopoverPlacement.Bottom, 0.0, 0.0, 8.0).X;
            double zeroMargin = PopoverLogic
                .Resolve(anchor, size, viewport, PopoverPlacement.Bottom, 0.0, 0.0, 0.0).X;

            Assert.That(defaulted, Is.EqualTo(explicitEight).Within(TOLERANCE));
            Assert.That(zeroMargin, Is.EqualTo(140.0).Within(TOLERANCE));
        }

        #endregion

        #region Arrow

        [Test]
        public void ArrowPointsAtTheAnchorFacingEdge()
        {
            Assert.That(ResolveInWideViewport(PopoverPlacement.BottomStart).ArrowSide,
                Is.EqualTo(PopoverLogic.ARROW_SIDE_TOP));
            Assert.That(ResolveInWideViewport(PopoverPlacement.TopStart).ArrowSide,
                Is.EqualTo(PopoverLogic.ARROW_SIDE_BOTTOM));
            Assert.That(ResolveInWideViewport(PopoverPlacement.RightStart).ArrowSide,
                Is.EqualTo(PopoverLogic.ARROW_SIDE_LEFT));
            Assert.That(ResolveInWideViewport(PopoverPlacement.LeftStart).ArrowSide,
                Is.EqualTo(PopoverLogic.ARROW_SIDE_RIGHT));
        }

        [Test]
        public void OverlappingPopoverFallsBackToTheRequestedSide()
        {
            // Sized to fully cover the anchor, i.e. no edge can be determined, so the opposite side of requestedSide is used.
            var anchor = new TweeqRect(20.0, 20.0, 10.0, 10.0);
            var size = new TweeqVec2(100.0, 100.0);
            var viewport = new TweeqVec2(50.0, 50.0);

            Assert.That(PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.TopStart).ArrowSide,
                Is.EqualTo(PopoverLogic.ARROW_SIDE_BOTTOM));
            Assert.That(PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.BottomEnd).ArrowSide,
                Is.EqualTo(PopoverLogic.ARROW_SIDE_TOP));
            Assert.That(PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.Right).ArrowSide,
                Is.EqualTo(PopoverLogic.ARROW_SIDE_LEFT));
            // The case where the Vue original just used 'right' outright. With a Left request, the right edge is actually correct.
            Assert.That(PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.LeftEnd).ArrowSide,
                Is.EqualTo(PopoverLogic.ARROW_SIDE_RIGHT));
        }

        [Test]
        public void ArrowFollowsTheAnchorCenter()
        {
            var anchor = new TweeqRect(100.0, 100.0, 60.0, 20.0);
            var size = new TweeqVec2(120.0, 30.0);

            PopoverResult result = PopoverLogic.Resolve(
                anchor, size, Viewport, PopoverPlacement.BottomStart);

            // anchor center 130 - popover left edge 100
            Assert.That(result.ArrowOffset, Is.EqualTo(30.0).Within(TOLERANCE));

            PopoverResult centered = PopoverLogic.Resolve(anchor, size, Viewport, PopoverPlacement.Bottom);

            Assert.That(centered.ArrowOffset, Is.EqualTo(60.0).Within(TOLERANCE));
        }

        [Test]
        public void ArrowOffsetIsClampedInsideTheRoundedCorners()
        {
            var anchor = new TweeqRect(180.0, 100.0, 20.0, 20.0);
            var size = new TweeqVec2(60.0, 30.0);
            var viewport = new TweeqVec2(200.0, 1000.0);

            // As shift moves the popover away from the anchor, the arrow drifts toward the edge, but stops at radius+AW/2 = 20.
            PopoverResult shifted = PopoverLogic.Resolve(anchor, size, viewport, PopoverPlacement.Bottom);

            Assert.That(shifted.ArrowOffset, Is.EqualTo(size.X - 20.0).Within(TOLERANCE));

            var leftAnchor = new TweeqRect(0.0, 100.0, 20.0, 20.0);
            PopoverResult clampedToStart = PopoverLogic.Resolve(
                leftAnchor, size, viewport, PopoverPlacement.Bottom);

            Assert.That(clampedToStart.ArrowOffset, Is.EqualTo(20.0).Within(TOLERANCE));
        }

        [Test]
        public void ArrowSitsAtTheCenterWhenTheEdgeIsTooShort()
        {
            // When the edge is at most twice radius+AW/2, the clamp range inverts, so it's fixed to the center.
            var size = new TweeqVec2(30.0, 20.0);

            PopoverResult result = PopoverLogic.Resolve(Anchor, size, Viewport, PopoverPlacement.Bottom);

            Assert.That(result.ArrowSide, Is.EqualTo(PopoverLogic.ARROW_SIDE_TOP));
            Assert.That(result.ArrowOffset, Is.EqualTo(15.0).Within(TOLERANCE));
        }

        [Test]
        public void VerticalArrowOffsetUsesTheAnchorVerticalCenter()
        {
            var anchor = new TweeqRect(100.0, 100.0, 20.0, 60.0);
            var size = new TweeqVec2(40.0, 120.0);

            PopoverResult result = PopoverLogic.Resolve(anchor, size, Viewport, PopoverPlacement.RightStart);

            Assert.That(result.ArrowSide, Is.EqualTo(PopoverLogic.ARROW_SIDE_LEFT));
            // anchor center 130 - popover top edge 100
            Assert.That(result.ArrowOffset, Is.EqualTo(30.0).Within(TOLERANCE));
        }

        #endregion

        #region Rect helper

        [Test]
        public void RectExposesEdgesAndCenter()
        {
            var rect = TweeqRect.FromEdges(10.0, 20.0, 40.0, 60.0);

            Assert.That(rect.Width, Is.EqualTo(30.0).Within(TOLERANCE));
            Assert.That(rect.Height, Is.EqualTo(40.0).Within(TOLERANCE));
            Assert.That(rect.CenterX, Is.EqualTo(25.0).Within(TOLERANCE));
            Assert.That(rect.CenterY, Is.EqualTo(40.0).Within(TOLERANCE));
            Assert.That(rect.Right, Is.EqualTo(40.0).Within(TOLERANCE));
            Assert.That(rect.Bottom, Is.EqualTo(60.0).Within(TOLERANCE));
        }

        #endregion
    }
}
