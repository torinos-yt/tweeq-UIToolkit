using NUnit.Framework;

namespace Tweeq.Core.Tests
{
    /// <summary>
    /// Verifies the timeline viewport math against the Vue original's Timeline.vue
    /// (scrollBounds / clampRange / the Alt+scroll zoom / the .knob percentages / showRange).
    /// </summary>
    public class TimelineLogicTests
    {
        const double EPSILON = 1e-9;

        #region Constants

        [Test]
        public void Defaults_MatchTheOriginal()
        {
            Assert.AreEqual(60.0, TimelineLogic.DEFAULT_FRAME_WIDTH, EPSILON);
            Assert.AreEqual(10.0, TimelineLogic.DEFAULT_FRAME_WIDTH_MIN, EPSILON);
            Assert.AreEqual(100.0, TimelineLogic.DEFAULT_FRAME_WIDTH_MAX, EPSILON);
            Assert.AreEqual(0.5, TimelineLogic.DEFAULT_OVERSCROLL, EPSILON);
            Assert.AreEqual(1.003, TimelineLogic.ZOOM_BASE, EPSILON);
            Assert.AreEqual(300L, TimelineLogic.CONFIRM_DEBOUNCE_MS);
        }

        #endregion

        #region ScrollBounds

        [Test]
        public void ScrollBounds_WithoutOverscroll_StopsAtTheContentEdges()
        {
            (double min, double max) = TimelineLogic.ScrollBounds(0.0, 100.0, 20.0, 0.0);

            Assert.AreEqual(0.0, min, EPSILON);
            Assert.AreEqual(80.0, max, EPSILON);
        }

        [Test]
        public void ScrollBounds_AddsHalfTheViewportOnEachSide()
        {
            (double min, double max) = TimelineLogic.ScrollBounds(0.0, 100.0, 20.0, 0.5);

            Assert.AreEqual(-10.0, min, EPSILON);
            Assert.AreEqual(90.0, max, EPSILON);
        }

        [Test]
        public void ScrollBounds_ShiftsWithTheContentStart()
        {
            (double min, double max) = TimelineLogic.ScrollBounds(50.0, 150.0, 20.0, 0.5);

            Assert.AreEqual(40.0, min, EPSILON);
            Assert.AreEqual(140.0, max, EPSILON);
        }

        #endregion

        #region ClampRange

        [Test]
        public void ClampRange_LeavesAWindowInsideTheLimitsAlone()
        {
            (double start, double end) =
                TimelineLogic.ClampRange(50.0, 70.0, 0.0, 100.0, 0.5);

            Assert.AreEqual(50.0, start, EPSILON);
            Assert.AreEqual(70.0, end, EPSILON);
        }

        [Test]
        public void ClampRange_PullsBackToTheLeftLimitKeepingTheDuration()
        {
            (double start, double end) =
                TimelineLogic.ClampRange(-50.0, -30.0, 0.0, 100.0, 0.5);

            Assert.AreEqual(-10.0, start, EPSILON);
            Assert.AreEqual(10.0, end, EPSILON);
        }

        [Test]
        public void ClampRange_PullsBackToTheRightLimitKeepingTheDuration()
        {
            (double start, double end) =
                TimelineLogic.ClampRange(200.0, 220.0, 0.0, 100.0, 0.5);

            Assert.AreEqual(90.0, start, EPSILON);
            Assert.AreEqual(110.0, end, EPSILON);
        }

        [Test]
        public void ClampRange_WithoutOverscroll_StopsAtTheContentEdge()
        {
            (double start, double end) =
                TimelineLogic.ClampRange(-50.0, -30.0, 0.0, 100.0, 0.0);

            Assert.AreEqual(0.0, start, EPSILON);
            Assert.AreEqual(20.0, end, EPSILON);
        }

        // A viewport wider than the content plus both margins inverts the limits, and snapping to
        // either one would be arbitrary, so the position must survive untouched.
        [Test]
        public void ClampRange_LeavesThePositionAloneWhenTheLimitsInvert()
        {
            (double start, double end) =
                TimelineLogic.ClampRange(999.0, 1199.0, 0.0, 100.0, 0.0);

            Assert.AreEqual(999.0, start, EPSILON);
            Assert.AreEqual(1199.0, end, EPSILON);
        }

        #endregion

        #region ZoomAroundAnchor

        [Test]
        public void ZoomAroundAnchor_AtTheLeftEdge_KeepsTheStart()
        {
            double start = TimelineLogic.ZoomAroundAnchor(10.0, 20.0, 10.0, 0.0);

            Assert.AreEqual(10.0, start, EPSILON);
        }

        [Test]
        public void ZoomAroundAnchor_AtTheCenter_KeepsTheCenterFrame()
        {
            double start = TimelineLogic.ZoomAroundAnchor(10.0, 20.0, 10.0, 0.5);

            // The frame under the anchor was 20; it must still sit at the anchor afterwards.
            Assert.AreEqual(15.0, start, EPSILON);
            Assert.AreEqual(20.0, start + 0.5 * 10.0, EPSILON);
        }

        [Test]
        public void ZoomAroundAnchor_AtTheRightEdge_KeepsTheEnd()
        {
            double start = TimelineLogic.ZoomAroundAnchor(10.0, 20.0, 10.0, 1.0);

            Assert.AreEqual(20.0, start, EPSILON);
            Assert.AreEqual(30.0, start + 10.0, EPSILON);
        }

        [Test]
        public void ZoomAroundAnchor_ClampsTheAnchorLikeScalarFit()
        {
            Assert.AreEqual(
                TimelineLogic.ZoomAroundAnchor(10.0, 20.0, 10.0, 1.0),
                TimelineLogic.ZoomAroundAnchor(10.0, 20.0, 10.0, 2.5),
                EPSILON);

            Assert.AreEqual(
                TimelineLogic.ZoomAroundAnchor(10.0, 20.0, 10.0, 0.0),
                TimelineLogic.ZoomAroundAnchor(10.0, 20.0, 10.0, -3.0),
                EPSILON);
        }

        [Test]
        public void ZoomAroundAnchor_ZoomingOutKeepsTheAnchorToo()
        {
            double start = TimelineLogic.ZoomAroundAnchor(10.0, 10.0, 20.0, 0.5);

            Assert.AreEqual(5.0, start, EPSILON);
            Assert.AreEqual(15.0, start + 0.5 * 20.0, EPSILON);
        }

        #endregion

        #region ScrollbarKnob

        [Test]
        public void ScrollbarKnob_WidthIsTheVisibleFractionOfTheContent()
        {
            (double leftT, double widthT) =
                TimelineLogic.ScrollbarKnob(0.0, 20.0, 0.0, 100.0);

            Assert.AreEqual(0.2, widthT, EPSILON);
            Assert.AreEqual(0.0, leftT, EPSILON);
        }

        [Test]
        public void ScrollbarKnob_OverhangsTheTrackAtTheRightmostScroll()
        {
            (double leftT, double widthT) =
                TimelineLogic.ScrollbarKnob(90.0, 110.0, 0.0, 100.0);

            Assert.AreEqual(0.2, widthT, EPSILON);
            Assert.AreEqual(0.9, leftT, EPSILON);
        }

        [Test]
        public void ScrollbarKnob_FillsTheTrackWhenEverythingIsVisible()
        {
            (double leftT, double widthT) =
                TimelineLogic.ScrollbarKnob(-50.0, 150.0, 0.0, 100.0);

            Assert.AreEqual(1.0, widthT, EPSILON);
            Assert.AreEqual(0.0, leftT, EPSILON);
        }

        [Test]
        public void ScrollbarKnob_ParksInTheMiddleWithoutTravel()
        {
            // overscroll 0 with a viewport wider than the content leaves nothing to scroll.
            (double leftT, double widthT) =
                TimelineLogic.ScrollbarKnob(0.0, 200.0, 0.0, 100.0, 0.0);

            Assert.AreEqual(1.0, widthT, EPSILON);
            Assert.AreEqual(0.0, leftT, EPSILON);
        }

        [Test]
        public void ScrollbarKnob_IsInertOnADegenerateRange()
        {
            (double leftT, double widthT) =
                TimelineLogic.ScrollbarKnob(0.0, 0.0, 0.0, 0.0);

            Assert.AreEqual(0.0, leftT, EPSILON);
            Assert.AreEqual(1.0, widthT, EPSILON);
        }

        #endregion

        #region BringIntoView

        [Test]
        public void BringIntoView_DoesNotMoveForATargetAlreadyOnScreen()
        {
            (double start, double end) =
                TimelineLogic.BringIntoView(10.0, 30.0, 15.0, 20.0);

            Assert.AreEqual(10.0, start, EPSILON);
            Assert.AreEqual(30.0, end, EPSILON);
        }

        [Test]
        public void BringIntoView_AlignsToTheLeftForATargetBefore()
        {
            (double start, double end) =
                TimelineLogic.BringIntoView(10.0, 30.0, 0.0, 5.0);

            Assert.AreEqual(0.0, start, EPSILON);
            Assert.AreEqual(20.0, end, EPSILON);
        }

        [Test]
        public void BringIntoView_AlignsToTheRightForATargetAfter()
        {
            (double start, double end) =
                TimelineLogic.BringIntoView(10.0, 30.0, 40.0, 45.0);

            Assert.AreEqual(25.0, start, EPSILON);
            Assert.AreEqual(45.0, end, EPSILON);
        }

        [Test]
        public void BringIntoView_TakesTheTargetVerbatimWhenItOverflowsBothSides()
        {
            (double start, double end) =
                TimelineLogic.BringIntoView(10.0, 30.0, 5.0, 40.0);

            Assert.AreEqual(5.0, start, EPSILON);
            Assert.AreEqual(40.0, end, EPSILON);
        }

        [Test]
        public void BringIntoView_TreatsAnEdgeTouchAsVisible()
        {
            (double start, double end) =
                TimelineLogic.BringIntoView(10.0, 30.0, 10.0, 30.0);

            Assert.AreEqual(10.0, start, EPSILON);
            Assert.AreEqual(30.0, end, EPSILON);
        }

        #endregion
    }
}
