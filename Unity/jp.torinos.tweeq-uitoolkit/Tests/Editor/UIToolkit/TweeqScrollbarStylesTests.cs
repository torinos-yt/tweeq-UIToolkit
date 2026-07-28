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
        public void ApplySlim_IgnoresNulls()
        {
            Assert.DoesNotThrow(() => TweeqScrollbarStyles.ApplySlim(null, TweeqTheme.Dark()));
            Assert.DoesNotThrow(() => TweeqScrollbarStyles.ApplySlim(new ScrollView(), null));
        }
    }
}
