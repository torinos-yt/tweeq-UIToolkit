using System.IO;
using NUnit.Framework;
using Tweeq.Core;
using Tweeq.UIToolkit.TestSupport;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Verifies TweeqTimeline's contract (m10-timeline-spec.md "Test Contract") with synthesized
    /// events.
    ///
    /// EditMode runs no layout pass, so the viewport width is driven through SetViewportWidth,
    /// which is the same seam GeometryChangedEvent uses. Actual painting of the In/Out band is
    /// the Play Mode side's responsibility; only the gate that decides whether it draws is pinned
    /// down here.
    /// </summary>
    public class TweeqTimelineTests
    {
        const double EPSILON = 1e-6;
        const float PIXEL_EPSILON = 1e-3f;

        // 600px at 60px/frame shows exactly 10 frames, which keeps the expected values readable.
        const float VIEWPORT = 600f;

        const string TEMP_FOLDER = "Assets/TweeqTimelineUxmlTests";
        const string TEMP_ASSET = TEMP_FOLDER + "/tweeq-timeline-uxml-test.uxml";

        TweeqRuntimeTestPanel _panel;

        [TearDown]
        public void TearDown()
        {
            _panel?.Dispose();
            _panel = null;

            if (AssetDatabase.IsValidFolder(TEMP_FOLDER))
            {
                AssetDatabase.DeleteAsset(TEMP_FOLDER);
            }
        }

        static TweeqTimeline Arrange()
        {
            TweeqTimeline timeline = new TweeqTimeline();
            timeline.SetViewportWidth(VIEWPORT);
            return timeline;
        }

        #region Defaults

        [Test]
        public void Defaults_MatchTheOriginal()
        {
            TweeqTimeline timeline = new TweeqTimeline();

            Assert.AreEqual(0.0, timeline.RangeStart, EPSILON);
            Assert.AreEqual(100.0, timeline.RangeEnd, EPSILON);
            Assert.AreEqual(60.0, timeline.FrameWidth, EPSILON);
            Assert.AreEqual(10.0, timeline.FrameWidthMin, EPSILON);
            Assert.AreEqual(100.0, timeline.FrameWidthMax, EPSILON);
            Assert.AreEqual(0.5, timeline.Overscroll, EPSILON);
            Assert.AreEqual(1.0, timeline.WheelSensitivity, EPSILON);
            Assert.IsNull(timeline.InPoint);
            Assert.IsNull(timeline.OutPoint);
            Assert.IsFalse(timeline.HasInOut);
        }

        [Test]
        public void VisibleRange_IsDerivedFromTheViewportWidth()
        {
            TweeqTimeline timeline = Arrange();

            Assert.AreEqual(10.0, timeline.VisibleFrames, EPSILON);
            Assert.AreEqual(0.0, timeline.VisibleStart, EPSILON);
            Assert.AreEqual(10.0, timeline.VisibleEnd, EPSILON);
        }

        [Test]
        public void ResizingRaisesVisibleRangeChanged()
        {
            TweeqTimeline timeline = Arrange();
            int changed = 0;
            timeline.VisibleRangeChanged += () => changed++;

            timeline.SetViewportWidth(300f);

            Assert.AreEqual(1, changed);
            Assert.AreEqual(5.0, timeline.VisibleFrames, EPSILON);
        }

        #endregion

        #region Zoom bounds

        [Test]
        public void FrameWidth_IsClampedToTheZoomBounds()
        {
            TweeqTimeline timeline = Arrange();

            timeline.FrameWidth = 5.0;
            Assert.AreEqual(10.0, timeline.FrameWidth, EPSILON);

            timeline.FrameWidth = 500.0;
            Assert.AreEqual(100.0, timeline.FrameWidth, EPSILON);
        }

        [Test]
        public void FrameWidth_IsRefoldedWhenTheBoundsMove()
        {
            TweeqTimeline timeline = Arrange();

            timeline.FrameWidthMax = 40.0;

            Assert.AreEqual(40.0, timeline.FrameWidth, EPSILON);
        }

        [Test]
        public void FrameWidth_AssignmentDoesNotEchoTheChangeEvent()
        {
            TweeqTimeline timeline = Arrange();
            int reported = 0;
            timeline.FrameWidthChanged += _ => reported++;

            timeline.FrameWidth = 30.0;

            Assert.AreEqual(0, reported);
        }

        #endregion

        #region Pan

        [Test]
        public void PanByPixels_MovesByPixelsOverFrameWidth()
        {
            TweeqTimeline timeline = Arrange();

            timeline.PanByPixels(30.0);

            Assert.AreEqual(0.5, timeline.VisibleStart, EPSILON);
        }

        [Test]
        public void PanByPixels_StopsAtTheOverscrollLimit()
        {
            TweeqTimeline timeline = Arrange();

            timeline.PanByPixels(-6000.0);

            // overscroll 0.5 of a 10 frame viewport lets the content start reach the middle.
            Assert.AreEqual(-5.0, timeline.VisibleStart, EPSILON);
        }

        [Test]
        public void Wheel_PansHorizontallyFromTheVerticalAxis()
        {
            _panel = TweeqRuntimeTestPanel.Create();
            TweeqTimeline timeline = Arrange();
            _panel.Root.Add(timeline);

            SendWheel(timeline, new Vector2(0f, 30f), new Vector2(0f, 0f), EventModifiers.None);

            Assert.AreEqual(0.5, timeline.VisibleStart, EPSILON);
        }

        [Test]
        public void Wheel_PrefersTheHorizontalAxisWhenPresent()
        {
            _panel = TweeqRuntimeTestPanel.Create();
            TweeqTimeline timeline = Arrange();
            _panel.Root.Add(timeline);

            SendWheel(timeline, new Vector2(60f, 30f), new Vector2(0f, 0f), EventModifiers.None);

            Assert.AreEqual(1.0, timeline.VisibleStart, EPSILON);
        }

        [Test]
        public void MiddleDrag_PansWithThePointer()
        {
            _panel = TweeqRuntimeTestPanel.Create();
            TweeqTimeline timeline = Arrange();
            _panel.Root.Add(timeline);
            timeline.CapturePointer(PointerId.mousePointerId);

            SendPointer(timeline, EventType.MouseDown, new Vector2(100f, 10f), 2);
            SendPointer(timeline, EventType.MouseDrag, new Vector2(220f, 10f), 2);

            // Dragging 120px to the right at 60px/frame pulls the window 2 frames back.
            Assert.AreEqual(-2.0, timeline.VisibleStart, EPSILON);

            SendPointer(timeline, EventType.MouseUp, new Vector2(220f, 10f), 2);
        }

        [Test]
        public void LeftDrag_DoesNotPan()
        {
            _panel = TweeqRuntimeTestPanel.Create();
            TweeqTimeline timeline = Arrange();
            _panel.Root.Add(timeline);
            timeline.CapturePointer(PointerId.mousePointerId);

            SendPointer(timeline, EventType.MouseDown, new Vector2(100f, 10f), 0);
            SendPointer(timeline, EventType.MouseDrag, new Vector2(220f, 10f), 0);

            Assert.AreEqual(0.0, timeline.VisibleStart, EPSILON);
        }

        #endregion

        #region Zoom

        [Test]
        public void Zoom_KeepsTheFrameUnderTheAnchorInPlace()
        {
            TweeqTimeline timeline = Arrange();
            double anchored = timeline.LocalXToFrame(300f);

            timeline.ApplyZoom(-100.0, 300f);

            Assert.Greater(timeline.FrameWidth, 60.0);
            Assert.AreEqual(300f, timeline.FrameToLocalX(anchored), PIXEL_EPSILON);
        }

        [Test]
        public void Zoom_KeepsTheLeftEdgeWhenAnchoredThere()
        {
            TweeqTimeline timeline = Arrange();

            timeline.ApplyZoom(-100.0, 0f);

            Assert.AreEqual(0.0, timeline.VisibleStart, EPSILON);
        }

        [Test]
        public void Zoom_UsesTheOriginalsExponentialBase()
        {
            TweeqTimeline timeline = Arrange();

            timeline.ApplyZoom(-100.0, 0f);

            double expected = 60.0 * System.Math.Pow(TimelineLogic.ZOOM_BASE, 100.0);
            Assert.AreEqual(expected, timeline.FrameWidth, 1e-6);
        }

        [Test]
        public void Zoom_ReportsTheLiveFrameWidth()
        {
            TweeqTimeline timeline = Arrange();
            double reported = 0.0;
            int count = 0;
            timeline.FrameWidthChanged += value =>
            {
                reported = value;
                count++;
            };

            timeline.ApplyZoom(-100.0, 0f);

            Assert.AreEqual(1, count);
            Assert.AreEqual(timeline.FrameWidth, reported, EPSILON);
        }

        [Test]
        public void Zoom_StopsAtTheZoomBounds()
        {
            TweeqTimeline timeline = Arrange();

            for (int index = 0; index < 20; index++)
            {
                timeline.ApplyZoom(-100.0, 0f);
            }

            Assert.AreEqual(100.0, timeline.FrameWidth, EPSILON);
        }

        [Test]
        public void Wheel_WithAlt_Zooms()
        {
            _panel = TweeqRuntimeTestPanel.Create();
            TweeqTimeline timeline = Arrange();
            _panel.Root.Add(timeline);

            SendWheel(timeline, new Vector2(0f, -100f), new Vector2(0f, 0f), EventModifiers.Alt);

            Assert.Greater(timeline.FrameWidth, 60.0);
            Assert.AreEqual(0.0, timeline.VisibleStart, EPSILON);
        }

        #endregion

        #region Confirm debounce

        [Test]
        public void Confirm_IsNotRaisedWhileZoomingContinues()
        {
            TweeqTimeline timeline = Arrange();
            int confirmed = 0;
            timeline.Confirmed += () => confirmed++;

            timeline.ApplyZoom(-100.0, 0f);
            timeline.ApplyZoom(-100.0, 0f);

            Assert.AreEqual(0, confirmed);
        }

        [Test]
        public void Confirm_IsRaisedOnceAfterTheZoomSettles()
        {
            TweeqTimeline timeline = Arrange();
            int confirmed = 0;
            timeline.Confirmed += () => confirmed++;

            timeline.ApplyZoom(-100.0, 0f);
            timeline.ApplyZoom(-100.0, 0f);
            timeline.FlushPendingConfirm();

            Assert.AreEqual(1, confirmed);

            // A second flush with nothing pending must stay silent.
            timeline.FlushPendingConfirm();
            Assert.AreEqual(1, confirmed);
        }

        [Test]
        public void Confirm_IsSilentWithoutAZoom()
        {
            TweeqTimeline timeline = Arrange();
            int confirmed = 0;
            timeline.Confirmed += () => confirmed++;

            timeline.PanByPixels(120.0);
            timeline.FlushPendingConfirm();

            Assert.AreEqual(0, confirmed);
        }

        #endregion

        #region Navigation

        [Test]
        public void ShowRange_DoesNotMoveForARangeAlreadyOnScreen()
        {
            TweeqTimeline timeline = Arrange();

            timeline.ShowRange(2.0, 5.0);

            Assert.AreEqual(0.0, timeline.VisibleStart, EPSILON);
        }

        [Test]
        public void ShowRange_AlignsToTheRightForARangeAhead()
        {
            TweeqTimeline timeline = Arrange();

            timeline.ShowRange(40.0, 45.0);

            Assert.AreEqual(35.0, timeline.VisibleStart, EPSILON);
        }

        [Test]
        public void ShowFrame_RevealsASingleFrame()
        {
            TweeqTimeline timeline = Arrange();

            timeline.ShowFrame(40.0);

            // The original treats a bare frame as [frame, frame+1].
            Assert.AreEqual(31.0, timeline.VisibleStart, EPSILON);
        }

        [Test]
        public void CenterFrame_PutsTheFrameInTheMiddle()
        {
            TweeqTimeline timeline = Arrange();

            timeline.CenterFrame(50.0);

            Assert.AreEqual(45.0, timeline.VisibleStart, EPSILON);
            Assert.AreEqual(300f, timeline.FrameToLocalX(50.0), PIXEL_EPSILON);
        }

        #endregion

        #region Pinned children

        [Test]
        public void PinItem_PlacesTheElementAtItsFrame()
        {
            TweeqTimeline timeline = Arrange();
            VisualElement clip = new VisualElement();
            timeline.Add(clip);

            timeline.PinItem(clip, 3.0, 2.0);

            Assert.AreEqual(Position.Absolute, clip.style.position.value);
            Assert.AreEqual(180f, clip.style.translate.value.x.value, PIXEL_EPSILON);
            Assert.AreEqual(120f, clip.style.width.value.value, PIXEL_EPSILON);
        }

        [Test]
        public void PinItem_FollowsThePan()
        {
            TweeqTimeline timeline = Arrange();
            VisualElement clip = new VisualElement();
            timeline.Add(clip);
            timeline.PinItem(clip, 3.0, 2.0);

            timeline.PanByPixels(60.0);

            Assert.AreEqual(120f, clip.style.translate.value.x.value, PIXEL_EPSILON);
        }

        [Test]
        public void PinItem_FollowsTheZoom()
        {
            TweeqTimeline timeline = Arrange();
            VisualElement clip = new VisualElement();
            timeline.Add(clip);
            timeline.PinItem(clip, 3.0, 2.0);

            timeline.FrameWidth = 30.0;

            Assert.AreEqual(90f, clip.style.translate.value.x.value, PIXEL_EPSILON);
            Assert.AreEqual(60f, clip.style.width.value.value, PIXEL_EPSILON);
        }

        [Test]
        public void PinItem_WithoutALengthLeavesTheWidthAlone()
        {
            TweeqTimeline timeline = Arrange();
            VisualElement playhead = new VisualElement();
            playhead.style.width = 1f;
            timeline.Add(playhead);

            timeline.PinItem(playhead, 4.0);
            timeline.FrameWidth = 30.0;

            Assert.AreEqual(1f, playhead.style.width.value.value, PIXEL_EPSILON);
            Assert.AreEqual(120f, playhead.style.translate.value.x.value, PIXEL_EPSILON);
        }

        [Test]
        public void PinItem_RepinningUpdatesInPlace()
        {
            TweeqTimeline timeline = Arrange();
            VisualElement clip = new VisualElement();
            timeline.Add(clip);

            timeline.PinItem(clip, 3.0);
            timeline.PinItem(clip, 5.0);
            timeline.PanByPixels(60.0);

            Assert.AreEqual(240f, clip.style.translate.value.x.value, PIXEL_EPSILON);
        }

        [Test]
        public void UnpinItem_StopsTracking()
        {
            TweeqTimeline timeline = Arrange();
            VisualElement clip = new VisualElement();
            timeline.Add(clip);
            timeline.PinItem(clip, 3.0);

            timeline.UnpinItem(clip);
            timeline.PanByPixels(60.0);

            Assert.AreEqual(180f, clip.style.translate.value.x.value, PIXEL_EPSILON);
        }

        [Test]
        public void ContentContainer_TakesPlainChildren()
        {
            TweeqTimeline timeline = Arrange();
            VisualElement clip = new VisualElement();

            timeline.Add(clip);

            Assert.AreEqual(1, timeline.childCount);
            Assert.AreSame(timeline.contentContainer, clip.hierarchy.parent);
            Assert.AreNotSame(timeline, clip.hierarchy.parent);
        }

        [Test]
        public void PinItem_IgnoresNull()
        {
            TweeqTimeline timeline = Arrange();

            Assert.DoesNotThrow(() => timeline.PinItem(null, 1.0));
            Assert.DoesNotThrow(() => timeline.UnpinItem(null));
        }

        #endregion

        #region In / Out

        [Test]
        public void InOut_IsNotDrawnUntilBothEndsAreSet()
        {
            TweeqTimeline timeline = Arrange();

            timeline.InPoint = 24.0;
            Assert.IsFalse(timeline.HasInOut);

            timeline.OutPoint = 120.0;
            Assert.IsTrue(timeline.HasInOut);
        }

        [Test]
        public void InOut_IsNotDrawnWhenReversed()
        {
            TweeqTimeline timeline = Arrange();

            timeline.InPoint = 120.0;
            timeline.OutPoint = 24.0;

            // Programmatic misuse is ignored rather than swapped or thrown on.
            Assert.IsFalse(timeline.HasInOut);
        }

        [Test]
        public void InOut_RaisesItsChangeEvent()
        {
            TweeqTimeline timeline = Arrange();
            int changed = 0;
            timeline.InOutChanged += () => changed++;

            timeline.InPoint = 24.0;
            timeline.OutPoint = 120.0;
            timeline.OutPoint = 120.0;
            timeline.InPoint = null;

            Assert.AreEqual(3, changed);
        }

        [Test]
        public void FocusInOut_RevealsTheMarkedRange()
        {
            TweeqTimeline timeline = Arrange();
            timeline.RangeEnd = 240.0;
            timeline.InPoint = 24.0;
            timeline.OutPoint = 120.0;

            timeline.FocusInOut();

            // 5% of the 96 frame span on each side, then the least move that reveals the end.
            Assert.AreEqual(114.8, timeline.VisibleStart, 1e-6);
        }

        [Test]
        public void FocusInOut_DoesNothingWithoutBothEnds()
        {
            TweeqTimeline timeline = Arrange();
            timeline.InPoint = 24.0;

            timeline.FocusInOut();

            Assert.AreEqual(0.0, timeline.VisibleStart, EPSILON);
        }

        [Test]
        public void FocusInOut_DoesNotMoveWhenTheRangeIsAlreadyVisible()
        {
            TweeqTimeline timeline = Arrange();
            timeline.RangeEnd = 240.0;
            timeline.FrameWidthMin = 1.0;
            timeline.FrameWidth = 4.0;
            timeline.InPoint = 24.0;
            timeline.OutPoint = 120.0;

            timeline.FocusInOut();

            Assert.AreEqual(0.0, timeline.VisibleStart, EPSILON);
        }

        #endregion

        #region Coordinates

        [Test]
        public void FrameAndPixelMappingsAreInverses()
        {
            TweeqTimeline timeline = Arrange();
            timeline.PanByPixels(137.0);

            double frame = timeline.LocalXToFrame(212f);

            Assert.AreEqual(212f, timeline.FrameToLocalX(frame), PIXEL_EPSILON);
        }

        #endregion

        #region Theme

        [Test]
        public void Theme_FallsBackToDarkOnNull()
        {
            TweeqTimeline timeline = Arrange();

            timeline.Theme = null;

            Assert.IsNotNull(timeline.Theme);
        }

        [Test]
        public void Theme_ReachesPinnedChildrenThroughDistribution()
        {
            TweeqTimeline timeline = Arrange();
            TweeqRuler child = new TweeqRuler();
            timeline.Add(child);

            VisualElement host = new VisualElement();
            host.Add(timeline);

            TweeqTheme theme = TweeqTheme.Light();
            TweeqThemeDistribution.Distribute(host, theme);

            Assert.AreSame(theme, timeline.Theme);
            Assert.AreSame(theme, child.Theme);
        }

        #endregion

        #region UXML

        [Test]
        public void UxmlAttributes_AreApplied()
        {
            VisualElement root = Instantiate(
                "<tq:TweeqTimeline range-start=\"5\" range-end=\"240\" frame-width=\"25\""
                + " overscroll=\"0.25\" />");

            TweeqTimeline timeline = root.Q<TweeqTimeline>();

            Assert.IsNotNull(timeline);
            Assert.AreEqual(5.0, timeline.RangeStart, EPSILON);
            Assert.AreEqual(240.0, timeline.RangeEnd, EPSILON);
            Assert.AreEqual(25.0, timeline.FrameWidth, EPSILON);
            Assert.AreEqual(0.25, timeline.Overscroll, EPSILON);
        }

        [Test]
        public void UxmlAttributes_AreAppliedToTheRuler()
        {
            VisualElement root = Instantiate(
                "<tq:TweeqRuler range-start=\"10\" range-end=\"70\" />");

            TweeqRuler ruler = root.Q<TweeqRuler>();

            Assert.IsNotNull(ruler);
            Assert.AreEqual(10.0, ruler.RangeStart, EPSILON);
            Assert.AreEqual(70.0, ruler.RangeEnd, EPSILON);
        }

        // There is no public API to build a VisualTreeAsset from a string, so the document is
        // written under Assets, imported, and deleted again in TearDown.
        static VisualElement Instantiate(string body)
        {
            if (!AssetDatabase.IsValidFolder(TEMP_FOLDER))
            {
                AssetDatabase.CreateFolder("Assets", "TweeqTimelineUxmlTests");
            }

            string document =
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:tq=\"Tweeq.UIToolkit\">"
                + body
                + "</ui:UXML>";

            File.WriteAllText(TEMP_ASSET, document);
            AssetDatabase.ImportAsset(TEMP_ASSET, ImportAssetOptions.ForceSynchronousImport);

            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TEMP_ASSET);
            Assert.That(asset, Is.Not.Null, "could not import the temporary UXML");

            VisualElement root = asset.Instantiate();
            Assert.That(root, Is.Not.Null, "could not instantiate the UXML");
            return root;
        }

        #endregion

        #region Event helpers

        static void SendWheel(
            VisualElement element, Vector2 delta, Vector2 position, EventModifiers modifiers)
        {
            Event systemEvent = new Event
            {
                type = EventType.ScrollWheel,
                delta = delta,
                mousePosition = position,
                modifiers = modifiers,
            };

            using (WheelEvent wheel = WheelEvent.GetPooled(systemEvent))
            {
                wheel.target = element;
                element.SendEvent(wheel);
            }
        }

        static void SendPointer(
            VisualElement element, EventType type, Vector2 position, int button)
        {
            Event systemEvent = new Event
            {
                type = type,
                mousePosition = position,
                button = button,
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
