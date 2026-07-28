using System;

namespace Tweeq.Core
{
    /// <summary>The size after applying the ratio lock, and the lock state after applying it.</summary>
    public readonly struct SizeApplyResult
    {
        /// <summary>Axis 0 (width) after applying.</summary>
        public readonly double X;

        /// <summary>Axis 1 (height) after applying.</summary>
        public readonly double Y;

        /// <summary>The ratio lock state after applying. Differs from the input only when it was automatically released.</summary>
        public readonly bool KeepRatio;

        public SizeApplyResult(double x, double y, bool keepRatio)
        {
            X = x;
            Y = y;
            KeepRatio = keepRatio;
        }
    }

    /// <summary>
    /// InputSize's ratio lock (Vue's InputSize.vue onUpdate).
    /// Handles the other axis following along when only one axis moves while locked, and automatic release when both axes change at once.
    /// </summary>
    public static class SizeLogic
    {
        #region Constants

        // The ratio can swing wildly in magnitude, e.g. 100x or 0.01x, so this looks at relative error rather than absolute error.
        // linearly's scalar.approx uses a fixed absolute 1e-6, but that would misfire at large ratios and cause
        // the lock to release itself (an intentional deviation)
        const double RATIO_TOLERANCE = 1e-6;

        #endregion

        #region Public API

        /// <summary>
        /// Runs the change from the previous values <paramref name="previousX"/>/<paramref name="previousY"/>
        /// to <paramref name="nextX"/>/<paramref name="nextY"/> through the ratio lock.
        /// The baseline ends up being the previous value rather than the value at gesture start, so for
        /// continuous application during a drag, use <see cref="Apply(double,double,double,double,double,double,bool)"/> instead.
        /// </summary>
        public static SizeApplyResult Apply(
            double previousX, double previousY, double nextX, double nextY, bool keepRatio)
        {
            return Apply(previousX, previousY, nextX, nextY, previousX, previousY, keepRatio);
        }

        /// <summary>
        /// Applies the ratio lock. <paramref name="baselineX"/>/<paramref name="baselineY"/> are
        /// the values recorded at edit start (Vue's valueOnEdit).
        /// If the previous value were used as the baseline during a drag, the multiplier would compound and error would build up,
        /// so a fixed baseline is passed in instead.
        /// </summary>
        public static SizeApplyResult Apply(
            double previousX,
            double previousY,
            double nextX,
            double nextY,
            double baselineX,
            double baselineY,
            bool keepRatio)
        {
            bool changedX = previousX != nextX;
            bool changedY = previousY != nextY;

            // Both axes moving at once, changing the ratio itself, is input where "the user came to break the ratio".
            // If the lock isn't released here, the input keeps getting cancelled out (in line with Vue's onUpdate)
            if (keepRatio && changedX && changedY
                && !ApproximatelySameRatio(previousX / previousY, nextX / nextY))
            {
                keepRatio = false;
            }

            if (!keepRatio)
            {
                return new SizeApplyResult(nextX, nextY, false);
            }

            // Vue treats it as "axis 1 moved" whenever axis 0 hasn't changed (axis 0 takes priority when both axes change)
            bool primaryIsX = changedX;
            double primaryBaseline = primaryIsX ? baselineX : baselineY;
            double primaryNext = primaryIsX ? nextX : nextY;

            double ratio = primaryNext / primaryBaseline;
            if (!TweeqMath.IsFinite(ratio))
            {
                // If the baseline is 0 (division by zero), no ratio can be formed, so pass through with multiplier 1 = the other axis left unchanged
                ratio = 1.0;
            }

            return primaryIsX
                ? new SizeApplyResult(primaryNext, baselineY * ratio, true)
                : new SizeApplyResult(baselineX * ratio, primaryNext, true);
        }

        #endregion

        #region Internals

        static bool ApproximatelySameRatio(double left, double right)
        {
            // With 0 width or 0 height, the ratio becomes +/-Infinity / NaN. The same non-finite values are treated as "unchanged",
            // and the lock is released only when crossing over 0
            if (left == right)
            {
                return true;
            }

            if (!TweeqMath.IsFinite(left) || !TweeqMath.IsFinite(right))
            {
                return double.IsNaN(left) && double.IsNaN(right);
            }

            double scale = Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= RATIO_TOLERANCE * scale;
        }

        #endregion
    }
}
