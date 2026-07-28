using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>
    /// Colors representing state. Corresponds to buildSemanticColors in ref/tweeq/src/theme/palette.ts.
    /// </summary>
    public struct SemanticColors
    {
        /// <summary>Error (invalid input value / failure).</summary>
        public Rgba32 Error;

        /// <summary>Error's soft fill color.</summary>
        public Rgba32 ErrorSoft;

        /// <summary>Warning.</summary>
        public Rgba32 Warning;

        /// <summary>Warning's soft fill color.</summary>
        public Rgba32 WarningSoft;

        /// <summary>Success.</summary>
        public Rgba32 Success;

        /// <summary>Success's soft fill color.</summary>
        public Rgba32 SuccessSoft;

        /// <summary>Info.</summary>
        public Rgba32 Info;

        /// <summary>Info's soft fill color.</summary>
        public Rgba32 InfoSoft;

        /// <summary>Recording indicator. Equal to <see cref="Error"/>, per the original's specification.</summary>
        public Rgba32 Rec;
    }

    #endregion

    /// <summary>
    /// Builds semantic colors nudged toward the accent from a curated, base16-like hue palette.
    /// Ported from ref/tweeq/src/theme/palette.ts.
    /// </summary>
    /// <remarks>
    /// The "representative color" doesn't go through the Radix scale — it reuses the accent's lightness
    /// and chroma as-is and only swaps the hue. The goal is for the whole UI to share the same vividness,
    /// and a cap is placed on the hue nudge to keep red from stopping looking like red.
    /// </remarks>
    public static class TweeqSemanticColors
    {
        #region Constants

        /// <summary>Red seed hue (Radix red step9 #e5484d).</summary>
        public static readonly Rgba SeedRed = FromHex(0xE5, 0x48, 0x4D);

        /// <summary>Yellow seed hue (Radix amber step9 #ffc53d).</summary>
        public static readonly Rgba SeedYellow = FromHex(0xFF, 0xC5, 0x3D);

        /// <summary>Green seed hue (Radix grass step9 #46a758).</summary>
        public static readonly Rgba SeedGreen = FromHex(0x46, 0xA7, 0x58);

        /// <summary>Blue seed hue (Radix blue step9 #3e63dd).</summary>
        public static readonly Rgba SeedBlue = FromHex(0x3E, 0x63, 0xDD);

        // The ratio to nudge toward the accent hue, and the cap on how far it may move from the seed.
        // Precisely because of this cap, red still looks like red even when the accent sits on the opposite side of the hue wheel
        const double NUDGE_T = 0.3;
        const double NUDGE_MAX_DEG = 24.0;

        // The mix ratio from background toward the representative color, used when building the soft fill color
        const double SOFT_TINT_T = 0.15;

        #endregion

        #region Public

        /// <summary>Builds the full set of semantic colors from the background color and the accent color.</summary>
        public static SemanticColors Build(Rgba background, Rgba accent)
        {
            Rgba32 red = RepresentativeColor(SeedRed, accent);
            Rgba32 yellow = RepresentativeColor(SeedYellow, accent);
            Rgba32 green = RepresentativeColor(SeedGreen, accent);
            Rgba32 blue = RepresentativeColor(SeedBlue, accent);

            return new SemanticColors
            {
                Error = red,
                ErrorSoft = SoftTint(background, red),
                Warning = yellow,
                WarningSoft = SoftTint(background, yellow),
                Success = green,
                SuccessSoft = SoftTint(background, green),
                Info = blue,
                InfoSoft = SoftTint(background, blue),
                Rec = red,
            };
        }

        /// <summary>
        /// The representative color for a seed hue. Uses the accent's lightness and chroma as-is,
        /// and nudges only the hue from the seed toward the accent, by at most <see cref="NUDGE_MAX_DEG"/> degrees.
        /// </summary>
        public static Rgba32 RepresentativeColor(Rgba seed, Rgba accent)
        {
            Oklch seedColor = TweeqOklch.SrgbToOklch(seed.R, seed.G, seed.B);
            Oklch accentColor = TweeqOklch.SrgbToOklch(accent.R, accent.G, accent.B);

            double hue = NudgedHue(seedColor.H, accentColor.H);
            if (double.IsNaN(hue))
            {
                hue = seedColor.H;
            }

            return TweeqOklch.OklchToBytes(new Oklch(accentColor.L, accentColor.C, hue));
        }

        /// <summary>The soft fill color, the representative color pulled toward the background. Mixed 15% in OKLCH.</summary>
        public static Rgba32 SoftTint(Rgba background, Rgba32 color)
        {
            Oklch backgroundColor = TweeqOklch.SrgbToOklch(background.R, background.G, background.B);
            Oklch target = TweeqOklch.SrgbToOklch(color.R / 255.0, color.G / 255.0, color.B / 255.0);

            // Hue is interpolated along the shorter arc (hue: 'shorter' in the original's colorjs.io)
            double h1 = backgroundColor.H;
            double h2 = target.H;
            double delta = h2 - h1;
            if (delta > 180.0)
            {
                h1 += 360.0;
            }
            else if (delta < -180.0)
            {
                h2 += 360.0;
            }

            return TweeqOklch.OklchToBytes(new Oklch(
                Interpolate(backgroundColor.L, target.L),
                Interpolate(backgroundColor.C, target.C),
                Interpolate(h1, h2)));
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Nudges the seed hue toward the accent hue along the shorter arc. Returns the seed unchanged
        /// if either one is achromatic.
        /// </summary>
        public static double NudgedHue(double seedHue, double accentHue)
        {
            if (double.IsNaN(seedHue) || double.IsNaN(accentHue))
            {
                return seedHue;
            }

            double delta = ((accentHue - seedHue + 540.0) % 360.0) - 180.0;
            double shifted = Math.Max(
                seedHue - NUDGE_MAX_DEG,
                Math.Min(seedHue + NUDGE_MAX_DEG, seedHue + delta * NUDGE_T));

            return (shifted % 360.0 + 360.0) % 360.0;
        }

        // If one side is undefined (NaN), take the other side's value as-is (the original's interpolation rule)
        static double Interpolate(double from, double to)
        {
            if (double.IsNaN(from))
            {
                return to;
            }

            if (double.IsNaN(to))
            {
                return from;
            }

            return from + (to - from) * SOFT_TINT_T;
        }

        static Rgba FromHex(int r, int g, int b)
        {
            return new Rgba(r / 255.0, g / 255.0, b / 255.0, 1.0);
        }

        #endregion
    }
}
