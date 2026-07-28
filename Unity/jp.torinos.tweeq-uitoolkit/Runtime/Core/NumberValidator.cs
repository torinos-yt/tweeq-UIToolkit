using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>制約適用の結果。フラグは「その段で実際に値が動いたか」。</summary>
    public struct NumberValidation
    {
        /// <summary>クランプ・量子化を通した値。</summary>
        public double Value;

        /// <summary>クランプで値が変わった。</summary>
        public bool Clamped;

        /// <summary>step / snap の量子化で値が変わった。</summary>
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
    /// InputNumber の出力値検証。clamp → step 量子化 → snap 量子化の順に適用する。
    /// </summary>
    public static class NumberValidator
    {
        #region Constants

        // 浮動小数の丸め残差を「量子化された」と誤検出しないための許容差（TS 版 scalar.approx 相当）。
        const double APPROX_EPSILON = 1e-9;

        #endregion

        #region Public API

        /// <summary>
        /// clamp(validMin, validMax) → quantize(step) → quantize(snapEnabled ? snap : 0)。量子化の origin は 0。
        /// 非有限値はホスト側の方針に委ねてそのまま返す。
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

            // clamp が先。範囲端が step の倍数でない場合、量子化で端から動くのが正しい順序。
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
