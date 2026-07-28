using System;

namespace Tweeq.Core
{
    /// <summary>
    /// InputNumber の表示桁・ドラッグ感度・矢印キー増分。UnityEngine 非依存・すべて double。
    /// </summary>
    public static class NumberLogic
    {
        #region Constants

        /// <summary>バー無しフィールドで 1 step 進むのに必要なピクセル数。</summary>
        public const double PX_PER_STEP = 20.0;

        #endregion

        #region Precision

        /// <summary>表示文字列の小数桁数。最後の '.' 以降が数字だけのときその桁数、それ以外は 0。</summary>
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

            // TS 版の /\.[0-9]*$/ 相当。指数表記など末尾が数字でない場合は桁数として扱わない。
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
        /// 仕様 §4 の precision()。step があれば常にそれが最優先。
        /// </summary>
        public static int GetDisplayPrecision(
            double step, string display, double min, double max, double width,
            bool barVisible, bool tweaking, double speed, int precisionLimit)
        {
            if (step != 0.0 && TweeqMath.IsFinite(step))
            {
                return TweeqMath.PrecisionOf(step);
            }

            int displayPrecision = PrecisionOfDisplay(display);
            int sliderPrecision = 0;
            if (barVisible && width > 0.0 && TweeqMath.IsFinite(min) && TweeqMath.IsFinite(max))
            {
                sliderPrecision = TweeqMath.PrecisionOf(Math.Abs(max - min) / width);
            }

            if (tweaking)
            {
                // ドラッグ中は感度そのものが桁の下限になる（細かい速度なら細かく見せる）。
                return Math.Max(displayPrecision, Math.Max(sliderPrecision, TweeqMath.PrecisionOf(speed)));
            }

            int limit = precisionLimit < 0 ? 0 : precisionLimit;
            return Math.Min(limit, Math.Max(displayPrecision, sliderPrecision));
        }

        #endregion

        #region Format

        /// <summary>
        /// tweaking 中は末尾ゼロを維持した固定小数、静止時は末尾ゼロ・末尾ドットをトリムし -0 を "0" に正規化する。
        /// </summary>
        /// <remarks>
        /// 実体は <see cref="TweeqFormat.Format"/>。文字列生成をここに残すと ZString 版と二重管理になるので、
        /// 既存呼び出し互換のための薄い転送だけを置いている。
        /// </remarks>
        public static string Format(double value, int precision, bool tweaking)
        {
            return TweeqFormat.Format(value, precision, tweaking);
        }

        #endregion

        #region Speed

        /// <summary>px あたりの値変化量。バー有りはレンジ全体を幅に写し、無しは step ベース。</summary>
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

        /// <summary>縦ドラッグで下げられる感度の下限。step 付きバーは「1 step が何 px か」で決まる。</summary>
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

        /// <summary>縦ドラッグで上げられる感度の上限。バー有りはレンジを超えて加速しても無意味なので 1。</summary>
        public static double MaxSpeed(bool barVisible)
        {
            return barVisible ? 1.0 : 1000.0;
        }

        #endregion

        #region Keyboard

        /// <summary>
        /// ↑/↓ キーの新しい値。direction は ±1。戻り値は [validMin, validMax] にクランプ済み。
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
                // step 有りでは Alt(×0.1) を無効化したいので max(1, ·) を通す。
                next = current + direction * step * Math.Max(1.0, keyMultiplier);
            }
            else
            {
                double multiplier = keyMultiplier;
                double span = validMax - validMin;
                if (TweeqMath.IsFinite(span) && span <= 1.0)
                {
                    // 0〜1 のような狭いレンジで 1 刻みは粗すぎるため、さらに一桁落とす。
                    multiplier *= 0.1;
                }

                next = current + direction * multiplier;
            }

            return TweeqMath.NormalizeZero(TweeqMath.Clamp(next, validMin, validMax));
        }

        #endregion
    }
}
