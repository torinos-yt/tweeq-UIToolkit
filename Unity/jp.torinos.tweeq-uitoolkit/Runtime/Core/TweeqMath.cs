using System;

namespace Tweeq.Core
{
    /// <summary>
    /// Math shared across tweeq interactions. No UnityEngine dependency; everything is double.
    /// </summary>
    public static class TweeqMath
    {
        #region Constants

        // C#'s double.Epsilon is the smallest denormalized number, not the machine epsilon (2^-52) that the term usually refers to.
        // Defined explicitly here to match the same threshold used by the reference implementation.
        public const double MACHINE_EPSILON = 2.220446049250313e-16;

        #endregion

        #region Interpolation

        /// <summary>Linear interpolation.</summary>
        public static double Lerp(double from, double to, double amount)
        {
            return from + (to - from) * amount;
        }

        /// <summary>A smooth weight that maps edge0-edge1 to 0-1.</summary>
        public static double Smoothstep(double edge0, double edge1, double value)
        {
            double amount = Clamp01((value - edge0) / (edge1 - edge0));
            return amount * amount * (3.0 - 2.0 * amount);
        }

        #endregion

        #region Angles

        /// <summary>Always returns a remainder with the same sign as modulo.</summary>
        public static double UnsignedMod(double value, double modulo)
        {
            return (value % modulo + modulo) % modulo;
        }

        /// <summary>
        /// The shortest signed angular difference (in degrees) from source to target. The return value is in [-180, 180).
        /// </summary>
        public static double SignedAngleBetween(double target, double source)
        {
            return UnsignedMod(target - source + 180.0, 360.0) - 180.0;
        }

        #endregion

        #region Quantization

        /// <summary>
        /// Snaps to step intervals relative to origin.
        /// step&lt;=0 or non-finite values are left up to the host's policy, so the value is returned unchanged.
        /// </summary>
        public static double Quantize(double value, double step, double origin = 0.0)
        {
            if (!IsFinite(value) || !IsFinite(step) || step <= 0.0 || !IsFinite(origin))
            {
                return value;
            }

            // The rounding rule here is round-half-away-from-zero, matching the reference implementation; C#'s default is banker's rounding, so it must be specified explicitly.
            double steps = Math.Round((value - origin) / step, MidpointRounding.AwayFromZero);
            return NormalizeZero(steps * step + origin);
        }

        /// <summary>The number of decimal digits in step. max(0, ceil(-log10(step))).</summary>
        public static int PrecisionOf(double step)
        {
            if (step == 0.0 || !IsFinite(step))
            {
                return 0;
            }

            // A negative step makes log10 NaN, so this uses the absolute value (the Vue original ends up returning NaN).
            double precision = Math.Ceiling(-Math.Log10(Math.Abs(step)));
            if (double.IsNaN(precision) || precision <= 0.0)
            {
                return 0;
            }

            return precision >= int.MaxValue ? int.MaxValue : (int)precision;
        }

        #endregion

        #region Helpers

        /// <summary>Not NaN and not infinite.</summary>
        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>Normalizes -0 to +0, to avoid inconsistencies in display and comparison.</summary>
        public static double NormalizeZero(double value)
        {
            return value == 0.0 ? 0.0 : value;
        }

        /// <summary>Unlike Math.Clamp, does not throw even when min&gt;max.</summary>
        public static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        static double Clamp01(double value)
        {
            return Clamp(value, 0.0, 1.0);
        }

        #endregion
    }
}
