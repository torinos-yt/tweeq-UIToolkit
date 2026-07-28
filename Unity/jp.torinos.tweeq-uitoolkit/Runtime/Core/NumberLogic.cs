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

        #region Scale dots

        /// <summary>ドット列の帯を巡回させる周期。3 本の帯が 1 桁ずつずれて重なる。</summary>
        public const double SCALE_DOT_PRECISION_CYCLE = 3.0;

        /// <summary>
        /// 1 帯あたりの点数の安全弁。opacity ゲートを通った帯は間隔が 10px 以上になるので、
        /// 通常の幅ではここに届かない（位相が壊れたときだけの保険）。
        /// </summary>
        public const int SCALE_DOT_MAX_PER_LAYER = 256;

        // 位相が double の刻み幅より粗い領域まで飛ぶと mod の結果が意味を失う。
        // そこまで行くのは画面外なので帯ごと捨てる
        const double SCALE_DOT_MAX_PHASE = 1e15;

        /// <summary>スケールドット 1 帯ぶんの幾何。ドラッグ中に毎フレーム組み直すので struct。</summary>
        public struct ScaleDotLayer
        {
            /// <summary>点の間隔（px）。</summary>
            public double Gap;

            /// <summary>フィールド左端（x=0）以降で最初に来る点の中心 x。</summary>
            public double FirstX;

            /// <summary>幅に収まる点の数。</summary>
            public int Count;

            /// <summary>帯の不透明度（0〜1）。</summary>
            public double Opacity;

            public ScaleDotLayer(double gap, double firstX, int count, double opacity)
            {
                Gap = gap;
                FirstX = firstX;
                Count = count;
                Opacity = opacity;
            }

            /// <summary>index 番目（0 始まり）の点の中心 x。</summary>
            public double DotX(int index)
            {
                return FirstX + index * Gap;
            }
        }

        /// <summary>
        /// 帯 offset の「桁」。感度が 1 桁変わるごとに帯が 1 つぶん送られ、
        /// 3 本が入れ替わりながら循環する。非有限な感度では NaN を返す。
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

        /// <summary>帯の濃さ。密になる（桁が小さい）ほど消え、粗くなるほど濃くなる。</summary>
        public static double ScaleDotOpacity(double precision)
        {
            if (!TweeqMath.IsFinite(precision))
            {
                return 0.0;
            }

            return Math.Sqrt(TweeqMath.Smoothstep(1.0, 2.0, precision));
        }

        /// <summary>
        /// 点列の基準位相（フィールドローカル x）。バー有りはハンドル位置、無しは
        /// 「値 0 が中央に来る」位置。点はここから gap 刻みで敷かれるので、値に整列する。
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
        /// 帯 offset の点列を組む。濃さが minOpacity に届かない帯は「見えないのに数百点になる」
        /// 側なので、丸ごと捨てて false を返す。
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
        /// スケールドットを出してよいか。step 付きで両端が Clamp されたフィールドは
        /// 離散的な止まり位置しか持たないので、連続感度を表すドットに意味が無い。
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
