using System;

namespace Tweeq.Core
{
    /// <summary>
    /// InputNumber's display digit count, drag sensitivity, and arrow-key increment. No UnityEngine dependency; all double.
    /// </summary>
    public static class NumberLogic
    {
        #region Constants

        /// <summary>Pixels needed to advance 1 step in a field without a bar.</summary>
        public const double PX_PER_STEP = 20.0;

        #endregion

        #region Precision

        /// <summary>Number of decimal digits in the display string. If everything after the last '.' is digits, that count; otherwise 0.</summary>
        public static int PrecisionOfDisplay(string display)
        {
            if (string.IsNullOrEmpty(display))
            {
                return 0;
            }

            int dot = display.LastIndexOf('.');
            if (dot < 0)
            {
                return 0;
            }

            // Equivalent to /\.[0-9]*$/ in the original's TypeScript implementation. When the tail isn't all digits (e.g. exponential notation), it isn't treated as a digit count.
            for (int i = dot + 1; i < display.Length; i++)
            {
                char c = display[i];
                if (c < '0' || c > '9')
                {
                    return 0;
                }
            }

            return display.Length - dot - 1;
        }

        /// <summary>
        /// precision() from spec §4. If a step is present, it always takes top priority.
        /// </summary>
        public static int GetDisplayPrecision(
            double step, string display, double min, double max, double width,
            bool barVisible, bool tweaking, double speed, int precisionLimit,
            int displayPrecisionOverride = -1)
        {
            int explicitPrecision = displayPrecisionOverride < 0 ? 0 : displayPrecisionOverride;
            if (step != 0.0 && TweeqMath.IsFinite(step))
            {
                return Math.Max(TweeqMath.PrecisionOf(step), explicitPrecision);
            }

            int displayPrecision = displayPrecisionOverride >= 0
                ? displayPrecisionOverride
                : PrecisionOfDisplay(display);
            int sliderPrecision = 0;
            if (barVisible && width > 0.0 && TweeqMath.IsFinite(min) && TweeqMath.IsFinite(max))
            {
                sliderPrecision = TweeqMath.PrecisionOf(Math.Abs(max - min) / width);
            }

            if (tweaking)
            {
                // While dragging, the sensitivity itself becomes the lower bound on digits (a finer speed shows finer digits).
                return Math.Max(displayPrecision, Math.Max(sliderPrecision, TweeqMath.PrecisionOf(speed)));
            }

            int limit = Math.Max(precisionLimit < 0 ? 0 : precisionLimit, explicitPrecision);
            return Math.Min(limit, Math.Max(displayPrecision, sliderPrecision));
        }

        #endregion

        #region Format

        /// <summary>
        /// While tweaking, fixed-point with trailing zeroes kept; when idle, trailing zeroes and a trailing dot are trimmed and -0 is normalized to "0".
        /// </summary>
        /// <remarks>
        /// The actual implementation is <see cref="TweeqFormat.Format"/>. Keeping the string-generation logic here as well would mean maintaining it in two places alongside the ZString version, so this is just a thin forwarder kept for compatibility with existing call sites.
        /// </remarks>
        public static string Format(double value, int precision, bool tweaking)
        {
            return TweeqFormat.Format(value, precision, tweaking);
        }

        #endregion

        #region Speed

        /// <summary>Value change per px. With a bar, the full range maps onto the width; without one, it's step-based.</summary>
        public static double BaseSpeed(bool barVisible, double min, double max, double width, double step)
        {
            if (barVisible && width > 0.0 && TweeqMath.IsFinite(min) && TweeqMath.IsFinite(max))
            {
                double perPixel = (max - min) / width;
                if (TweeqMath.IsFinite(perPixel))
                {
                    return perPixel;
                }
            }

            if (step != 0.0 && TweeqMath.IsFinite(step))
            {
                return step / PX_PER_STEP;
            }

            return 1.0;
        }

        /// <summary>Lower bound on sensitivity reachable by dragging vertically down. For a bar with a step, this is determined by "how many px is 1 step".</summary>
        public static double MinSpeed(
            bool barVisible, double min, double max, double width, double step, int precisionLimit)
        {
            int precision = precisionLimit < 0 ? 0 : precisionLimit;

            if (barVisible && step > 0.0 && width > 0.0
                && TweeqMath.IsFinite(step) && TweeqMath.IsFinite(min) && TweeqMath.IsFinite(max))
            {
                double stepCount = (max - min) / step;
                if (TweeqMath.IsFinite(stepCount) && stepCount != 0.0)
                {
                    double pixelsPerStep = width / stepCount;
                    if (TweeqMath.IsFinite(pixelsPerStep) && pixelsPerStep > 0.0)
                    {
                        precision = TweeqMath.PrecisionOf(pixelsPerStep);
                    }
                }
            }

            return Math.Pow(10.0, -precision);
        }

        /// <summary>Upper bound on sensitivity reachable by dragging vertically up. With a bar, accelerating past the range is meaningless, so it's 1.</summary>
        public static double MaxSpeed(bool barVisible)
        {
            return barVisible ? 1.0 : 1000.0;
        }

        #endregion

        #region Scale dots

        /// <summary>Period over which the dot-row bands cycle. Three bands overlap, offset by one digit each.</summary>
        public const double SCALE_DOT_PRECISION_CYCLE = 3.0;

        /// <summary>
        /// Safety valve for the number of dots per band. A band that passes the opacity gate has a spacing of at least 10px,
        /// so under normal widths this cap is never reached (it's only a safeguard for when the phase breaks down).
        /// </summary>
        public const int SCALE_DOT_MAX_PER_LAYER = 256;

        // If the phase jumps into a region coarser than double's step granularity, the mod result loses meaning.
        // Reaching that point means it's off-screen anyway, so the whole band is discarded.
        const double SCALE_DOT_MAX_PHASE = 1e15;

        /// <summary>Geometry for one band of scale dots. It's a struct because it's rebuilt every frame while dragging.</summary>
        public struct ScaleDotLayer
        {
            /// <summary>Spacing between dots (px).</summary>
            public double Gap;

            /// <summary>Center x of the first dot at or after the field's left edge (x=0).</summary>
            public double FirstX;

            /// <summary>Number of dots that fit within the width.</summary>
            public int Count;

            /// <summary>Band opacity (0 to 1).</summary>
            public double Opacity;

            public ScaleDotLayer(double gap, double firstX, int count, double opacity)
            {
                Gap = gap;
                FirstX = firstX;
                Count = count;
                Opacity = opacity;
            }

            /// <summary>Center x of the dot at the given (0-based) index.</summary>
            public double DotX(int index)
            {
                return FirstX + index * Gap;
            }
        }

        /// <summary>
        /// The "digit" for band offset. Every time sensitivity shifts by one digit, the band advances by one,
        /// and the three bands cycle while swapping. Returns NaN for a non-finite sensitivity.
        /// </summary>
        public static double ScaleDotPrecision(double gestureSpeed, int offset)
        {
            if (!TweeqMath.IsFinite(gestureSpeed) || gestureSpeed <= 0.0)
            {
                return double.NaN;
            }

            return TweeqMath.UnsignedMod(
                -Math.Log10(gestureSpeed) + offset, SCALE_DOT_PRECISION_CYCLE);
        }

        /// <summary>Band opacity. Fades out as it gets denser (smaller digit) and becomes more opaque as it gets coarser.</summary>
        public static double ScaleDotOpacity(double precision)
        {
            if (!TweeqMath.IsFinite(precision))
            {
                return 0.0;
            }

            return Math.Sqrt(TweeqMath.Smoothstep(1.0, 2.0, precision));
        }

        /// <summary>
        /// The dot row's reference phase (field-local x). With a bar, this is the handle position; without one,
        /// it's the position where "value 0 sits at the center". Dots are laid out from here in gap-sized steps, so they align with the value.
        /// </summary>
        public static double ScaleDotPhase(
            bool barVisible, double value, double min, double max, double width, double valuePerPixel)
        {
            if (!TweeqMath.IsFinite(value) || !TweeqMath.IsFinite(width))
            {
                return double.NaN;
            }

            if (barVisible
                && TweeqMath.IsFinite(min) && TweeqMath.IsFinite(max) && max != min && width > 0.0)
            {
                double t = TweeqMath.Clamp((value - min) / (max - min), 0.0, 1.0);
                return t * width;
            }

            if (!TweeqMath.IsFinite(valuePerPixel) || valuePerPixel == 0.0)
            {
                return double.NaN;
            }

            return width * 0.5 - value / valuePerPixel;
        }

        /// <summary>
        /// Builds the dot row for band offset. A band whose opacity doesn't reach minOpacity would end up as
        /// "invisible yet hundreds of dots", so it's discarded entirely and false is returned.
        /// </summary>
        public static bool TryBuildScaleDotLayer(
            double gestureSpeed, int offset, double phase, double width, double minOpacity,
            out ScaleDotLayer layer)
        {
            layer = default(ScaleDotLayer);

            if (!TweeqMath.IsFinite(width) || width <= 0.0)
            {
                return false;
            }

            if (!TweeqMath.IsFinite(phase) || Math.Abs(phase) > SCALE_DOT_MAX_PHASE)
            {
                return false;
            }

            double precision = ScaleDotPrecision(gestureSpeed, offset);
            if (!TweeqMath.IsFinite(precision))
            {
                return false;
            }

            double opacity = ScaleDotOpacity(precision);
            if (opacity < minOpacity)
            {
                return false;
            }

            double gap = Math.Pow(10.0, precision);
            if (!TweeqMath.IsFinite(gap) || gap <= 0.0)
            {
                return false;
            }

            double firstX = TweeqMath.UnsignedMod(phase, gap);
            if (!TweeqMath.IsFinite(firstX))
            {
                return false;
            }

            double span = (width - firstX) / gap;
            if (!TweeqMath.IsFinite(span) || span < 0.0)
            {
                return false;
            }

            int count = (int)Math.Floor(span) + 1;
            if (count <= 0)
            {
                return false;
            }

            if (count > SCALE_DOT_MAX_PER_LAYER)
            {
                count = SCALE_DOT_MAX_PER_LAYER;
            }

            layer = new ScaleDotLayer(gap, firstX, count, opacity);
            return true;
        }

        /// <summary>
        /// Whether scale dots may be shown. A field with a step where both ends are clamped only has
        /// discrete stopping positions, so dots representing continuous sensitivity are meaningless.
        /// </summary>
        public static bool ShowScaleDots(
            double step, bool clampMin, bool clampMax, double min, double max)
        {
            bool stepped = step > 0.0 && TweeqMath.IsFinite(step);
            bool clamped = clampMin && clampMax
                && TweeqMath.IsFinite(min) && TweeqMath.IsFinite(max);

            return !(stepped && clamped);
        }

        #endregion

        #region Keyboard

        /// <summary>
        /// New value for the up/down arrow keys. direction is ±1. The return value is already clamped to [validMin, validMax].
        /// </summary>
        public static double ArrowIncrement(
            double current, int direction, double step, double snap,
            bool fast, bool fine, double validMin, double validMax)
        {
            if (!TweeqMath.IsFinite(current))
            {
                return current;
            }

            double fastMultiplier = fast && TweeqMath.IsFinite(snap) && snap > 0.0 ? snap : 1.0;
            double keyMultiplier = (fine ? 0.1 : 1.0) * fastMultiplier;

            double next;
            if (step != 0.0 && TweeqMath.IsFinite(step))
            {
                // With a step present, we want to disable Alt (×0.1), so this is passed through max(1, ·).
                next = current + direction * step * Math.Max(1.0, keyMultiplier);
            }
            else
            {
                double multiplier = keyMultiplier;
                double span = validMax - validMin;
                if (TweeqMath.IsFinite(span) && span <= 1.0)
                {
                    // For a narrow range like 0 to 1, a step of 1 is too coarse, so drop one more digit.
                    multiplier *= 0.1;
                }

                next = current + direction * multiplier;
            }

            return TweeqMath.NormalizeZero(TweeqMath.Clamp(next, validMin, validMax));
        }

        #endregion
    }
}
