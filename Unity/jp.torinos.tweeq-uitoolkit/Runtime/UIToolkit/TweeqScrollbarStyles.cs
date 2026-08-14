using System.Collections.Generic;
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

        /// <summary>Idle time before an auto-hidden scroller starts fading out.</summary>
        public const long AUTO_HIDE_IDLE_DELAY_MS = 700L;

        /// <summary>Duration of the auto-hide opacity transition.</summary>
        public const float AUTO_HIDE_FADE_DURATION_SECONDS = 0.15f;

        const float THUMB_ALPHA = 0.35f;
        const float THUMB_HOVER_ALPHA = 0.7f;
        const float OVERLAY_RIGHT_INSET = 2f;
        const float OVERLAY_VERTICAL_INSET = 3f;
        const string AUTO_HIDE_CLASS = "tweeq-scrollbar-auto-hide";

        static readonly StyleList<StylePropertyName> OpacityProperty =
            new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("opacity") });

        static readonly StyleList<EasingFunction> EaseOut =
            new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic) });

        static readonly StyleList<TimeValue> FadeDuration =
            new StyleList<TimeValue>(new List<TimeValue>
            {
                new TimeValue(AUTO_HIDE_FADE_DURATION_SECONDS, TimeUnit.Second),
            });

        /// <summary>Applies the slim style to both scrollers. Null-safe on every part.</summary>
        public static void ApplySlim(
            ScrollView scrollView,
            TweeqTheme theme,
            float rightInset = OVERLAY_RIGHT_INSET,
            bool autoHide = false)
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

            Apply(scrollView.verticalScroller, theme, true, rightInset);
            Apply(scrollView.horizontalScroller, theme, false, 0f);

            if (autoHide && !scrollView.ClassListContains(AUTO_HIDE_CLASS))
            {
                scrollView.AddToClassList(AUTO_HIDE_CLASS);
                new AutoHideController(scrollView);
            }
        }

        static void Apply(Scroller scroller, TweeqTheme theme, bool vertical, float rightInset)
        {
            if (scroller == null)
            {
                return;
            }

            if (vertical)
            {
                scroller.style.width = TRACK_SIZE;
                scroller.style.position = Position.Absolute;
                scroller.style.top = OVERLAY_VERTICAL_INSET;
                scroller.style.bottom = OVERLAY_VERTICAL_INSET;
                scroller.style.right = rightInset;
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

        sealed class AutoHideController
        {
            readonly Scroller[] _scrollers;
            readonly HashSet<Scroller> _hoveredScrollers = new HashSet<Scroller>();
            readonly HashSet<int> _pressedPointers = new HashSet<int>();
            readonly IVisualElementScheduledItem _hideItem;

            public AutoHideController(ScrollView scrollView)
            {
                _scrollers = new[]
                {
                    scrollView.verticalScroller,
                    scrollView.horizontalScroller,
                };

                for (int index = 0; index < _scrollers.Length; index++)
                {
                    Scroller scroller = _scrollers[index];
                    if (scroller == null)
                    {
                        continue;
                    }

                    ApplyOpacityTransition(scroller);
                    scroller.style.opacity = 0f;
                    scroller.valueChanged += OnScrollValueChanged;
                    scroller.RegisterCallback<PointerEnterEvent>(OnPointerEnter);
                    scroller.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
                    scroller.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                    scroller.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
                    scroller.RegisterCallback<PointerCaptureOutEvent>(
                        OnPointerCaptureOut,
                        TrickleDown.TrickleDown);
                }

                scrollView.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
                _hideItem = scrollView.schedule.Execute(HideIfIdle);
                _hideItem.Pause();
            }

            void OnScrollValueChanged(float _)
            {
                ShowForActivity();
            }

            void OnWheel(WheelEvent _)
            {
                ShowForActivity();
            }

            void OnPointerEnter(PointerEnterEvent evt)
            {
                if (evt.currentTarget is Scroller scroller)
                {
                    _hoveredScrollers.Add(scroller);
                }

                Show();
                _hideItem.Pause();
            }

            void OnPointerLeave(PointerLeaveEvent evt)
            {
                if (evt.currentTarget is Scroller scroller)
                {
                    _hoveredScrollers.Remove(scroller);
                }

                ScheduleHideIfIdle();
            }

            void OnPointerDown(PointerDownEvent evt)
            {
                _pressedPointers.Add(evt.pointerId);
                Show();
                _hideItem.Pause();
            }

            void OnPointerUp(PointerUpEvent evt)
            {
                _pressedPointers.Remove(evt.pointerId);
                ScheduleHideIfIdle();
            }

            void OnPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                _pressedPointers.Remove(evt.pointerId);
                ScheduleHideIfIdle();
            }

            void ShowForActivity()
            {
                Show();
                ScheduleHideIfIdle();
            }

            void Show()
            {
                for (int index = 0; index < _scrollers.Length; index++)
                {
                    if (_scrollers[index] != null)
                    {
                        _scrollers[index].style.opacity = 1f;
                    }
                }
            }

            void ScheduleHideIfIdle()
            {
                if (_hoveredScrollers.Count > 0 || _pressedPointers.Count > 0)
                {
                    _hideItem.Pause();
                    return;
                }

                _hideItem.ExecuteLater(AUTO_HIDE_IDLE_DELAY_MS);
            }

            void HideIfIdle()
            {
                if (_hoveredScrollers.Count > 0 || _pressedPointers.Count > 0)
                {
                    return;
                }

                for (int index = 0; index < _scrollers.Length; index++)
                {
                    if (_scrollers[index] != null)
                    {
                        _scrollers[index].style.opacity = 0f;
                    }
                }
            }
        }

        static void ApplyOpacityTransition(Scroller scroller)
        {
            scroller.style.transitionProperty = OpacityProperty;
            scroller.style.transitionTimingFunction = EaseOut;
            scroller.style.transitionDuration = FadeDuration;
        }
    }
}
