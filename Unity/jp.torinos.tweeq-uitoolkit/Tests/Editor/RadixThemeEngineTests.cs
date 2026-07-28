using NUnit.Framework;

namespace Tweeq.Core.Tests
{
    public class RadixThemeEngineTests
    {
        #region Helpers

        // The Vue original's store's default inputs (stores/theme.ts)
        const string DEFAULT_ACCENT = "#0000ff";
        const string DEFAULT_GRAY = "#8B8D98";
        const string LIGHT_BACKGROUND = "#ffffff";
        const string DARK_BACKGROUND = "#111111";

        static Rgba Parse(string hex)
        {
            Assert.That(TweeqColorLogic.TryParseHex(hex, out Rgba rgba), Is.True, hex);
            return rgba;
        }

        static string Hex(Rgba32 color)
        {
            return TweeqColorLogic.FormatHex(color.ToRgba());
        }

        static RadixThemeColors Generate(
            RadixAppearance appearance, string background, string accent, string gray)
        {
            return RadixThemeEngine.GenerateThemeColors(
                appearance, Parse(background), Parse(accent), Parse(gray));
        }

        #endregion

        #region Snapshot

        // Expected values are taken from a reference port's Vitest snapshot
        // (packages/core/src/theme/__snapshots__/computeTheme.test.ts.snap).
        [Test]
        public void GenerateThemeColors_MatchesLightSnapshot()
        {
            RadixThemeColors radix = Generate(
                RadixAppearance.Light, LIGHT_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);

            Assert.That(Hex(radix.Background), Is.EqualTo("#ffffff"), "background");

            Assert.That(Hex(radix.AccentScale[8]), Is.EqualTo("#0000ff"), "colorAccent");
            Assert.That(Hex(radix.AccentScale[10]), Is.EqualTo("#0744ff"), "colorAccentHover");
            Assert.That(Hex(radix.AccentScale[4]), Is.EqualTo("#c9dfff"), "colorAccentSoft");
            Assert.That(Hex(radix.AccentScale[5]), Is.EqualTo("#b5d2ff"), "colorAccentSoftHover");
            Assert.That(Hex(radix.AccentContrast), Is.EqualTo("#ffffff"), "colorOnAccent");

            Assert.That(Hex(radix.GrayScale[11]), Is.EqualTo("#1e1f24"), "colorText");
            Assert.That(Hex(radix.GrayScale[10]), Is.EqualTo("#62636c"), "colorTextMute");
            Assert.That(Hex(radix.GrayScale[9]), Is.EqualTo("#80828d"), "colorTextSubtle");
            Assert.That(Hex(radix.GrayScale[0]), Is.EqualTo("#fcfcfd"), "colorSurface base");
            Assert.That(Hex(radix.GrayScale[2]), Is.EqualTo("#eff0f3"), "colorInput");
            Assert.That(Hex(radix.GrayScale[3]), Is.EqualTo("#e7e8ec"), "colorInputHover");
            Assert.That(Hex(radix.GrayScale[4]), Is.EqualTo("#e0e1e6"), "colorNeutral");
            Assert.That(Hex(radix.GrayScale[5]), Is.EqualTo("#d8d9e0"), "colorNeutralHover");

            Assert.That(Hex(radix.GrayScaleAlpha[3]), Is.EqualTo("#000b3618"), "colorBorder");
            Assert.That(Hex(radix.GrayScaleAlpha[2]), Is.EqualTo("#00104010"), "colorBorderSubtle");
        }

        [Test]
        public void GenerateThemeColors_MatchesDarkSnapshot()
        {
            RadixThemeColors radix = Generate(
                RadixAppearance.Dark, DARK_BACKGROUND, "#46a758", DEFAULT_GRAY);

            Assert.That(Hex(radix.Background), Is.EqualTo("#111111"), "background");

            Assert.That(Hex(radix.AccentScale[8]), Is.EqualTo("#46a758"), "colorAccent");
            Assert.That(Hex(radix.AccentScale[10]), Is.EqualTo("#73d081"), "colorAccentHover");
            Assert.That(Hex(radix.AccentScale[4]), Is.EqualTo("#27482b"), "colorAccentSoft");
            Assert.That(Hex(radix.AccentScale[5]), Is.EqualTo("#2f5735"), "colorAccentSoftHover");
            Assert.That(Hex(radix.AccentContrast), Is.EqualTo("#ffffff"), "colorOnAccent");

            Assert.That(Hex(radix.GrayScale[11]), Is.EqualTo("#eeeef0"), "colorText");
            Assert.That(Hex(radix.GrayScale[10]), Is.EqualTo("#b2b3bd"), "colorTextMute");
            Assert.That(Hex(radix.GrayScale[9]), Is.EqualTo("#797b86"), "colorTextSubtle");
            Assert.That(Hex(radix.GrayScale[0]), Is.EqualTo("#111113"), "colorSurface base");
            Assert.That(Hex(radix.GrayScale[2]), Is.EqualTo("#222325"), "colorInput");
            Assert.That(Hex(radix.GrayScale[3]), Is.EqualTo("#292a2e"), "colorInputHover");
            Assert.That(Hex(radix.GrayScale[4]), Is.EqualTo("#303136"), "colorNeutral");
            Assert.That(Hex(radix.GrayScale[5]), Is.EqualTo("#393a40"), "colorNeutralHover");

            Assert.That(Hex(radix.GrayScaleAlpha[3]), Is.EqualTo("#d1d9f920"), "colorBorder");
            Assert.That(Hex(radix.GrayScaleAlpha[2]), Is.EqualTo("#d6e2f916"), "colorBorderSubtle");
        }

        #endregion

        #region Properties

        [Test]
        public void GenerateThemeColors_IsDeterministic()
        {
            RadixThemeColors first = Generate(
                RadixAppearance.Dark, DARK_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);
            RadixThemeColors second = Generate(
                RadixAppearance.Dark, DARK_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);

            for (int i = 0; i < RadixPaletteData.STEP_COUNT; i++)
            {
                Assert.That(Hex(second.AccentScale[i]), Is.EqualTo(Hex(first.AccentScale[i])), "accent " + i);
                Assert.That(Hex(second.GrayScale[i]), Is.EqualTo(Hex(first.GrayScale[i])), "gray " + i);
            }
        }

        [Test]
        public void GenerateThemeColors_KeepsAccentSeedAsStep9()
        {
            // A seed sufficiently far from the background is adopted as-is for step9 (this is Radix's design itself)
            string[] accents = { "#0000ff", "#46a758", "#e5484d", "#3e63dd" };

            foreach (string accent in accents)
            {
                RadixThemeColors light = Generate(
                    RadixAppearance.Light, LIGHT_BACKGROUND, accent, DEFAULT_GRAY);
                RadixThemeColors dark = Generate(
                    RadixAppearance.Dark, DARK_BACKGROUND, accent, DEFAULT_GRAY);

                Assert.That(Hex(light.AccentScale[8]), Is.EqualTo(accent.ToLowerInvariant()), "light " + accent);
                Assert.That(Hex(dark.AccentScale[8]), Is.EqualTo(accent.ToLowerInvariant()), "dark " + accent);
            }
        }

        [Test]
        public void GenerateThemeColors_GrayScaleStartsAtTheBackgroundSide()
        {
            RadixThemeColors light = Generate(
                RadixAppearance.Light, LIGHT_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);
            RadixThemeColors dark = Generate(
                RadixAppearance.Dark, DARK_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);

            // step1 is nearly the same lightness as the background, step12 is pinned to the opposite extreme
            Assert.That(Luma(light.GrayScale[0]), Is.GreaterThan(Luma(light.GrayScale[11])));
            Assert.That(Luma(dark.GrayScale[0]), Is.LessThan(Luma(dark.GrayScale[11])));

            Assert.That(Luma(light.GrayScale[0]), Is.GreaterThan(240));
            Assert.That(Luma(dark.GrayScale[0]), Is.LessThan(30));
        }

        [Test]
        public void GenerateThemeColors_GrayScaleLightnessIsMonotonic()
        {
            // Steps 1-8 (index 0-7), used as surface colors, move away from the background in one direction.
            // From step 9 onward the role switches to text color, so monotonicity is not guaranteed
            RadixThemeColors light = Generate(
                RadixAppearance.Light, LIGHT_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);
            RadixThemeColors dark = Generate(
                RadixAppearance.Dark, DARK_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);

            for (int i = 1; i < 8; i++)
            {
                Assert.That(Luma(light.GrayScale[i]), Is.LessThan(Luma(light.GrayScale[i - 1])),
                    "light step " + i);
                Assert.That(Luma(dark.GrayScale[i]), Is.GreaterThan(Luma(dark.GrayScale[i - 1])),
                    "dark step " + i);
            }
        }

        [Test]
        public void GenerateThemeColors_PureWhiteAccentBorrowsGrayTint()
        {
            // Pure white/black has no hue, so the scale borrows the gray side's tint wholesale
            foreach (string accent in new[] { "#ffffff", "#000000" })
            {
                RadixThemeColors radix = Generate(
                    RadixAppearance.Dark, DARK_BACKGROUND, accent, DEFAULT_GRAY);

                for (int i = 0; i < 8; i++)
                {
                    Assert.That(Hex(radix.AccentScale[i]), Is.EqualTo(Hex(radix.GrayScale[i])),
                        accent + " step " + i);
                }
            }
        }

        [Test]
        public void GenerateThemeColors_AchromaticAccentKeepsAchromaticSurfaces()
        {
            // Conversely, even a hue-less seed doesn't stay "achromatic" — it picks up gray's tint,
            // meaning it becomes a surface color that blends with the background (confirms the borrowed result is in the same family as the background color)
            RadixThemeColors radix = Generate(
                RadixAppearance.Light, LIGHT_BACKGROUND, "#ffffff", DEFAULT_GRAY);

            Assert.That(Hex(radix.AccentScale[0]), Is.EqualTo(Hex(radix.GrayScale[0])));
            Assert.That(Luma(radix.AccentScale[0]), Is.GreaterThan(240));
        }

        [Test]
        public void GenerateThemeColors_AlphaScaleCompositesBackToTheOpaqueScale()
        {
            RadixThemeColors radix = Generate(
                RadixAppearance.Dark, DARK_BACKGROUND, DEFAULT_ACCENT, DEFAULT_GRAY);

            for (int i = 0; i < RadixPaletteData.STEP_COUNT; i++)
            {
                Rgba32 composited = Composite(radix.GrayScaleAlpha[i], radix.Background);
                Assert.That(Hex(composited), Is.EqualTo(Hex(radix.GrayScale[i])), "gray " + i);
            }
        }

        [Test]
        public void GenerateScale_ProducesAFullTwelveStepScale()
        {
            RadixScale scale = RadixThemeEngine.GenerateScale(
                RadixAppearance.Dark, Parse(DARK_BACKGROUND), Parse("#e5484d"));

            Assert.That(scale.Scale.Length, Is.EqualTo(RadixPaletteData.STEP_COUNT));
            Assert.That(scale.ScaleAlpha.Length, Is.EqualTo(RadixPaletteData.STEP_COUNT));
            Assert.That(Hex(scale.Scale[8]), Is.EqualTo("#e5484d"));
        }

        [Test]
        public void ToAlphaOverBackground_HonorsAFixedAlpha()
        {
            // Radix's accentSurface pins opacity at light 0.8 / dark 0.5
            Rgba32 target = new Rgba32(0xC9, 0xDF, 0xFF, 255);
            Rgba32 background = new Rgba32(0xFF, 0xFF, 0xFF, 255);

            Rgba32 surface = RadixThemeEngine.ToAlphaOverBackground(target, background, 0.8);

            Assert.That(surface.A, Is.EqualTo(204));
            Assert.That(Hex(Composite(surface, background)), Is.EqualTo(Hex(target)));
        }

        #endregion

        #region Semantic colors

        [Test]
        public void SemanticColors_MatchLightSnapshot()
        {
            SemanticColors colors = TweeqSemanticColors.Build(
                Parse(LIGHT_BACKGROUND), Parse(DEFAULT_ACCENT));

            Assert.That(Hex(colors.Error), Is.EqualTo("#a30053"), "colorError");
            Assert.That(Hex(colors.ErrorSoft), Is.EqualTo("#f6dde3"), "colorErrorSoft");
            Assert.That(Hex(colors.Warning), Is.EqualTo("#5d5900"), "colorWarning");
            Assert.That(Hex(colors.WarningSoft), Is.EqualTo("#e5e5da"), "colorWarningSoft");
            Assert.That(Hex(colors.Success), Is.EqualTo("#00694e"), "colorSuccess");
            Assert.That(Hex(colors.SuccessSoft), Is.EqualTo("#dce8e2"), "colorSuccessSoft");
            Assert.That(Hex(colors.Info), Is.EqualTo("#1900ff"), "colorInfo");
            Assert.That(Hex(colors.InfoSoft), Is.EqualTo("#d6e4ff"), "colorInfoSoft");
        }

        [Test]
        public void SemanticColors_MatchDarkSnapshot()
        {
            SemanticColors colors = TweeqSemanticColors.Build(
                Parse(DARK_BACKGROUND), Parse("#46a758"));

            Assert.That(Hex(colors.Error), Is.EqualTo("#d66f36"), "colorError");
            Assert.That(Hex(colors.ErrorSoft), Is.EqualTo("#2a1e18"), "colorErrorSoft");
            Assert.That(Hex(colors.Warning), Is.EqualTo("#a09200"), "colorWarning");
            Assert.That(Hex(colors.WarningSoft), Is.EqualTo("#232216"), "colorWarningSoft");
            Assert.That(Hex(colors.Success), Is.EqualTo("#46a758"), "colorSuccess");
            Assert.That(Hex(colors.SuccessSoft), Is.EqualTo("#1a241b"), "colorSuccessSoft");
            Assert.That(Hex(colors.Info), Is.EqualTo("#2297e1"), "colorInfo");
            Assert.That(Hex(colors.InfoSoft), Is.EqualTo("#18232b"), "colorInfoSoft");
        }

        [Test]
        public void SemanticColors_RecMatchesError()
        {
            SemanticColors colors = TweeqSemanticColors.Build(
                Parse(DARK_BACKGROUND), Parse(DEFAULT_ACCENT));

            Assert.That(Hex(colors.Rec), Is.EqualTo(Hex(colors.Error)));
        }

        [Test]
        public void NudgedHue_StaysWithinTheCanonicalBand()
        {
            // Even if the accent sits on the opposite side of the hue wheel, it never moves more than 24 degrees from the seed
            for (int accentHue = 0; accentHue < 360; accentHue += 5)
            {
                double nudged = TweeqSemanticColors.NudgedHue(30.0, accentHue);
                double delta = ((nudged - 30.0 + 540.0) % 360.0) - 180.0;

                Assert.That(System.Math.Abs(delta), Is.LessThanOrEqualTo(24.0 + 1e-9),
                    "accent hue " + accentHue);
            }
        }

        [Test]
        public void NudgedHue_KeepsSeedWhenEitherSideIsAchromatic()
        {
            Assert.That(TweeqSemanticColors.NudgedHue(30.0, double.NaN), Is.EqualTo(30.0));
            Assert.That(double.IsNaN(TweeqSemanticColors.NudgedHue(double.NaN, 200.0)), Is.True);
        }

        [Test]
        public void SoftTint_StaysCloseToTheBackground()
        {
            // A subtle surface color moves only 15% away from the background, so it should lean toward the background's own lightness/darkness
            Rgba32 error = TweeqSemanticColors.RepresentativeColor(
                TweeqSemanticColors.SeedRed, Parse(DEFAULT_ACCENT));

            Rgba32 onLight = TweeqSemanticColors.SoftTint(Parse(LIGHT_BACKGROUND), error);
            Rgba32 onDark = TweeqSemanticColors.SoftTint(Parse(DARK_BACKGROUND), error);

            Assert.That(Luma(onLight), Is.GreaterThan(200));
            Assert.That(Luma(onDark), Is.LessThan(60));
        }

        #endregion

        #region Local helpers

        static int Luma(Rgba32 color)
        {
            return (int)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
        }

        // The same "round each term then add" compositing as browsers. Used to verify the alpha-scale results
        static Rgba32 Composite(Rgba32 foreground, Rgba32 background)
        {
            double alpha = foreground.A / 255.0;
            return new Rgba32(
                Blend(foreground.R, alpha, background.R),
                Blend(foreground.G, alpha, background.G),
                Blend(foreground.B, alpha, background.B),
                255);
        }

        static int Blend(int foreground, double alpha, int background)
        {
            return (int)System.Math.Floor(background * (1.0 - alpha) + 0.5)
                + (int)System.Math.Floor(foreground * alpha + 0.5);
        }

        #endregion
    }
}
