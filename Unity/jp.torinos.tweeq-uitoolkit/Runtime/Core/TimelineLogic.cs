using System;

namespace Tweeq.Core
{
    /// <summary>
    /// Pan / zoom / clamp math for a timeline viewport. No UnityEngine dependency, all double.
    /// The original is tweeq/src/Timeline/Timeline.vue (scrollBounds / clampRange / the Alt+scroll
    /// zoom / the .knob percentages / showRange), lifted out so it can be exercised without a panel.
    /// </summary>
    /// <remarks>
    /// Every entry point is total: a non-finite or degenerate input returns the argument unchanged
    /// rather than throwing, because these run on the pointer path where an exception would take
    /// the whole panel down mid-gesture.
    /// </remarks>
    public static class TimelineLogic
    {
        #region Constants

        /// <summary>Default pixels per frame (the original's <c>frameWidth: 60</c>).</summary>
        public const double DEFAULT_FRAME_WIDTH = 60.0;

        /// <summary>Lower zoom bound (the original's <c>frameWidthRange: () =&gt; [10, 100]</c>).</summary>
        public const double DEFAULT_FRAME_WIDTH_MIN = 10.0;

        /// <summary>Upper zoom bound (the original's <c>frameWidthRange: () =&gt; [10, 100]</c>).</summary>
        public const double DEFAULT_FRAME_WIDTH_MAX = 100.0;

        /// <summary>Default overscroll fraction of the viewport (the original's <c>overscroll: 0.5</c>).</summary>
        public const double DEFAULT_OVERSCROLL = 0.5;

        /// <summary>
        /// Base of the exponential zoom (the original's <c>let zoomDelta = 1.003 ** y</c>).
        /// Tuned against browser wheel deltas, i.e. roughly 100 units per notch.
        /// </summary>
        public const double ZOOM_BASE = 1.003;

        /// <summary>
        /// Debounce for the settle signal after a zoom (the original's
        /// <c>debounce(() =&gt; emit('confirm'), 300)</c>). Wheel zoom has no pointer-up, so the
        /// end of a gesture can only be inferred from a quiet period.
        /// </summary>
        public const long CONFIRM_DEBOUNCE_MS = 300;

        #endregion

        #region Scrolling

        /// <summary>
        /// The travel limits of the visible window's start for a given visible duration. You may
        /// scroll until the content edge sits <paramref name="overscroll"/> of the viewport in
        /// from the screen edge, so at most that fraction of the view is empty on either side.
        /// </summary>
        public static (double min, double max) ScrollBounds(
            double contentStart, double contentEnd, double visibleFrames, double overscroll)
        {
            if (!TweeqMath.IsFinite(visibleFrames) || !TweeqMath.IsFinite(overscroll))
            {
                return (contentStart, contentEnd);
            }

            double margin = overscroll * visibleFrames;
            return (contentStart - margin, contentEnd - visibleFrames + margin);
        }

        /// <summary>
        /// Clamps a candidate window to those limits. The visible duration (i.e. the zoom) is kept
        /// intact; only the position is constrained.
        /// </summary>
        public static (double start, double end) ClampRange(
            double start, double end, double contentStart, double contentEnd, double overscroll)
        {
            double duration = end - start;
            if (!TweeqMath.IsFinite(start) || !TweeqMath.IsFinite(duration))
            {
                return (start, end);
            }

            (double minStart, double maxStart) =
                ScrollBounds(contentStart, contentEnd, duration, overscroll);

            // When the viewport is wider than the content plus both margins the limits invert, and
            // clamping against an inverted pair would snap the window to an arbitrary edge. The
            // original leaves the position untouched in that case, so the view stays where it is.
            double clamped = minStart <= maxStart
                ? TweeqMath.Clamp(start, minStart, maxStart)
                : start;

            return (clamped, clamped + duration);
        }

        #endregion

        #region Zoom

        /// <summary>
        /// The visible start after a zoom that keeps the frame under the pointer pinned to the
        /// same pixel. <paramref name="anchorT"/> is the pointer's position across the viewport
        /// (0 = left edge, 1 = right edge) and is clamped, matching linearly's <c>scalar.fit</c>.
        /// </summary>
        /// <remarks>
        /// The original scales both edges away from an origin frame. Since the origin is
        /// <c>start + anchorT * visibleFrames</c> and it must land back on the same pixel, the two
        /// sides collapse into the single term below.
        /// </remarks>
        public static double ZoomAroundAnchor(
            double visibleStart, double visibleFrames, double newVisibleFrames, double anchorT)
        {
            if (!TweeqMath.IsFinite(visibleStart)
                || !TweeqMath.IsFinite(visibleFrames)
                || !TweeqMath.IsFinite(newVisibleFrames)
                || !TweeqMath.IsFinite(anchorT))
            {
                return visibleStart;
            }

            return visibleStart + TweeqMath.Clamp(anchorT, 0.0, 1.0)
                * (visibleFrames - newVisibleFrames);
        }

        #endregion

        #region Scrollbar

        /// <summary>
        /// Position and width of the scrollbar knob, both as a fraction of the track.
        /// </summary>
        /// <remarks>
        /// The knob's CENTER tracks the scroll position across the whole scrollable travel, so it
        /// sits at the track's left edge at the leftmost scroll and at the right edge at the
        /// rightmost. That means <c>leftT</c> is deliberately allowed to go negative (and
        /// <c>leftT + widthT</c> past 1) at the extremes: the original clips the overhanging half
        /// rather than pulling the knob back inside.
        /// </remarks>
        public static (double leftT, double widthT) ScrollbarKnob(
            double visibleStart, double visibleEnd, double contentStart, double contentEnd,
            double overscroll = DEFAULT_OVERSCROLL)
        {
            double duration = visibleEnd - visibleStart;
            double content = contentEnd - contentStart;

            if (!TweeqMath.IsFinite(duration) || !TweeqMath.IsFinite(content)
                || duration <= 0.0 || content <= 0.0)
            {
                return (0.0, 1.0);
            }

            double width = Math.Min(duration / content, 1.0);

            (double minStart, double maxStart) =
                ScrollBounds(contentStart, contentEnd, duration, overscroll);

            // With no travel there is nothing to indicate, so the knob is parked in the middle.
            double center = minStart < maxStart
                ? (visibleStart - minStart) / (maxStart - minStart)
                : 0.5;

            return (center - width * 0.5, width);
        }

        #endregion

        #region Navigation

        /// <summary>
        /// Moves the window the least amount needed to reveal [<paramref name="targetStart"/>,
        /// <paramref name="targetEnd"/>]. A target already inside the window does not move it, and
        /// the zoom is never changed.
        /// </summary>
        /// <remarks>
        /// When the target overflows on both sides the original assigns it verbatim, which widens
        /// the window past what the current zoom can show. Only the start is ever used for
        /// rendering, so that branch behaves as "align to the target's start".
        /// </remarks>
        public static (double start, double end) BringIntoView(
            double visibleStart, double visibleEnd, double targetStart, double targetEnd)
        {
            double duration = visibleEnd - visibleStart;
            if (!TweeqMath.IsFinite(duration)
                || !TweeqMath.IsFinite(targetStart)
                || !TweeqMath.IsFinite(targetEnd))
            {
                return (visibleStart, visibleEnd);
            }

            if (targetStart < visibleStart && visibleEnd < targetEnd)
            {
                return (targetStart, targetEnd);
            }

            if (targetStart < visibleStart)
            {
                return (targetStart, targetStart + duration);
            }

            if (visibleEnd < targetEnd)
            {
                return (targetEnd - duration, targetEnd);
            }

            return (visibleStart, visibleEnd);
        }

        #endregion
    }
}
