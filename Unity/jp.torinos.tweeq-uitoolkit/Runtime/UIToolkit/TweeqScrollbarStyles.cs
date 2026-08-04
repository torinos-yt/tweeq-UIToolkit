using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Restyles a <see cref="ScrollView"/>'s scrollers into the slim, rounded thumb the rest of
    /// the tweeq chrome implies: no repeat buttons, an invisible track, and a subtle thumb that
    /// darkens on hover. Unity's default scroller is editor-style chrome and visually shouts next
    /// to tweeq widgets.
    /// </summary>
    public static class TweeqScrollbarStyles
    {
        /// <summary>Track thickness (the pointer target).</summary>
        public const float TRACK_SIZE = 8f;

        /// <summary>Thumb thickness, centered inside the track.</summary>
        public const float THUMB_SIZE = 4f;

        const float THUMB_ALPHA = 0.35f;
        const float THUMB_HOVER_ALPHA = 0.7f;
        const float OVERLAY_RIGHT_INSET = 2f;

        /// <summary>Applies the slim style to both scrollers. Null-safe on every part.</summary>
        public static void ApplySlim(ScrollView scrollView, TweeqTheme theme)
        {
            if (scrollView == null || theme == null)
            {
                return;
            }

            // ScrollView tags its content container with GroupTransform for cheap scrolling,
            // but text and Painter2D content under a group-transform escapes descendant
            // overflow:hidden clippers (ParameterGroup's collapse clip leaked its rows this
            // way). tweeq panels rely on that nested clipping, so correctness wins over the
            // scroll-mesh reuse. The hint is set in ScrollView's constructor, so clearing it
            // here sticks.
            if (scrollView.contentContainer != null)
            {
                scrollView.contentContainer.usageHints &= ~UsageHints.GroupTransform;
            }

            // The viewport and its vertical scroller are siblings inside this container. Keeping
            // the scroller in normal flex flow reserves TRACK_SIZE pixels and moves every row when
            // the bar appears; the tweeq chrome treats this bar as an overlay instead.
            VisualElement contentViewport = scrollView.Q<VisualElement>("unity-content-viewport");
            if (contentViewport != null)
            {
                contentViewport.style.marginRight = 0f;
                contentViewport.style.paddingRight = 0f;
                contentViewport.style.minWidth = 0f;
            }

            Apply(scrollView.verticalScroller, theme, true);
            Apply(scrollView.horizontalScroller, theme, false);
        }

        static void Apply(Scroller scroller, TweeqTheme theme, bool vertical)
        {
            if (scroller == null)
            {
                return;
            }

            if (vertical)
            {
                scroller.style.width = TRACK_SIZE;
                scroller.style.position = Position.Absolute;
                scroller.style.top = 0f;
                scroller.style.bottom = 0f;
                scroller.style.right = OVERLAY_RIGHT_INSET;
                scroller.style.flexGrow = 0f;
                scroller.style.flexShrink = 0f;
            }
            else
            {
                scroller.style.height = TRACK_SIZE;
            }

            if (scroller.lowButton != null)
            {
                scroller.lowButton.style.display = DisplayStyle.None;
            }

            if (scroller.highButton != null)
            {
                scroller.highButton.style.display = DisplayStyle.None;
            }

            Slider slider = scroller.slider;
            if (slider == null)
            {
                return;
            }

            // With the repeat buttons gone the slider owns the whole strip.
            slider.style.flexGrow = 1f;
            slider.style.marginTop = 0f;
            slider.style.marginBottom = 0f;
            slider.style.marginLeft = 0f;
            slider.style.marginRight = 0f;

            VisualElement tracker = slider.Q("unity-tracker");
            if (tracker != null)
            {
                tracker.style.backgroundColor = Color.clear;
                TweeqInputBoxStyles.SetBorderWidth(tracker, 0f);
            }

            VisualElement dragger = slider.Q("unity-dragger");
            if (dragger == null)
            {
                return;
            }

            float inset = (TRACK_SIZE - THUMB_SIZE) * 0.5f;
            if (vertical)
            {
                dragger.style.width = THUMB_SIZE;
                dragger.style.left = inset;
            }
            else
            {
                dragger.style.height = THUMB_SIZE;
                dragger.style.top = inset;
            }

            TweeqInputBoxStyles.SetBorderWidth(dragger, 0f);
            TweeqInputBoxStyles.SetCornerRadius(dragger, THUMB_SIZE * 0.5f);
            TweeqInputBoxStyles.ApplyBackgroundTransition(dragger, theme);
            dragger.style.backgroundColor = ThumbColor(theme, false);

            // Hover is tracked on the whole strip so the thumb responds before the pointer
            // lands on its 4px body.
            scroller.RegisterCallback<PointerEnterEvent>(_ =>
                dragger.style.backgroundColor = ThumbColor(theme, true));
            scroller.RegisterCallback<PointerLeaveEvent>(_ =>
                dragger.style.backgroundColor = ThumbColor(theme, false));
        }

        static Color ThumbColor(TweeqTheme theme, bool hovered)
        {
            Color color = theme.TextSubtle;
            color.a = hovered ? THUMB_HOVER_ALPHA : THUMB_ALPHA;
            return color;
        }
    }
}
