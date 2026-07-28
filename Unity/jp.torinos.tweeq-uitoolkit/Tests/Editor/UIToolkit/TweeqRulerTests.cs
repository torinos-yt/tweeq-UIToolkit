using System.Collections.Generic;
using NUnit.Framework;
using Tweeq.UIToolkit.TestSupport;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies TweeqRuler and the TweeqRulerScales helpers (m10-timeline-spec.md "Test Contract").
    ///
    /// EditMode runs no layout pass, so the width goes through SetViewportWidth, the same seam
    /// GeometryChangedEvent uses. Tick and label rendering is the Play Mode side's responsibility;
    /// what is pinned down here is which ticks are produced and where they map to.
    /// </summary>
    public class TweeqRulerTests
    {
        const double EPSILON = 1e-6;
        const float PIXEL_EPSILON = 1e-3f;

        // 300px over 10 units is 30px per unit, which keeps the expected values readable.
        const float VIEWPORT = 300f;

        TweeqRuntimeTestPanel _panel;

        [TearDown]
        public void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
        }

        static TweeqRuler Arrange()
        {
            TweeqRuler ruler = new TweeqRuler
            {
                RangeStart = 0.0,
                RangeEnd = 10.0,
            };
            ruler.SetViewportWidth(VIEWPORT);
            return ruler;
        }

        #region Defaults

        [Test]
        public void Defaults_AreAUnitRange()
        {
            TweeqRuler ruler = new TweeqRuler();

            Assert.AreEqual(0.0, ruler.RangeStart, EPSILON);
            Assert.AreEqual(1.0, ruler.RangeEnd, EPSILON);
            Assert.IsNull(ruler.Scales);
            Assert.AreEqual(0f, ruler.PixelsPerUnit, PIXEL_EPSILON);
        }

        #endregion

        #region Mapping

        [Test]
        public void PixelsPerUnit_DividesTheWidthByTheSpan()
        {
            TweeqRuler ruler = Arrange();

            Assert.AreEqual(30f, ruler.PixelsPerUnit, PIXEL_EPSILON);
        }

        [Test]
        public void ValueToLocalX_MapsTheRangeOntoTheWidth()
        {
            TweeqRuler ruler = Arrange();

            Assert.AreEqual(0f, ruler.ValueToLocalX(0.0), PIXEL_EPSILON);
            Assert.AreEqual(150f, ruler.ValueToLocalX(5.0), PIXEL_EPSILON);
            Assert.AreEqual(300f, ruler.ValueToLocalX(10.0), PIXEL_EPSILON);
        }

        [Test]
        public void ValueToLocalX_HonoursAnOffsetRangeStart()
        {
            TweeqRuler ruler = Arrange();
            ruler.RangeStart = 20.0;
            ruler.RangeEnd = 30.0;

            Assert.AreEqual(0f, ruler.ValueToLocalX(20.0), PIXEL_EPSILON);
            Assert.AreEqual(150f, ruler.ValueToLocalX(25.0), PIXEL_EPSILON);
        }

        [Test]
        public void LocalXToValue_IsTheInverse()
        {
            TweeqRuler ruler = Arrange();

            Assert.AreEqual(5.0, ruler.LocalXToValue(150f), EPSILON);
        }

        [Test]
        public void MappingIsInertWithoutAWidth()
        {
            TweeqRuler ruler = new TweeqRuler { RangeEnd = 10.0 };

            Assert.AreEqual(0f, ruler.ValueToLocalX(5.0), PIXEL_EPSILON);
            Assert.AreEqual(0.0, ruler.LocalXToValue(150f), EPSILON);
        }

        #endregion

        #region Automatic scales

        [Test]
        public void AutoScales_AreOnePerIntegerValue()
        {
            TweeqRuler ruler = Arrange();

            IList<RulerScale> scales = ruler.ResolvedScales;

            Assert.AreEqual(11, scales.Count);
            Assert.AreEqual(0.0, scales[0].Value, EPSILON);
            Assert.AreEqual(10.0, scales[10].Value, EPSILON);
        }

        [Test]
        public void AutoScales_CarryTheValueAsTheirLabel()
        {
            TweeqRuler ruler = Arrange();

            Assert.AreEqual("7", ruler.ResolvedScales[7].Label);
            Assert.AreEqual(1f, ruler.ResolvedScales[7].Opacity, PIXEL_EPSILON);
        }

        [Test]
        public void AutoScales_RoundInwardsOnAFractionalRange()
        {
            TweeqRuler ruler = Arrange();
            ruler.RangeStart = 2.4;
            ruler.RangeEnd = 6.7;

            IList<RulerScale> scales = ruler.ResolvedScales;

            Assert.AreEqual(4, scales.Count);
            Assert.AreEqual(3.0, scales[0].Value, EPSILON);
            Assert.AreEqual(6.0, scales[3].Value, EPSILON);
        }

        [Test]
        public void AutoScales_AreEmptyOnAnInvertedRange()
        {
            TweeqRuler ruler = Arrange();
            ruler.RangeStart = 10.0;
            ruler.RangeEnd = 0.0;

            Assert.AreEqual(0, ruler.ResolvedScales.Count);
        }

        [Test]
        public void ExplicitScales_WinOverTheAutomaticOnes()
        {
            TweeqRuler ruler = Arrange();
            List<RulerScale> scales = new List<RulerScale>
            {
                new RulerScale(1.0, "one"),
                new RulerScale(2.0, null, 0.5f),
            };

            ruler.Scales = scales;

            Assert.AreEqual(2, ruler.ResolvedScales.Count);
            Assert.AreEqual("one", ruler.ResolvedScales[0].Label);
            Assert.IsNull(ruler.ResolvedScales[1].Label);
            Assert.AreEqual(0.5f, ruler.ResolvedScales[1].Opacity, PIXEL_EPSILON);

            ruler.Scales = null;
            Assert.AreEqual(11, ruler.ResolvedScales.Count);
        }

        #endregion

        #region Drag

        [Test]
        public void Drag_ReportsTheValueOnPressWithNoThreshold()
        {
            _panel = TweeqRuntimeTestPanel.Create();
            TweeqRuler ruler = Arrange();
            _panel.Root.Add(ruler);
            ruler.CapturePointer(PointerId.mousePointerId);

            List<double> reported = new List<double>();
            ruler.Dragged += value => reported.Add(value);

            SendPointer(ruler, EventType.MouseDown, new Vector2(150f, 5f));

            Assert.AreEqual(1, reported.Count);
            Assert.AreEqual(5.0, reported[0], EPSILON);

            SendPointer(ruler, EventType.MouseUp, new Vector2(150f, 5f));
        }

        [Test]
        public void Drag_KeepsReportingWhileMoving()
        {
            _panel = TweeqRuntimeTestPanel.Create();
            TweeqRuler ruler = Arrange();
            _panel.Root.Add(ruler);
            ruler.CapturePointer(PointerId.mousePointerId);

            List<double> reported = new List<double>();
            ruler.Dragged += value => reported.Add(value);

            SendPointer(ruler, EventType.MouseDown, new Vector2(60f, 5f));
            SendPointer(ruler, EventType.MouseDrag, new Vector2(120f, 5f));
            SendPointer(ruler, EventType.MouseDrag, new Vector2(240f, 5f));
            SendPointer(ruler, EventType.MouseUp, new Vector2(240f, 5f));

            Assert.AreEqual(3, reported.Count);
            Assert.AreEqual(2.0, reported[0], EPSILON);
            Assert.AreEqual(4.0, reported[1], EPSILON);
            Assert.AreEqual(8.0, reported[2], EPSILON);
        }

        [Test]
        public void Drag_ClampsToTheRangeLikeScalarFit()
        {
            _panel = TweeqRuntimeTestPanel.Create();
            TweeqRuler ruler = Arrange();
            _panel.Root.Add(ruler);
            ruler.CapturePointer(PointerId.mousePointerId);

            List<double> reported = new List<double>();
            ruler.Dragged += value => reported.Add(value);

            SendPointer(ruler, EventType.MouseDown, new Vector2(150f, 5f));
            SendPointer(ruler, EventType.MouseDrag, new Vector2(-500f, 5f));
            SendPointer(ruler, EventType.MouseDrag, new Vector2(900f, 5f));
            SendPointer(ruler, EventType.MouseUp, new Vector2(900f, 5f));

            Assert.AreEqual(0.0, reported[1], EPSILON);
            Assert.AreEqual(10.0, reported[2], EPSILON);
        }

        [Test]
        public void Drag_StopsAfterTheRelease()
        {
            _panel = TweeqRuntimeTestPanel.Create();
            TweeqRuler ruler = Arrange();
            _panel.Root.Add(ruler);
            ruler.CapturePointer(PointerId.mousePointerId);

            List<double> reported = new List<double>();
            ruler.Dragged += value => reported.Add(value);

            SendPointer(ruler, EventType.MouseDown, new Vector2(150f, 5f));
            SendPointer(ruler, EventType.MouseUp, new Vector2(150f, 5f));
            SendPointer(ruler, EventType.MouseDrag, new Vector2(240f, 5f));

            Assert.AreEqual(1, reported.Count);
        }

        #endregion

        #region Theme

        [Test]
        public void Theme_FallsBackToDarkOnNull()
        {
            TweeqRuler ruler = Arrange();

            ruler.Theme = null;

            Assert.IsNotNull(ruler.Theme);
        }

        [Test]
        public void Theme_ArrivesThroughDistribution()
        {
            TweeqRuler ruler = Arrange();
            VisualElement host = new VisualElement();
            host.Add(ruler);

            TweeqTheme theme = TweeqTheme.Light();
            TweeqThemeDistribution.Distribute(host, theme);

            Assert.AreSame(theme, ruler.Theme);
        }

        #endregion

        #region TweeqRulerScales

        [Test]
        public void Build_ThinsToARoundStepThatHonoursTheGap()
        {
            List<RulerScale> scales = TweeqRulerScales.Build(0.0, 100.0, 48.0, 600f);

            // 600px over 100 units needs at least 8 units per label, so the 1-2-5 ladder lands on 10.
            Assert.AreEqual(11, scales.Count);
            Assert.AreEqual(0.0, scales[0].Value, EPSILON);
            Assert.AreEqual(10.0, scales[1].Value, EPSILON);
            Assert.AreEqual(100.0, scales[10].Value, EPSILON);
            Assert.AreEqual("20", scales[2].Label);
        }

        [Test]
        public void Build_KeepsEveryUnitWhenThereIsRoom()
        {
            List<RulerScale> scales = TweeqRulerScales.Build(0.0, 10.0, 48.0, 600f);

            Assert.AreEqual(11, scales.Count);
            Assert.AreEqual("3", scales[3].Label);
        }

        [Test]
        public void Build_StartsAtTheFirstRoundValueInsideTheRange()
        {
            List<RulerScale> scales = TweeqRulerScales.Build(37.0, 100.0, 48.0, 600f);

            Assert.AreEqual(40.0, scales[0].Value, EPSILON);
        }

        [Test]
        public void Build_GoesSubUnitWhenZoomedFarIn()
        {
            List<RulerScale> scales = TweeqRulerScales.Build(0.0, 1.0, 48.0, 600f);

            Assert.AreEqual(0.1, TweeqRulerScales.NiceStep(0.0, 1.0, 48.0, 600f), EPSILON);
            Assert.AreEqual("0.1", scales[1].Label);
        }

        [Test]
        public void Build_IsEmptyOnADegenerateRange()
        {
            Assert.AreEqual(0, TweeqRulerScales.Build(10.0, 10.0, 48.0, 600f).Count);
            Assert.AreEqual(0, TweeqRulerScales.Build(0.0, 100.0, 48.0, 0f).Count);
        }

        [Test]
        public void Build_FillsACallerOwnedBuffer()
        {
            List<RulerScale> buffer = new List<RulerScale> { new RulerScale(999.0, "stale") };

            TweeqRulerScales.Build(buffer, 0.0, 100.0, 48.0, 600f);

            Assert.AreEqual(11, buffer.Count);
            Assert.AreEqual(0.0, buffer[0].Value, EPSILON);
        }

        [Test]
        public void BuildTimecode_StepsOnWholeSeconds()
        {
            List<RulerScale> scales = TweeqRulerScales.BuildTimecode(0.0, 240.0, 24.0, 60.0, 600f);

            Assert.AreEqual(11, scales.Count);
            Assert.AreEqual(24.0, scales[1].Value, EPSILON);
            Assert.AreEqual("00:00:00", scales[0].Label);
            Assert.AreEqual("00:01:00", scales[1].Label);
            Assert.AreEqual("00:10:00", scales[10].Label);
        }

        [Test]
        public void BuildTimecode_FallsBackToWholeFramesWhenZoomedIn()
        {
            double step = TweeqRulerScales.TimecodeStep(0.0, 48.0, 24.0, 48.0, 600f);

            // 600px over 48 frames leaves room for a label every 4 frames, so the ladder lands on 5.
            Assert.AreEqual(5.0, step, EPSILON);
        }

        // The same frame count reads as a different time, which is the whole point of a variable rate.
        [Test]
        public void BuildTimecode_FollowsTheFrameRate()
        {
            List<RulerScale> at24 = TweeqRulerScales.BuildTimecode(0.0, 240.0, 24.0, 60.0, 600f);
            List<RulerScale> at60 = TweeqRulerScales.BuildTimecode(0.0, 240.0, 60.0, 60.0, 600f);

            // At 24fps a whole second is the finest step that still fits the gap.
            Assert.AreEqual(24.0, at24[1].Value, EPSILON);
            Assert.AreEqual("00:01:00", at24[1].Label);

            // At 60fps the same gap has room for half a second, so it stays on a frame step.
            Assert.AreEqual(30.0, at60[1].Value, EPSILON);
            Assert.AreEqual("00:00:30", at60[1].Label);
            Assert.AreEqual(120.0, at60[4].Value, EPSILON);
            Assert.AreEqual("00:02:00", at60[4].Label);
        }

        [Test]
        public void BuildTimecode_IsEmptyOnADegenerateFrameRate()
        {
            Assert.AreEqual(0, TweeqRulerScales.BuildTimecode(0.0, 240.0, 0.0, 48.0, 600f).Count);
        }

        [Test]
        public void Scales_FeedTheRulerDirectly()
        {
            TweeqRuler ruler = Arrange();
            ruler.RangeStart = 0.0;
            ruler.RangeEnd = 100.0;

            ruler.Scales = TweeqRulerScales.Build(0.0, 100.0, 48.0, VIEWPORT);

            Assert.AreEqual(6, ruler.ResolvedScales.Count);
            Assert.AreEqual(60f, ruler.ValueToLocalX(20.0), PIXEL_EPSILON);
        }

        #endregion

        #region Event helpers

        static void SendPointer(VisualElement element, EventType type, Vector2 position)
        {
            Event systemEvent = new Event
            {
                type = type,
                mousePosition = position,
                button = 0,
                modifiers = EventModifiers.None,
            };

            if (type == EventType.MouseDown)
            {
                using (PointerDownEvent down = PointerDownEvent.GetPooled(systemEvent))
                {
                    element.SendEvent(down);
                }

                return;
            }

            if (type == EventType.MouseUp)
            {
                using (PointerUpEvent up = PointerUpEvent.GetPooled(systemEvent))
                {
                    element.SendEvent(up);
                }

                return;
            }

            using (PointerMoveEvent move = PointerMoveEvent.GetPooled(systemEvent))
            {
                element.SendEvent(move);
            }
        }

        #endregion
    }
}
