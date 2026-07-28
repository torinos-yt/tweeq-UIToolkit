using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>Appearance mode. Radix draws from a different palette for light versus dark.</summary>
    public enum RadixAppearance
    {
        Light,

        Dark,
    }

    /// <summary>
    /// The result of fitting one hue onto the Radix 12-step scale. Per the original, the steps'
    /// roles are: 9 (<c>Scale[8]</c>) = solid fill, 10 = its hover, 11/12 = text, 2/3 = subtle
    /// surfaces, 6/7 = borders.
    /// </summary>
    public struct RadixScale
    {
        /// <summary>The 12 opaque step colors.</summary>
        public Rgba32[] Scale;

        /// <summary>The 12 steps converted into "the translucent color that looks identical when placed over the background".</summary>
        public Rgba32[] ScaleAlpha;

        /// <summary>The text color that reads legibly on top of Scale[8].</summary>
        public Rgba32 Contrast;
    }

    /// <summary>The full accent + gray scale set. Corresponds to the return value of the Vue original's generateThemeColorsRadix.</summary>
    public sealed class RadixThemeColors
    {
        public Rgba32[] AccentScale;

        public Rgba32[] AccentScaleAlpha;

        public Rgba32 AccentContrast;

        public Rgba32[] GrayScale;

        public Rgba32[] GrayScaleAlpha;

        /// <summary>The input background color rounded back into sRGB (the original also round-trips it through OKLCH).</summary>
        public Rgba32 Background;
    }

    #endregion

    /// <summary>
    /// The theme generator for Radix Colors. A port of ref/tweeq/src/theme/radix.ts (a copy of
    /// the Radix official site's generateRadixColors); generation is synchronous and deterministic.
    /// </summary>
    /// <remarks>
    /// The intermediate calculations are matched to colorjs.io 0.5.2's behavior. In particular:
    /// - Scale mixing happens in CIE Lab(D50), not OKLab (colorjs.io's default interpolation space)
    /// - An achromatic hue of NaN is read as "0 degrees" during conversion
    /// - Writing out to sRGB goes through CSS Color 4's Gamut Mapping
    /// These three points are the sources of 1/255-unit discrepancies in the result, so they must not be simplified away.
    /// </remarks>
    public static class RadixThemeEngine
    {
        #region Constants

        const int STEP_COUNT = RadixPaletteData.STEP_COUNT;
        const int SCALE_COUNT = RadixPaletteData.SCALE_COUNT;
        const int GRAY_SCALE_COUNT = RadixPaletteData.GRAY_SCALE_COUNT;

        // Bezier curves used to redistribute lightness. Dark flattens near step1, light steepens it instead
        static readonly double[] DARK_EASING = { 1.0, 0.0, 1.0, 0.0 };
        static readonly double[] LIGHT_EASING = { 0.0, 2.0, 0.0, 2.0 };

        // When the background is lighter than step1, the upper ratio at which the easing is pulled all the way to linear
        const double MAX_LIGHTNESS_RATIO = 1.5;

        // Light/dark branch threshold (the mixed scale's step1 lightness)
        const double LIGHT_MODE_THRESHOLD = 0.5;

        // deltaEOK x100 threshold for judging the seed too close to the background (white on white / black on black)
        const double STEP9_FALLBACK_DISTANCE = 25.0;

        // APCA Lc threshold for judging that white text is unreadable
        const double TEXT_CONTRAST_THRESHOLD = 40.0;

        // Sentinel meaning "no targetAlpha specified". 0 is a valid specified value, so a negative number is used
        const double NO_TARGET_ALPHA = -1.0;

        #endregion

        #region Public

        /// <summary>
        /// Generates the full set of 12-step scales from the accent, gray, and background colors.
        /// </summary>
        /// <param name="appearance">Light or dark.</param>
        /// <param name="background">The background color (sRGB, [0, 1]).</param>
        /// <param name="accent">The accent seed color.</param>
        /// <param name="gray">The gray seed color.</param>
        public static RadixThemeColors GenerateThemeColors(
            RadixAppearance appearance, Rgba background, Rgba accent, Rgba gray)
        {
            bool dark = appearance == RadixAppearance.Dark;

            Oklch backgroundColor = ToOklch(background);
            Rgba32 backgroundBytes = TweeqOklch.OklchToBytes(backgroundColor);

            // Gray only considers the 6 gray-family scales as candidates (so it isn't pulled toward a chromatic one)
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
        /// Fits a single seed color onto the 12-step scale (the original's generateRadixScale).
        /// The entry point for when you want to run "a hue that isn't the accent" — like a
        /// semantic color or syntax highlighting — through the same mechanism.
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
        /// Converts an opaque color into "the translucent color that looks identical when layered
        /// over the background" (the original's getAlphaColorSrgb).
        /// </summary>
        public static Rgba32 ToAlphaOverBackground(Rgba32 target, Rgba32 background)
        {
            return GetAlphaColor(target, background, NO_TARGET_ALPHA);
        }

        /// <summary>
        /// The alpha-fixed variant of <see cref="ToAlphaOverBackground(Rgba32, Rgba32)"/>.
        /// For pinning down an opacity in advance, as with Radix's accentSurface (light 0.8 / dark 0.5).
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

        // The core of accent processing. Semantic colors are also run through the same mechanism (the original's buildAccentLikeScale).
        // grayScaleColors exists only to "lend gray's tint to a pure white/black seed"
        static RadixScale BuildAccentLikeScale(
            Oklch seed,
            bool dark,
            int scaleCount,
            Oklch backgroundColor,
            Rgba32 backgroundBytes,
            Oklch[] grayScaleColors)
        {
            Oklch[] scale = GetScaleFromColor(seed, dark, scaleCount, backgroundColor);

            // A pure white/black seed has no hue, so it borrows gray scale's tint
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

            // Chroma ceiling for the text steps (11/12). Keeps them from exceeding the chroma of the solid fill and the border
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
        /// Mixes the 2 scales nearest the seed color via triangulation, aligns chroma and hue to
        /// the seed, and redistributes lightness relative to the background (the original's getScaleFromColor).
        /// </summary>
        static Oklch[] GetScaleFromColor(Oklch source, bool dark, int scaleCount, Oklch backgroundColor)
        {
            // Each scale's distance to "the single color closest to the seed"
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

            // Insertion sort. Preserves scale-definition order for equal distances (same ordering as the original's stable sort + dedup)
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

            // If the top 2 are both gray, the grays are too close to each other for the 2nd place
            // to yield any information. When 1st place is gray, skip further grays to pick up a chromatic one
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

            // Triangulation. In the triangle formed by A, B, and the seed, if neither the angle
            // at A nor at B is obtuse, mixing by the AD:BD ratio gets closer to the seed. If obtuse, B lies in the same direction as A, so don't mix
            double sideA = distances[indexB];
            double sideB = distances[indexA];
            double sideC = TweeqOklch.DeltaEOK(nearest[indexA], nearest[indexB]);

            double cosA = (sideB * sideB + sideC * sideC - sideA * sideA) / (2.0 * sideB * sideC);
            double sinA = Math.Sin(Math.Acos(cosA));
            double cosB = (sideA * sideA + sideC * sideC - sideB * sideB) / (2.0 * sideA * sideC);
            double sinB = Math.Sin(Math.Acos(cosB));

            double tangentRatio = (cosA / sinA) / (cosB / sinB);
            double ratio = Math.Max(0.0, tangentRatio) * 0.5;

            // Mixing happens in CIE Lab(D50). colorjs.io's Color.mix uses this space by default
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

            // Using the step in the mixed scale nearest the seed as the reference, apply its chroma difference to every step
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

            // If ratio becomes 0 and A is a purely achromatic gray scale, the mixed scale's chroma
            // is left with nothing but floating-point rounding residue (on the order of 1e-16). When a
            // chromatic seed then comes in, this division amplifies it by a factor of 1e14, and the
            // result changes based solely on least-significant-bit differences versus the original (browser/Node).
            // The original's formula itself is ill-conditioned here, so no attempt is made to match it exactly — this just notes that such inputs exist
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

        // Light: redistribute across 13 points with white appended as "step 0", then discard the appended point
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

        // Dark: the lighter the background is versus step1, the more the easing is pulled toward linear, to avoid crushing lightness differences too much
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

            // Using the pre-clamp background lightness here, too, matches the original (the ratioL side uses the clamped value)
            TransposeProgressionStart(backgroundColor.L, lightness, easing);
            for (int step = 0; step < STEP_COUNT; step++)
            {
                scale[step].L = lightness[step];
            }
        }

        /// <summary>
        /// Moves the start of the sequence to <paramref name="to"/> and decays that offset toward
        /// the end via a Bezier curve. Mutates the array in place (the original's transposeProgressionStart).
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

        // If the seed is too close to the background (white on white / black on black), fall back to the scale's own step9
        static void GetStep9Colors(Oklch[] scale, Oklch seed, out Oklch step9, out Oklch contrast)
        {
            double distance = TweeqOklch.DeltaEOK(seed, scale[0]) * 100.0;
            Oklch chosen = distance < STEP9_FALLBACK_DISTANCE ? scale[8] : seed;

            step9 = chosen;
            contrast = GetTextColor(chosen);
        }

        // The text color placed on top of step9. If white isn't legible, produce a dark color of the same hue
        static Oklch GetTextColor(Oklch background)
        {
            TweeqOklch.OklchToSrgb(background, out double r, out double g, out double b);
            double contrast = TweeqOklch.ContrastApca(1.0, 1.0, 1.0, r, g, b);

            // NaN (when luminance goes negative outside the gamut) falls on the "white is legible" side. Same as the original
            if (Math.Abs(contrast) < TEXT_CONTRAST_THRESHOLD)
            {
                return new Oklch(0.25, Math.Max(0.08 * background.C, 0.04), background.H);
            }

            return new Oklch(1.0, 0.0, 0.0);
        }

        // Hover state for the solid fill. After shifting lightness, chroma and hue are borrowed
        // from the nearest neighbor in the scale. This borrowing ensures even a pure white/black seed picks up the gray scale's tint
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
        /// Solves target = background * (1 - alpha) + foreground * alpha for alpha (the original's getAlphaColor).
        /// </summary>
        /// <remarks>
        /// The integer-rounding correction exists because browsers round each channel separately
        /// when compositing translucency. Per the original's comment, this is behavior confirmed
        /// by measurement, and it drifts by one step if you simplify the formula.
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

            // "Add white" if even one channel is brighter than the background, otherwise "add black"
            int desired = 0;
            if (tr > br || tg > bg || tb > bb)
            {
                desired = (int)PRECISION;
            }

            double alphaR = (double)(tr - br) / (desired - br);
            double alphaG = (double)(tg - bg) / (desired - bg);
            double alphaB = (double)(tb - bb) / (desired - bb);

            // When both are pure gray, precision matching is unnecessary, and outputting the value as-is gives a cleaner result
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

            // The +/-1 correction can push a value outside the byte range. The original re-clamps this at HEX-serialization time
            return new Rgba32(
                ClampByte(r), ClampByte(g), ClampByte(b), RoundToByte(alpha * PRECISION));
        }

        // Browsers don't round the composited result as a whole; they round the foreground and background separately, then add them
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

        // JS's Math.round always rounds up. This differs from C#'s default ToEven in how it handles 0.5
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
