using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>The result of applying constraints. Each flag is "did the value actually move at that stage".</summary>
    public struct NumberValidation
    {
        /// <summary>The value after clamping and quantization.</summary>
        public double Value;

        /// <summary>Clamping changed the value.</summary>
        public bool Clamped;

        /// <summary>step / snap quantization changed the value.</summary>
        public bool Quantized;

        public NumberValidation(double value, bool clamped, bool quantized)
        {
            Value = value;
            Clamped = clamped;
            Quantized = quantized;
        }
    }

    #endregion

    /// <summary>
    /// Validates InputNumber's output value. Applies, in order, clamp -> step quantization -> snap quantization.
    /// </summary>
    public static class NumberValidator
    {
        #region Constants

        // A tolerance so floating-point rounding residue isn't misdetected as "quantized" (equivalent to the Vue original's scalar.approx).
        const double APPROX_EPSILON = 1e-9;

        #endregion

        #region Public API

        /// <summary>
        /// clamp(validMin, validMax) -> quantize(step) -> quantize(snapEnabled ? snap : 0). The quantization origin is 0.
        /// Non-finite values are left up to the host's policy and returned unchanged.
        /// </summary>
        public static NumberValidation Validate(
            double value, double validMin, double validMax,
            double step, double snap, bool snapEnabled)
        {
            if (!TweeqMath.IsFinite(value))
            {
                return new NumberValidation(value, false, false);
            }

            double clamped = TweeqMath.Clamp(value, validMin, validMax);
            bool didClamp = clamped != value;

            // Clamp comes first. When the range boundary isn't a multiple of step, moving away from the boundary during quantization is the correct order.
            double quantized = TweeqMath.Quantize(clamped, step, 0.0);
            if (snapEnabled)
            {
                quantized = TweeqMath.Quantize(quantized, snap, 0.0);
            }

            return new NumberValidation(
                TweeqMath.NormalizeZero(quantized), didClamp, !Approximately(clamped, quantized));
        }

        #endregion

        #region Helpers

        static bool Approximately(double left, double right)
        {
            double difference = Math.Abs(left - right);
            return difference <= APPROX_EPSILON * Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));
        }

        #endregion
    }
}
