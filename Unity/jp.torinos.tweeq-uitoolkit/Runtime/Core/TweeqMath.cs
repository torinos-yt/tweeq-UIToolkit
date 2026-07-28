using System;

namespace Tweeq.Core
{
    /// <summary>
    /// tweeq のインタラクションで共有する数式。UnityEngine 非依存・すべて double。
    /// </summary>
    public static class TweeqMath
    {
        #region Constants

        // C# の double.Epsilon は最小非正規化数であって Rust の f64::EPSILON（機械イプシロン）ではない。
        // 参照実装と同じ閾値を使うため自前で定義する。
        public const double MACHINE_EPSILON = 2.220446049250313e-16;

        #endregion

        #region Interpolation

        /// <summary>線形補間。</summary>
        public static double Lerp(double from, double to, double amount)
        {
            return from + (to - from) * amount;
        }

        /// <summary>edge0〜edge1 を 0〜1 に写す滑らかな重み。</summary>
        public static double Smoothstep(double edge0, double edge1, double value)
        {
            double amount = Clamp01((value - edge0) / (edge1 - edge0));
            return amount * amount * (3.0 - 2.0 * amount);
        }

        #endregion

        #region Angles

        /// <summary>常に modulo と同符号の剰余を返す。</summary>
        public static double UnsignedMod(double value, double modulo)
        {
            return (value % modulo + modulo) % modulo;
        }

        /// <summary>
        /// source から target への最短の符号付き角度差（度）。戻り値は [-180, 180)。
        /// </summary>
        public static double SignedAngleBetween(double target, double source)
        {
            return UnsignedMod(target - source + 180.0, 360.0) - 180.0;
        }

        #endregion

        #region Quantization

        /// <summary>
        /// origin を基準に step 間隔へスナップする。
        /// step&lt;=0 や非有限値は「ホスト側の方針に委ねる」ため値をそのまま返す。
        /// </summary>
        public static double Quantize(double value, double step, double origin = 0.0)
        {
            if (!IsFinite(value) || !IsFinite(step) || step <= 0.0 || !IsFinite(origin))
            {
                return value;
            }

            // Rust の f64::round は「0 から遠い方へ」丸める。C# の既定は銀行家丸めなので明示指定が必須。
            double steps = Math.Round((value - origin) / step, MidpointRounding.AwayFromZero);
            return NormalizeZero(steps * step + origin);
        }

        /// <summary>step の小数桁数。max(0, ceil(-log10(step)))。</summary>
        public static int PrecisionOf(double step)
        {
            if (step == 0.0 || !IsFinite(step))
            {
                return 0;
            }

            // 負の step は log10 が NaN になるため絶対値で扱う（TS 版は NaN を返してしまう）。
            double precision = Math.Ceiling(-Math.Log10(Math.Abs(step)));
            if (double.IsNaN(precision) || precision <= 0.0)
            {
                return 0;
            }

            return precision >= int.MaxValue ? int.MaxValue : (int)precision;
        }

        #endregion

        #region Helpers

        /// <summary>NaN・無限大でないこと。</summary>
        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>-0 を +0 に正規化する。表示・比較のブレを避けるため。</summary>
        public static double NormalizeZero(double value)
        {
            return value == 0.0 ? 0.0 : value;
        }

        /// <summary>Math.Clamp と違い min&gt;max でも例外を投げない。</summary>
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
