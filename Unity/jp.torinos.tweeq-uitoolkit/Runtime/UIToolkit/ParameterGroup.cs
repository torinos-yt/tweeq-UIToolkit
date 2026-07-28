using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// A collapsible parameter group (spec §3).
    ///
    /// The original transitions grid-template-rows between 1fr and 0fr, but UI Toolkit has no grid.
    /// Instead, the clip element's max-height is transitioned between "measured height" and 0, with
    /// overflow:hidden applied only while closed / transitioning (if not released once fully open,
    /// the input field's focus ring gets clipped).
    /// </summary>
    [UxmlElement]
    public partial class ParameterGroup : VisualElement, ITweeqThemed
    {
        #region Constants

        /// <summary>PlayerPrefs key prefix for the expanded/collapsed state.</summary>
        public const string PREFS_PREFIX = "tweeq.";

        /// <summary>PlayerPrefs key suffix for the expanded/collapsed state.</summary>
        public const string PREFS_SUFFIX = ".expanded";

        const float CHEVRON_SIZE = 12f;

        // Equivalent to the gap: 0.25em in the original .heading
        const float CHEVRON_GAP = 4f;

        // A safeguard to always return to the fully-open state even in environments where
        // TransitionEndEvent never arrives (animation disabled, value doesn't move, etc).
        // Transition duration + margin
        const long FINISH_FALLBACK_MARGIN_MS = 80;

        // Tolerance (px) for treating "the pinned height" as already at the target height. A transition
        // under 1px isn't visible, so rather than waiting on a transition that won't run, open fully at once
        const float PIN_EPSILON = 0.5f;

        #endregion

        #region Fields

        TweeqTheme _theme = TweeqTheme.Dark();

        readonly ParameterHeading _heading;
        readonly VisualElement _chevron;
        readonly VisualElement _clip;
        readonly VisualElement _content;

        string _name = string.Empty;
        bool _expanded = true;
        bool _hovered;

        IVisualElementScheduledItem _transitionItem;
        IVisualElementScheduledItem _finishItem;

        // The clip height pinned as the transition's starting point. Used to judge "already at the
        // target height" when the direction is reversed mid-transition
        float _pinnedHeight;

        // The last successfully measured content height. A safeguard for when the measurement
        // returns 0 while clipped
        float _naturalContentHeight;

        #endregion

        #region Public API

        /// <summary>The heading string.</summary>
        // Named heading-text on the UXML side so it doesn't collide with VisualElement's built-in text attributes
        [UxmlAttribute("heading-text")]
        public string Label
        {
            get => _heading.Text;
            set => _heading.Text = value;
        }

        /// <summary>Whether it is open. Changing it expands/collapses with an animation and persists the state.</summary>
        [UxmlAttribute("expanded")]
        public bool Expanded
        {
            get => _expanded;
            set
            {
                if (_expanded == value)
                {
                    return;
                }

                _expanded = value;
                SaveExpanded();
                ApplyExpanded(true);
            }
        }

        /// <summary>
        /// The persistence key. Setting it loads the saved expanded/collapsed state (stays expanded if unsaved).
        /// </summary>
        // Named group-name since UXML's name collides with VisualElement's built-in name.
        // Attributes are applied in declaration order, so this is placed after Expanded so the
        // saved state wins over the default written in UXML (same priority as the constructor)
        [UxmlAttribute("group-name")]
        public string Name
        {
            get => _name;
            set
            {
                _name = value ?? string.Empty;

                if (TryLoadExpanded(PrefsKey(_name), out bool stored) && stored != _expanded)
                {
                    // Loading is not a user action, so neither animate nor save
                    _expanded = stored;
                    ApplyExpanded(false);
                }
            }
        }

        /// <summary>The destination to Add() Parameters and the like to.</summary>
        public VisualElement Content => _content;

        /// <summary>
        /// Makes UXML children and plain Add() calls become part of the collapsible target (internal
        /// construction goes through hierarchy.Add). Guarded against null since this can be called during
        /// the constructor before _content is created
        /// </summary>
        public override VisualElement contentContainer => _content ?? this;

        /// <summary>The slot at the right edge of the heading.</summary>
        public VisualElement HeadingRight => _heading.Right;

        /// <summary>Color theme. Normally distributed by the ParameterGrid.</summary>
        public TweeqTheme Theme
        {
            get => _theme;
            set
            {
                // Don't bail out even for the same instance, so rows added after the theme is set still receive it.
                // This setter is the only entry point for redistribution (fix for a gap in the M7 propagation contract)
                _theme = value ?? TweeqTheme.Dark();
                _heading.Theme = _theme;
                ApplyStaticStyles();
                RefreshContentGaps();
                RefreshHeadingColor();
                TweeqThemeDistribution.Distribute(_content, _theme);
            }
        }

        /// <summary>Returns the PlayerPrefs key corresponding to the given name.</summary>
        public static string PrefsKey(string name)
        {
            return string.IsNullOrEmpty(name) ? string.Empty : PREFS_PREFIX + name + PREFS_SUFFIX;
        }

        /// <summary>Redistributes the row gap (gapControl) inside content. Call after adding children.</summary>
        public void RefreshContentGaps()
        {
            TweeqGap.Apply(_content, _theme.GapControl, FlexDirection.Column);
        }

        #endregion

        #region Construction

        public ParameterGroup()
        {
            this.AddToClassList("tweeq-parameter-group");
            this.style.flexDirection = FlexDirection.Column;

            _heading = new ParameterHeading();
            this.hierarchy.Add(_heading);

            _chevron = new VisualElement { name = "tweeq-parameter-group-chevron" };
            _chevron.style.width = CHEVRON_SIZE;
            _chevron.style.height = CHEVRON_SIZE;
            _chevron.style.flexShrink = 0f;
            _chevron.style.marginRight = CHEVRON_GAP;
            _chevron.pickingMode = PickingMode.Ignore;
            _chevron.generateVisualContent += OnGenerateChevron;
            _heading.HeadingContainer.Insert(0, _chevron);

            VisualElement headingBox = _heading.HeadingContainer;

            // Acts like a button. Toggles open/closed via click and Enter/Space
            headingBox.focusable = true;
            headingBox.RegisterCallback<PointerDownEvent>(OnHeadingPointerDown);
            headingBox.RegisterCallback<ClickEvent>(OnHeadingClick);
            headingBox.RegisterCallback<KeyDownEvent>(OnHeadingKeyDown);
            headingBox.RegisterCallback<PointerEnterEvent>(OnHeadingPointerEnter);
            headingBox.RegisterCallback<PointerLeaveEvent>(OnHeadingPointerLeave);

            _clip = new VisualElement { name = "tweeq-parameter-group-clip" };
            this.hierarchy.Add(_clip);

            _content = new VisualElement { name = "tweeq-parameter-group-content" };
            _content.style.flexDirection = FlexDirection.Column;

            // Prevent shrinking so the measured height is preserved even when the clip side's max-height is 0.
            // This keeps _content.resolvedStyle.height equal to "the height when open" even while closed
            _content.style.flexShrink = 0f;
            _content.RegisterCallback<GeometryChangedEvent>(OnContentGeometryChanged);
            _clip.Add(_content);

            ApplyStaticStyles();
            RefreshHeadingColor();

            // Writing the initial state before being attached to a panel means the first transition never runs
            ApplyExpanded(false);

            _clip.RegisterCallback<TransitionEndEvent>(OnClipTransitionEnd);
            this.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            this.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        public ParameterGroup(string name, string label)
            : this()
        {
            this.Label = label;
            this.Name = name;
        }

        void ApplyStaticStyles()
        {
            float duration = _theme.HoverTransitionDuration;

            // The original specifies `ease` (cubic-bezier(0.25,0.1,0.25,1)), so match that
            ApplyTransition(_clip, duration, EasingMode.Ease, "max-height", "padding-top");
            ApplyTransition(_chevron, duration, EasingMode.Ease, "rotate");

            // Color is switched via the Label's own inline style, so apply the transition to the Label too
            ApplyTransition(_heading.TextElement, duration, EasingMode.Ease, "color");

            RefreshContentGaps();
        }

        #endregion

        #region Expand / collapse

        void ApplyExpanded(bool animate)
        {
            CancelScheduled();

            _chevron.style.rotate = new Rotate(new Angle(_expanded ? 0f : -90f, AngleUnit.Degree));

            if (!animate || this.panel == null)
            {
                ApplyEndState();
                return;
            }

            // UI Toolkit transitions only interpolate from "the value resolved on the previous frame" to
            // "the new value". Writing the target value within the same frame skips interpolation, whether
            // starting from none(auto) or from a mid-transition value.
            // Previously only the closing side did "pin at measured height -> 0 on the next tick", so the
            // opening side wasn't interpolated and snapped open all at once via the +80ms fallback
            // (feedback-fixes-01.md B). Align both open and close to the same two-step approach.
            //
            // The pin must be "the height currently being drawn", otherwise it snaps when reversed, so
            // take it from resolvedStyle rather than the natural height we're holding onto
            _pinnedHeight = CurrentClipHeight();

            // Always clip while transitioning
            _clip.style.overflow = Overflow.Hidden;
            _clip.style.maxHeight = _pinnedHeight;
            _clip.style.paddingTop = CurrentClipPaddingTop();

            _transitionItem = this.schedule.Execute(StartTransition).StartingIn(0);
        }

        // One tick after the pin. This is where the target value is first written (the start point is
        // already resolved, so it gets interpolated)
        void StartTransition()
        {
            _transitionItem = null;

            if (this.panel == null)
            {
                ApplyEndState();
                return;
            }

            float gap = _theme.GapControl;

            if (!_expanded)
            {
                _clip.style.paddingTop = 0f;
                _clip.style.maxHeight = 0f;
                return;
            }

            // Measurement is done at this point after the pin. At click time we might grab a stale,
            // not-yet-laid-out value
            float content = MeasuredContentHeight();
            if (content <= 0f)
            {
                // The content has never been laid out even once (e.g. first expansion). Transitioning
                // toward 0 wouldn't move anything and would just snap open via the fallback, so open at once
                ApplyEndState();
                return;
            }

            float target = content + gap;
            if (_pinnedHeight >= target - PIN_EPSILON)
            {
                // Already at the target height, e.g. from reopening mid-way through a closing animation.
                // No transition runs, meaning TransitionEndEvent never arrives either, so open fully here
                ApplyEndState();
                return;
            }

            _clip.style.paddingTop = gap;
            _clip.style.maxHeight = target;

            // The fallback counts from this moment the target value was written
            ScheduleFinishExpand();
        }

        // The final state with no animation involved. If expanded, remove the max-height constraint
        void ApplyEndState()
        {
            float gap = _theme.GapControl;

            if (_expanded)
            {
                _clip.style.paddingTop = gap;
                _clip.style.maxHeight = StyleKeyword.None;
                _clip.style.overflow = Overflow.Visible;
            }
            else
            {
                _clip.style.paddingTop = 0f;
                _clip.style.maxHeight = 0f;
                _clip.style.overflow = Overflow.Hidden;
            }
        }

        void CancelScheduled()
        {
            _transitionItem?.Pause();
            _transitionItem = null;
            _finishItem?.Pause();
            _finishItem = null;
        }

        void ScheduleFinishExpand()
        {
            long delay = (long)(_theme.HoverTransitionDuration * 1000f) + FINISH_FALLBACK_MARGIN_MS;
            _finishItem = this.schedule.Execute(FinishExpand).StartingIn(delay);
        }

        // Remove the max-height constraint once fully open, so growth of the content afterward is followed
        void FinishExpand()
        {
            _finishItem?.Pause();
            _finishItem = null;

            if (!_expanded)
            {
                return;
            }

            _clip.style.maxHeight = StyleKeyword.None;
            _clip.style.overflow = Overflow.Visible;
        }

        float MeasuredContentHeight()
        {
            float height = _content.resolvedStyle.height;
            if (float.IsNaN(height) || height <= 0f)
            {
                return _naturalContentHeight;
            }

            return height;
        }

        float CurrentClipHeight()
        {
            float height = _clip.resolvedStyle.height;
            return float.IsNaN(height) || height < 0f ? 0f : height;
        }

        float CurrentClipPaddingTop()
        {
            float padding = _clip.resolvedStyle.paddingTop;
            return float.IsNaN(padding) || padding < 0f ? 0f : padding;
        }

        void OnClipTransitionEnd(TransitionEndEvent evt)
        {
            // TransitionEndEvent bubbles, so this fires every time an input field inside content
            // (e.g. a background-color transition) finishes a transition. Check target so the
            // animation isn't cut short by those
            if (evt == null || !ReferenceEquals(evt.target, _clip) || !_expanded)
            {
                return;
            }

            // Ignore anything that isn't the end of the expand transition we started. If we opened
            // fully right after cutting a close transition short with a pin, or at the end of the
            // padding-top transition from an instant-open, the opening animation would appear skipped
            if (_finishItem == null)
            {
                return;
            }

            FinishExpand();
        }

        void OnContentGeometryChanged(GeometryChangedEvent evt)
        {
            // Ignore descendant layout changes that bubble up
            if (evt == null || !ReferenceEquals(evt.target, _content))
            {
                return;
            }

            // Even when the clip's max-height is 0, content keeps its natural height thanks to flexShrink 0.
            // However 0 can still be returned depending on the environment, so keep any measured value as a fallback
            float height = evt.newRect.height;
            if (!float.IsNaN(height) && height > 0f)
            {
                _naturalContentHeight = height;
            }

            RefreshContentGaps();
        }

        #endregion

        #region Heading interaction

        void OnHeadingPointerDown(PointerDownEvent evt)
        {
            if (evt == null || evt.button != 0)
            {
                return;
            }

            // Take focus on click, so it can also be expanded/collapsed via keyboard afterward
            _heading.HeadingContainer.Focus();
        }

        void OnHeadingClick(ClickEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            this.Expanded = !_expanded;
            evt.StopPropagation();
        }

        void OnHeadingKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            bool activate = evt.keyCode == KeyCode.Return
                || evt.keyCode == KeyCode.KeypadEnter
                || evt.keyCode == KeyCode.Space;

            if (!activate)
            {
                return;
            }

            this.Expanded = !_expanded;
            evt.StopPropagation();
        }

        void OnHeadingPointerEnter(PointerEnterEvent evt)
        {
            _hovered = true;
            RefreshHeadingColor();
        }

        void OnHeadingPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = false;
            RefreshHeadingColor();
        }

        void RefreshHeadingColor()
        {
            Color color = _hovered ? _theme.Text : _theme.TextMuted;

            // The text color switches via transition, while the chevron switches instantly since it's Painter2D
            _heading.HeadingContainer.style.color = color;
            _heading.TextColor = color;
            _chevron.MarkDirtyRepaint();
        }

        #endregion

        #region Persistence

        void SaveExpanded()
        {
            string key = PrefsKey(_name);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            // PlayerPrefs can be unavailable in batch mode or a sandbox.
            // It isn't worth throwing an exception that halts the caller just to save the collapsed state
            try
            {
                PlayerPrefs.SetInt(key, _expanded ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{nameof(ParameterGroup)}: 開閉状態を保存できない（{key}）: {exception.Message}");
            }
        }

        static bool TryLoadExpanded(string key, out bool expanded)
        {
            expanded = true;

            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            try
            {
                if (!PlayerPrefs.HasKey(key))
                {
                    return false;
                }

                expanded = PlayerPrefs.GetInt(key, 1) != 0;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{nameof(ParameterGroup)}: 開閉状態を読めない（{key}）: {exception.Message}");
                return false;
            }
        }

        #endregion

        #region Events

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            ParameterGrid.Find(this)?.RequestRefresh();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // Only check for this element's own detachment, so merely removing a child from content
            // doesn't cut the transition short
            if (evt != null && !ReferenceEquals(evt.target, this))
            {
                return;
            }

            // If we keep holding onto the scheduled item from the panel we were detached from, the next
            // time we're attached it would stay stuck mid-transition at the pin (fixed max-height + overflow hidden)
            CancelScheduled();
            ApplyEndState();
        }

        #endregion

        #region Painting

        void OnGenerateChevron(MeshGenerationContext context)
        {
            Painter2D painter = context?.painter2D;
            if (painter == null)
            {
                return;
            }

            Rect rect = _chevron.contentRect;
            float width = rect.width;
            float height = rect.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return;
            }

            // A downward-pointing triangle. Positioned to balance around the center of the square
            // so it doesn't look lopsided when rotated
            float halfWidth = width * 0.26f;
            float halfHeight = height * 0.16f;
            float centerX = width * 0.5f;
            float centerY = height * 0.5f;

            painter.fillColor = _hovered ? _theme.Text : _theme.TextMuted;
            painter.BeginPath();
            painter.MoveTo(new Vector2(centerX - halfWidth, centerY - halfHeight));
            painter.LineTo(new Vector2(centerX + halfWidth, centerY - halfHeight));
            painter.LineTo(new Vector2(centerX, centerY + halfHeight * 2f));
            painter.ClosePath();
            painter.Fill();
        }

        #endregion

        #region Helpers

        static void ApplyTransition(
            VisualElement element, float duration, EasingMode easing, params string[] properties)
        {
            if (element == null || properties == null || properties.Length == 0)
            {
                return;
            }

            List<StylePropertyName> names = new List<StylePropertyName>(properties.Length);
            List<TimeValue> durations = new List<TimeValue>(properties.Length);
            List<EasingFunction> easings = new List<EasingFunction>(properties.Length);

            for (int i = 0; i < properties.Length; i++)
            {
                names.Add(new StylePropertyName(properties[i]));
                durations.Add(new TimeValue(duration, TimeUnit.Second));
                easings.Add(new EasingFunction(easing));
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(names);
            element.style.transitionDuration = new StyleList<TimeValue>(durations);
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(easings);
        }

        #endregion
    }
}
