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

        /// <summary>Applies the slim style to both scrollers. Null-safe on every part.</summary>
        public static void ApplySlim(ScrollView scrollView, TweeqTheme theme)
        {
            if (scrollView == null || theme == null)
            {
                return;
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
