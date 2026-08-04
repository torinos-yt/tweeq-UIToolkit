using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tweeq.UIToolkit
{
    /// <summary>
    /// Helper that assembles the input field's "chrome" (border, corner radius, background).
    /// </summary>
    /// <remarks>
    /// The implementation was extracted using NumberInput as the canonical source, so the
    /// appearance is bit-for-bit identical. Made public so that custom widgets in external
    /// asmdefs can have the same exterior as tweeq's input fields (ext-custom-widgets-spec.md EXT-01-A).
    /// </remarks>
    public static class TweeqInputBoxStyles
    {
        #region Constants

        /// <summary>Input field text size (px).</summary>
        public const float TEXT_FONT_SIZE = 12f;

        /// <summary>Thickness of the inset border when disabled (px).</summary>
        public const float DISABLED_BORDER_WIDTH = 1f;

        // The inner element of TextField. Touched to remove the background/border and make
        // full use of the 24px height.
        const string TEXT_INPUT_NAME = "unity-text-input";

        #endregion

        #region Edge helpers

        /// <summary>Sets the border width on all 4 sides at once.</summary>
        public static void SetBorderWidth(VisualElement element, float width)
        {
            if (element == null)
            {
                return;
            }

            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
        }

        /// <summary>Sets the border color on all 4 sides at once.</summary>
        public static void SetBorderColor(VisualElement element, Color color)
        {
            if (element == null)
            {
                return;
            }

            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        /// <summary>Sets the corner radius on all 4 corners at once.</summary>
        public static void SetCornerRadius(VisualElement element, float radius)
        {
            SetCornerRadius(element, radius, true, true, true, true);
        }

        #endregion

        #region Chrome

        /// <summary>
        /// Flattens corner radii depending on position within a group (see the corner-radius
        /// table in spec §1).
        /// </summary>
        /// <remarks>
        /// The two axes are combined with OR (if either axis says "flatten", it's flattened).
        /// Apply the same arguments to elements that draw a border on a separate layer, like
        /// the focus ring.
        /// </remarks>
        public static void ApplyCornerRadius(
            VisualElement element,
            TweeqTheme theme,
            TweeqBoxPosition inlinePosition,
            TweeqBoxPosition blockPosition)
        {
            if (element == null)
            {
                return;
            }

            float radius = theme != null ? theme.InputRadius : 0f;

            bool topLeft = true;
            bool topRight = true;
            bool bottomLeft = true;
            bool bottomRight = true;

            switch (inlinePosition)
            {
                case TweeqBoxPosition.Start:
                    topRight = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    topLeft = false;
                    topRight = false;
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    topLeft = false;
                    bottomLeft = false;
                    break;
            }

            switch (blockPosition)
            {
                case TweeqBoxPosition.Start:
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.Middle:
                    topLeft = false;
                    topRight = false;
                    bottomLeft = false;
                    bottomRight = false;
                    break;

                case TweeqBoxPosition.End:
                    topLeft = false;
                    topRight = false;
                    break;
            }

            SetCornerRadius(element, radius, topLeft, topRight, bottomLeft, bottomRight);
        }

        /// <summary>
        /// Transitions only the background color (spec §5: 0.15s / cubic-bezier(0.4,0,0.2,1)).
        /// </summary>
        /// <remarks>
        /// UI Toolkit has no identical curve, so this approximates it with EaseInOutCubic
        /// (the same call made for NumberInput / RotaryInput).
        /// </remarks>
        /// <summary>
        /// Marks elements whose transition is muted for one frame after every panel attach.
        /// Doubles as the "already registered" guard so repeated theme applications don't stack
        /// callbacks.
        /// </summary>
        public const string ATTACH_GUARD_USS_CLASS = "tweeq-attach-transition-guard";

        public static void ApplyBackgroundTransition(VisualElement element, TweeqTheme theme)
        {
            if (element == null || theme == null)
            {
                return;
            }

            element.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("background-color") });
            element.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(theme.HoverTransitionDuration, TimeUnit.Second) });
            element.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.EaseInOutCubic) });

            // On (re)attach the first style resolution transitions from the default value, not
            // the inline one, so a freshly mounted element (e.g. modal content moving onto the
            // overlay layer) briefly animates from transparent — read as a black flash. Muting
            // the transition for that one frame makes the first paint land on the final color.
            if (!element.ClassListContains(ATTACH_GUARD_USS_CLASS))
            {
                element.AddToClassList(ATTACH_GUARD_USS_CLASS);
                element.RegisterCallback<AttachToPanelEvent>(MuteTransitionForFirstFrame);
            }
        }

        static void MuteTransitionForFirstFrame(AttachToPanelEvent evt)
        {
            if (!(evt.currentTarget is VisualElement element))
            {
                return;
            }

            StyleList<TimeValue> restored = element.style.transitionDuration;
            element.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) });
            element.schedule.Execute(() => element.style.transitionDuration = restored);
        }

        /// <summary>Returns the input field's background color according to hover state.</summary>
        /// <remarks>
        /// disabled changes the composition rather than the color — "transparent background +
        /// 1px border inset" — so it isn't handled here (the caller branches and applies
        /// <see cref="SetBorderWidth"/>).
        /// </remarks>
        public static Color ResolveBackground(TweeqTheme theme, bool hovered)
        {
            if (theme == null)
            {
                return Color.clear;
            }

            return hovered ? theme.InputHover : theme.Input;
        }

        /// <summary>
        /// Toggles the disabled appearance on/off (spec §5: transparent background + 1px
        /// border inset).
        /// </summary>
        /// <remarks>
        /// The disable-removal path doesn't repaint the normal background color, because the
        /// caller is the one that knows the hover state. After removing it, put the result of
        /// <see cref="ResolveBackground"/> into the background.
        /// </remarks>
        public static void ApplyDisabledChrome(VisualElement element, TweeqTheme theme, bool disabled)
        {
            if (element == null)
            {
                return;
            }

            if (!disabled)
            {
                SetBorderWidth(element, 0f);
                return;
            }

            element.style.backgroundColor = Color.clear;
            SetBorderWidth(element, DISABLED_BORDER_WIDTH);

            if (theme != null)
            {
                SetBorderColor(element, theme.Border);
            }
        }

        #endregion

        #region Text field

        /// <summary>
        /// A set of normalizations that fit an always-visible <see cref="TextField" /> into
        /// the input field's 24px frame.
        /// </summary>
        /// <remarks>
        /// <para>
        /// UI Toolkit's default USS adds top/bottom padding and an auto height, so left as-is
        /// the line gets crushed and unreadable within the 24px frame (feedback-fixes-01.md A-6).
        /// Height, margins, and font size are set explicitly, and the background/border are
        /// left to the outer box.
        /// </para>
        /// <para>
        /// Left/right padding is forced to 0. The centering width for the value differs per
        /// widget, so whichever side needs it overrides after the call (NumberInput /
        /// StringInput add 0.5em).
        /// </para>
        /// </remarks>
        public static void ApplyTextField(TextField field, TweeqTheme theme)
        {
            if (field == null)
            {
                return;
            }

            field.style.fontSize = theme != null ? theme.FontSizeInput : TEXT_FONT_SIZE;
            field.style.paddingLeft = 0f;
            field.style.paddingRight = 0f;
            field.style.paddingTop = 0f;
            field.style.paddingBottom = 0f;
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;
            field.style.marginTop = 0f;
            field.style.marginBottom = 0f;
            field.style.minHeight = 0f;
            field.style.alignItems = Align.Stretch;
            field.style.unityTextAlign = TextAnchor.MiddleCenter;

            ApplyTextSelectionColors(field, theme);

            VisualElement textInput = field.Q(TEXT_INPUT_NAME);
            if (textInput != null)
            {
                textInput.style.backgroundColor = Color.clear;
                SetBorderWidth(textInput, 0f);
                SetBorderColor(textInput, Color.clear);
                textInput.style.paddingLeft = 0f;
                textInput.style.paddingRight = 0f;
                textInput.style.paddingTop = 0f;
                textInput.style.paddingBottom = 0f;
                textInput.style.marginLeft = 0f;
                textInput.style.marginRight = 0f;
                textInput.style.marginTop = 0f;
                textInput.style.marginBottom = 0f;
                textInput.style.height = Length.Percent(100f);
                textInput.style.minHeight = 0f;
                textInput.style.unityTextAlign = TextAnchor.MiddleCenter;
                textInput.style.fontSize = theme != null ? theme.FontSizeInput : TEXT_FONT_SIZE;
                textInput.style.whiteSpace = WhiteSpace.NoWrap;
            }

            // The actual glyph drawing is done by the TextElement inside unity-text-input.
            // Vertical crushing remains even if only the input side is fixed, so the same
            // settings are applied here too.
            TextElement textElement = textInput != null ? textInput.Q<TextElement>() : null;
            if (textElement != null)
            {
                textElement.style.height = Length.Percent(100f);
                textElement.style.minHeight = 0f;
                textElement.style.paddingTop = 0f;
                textElement.style.paddingBottom = 0f;
                textElement.style.marginTop = 0f;
                textElement.style.marginBottom = 0f;
                textElement.style.unityTextAlign = TextAnchor.MiddleCenter;
                textElement.style.fontSize = theme != null ? theme.FontSizeInput : TEXT_FONT_SIZE;
            }
        }

        #endregion

        #region Internals

        // If the caret/selection color is left at the USS default (black), it's invisible on
        // a dark background. selectionColor is obsolete, but the recommended
        // --unity-selection-color can't be set per-instance from C# (theming is driven by
        // TweeqTheme), so this keeps using it. Confining the warning suppression to this one
        // method is one of the goals of making this API public.
        static void ApplyTextSelectionColors(TextField field, TweeqTheme theme)
        {
            if (theme == null)
            {
                return;
            }

#pragma warning disable 618
            field.textSelection.cursorColor = theme.Text;
            field.textSelection.selectionColor = theme.AccentSoft;
#pragma warning restore 618
        }

        static void SetCornerRadius(
            VisualElement element,
            float radius,
            bool topLeft,
            bool topRight,
            bool bottomLeft,
            bool bottomRight)
        {
            if (element == null)
            {
                return;
            }

            element.style.borderTopLeftRadius = topLeft ? radius : 0f;
            element.style.borderTopRightRadius = topRight ? radius : 0f;
            element.style.borderBottomLeftRadius = bottomLeft ? radius : 0f;
            element.style.borderBottomRightRadius = bottomRight ? radius : 0f;
        }

        #endregion
    }
}
