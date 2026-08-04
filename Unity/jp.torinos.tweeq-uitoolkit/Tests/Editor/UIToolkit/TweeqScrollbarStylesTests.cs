using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit.Tests
{
    /// <summary>
    /// Structure-level contract for the slim scrollbar restyle. Colors on hover are driven by
    /// pointer events that need a live panel, so only the static styling is asserted here.
    /// </summary>
    public class TweeqScrollbarStylesTests
    {
        [Test]
        public void ApplySlim_RestylesBothScrollers()
        {
            ScrollView scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            TweeqTheme theme = TweeqTheme.Dark();

            TweeqScrollbarStyles.ApplySlim(scroll, theme);

            Assert.AreEqual(TweeqScrollbarStyles.TRACK_SIZE, scroll.verticalScroller.style.width.value.value);
            Assert.AreEqual(TweeqScrollbarStyles.TRACK_SIZE, scroll.horizontalScroller.style.height.value.value);
            Assert.AreEqual(DisplayStyle.None, scroll.verticalScroller.lowButton.style.display.value);
            Assert.AreEqual(DisplayStyle.None, scroll.verticalScroller.highButton.style.display.value);
        }

        [Test]
        public void ApplySlim_ShapesTheThumbAndClearsTheTrack()
        {
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            TweeqTheme theme = TweeqTheme.Dark();

            TweeqScrollbarStyles.ApplySlim(scroll, theme);

            VisualElement dragger = scroll.verticalScroller.slider.Q("unity-dragger");
            Assert.NotNull(dragger);
            Assert.AreEqual(TweeqScrollbarStyles.THUMB_SIZE, dragger.style.width.value.value);
            Assert.AreEqual(TweeqScrollbarStyles.THUMB_SIZE * 0.5f, dragger.style.borderTopLeftRadius.value.value);
            Assert.AreEqual(theme.TextSubtle.r, dragger.style.backgroundColor.value.r, 1e-4f);

            VisualElement tracker = scroll.verticalScroller.slider.Q("unity-tracker");
            Assert.NotNull(tracker);
            Assert.AreEqual(Color.clear, tracker.style.backgroundColor.value);
        }

        [Test]
        public void ApplySlim_OverlaysVerticalScrollerWithoutViewportReservation()
        {
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);

            TweeqScrollbarStyles.ApplySlim(scroll, TweeqTheme.Dark());

            Assert.AreEqual(Position.Absolute, scroll.verticalScroller.style.position.value);
            Assert.AreEqual(3f, scroll.verticalScroller.style.top.value.value);
            Assert.AreEqual(3f, scroll.verticalScroller.style.bottom.value.value);
            Assert.AreEqual(2f, scroll.verticalScroller.style.right.value.value);
            VisualElement viewport = scroll.Q<VisualElement>("unity-content-viewport");
            Assert.NotNull(viewport);
            Assert.AreEqual(0f, viewport.style.marginRight.value.value);
        }

        [Test]
        public void ApplySlim_ClearsTheGroupTransformHintOnTheContentContainer()
        {
            ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
            Assert.That(
                scroll.contentContainer.usageHints & UsageHints.GroupTransform,
                Is.EqualTo(UsageHints.GroupTransform),
                "precondition: ScrollView tags its content container with GroupTransform");

            TweeqScrollbarStyles.ApplySlim(scroll, TweeqTheme.Dark());

            // Text and Painter2D content under a group-transform escapes descendant
            // overflow:hidden clippers, which breaks ParameterGroup's collapse
            Assert.That(
                scroll.contentContainer.usageHints & UsageHints.GroupTransform,
                Is.EqualTo(UsageHints.None));
        }

        [Test]
        public void ApplySlim_IgnoresNulls()
        {
            Assert.DoesNotThrow(() => TweeqScrollbarStyles.ApplySlim(null, TweeqTheme.Dark()));
            Assert.DoesNotThrow(() => TweeqScrollbarStyles.ApplySlim(new ScrollView(), null));
        }
    }
}
