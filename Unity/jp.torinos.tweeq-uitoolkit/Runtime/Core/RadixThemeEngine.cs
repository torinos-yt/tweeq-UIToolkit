using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>外観モード。Radix はライト／ダークで別のパレットを引く。</summary>
    public enum RadixAppearance
    {
        Light,

        Dark,
    }

    /// <summary>
    /// 1 色相を Radix 12 段スケールへ当てはめた結果。段の役割は原典どおり
    /// 9(<c>Scale[8]</c>) = ソリッド塗り、10 = そのホバー、11/12 = 文字、2/3 = 淡い面、6/7 = 境界。
    /// </summary>
    public struct RadixScale
    {
        /// <summary>12 段の不透明色。</summary>
        public Rgba32[] Scale;

        /// <summary>12 段を「背景の上に半透明で置いたら同じ見えになる色」へ変換したもの。</summary>
        public Rgba32[] ScaleAlpha;

        /// <summary>Scale[8] の上に載せて読める文字色。</summary>
        public Rgba32 Contrast;
    }

    /// <summary>アクセント＋グレーのスケール一式。Vue 版 generateThemeColorsRadix の戻り値に対応。</summary>
    public sealed class RadixThemeColors
    {
        public Rgba32[] AccentScale;

        public Rgba32[] AccentScaleAlpha;

        public Rgba32 AccentContrast;

        public Rgba32[] GrayScale;

        public Rgba32[] GrayScaleAlpha;

        /// <summary>入力背景色を sRGB へ丸め直したもの（原典も OKLCH 経由で往復させる）。</summary>
        public Rgba32 Background;
    }

    #endregion

    /// <summary>
    /// Radix Colors のテーマ生成器。ref/tweeq/src/theme/radix.ts（Radix 公式サイトの
    /// generateRadixColors のコピー）の移植で、生成は同期・決定的。
    /// </summary>
    /// <remarks>
    /// 中間計算は colorjs.io 0.5.2 の挙動に合わせてある。特に
    /// ・スケール混合は OKLab ではなく CIE Lab(D50)（colorjs.io の既定補間空間）
    /// ・無彩色の色相 NaN は変換時に「0 度」として読まれる
    /// ・sRGB への書き出しは CSS Color 4 の Gamut Mapping を通る
    /// の 3 点は結果が 1/255 単位でずれる要因なので、簡略化してはいけない。
    /// </remarks>
    public static class RadixThemeEngine
    {
        #region Constants

        const int STEP_COUNT = RadixPaletteData.STEP_COUNT;
        const int SCALE_COUNT = RadixPaletteData.SCALE_COUNT;
        const int GRAY_SCALE_COUNT = RadixPaletteData.GRAY_SCALE_COUNT;

        // 明度の再配置に使うベジェ。ダークは step1 付近を寝かせ、ライトは逆に立てる
        static readonly double[] DARK_EASING = { 1.0, 0.0, 1.0, 0.0 };
        static readonly double[] LIGHT_EASING = { 0.0, 2.0, 0.0, 2.0 };

        // 背景が step1 より明るいとき、イージングを線形へ寄せ切る上限比
        const double MAX_LIGHTNESS_RATIO = 1.5;

        // ライト／ダークの分岐しきい値（混合スケールの step1 明度）
        const double LIGHT_MODE_THRESHOLD = 0.5;

        // シードが背景と近すぎる（白地に白／黒地に黒）と判定する deltaEOK×100 のしきい値
        const double STEP9_FALLBACK_DISTANCE = 25.0;

        // 白文字が読めないと判定する APCA Lc のしきい値
        const double TEXT_CONTRAST_THRESHOLD = 40.0;

        // 「targetAlpha 指定なし」を表す番兵。0 は有効な指定値なので負値を使う
        const double NO_TARGET_ALPHA = -1.0;

        #endregion

        #region Public

        /// <summary>
        /// アクセント色・グレー色・背景色から 12 段スケール一式を生成する。
        /// </summary>
        /// <param name="appearance">ライト／ダーク。</param>
        /// <param name="background">背景色（sRGB, [0, 1]）。</param>
        /// <param name="accent">アクセントのシード色。</param>
        /// <param name="gray">グレーのシード色。</param>
        public static RadixThemeColors GenerateThemeColors(
            RadixAppearance appearance, Rgba background, Rgba accent, Rgba gray)
        {
            bool dark = appearance == RadixAppearance.Dark;

            Oklch backgroundColor = ToOklch(background);
            Rgba32 backgroundBytes = TweeqOklch.OklchToBytes(backgroundColor);

            // グレーはグレー系 6 スケールだけを候補にする（有彩色に吸われないため）
            Oklch[] grayScale = GetScaleFromColor(ToOklch(gray), dark, GRAY_SCALE_COUNT, backgroundColor);

            RadixScale accentScale = BuildAccentLikeScale(
                ToOklch(accent), dark, SCALE_COUNT, backgroundColor, backgroundBytes, grayScale);

            Rgba32[] grayBytes = new Rgba32[STEP_COUNT];
            Rgba32[] grayAlpha = new Rgba32[STEP_COUNT];
            for (int i = 0; i < STEP_COUNT; i++)
            {
                grayBytes[i] = TweeqOklch.OklchToBytes(grayScale[i]);
                grayAlpha[i] = GetAlphaColor(grayBytes[i], backgroundBytes, NO_TARGET_ALPHA);
            }

            return new RadixThemeColors
            {
                AccentScale = accentScale.Scale,
                AccentScaleAlpha = accentScale.ScaleAlpha,
                AccentContrast = accentScale.Contrast,
                GrayScale = grayBytes,
                GrayScaleAlpha = grayAlpha,
                Background = backgroundBytes,
            };
        }

        /// <summary>
        /// 単一のシード色を 12 段スケールへ当てはめる（原典 generateRadixScale）。
        /// セマンティック色やシンタックスハイライトのように「アクセントとは別の色相」を
        /// 同じ機構で扱いたいときの入口。
        /// </summary>
        public static RadixScale GenerateScale(RadixAppearance appearance, Rgba background, Rgba seed)
        {
            bool dark = appearance == RadixAppearance.Dark;
            Oklch backgroundColor = ToOklch(background);
            Rgba32 backgroundBytes = TweeqOklch.OklchToBytes(backgroundColor);

            return BuildAccentLikeScale(
                ToOklch(seed), dark, SCALE_COUNT, backgroundColor, backgroundBytes, null);
        }

        /// <summary>
        /// 不透明色を「背景の上に重ねたら同じ見えになる半透明色」へ変換する（原典 getAlphaColorSrgb）。
        /// </summary>
        public static Rgba32 ToAlphaOverBackground(Rgba32 target, Rgba32 background)
        {
            return GetAlphaColor(target, background, NO_TARGET_ALPHA);
        }

        /// <summary>
        /// <see cref="ToAlphaOverBackground(Rgba32, Rgba32)"/> の α 固定版。
        /// Radix の accentSurface（ライト 0.8 / ダーク 0.5）のように不透明度を決め打ちする用途。
        /// </summary>
        public static Rgba32 ToAlphaOverBackground(Rgba32 target, Rgba32 background, double targetAlpha)
        {
            if (targetAlpha < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetAlpha));
            }

            return GetAlphaColor(target, background, targetAlpha);
        }

        #endregion

        #region Scale building

        // アクセント処理の本体。セマンティック色も同じ機構を通す（原典 buildAccentLikeScale）。
        // grayScaleColors は「純白／純黒シードにグレーの色味を貸す」ためだけに使う
        static RadixScale BuildAccentLikeScale(
            Oklch seed,
            bool dark,
            int scaleCount,
            Oklch backgroundColor,
            Rgba32 backgroundBytes,
            Oklch[] grayScaleColors)
        {
            Oklch[] scale = GetScaleFromColor(seed, dark, scaleCount, backgroundColor);

            // 純白／純黒のシードは色相を持たないので、グレースケールの色味を借りる
            Rgba32 seedBytes = TweeqOklch.OklchToBytes(seed);
            bool isPureBlack = seedBytes.R == 0 && seedBytes.G == 0 && seedBytes.B == 0;
            bool isPureWhite = seedBytes.R == 255 && seedBytes.G == 255 && seedBytes.B == 255;
            if ((isPureBlack || isPureWhite) && grayScaleColors != null)
            {
                scale = (Oklch[])grayScaleColors.Clone();
            }

            GetStep9Colors(scale, seed, out Oklch step9, out Oklch contrast);
            scale[8] = step9;
            scale[9] = GetButtonHoverColor(step9, scale);

            // 文字段（11/12）の彩度上限。ソリッド塗りと境界の彩度を超えさせない
            double chromaCap = Math.Max(scale[8].C, scale[7].C);
            scale[10].C = Math.Min(chromaCap, scale[10].C);
            scale[11].C = Math.Min(chromaCap, scale[11].C);

            Rgba32[] bytes = new Rgba32[STEP_COUNT];
            Rgba32[] alpha = new Rgba32[STEP_COUNT];
            for (int i = 0; i < STEP_COUNT; i++)
            {
                bytes[i] = TweeqOklch.OklchToBytes(scale[i]);
                alpha[i] = GetAlphaColor(bytes[i], backgroundBytes, NO_TARGET_ALPHA);
            }

            return new RadixScale
            {
                Scale = bytes,
                ScaleAlpha = alpha,
                Contrast = TweeqOklch.OklchToBytes(contrast),
            };
        }

        /// <summary>
        /// シード色に最も近い 2 スケールを三角測量で混ぜ、彩度・色相をシードへ合わせ、
        /// 明度を背景基準へ再配置する（原典 getScaleFromColor）。
        /// </summary>
        static Oklch[] GetScaleFromColor(Oklch source, bool dark, int scaleCount, Oklch backgroundColor)
        {
            // 各スケールの「シードに最も近い 1 色」までの距離
            int[] order = new int[scaleCount];
            double[] distances = new double[scaleCount];
            Oklch[] nearest = new Oklch[scaleCount];

            for (int scaleIndex = 0; scaleIndex < scaleCount; scaleIndex++)
            {
                double best = double.PositiveInfinity;
                Oklch bestColor = default(Oklch);
                for (int step = 0; step < STEP_COUNT; step++)
                {
                    Oklch candidate = RadixPaletteData.Get(dark, scaleIndex, step);
                    double distance = TweeqOklch.DeltaEOK(source, candidate);
                    if (distance < best)
                    {
                        best = distance;
                        bestColor = candidate;
                    }
                }

                order[scaleIndex] = scaleIndex;
                distances[scaleIndex] = best;
                nearest[scaleIndex] = bestColor;
            }

            // 挿入ソート。同距離のときスケール定義順を保つ（原典の安定ソート＋重複除去と同じ並び）
            for (int i = 1; i < scaleCount; i++)
            {
                int current = order[i];
                int j = i - 1;
                while (j >= 0 && distances[order[j]] > distances[current])
                {
                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = current;
            }

            int count = scaleCount;

            // 上位 2 件がどちらもグレーだと、グレー同士は互いに近すぎて 2 番目から情報が取れない。
            // 1 位がグレーのときは 2 位以降のグレーを飛ばして有彩色を拾う
            bool allAreGrays = true;
            for (int i = 0; i < count; i++)
            {
                if (!RadixPaletteData.IsGrayScale(order[i]))
                {
                    allAreGrays = false;
                    break;
                }
            }

            if (!allAreGrays && RadixPaletteData.IsGrayScale(order[0]))
            {
                while (RadixPaletteData.IsGrayScale(order[1]))
                {
                    Array.Copy(order, 2, order, 1, count - 2);
                    count--;
                }
            }

            int indexA = order[0];
            int indexB = order[1];

            // 三角測量。A・B・シードの三角形で、A と B のどちらの角も鈍角でなければ
            // AD:BD の比で混ぜたほうがシードに近づく。鈍角なら B は A と同方向なので混ぜない
            double sideA = distances[indexB];
            double sideB = distances[indexA];
            double sideC = TweeqOklch.DeltaEOK(nearest[indexA], nearest[indexB]);

            double cosA = (sideB * sideB + sideC * sideC - sideA * sideA) / (2.0 * sideB * sideC);
            double sinA = Math.Sin(Math.Acos(cosA));
            double cosB = (sideA * sideA + sideC * sideC - sideB * sideB) / (2.0 * sideA * sideC);
            double sinB = Math.Sin(Math.Acos(cosB));

            double tangentRatio = (cosA / sinA) / (cosB / sinB);
            double ratio = Math.Max(0.0, tangentRatio) * 0.5;

            // 混合は CIE Lab(D50)。colorjs.io の Color.mix が既定でこの空間を使う
            Oklch[] scale = new Oklch[STEP_COUNT];
            for (int step = 0; step < STEP_COUNT; step++)
            {
                Oklab labA = TweeqOklch.OklabToLabD50(
                    TweeqOklch.OklchToOklab(RadixPaletteData.Get(dark, indexA, step)));
                Oklab labB = TweeqOklch.OklabToLabD50(
                    TweeqOklch.OklchToOklab(RadixPaletteData.Get(dark, indexB, step)));

                Oklab mixed = new Oklab(
                    labA.L + (labB.L - labA.L) * ratio,
                    labA.A + (labB.A - labA.A) * ratio,
                    labA.B + (labB.B - labA.B) * ratio);

                scale[step] = TweeqOklch.OklabToOklch(TweeqOklch.LabD50ToOklab(mixed));
            }

            // 混合スケール内でシードに最も近い段を基準に、彩度差を全段へ反映する
            int baseIndex = 0;
            double baseDistance = TweeqOklch.DeltaEOK(source, scale[0]);
            for (int step = 1; step < STEP_COUNT; step++)
            {
                double distance = TweeqOklch.DeltaEOK(source, scale[step]);
                if (distance < baseDistance)
                {
                    baseDistance = distance;
                    baseIndex = step;
                }
            }

            // ratio が 0 になり、かつ A が純無彩の gray スケールだと、混合スケールの彩度は
            // 浮動小数の丸め残り（1e-16 台）しか残らない。そこへ有彩色のシードが来るとこの除算が
            // 1e14 倍に増幅され、原典（ブラウザ／Node）と最下位ビットの違いだけで結果が変わる。
            // 原典の式そのものが悪条件なので合わせ込みはせず、そういう入力があることだけ記す
            double chromaRatio = source.C / scale[baseIndex].C;
            double chromaCeiling = source.C * 1.5;
            for (int step = 0; step < STEP_COUNT; step++)
            {
                scale[step].C = Math.Min(chromaCeiling, scale[step].C * chromaRatio);
                scale[step].H = source.H;
            }

            if (scale[0].L > LIGHT_MODE_THRESHOLD)
            {
                ApplyLightModeLightness(scale, backgroundColor);
            }
            else
            {
                ApplyDarkModeLightness(scale, backgroundColor);
            }

            return scale;
        }

        // ライト: 白を「0 段目」として足した 13 点で再配置し、足した分を捨てる
        static void ApplyLightModeLightness(Oklch[] scale, Oklch backgroundColor)
        {
            double backgroundL = Clamp01(backgroundColor.L);

            double[] lightness = new double[STEP_COUNT + 1];
            lightness[0] = 1.0;
            for (int step = 0; step < STEP_COUNT; step++)
            {
                lightness[step + 1] = scale[step].L;
            }

            TransposeProgressionStart(backgroundL, lightness, LIGHT_EASING);
            for (int step = 0; step < STEP_COUNT; step++)
            {
                scale[step].L = lightness[step + 1];
            }
        }

        // ダーク: 背景が step1 より明るいほどイージングを線形へ寄せ、明度差を潰しすぎないようにする
        static void ApplyDarkModeLightness(Oklch[] scale, Oklch backgroundColor)
        {
            double[] easing = (double[])DARK_EASING.Clone();
            double referenceL = scale[0].L;
            double clampedBackgroundL = Clamp01(backgroundColor.L);
            double lightnessRatio = clampedBackgroundL / referenceL;

            if (lightnessRatio > 1.0)
            {
                double metaRatio = (lightnessRatio - 1.0)
                    * (MAX_LIGHTNESS_RATIO / (MAX_LIGHTNESS_RATIO - 1.0));
                for (int i = 0; i < easing.Length; i++)
                {
                    easing[i] = lightnessRatio > MAX_LIGHTNESS_RATIO
                        ? 0.0
                        : Math.Max(0.0, easing[i] * (1.0 - metaRatio));
                }
            }

            double[] lightness = new double[STEP_COUNT];
            for (int step = 0; step < STEP_COUNT; step++)
            {
                lightness[step] = scale[step].L;
            }

            // ここだけクランプ前の背景明度を使うのも原典どおり（ratioL 側は clamp 済みの値）
            TransposeProgressionStart(backgroundColor.L, lightness, easing);
            for (int step = 0; step < STEP_COUNT; step++)
            {
                scale[step].L = lightness[step];
            }
        }

        /// <summary>
        /// 数列の先頭を <paramref name="to"/> へ移し、そのずれをベジェ曲線で末尾へ向けて減衰させる。
        /// 配列を破壊的に書き換える（原典 transposeProgressionStart）。
        /// </summary>
        static void TransposeProgressionStart(double to, double[] values, double[] curve)
        {
            CubicBezierEasing easing = new CubicBezierEasing(curve[0], curve[1], curve[2], curve[3]);
            int lastIndex = values.Length - 1;
            double diff = values[0] - to;

            for (int i = 0; i < values.Length; i++)
            {
                values[i] -= diff * easing.Evaluate(1.0 - (double)i / lastIndex);
            }
        }

        #endregion

        #region Step 9

        // シードが背景と近すぎる（白地に白／黒地に黒）ならスケール側の step9 に逃がす
        static void GetStep9Colors(Oklch[] scale, Oklch seed, out Oklch step9, out Oklch contrast)
        {
            double distance = TweeqOklch.DeltaEOK(seed, scale[0]) * 100.0;
            Oklch chosen = distance < STEP9_FALLBACK_DISTANCE ? scale[8] : seed;

            step9 = chosen;
            contrast = GetTextColor(chosen);
        }

        // step9 の上に載せる文字色。白で読めなければ同色相の暗色を作る
        static Oklch GetTextColor(Oklch background)
        {
            TweeqOklch.OklchToSrgb(background, out double r, out double g, out double b);
            double contrast = TweeqOklch.ContrastApca(1.0, 1.0, 1.0, r, g, b);

            // NaN（ガモット外で輝度が負になった場合）は「白で読める」側に倒れる。原典と同じ
            if (Math.Abs(contrast) < TEXT_CONTRAST_THRESHOLD)
            {
                return new Oklch(0.25, Math.Max(0.08 * background.C, 0.04), background.H);
            }

            return new Oklch(1.0, 0.0, 0.0);
        }

        // ソリッド塗りのホバー。明度を動かしたあと、彩度と色相はスケール内の最近傍から借りる。
        // 借りるのは、純白／純黒のシードでもグレースケールの色味が乗るようにするため
        static Oklch GetButtonHoverColor(Oklch source, Oklch[] scale)
        {
            double newL = source.L > 0.4
                ? source.L - 0.03 / (source.L + 0.1)
                : source.L + 0.03 / (source.L + 0.1);
            double newC = source.L > 0.4 && !double.IsNaN(source.H) ? source.C * 0.93 : source.C;

            Oklch hover = new Oklch(newL, newC, source.H);

            Oklch closest = hover;
            double minDistance = double.PositiveInfinity;
            for (int i = 0; i < scale.Length; i++)
            {
                double distance = TweeqOklch.DeltaEOK(hover, scale[i]);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closest = scale[i];
                }
            }

            return new Oklch(newL, closest.C, closest.H);
        }

        #endregion

        #region Alpha

        /// <summary>
        /// target = background * (1 - α) + foreground * α を α について解く（原典 getAlphaColor）。
        /// </summary>
        /// <remarks>
        /// 整数丸めの補正が入るのは、ブラウザが半透明合成をチャンネルごとに丸めるため。
        /// 原典のコメントいわく実測で確かめられた挙動で、式を整理すると 1 段ずれる。
        /// </remarks>
        static Rgba32 GetAlphaColor(Rgba32 target, Rgba32 background, double targetAlpha)
        {
            const double PRECISION = 255.0;

            int tr = target.R;
            int tg = target.G;
            int tb = target.B;
            int br = background.R;
            int bg = background.G;
            int bb = background.B;

            // 背景より明るいチャンネルが 1 つでもあれば「白を足す」、なければ「黒を足す」
            int desired = 0;
            if (tr > br || tg > bg || tb > bb)
            {
                desired = (int)PRECISION;
            }

            double alphaR = (double)(tr - br) / (desired - br);
            double alphaG = (double)(tg - bg) / (desired - bg);
            double alphaB = (double)(tb - bb) / (desired - bb);

            // 純グレー同士は精度合わせが不要で、そのまま出したほうがきれいな値になる
            bool isPureGray = alphaR == alphaG && alphaR == alphaB;
            if (targetAlpha < 0.0 && isPureGray)
            {
                int gray = desired;
                return new Rgba32(gray, gray, gray, RoundToByte(ClampPrecision(alphaR * PRECISION)));
            }

            double maxAlpha = targetAlpha >= 0.0
                ? targetAlpha
                : Math.Max(alphaR, Math.Max(alphaG, alphaB));

            double alpha = ClampPrecision(Math.Ceiling(maxAlpha * PRECISION)) / PRECISION;

            int r = CeilToInt(ClampPrecision((br * (1.0 - alpha) - tr) / alpha * -1.0));
            int g = CeilToInt(ClampPrecision((bg * (1.0 - alpha) - tg) / alpha * -1.0));
            int b = CeilToInt(ClampPrecision((bb * (1.0 - alpha) - tb) / alpha * -1.0));

            int blendedR = BlendAlpha(r, alpha, br);
            int blendedG = BlendAlpha(g, alpha, bg);
            int blendedB = BlendAlpha(b, alpha, bb);

            if (desired == 0)
            {
                if (tr <= br && tr != blendedR)
                {
                    r += tr > blendedR ? 1 : -1;
                }

                if (tg <= bg && tg != blendedG)
                {
                    g += tg > blendedG ? 1 : -1;
                }

                if (tb <= bb && tb != blendedB)
                {
                    b += tb > blendedB ? 1 : -1;
                }
            }
            else
            {
                if (tr >= br && tr != blendedR)
                {
                    r += tr > blendedR ? 1 : -1;
                }

                if (tg >= bg && tg != blendedG)
                {
                    g += tg > blendedG ? 1 : -1;
                }

                if (tb >= bb && tb != blendedB)
                {
                    b += tb > blendedB ? 1 : -1;
                }
            }

            // ±1 補正でバイト範囲を越えることがある。原典は HEX 書き出し時に再クランプされる
            return new Rgba32(
                ClampByte(r), ClampByte(g), ClampByte(b), RoundToByte(alpha * PRECISION));
        }

        // ブラウザは合成結果をまとめて丸めず、前景・背景それぞれを丸めて足す
        static int BlendAlpha(int foreground, double alpha, int background)
        {
            return RoundHalfUp(background * (1.0 - alpha)) + RoundHalfUp(foreground * alpha);
        }

        static double ClampPrecision(double value)
        {
            if (double.IsNaN(value))
            {
                return 0.0;
            }

            return Math.Min(255.0, Math.Max(0.0, value));
        }

        static int CeilToInt(double value)
        {
            return double.IsNaN(value) ? 0 : (int)Math.Ceiling(value);
        }

        static int RoundToByte(double value)
        {
            return ClampByte(RoundHalfUp(value));
        }

        // JS の Math.round は常に上寄せ。C# 既定の ToEven とは 0.5 の扱いが違う
        static int RoundHalfUp(double value)
        {
            return double.IsNaN(value) ? 0 : (int)Math.Floor(value + 0.5);
        }

        static int ClampByte(int value)
        {
            return value < 0 ? 0 : value > 255 ? 255 : value;
        }

        #endregion

        #region Helpers

        static Oklch ToOklch(Rgba color)
        {
            return TweeqOklch.SrgbToOklch(color.R, color.G, color.B);
        }

        static double Clamp01(double value)
        {
            return value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }

        #endregion
    }
}
