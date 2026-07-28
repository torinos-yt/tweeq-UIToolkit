using System;

namespace Tweeq.Core
{
    /// <summary>
    /// The vertical position of a macOS-style dropdown. Back-calculates the top at which the selected option overlaps the field.
    /// No UnityEngine dependency; everything is double.
    /// </summary>
    public static class DropdownLogic
    {
        #region Constants

        /// <summary>The margin reserved at the viewport edge (InputDropdown.vue's VIEWPORT_MARGIN).</summary>
        public const double DEFAULT_VIEWPORT_MARGIN = 6.0;

        /// <summary>The thickness the popup's frame has outside the option rows (equivalent to padding + border. Vue's SELECT_CHROME).</summary>
        public const double DEFAULT_SELECT_CHROME = 2.0;

        /// <summary>
        /// The field's border (1px) plus focus outline (1px) — a 2px inset that comes from the DOM box model used by the web-based reference implementations.
        /// The UIToolkit version's fields don't have this inset (the focus ring is a separate layer),
        /// so the caller passes the measured value (usually 0). The default stays at 2 for numeric compatibility with those reference implementations.
        /// </summary>
        public const double DEFAULT_FIELD_INSET = 2.0;

        #endregion

        #region Public API

        /// <summary>
        /// The popup's top (panel coordinates). Treats the position where the currentIndex-th option overlaps the field
        /// as the ideal value, and clamps it to a range that keeps viewportMargin free in the viewport. Whatever doesn't
        /// fit is shown via internal scrolling.
        /// listHeight is the already-measured total list height (0 or less if not yet measured).
        /// </summary>
        public static double GetDropdownTop(
            double fieldWorldY, int currentIndex, double itemHeight, double viewportHeight,
            double viewportMargin = DEFAULT_VIEWPORT_MARGIN,
            double selectChrome = DEFAULT_SELECT_CHROME,
            double listHeight = 0.0,
            double fieldInset = DEFAULT_FIELD_INSET)
        {
            int index = currentIndex < 0 ? 0 : currentIndex;
            double idealTop = fieldWorldY - fieldInset - selectChrome - index * itemHeight;

            double available = viewportHeight - viewportMargin * 2.0;

            // An unmeasured value (0 or less) is treated as "doesn't fit". If the total height is assumed optimistically
            // before it's measured, the bottom edge would overflow off-screen, scroll arrows and all, just on the first frame.
            double measured = listHeight > 0.0 ? listHeight : double.PositiveInfinity;

            // If everything fits, cap it at the position that keeps the whole list on-screen. If it doesn't fit, it's fine
            // to lower it as far as "at least 1 row is visible" (assuming the bottom edge, scroll arrows included, extends to the viewport's bottom edge).
            double maxTop = measured <= available
                ? viewportHeight - viewportMargin - listHeight
                : viewportHeight - viewportMargin - itemHeight;

            // Preserves the top margin even in cases where maxTop falls below margin (an extremely short viewport).
            return Math.Max(viewportMargin, Math.Min(Math.Max(viewportMargin, maxTop), idealTop));
        }

        /// <summary>
        /// The popup's maximum height. Extends to the viewport's bottom edge, but never taller than the list itself
        /// (listHeight &lt;= 0 is treated as "unmeasured" and takes the full space down to the bottom edge).
        /// </summary>
        public static double GetDropdownMaxHeight(
            double top, double listHeight, double viewportHeight,
            double viewportMargin = DEFAULT_VIEWPORT_MARGIN)
        {
            double available = viewportHeight - top - viewportMargin;
            if (available < 0.0)
            {
                available = 0.0;
            }

            return listHeight > 0.0 ? Math.Min(listHeight, available) : available;
        }

        #endregion
    }
}
