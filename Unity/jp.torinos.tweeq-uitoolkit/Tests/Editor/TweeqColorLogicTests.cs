using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class TweeqColorLogicTests
    {
        const double TOLERANCE = 1e-12;

        // Hue is on a 0-360 scale, so it's checked with a looser tolerance than the [0,1] channels.
        const double HUE_TOLERANCE = 1e-9;

        #region Helpers

        static void AssertRgba(Rgba actual, double r, double g, double b, double a, string message)
        {
            Assert.That(actual.R, Is.EqualTo(r).Within(TOLERANCE), message + " (r)");
            Assert.That(actual.G, Is.EqualTo(g).Within(TOLERANCE), message + " (g)");
            Assert.That(actual.B, Is.EqualTo(b).Within(TOLERANCE), message + " (b)");
            Assert.That(actual.A, Is.EqualTo(a).Within(TOLERANCE), message + " (a)");
        }

        static void AssertHsva(Hsva actual, double h, double s, double v, double a, string message)
        {
            Assert.That(actual.H, Is.EqualTo(h).Within(HUE_TOLERANCE), message + " (h)");
            Assert.That(actual.S, Is.EqualTo(s).Within(TOLERANCE), message + " (s)");
            Assert.That(actual.V, Is.EqualTo(v).Within(TOLERANCE), message + " (v)");
            Assert.That(actual.A, Is.EqualTo(a).Within(TOLERANCE), message + " (a)");
        }

        // The boundaries between the 6 sectors. The branch switches here, so every discontinuity in the conversion occurs at these angles.
        static readonly double[] SectorBoundaries = { 0.0, 60.0, 120.0, 180.0, 240.0, 300.0 };

        static readonly double[][] BoundaryRgb =
        {
            new[] { 1.0, 0.0, 0.0 },
            new[] { 1.0, 1.0, 0.0 },
            new[] { 0.0, 1.0, 0.0 },
            new[] { 0.0, 1.0, 1.0 },
            new[] { 0.0, 0.0, 1.0 },
            new[] { 1.0, 0.0, 1.0 },
        };

        #endregion

        #region Sector boundaries

        [Test]
        public void HsvaToRgbaMatchesPrimariesAtSectorBoundaries()
        {
            for (int i = 0; i < SectorBoundaries.Length; i++)
            {
                Rgba rgba = TweeqColorLogic.HsvaToRgba(new Hsva(SectorBoundaries[i], 1.0, 1.0, 1.0));
                AssertRgba(
                    rgba, BoundaryRgb[i][0], BoundaryRgb[i][1], BoundaryRgb[i][2], 1.0,
                    $"hue={SectorBoundaries[i]}");
            }
        }

        [Test]
        public void RgbaToHsvaMatchesHueAtSectorBoundaries()
        {
            for (int i = 0; i < SectorBoundaries.Length; i++)
            {
                Rgba rgba = new Rgba(BoundaryRgb[i][0], BoundaryRgb[i][1], BoundaryRgb[i][2], 1.0);
                AssertHsva(
                    TweeqColorLogic.RgbaToHsva(rgba), SectorBoundaries[i], 1.0, 1.0, 1.0,
                    $"hue={SectorBoundaries[i]}");
            }
        }

        // Even as the branch changes right before/after a boundary, the color must not jump.
        [Test]
        public void HueIsContinuousAcrossSectorBoundaries()
        {
            foreach (double boundary in SectorBoundaries)
            {
                Rgba before = TweeqColorLogic.HsvaToRgba(new Hsva(boundary - 1e-6, 1.0, 1.0, 1.0));
                Rgba after = TweeqColorLogic.HsvaToRgba(new Hsva(boundary + 1e-6, 1.0, 1.0, 1.0));

                Assert.That(after.R - before.R, Is.EqualTo(0.0).Within(1e-6), $"hue={boundary} (r)");
                Assert.That(after.G - before.G, Is.EqualTo(0.0).Within(1e-6), $"hue={boundary} (g)");
                Assert.That(after.B - before.B, Is.EqualTo(0.0).Within(1e-6), $"hue={boundary} (b)");
            }
        }

        #endregion

        #region Round trip

        [Test]
        public void HsvaRoundTripsAcrossEveryHueQuadrant()
        {
            double[] saturations = { 0.05, 0.25, 0.5, 1.0 };
            double[] values = { 0.05, 0.3, 0.75, 1.0 };
            double[] alphas = { 0.0, 0.5, 1.0 };

            for (double hue = 0.0; hue < 360.0; hue += 5.0)
            {
                foreach (double saturation in saturations)
                {
                    foreach (double value in values)
                    {
                        foreach (double alpha in alphas)
                        {
                            Hsva source = new Hsva(hue, saturation, value, alpha);
                            Hsva roundTrip = TweeqColorLogic.RgbaToHsva(
                                TweeqColorLogic.HsvaToRgba(source));

                            AssertHsva(
                                roundTrip, hue, saturation, value, alpha,
                                $"hsva=({hue}, {saturation}, {value}, {alpha})");
                        }
                    }
                }
            }
        }

        [Test]
        public void RgbaRoundTripsForSampleColors()
        {
            double[][] samples =
            {
                new[] { 0.16, 0.42, 1.0, 0.75 },
                new[] { 1.0, 0.0, 0.5, 1.0 },
                new[] { 0.0, 0.0, 0.0, 1.0 },
                new[] { 1.0, 1.0, 1.0, 1.0 },
                new[] { 0.5, 0.5, 0.5, 0.25 },
                new[] { 0.2, 0.4, 0.6, 0.0 },
                new[] { 0.9, 0.9, 0.1, 1.0 },
            };

            foreach (double[] sample in samples)
            {
                Rgba source = new Rgba(sample[0], sample[1], sample[2], sample[3]);
                Rgba roundTrip = TweeqColorLogic.HsvaToRgba(TweeqColorLogic.RgbaToHsva(source));

                AssertRgba(
                    roundTrip, sample[0], sample[1], sample[2], sample[3],
                    $"rgba=({sample[0]}, {sample[1]}, {sample[2]}, {sample[3]})");
            }
        }

        #endregion

        #region Achromatic hue preservation

        [Test]
        public void ZeroSaturationKeepsPreviousHue()
        {
            Hsva previous = new Hsva(210.0, 0.8, 0.5, 1.0);
            Rgba gray = TweeqColorLogic.HsvaToRgba(new Hsva(210.0, 0.0, 0.5, 1.0));

            Hsva result = TweeqColorLogic.RgbaToHsva(gray, previous);

            Assert.That(result.H, Is.EqualTo(210.0).Within(HUE_TOLERANCE));
            Assert.That(result.S, Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(result.V, Is.EqualTo(0.5).Within(TOLERANCE));
        }

        [Test]
        public void ZeroValueKeepsPreviousHueAndSaturation()
        {
            Hsva previous = new Hsva(135.0, 0.6, 0.4, 1.0);
            Rgba black = TweeqColorLogic.HsvaToRgba(new Hsva(135.0, 0.6, 0.0, 1.0));

            Hsva result = TweeqColorLogic.RgbaToHsva(black, previous);

            Assert.That(result.H, Is.EqualTo(135.0).Within(HUE_TOLERANCE));
            Assert.That(result.S, Is.EqualTo(0.6).Within(TOLERANCE));
            Assert.That(result.V, Is.EqualTo(0.0).Within(TOLERANCE));
        }

        // Hue must not be lost on a round trip that drags the SV pad all the way to the bottom/left edge and then returns.
        [Test]
        public void HueSurvivesRoundTripThroughBlackAndGray()
        {
            Hsva source = new Hsva(285.0, 0.0, 0.0, 1.0);
            Hsva roundTrip = TweeqColorLogic.RgbaToHsva(TweeqColorLogic.HsvaToRgba(source), source);

            Assert.That(roundTrip.H, Is.EqualTo(285.0).Within(HUE_TOLERANCE));
        }

        [Test]
        public void AchromaticWithoutPreviousFallsBackToZeroHue()
        {
            Hsva white = TweeqColorLogic.RgbaToHsva(new Rgba(1.0, 1.0, 1.0, 1.0));
            Hsva black = TweeqColorLogic.RgbaToHsva(new Rgba(0.0, 0.0, 0.0, 1.0));

            AssertHsva(white, 0.0, 0.0, 1.0, 1.0, "white");
            AssertHsva(black, 0.0, 0.0, 0.0, 1.0, "black");
        }

        #endregion

        #region Normalization

        [Test]
        public void HueWrapsIntoSingleTurn()
        {
            Assert.That(TweeqColorLogic.NormalizeHue(0.0), Is.EqualTo(0.0).Within(HUE_TOLERANCE));
            Assert.That(TweeqColorLogic.NormalizeHue(360.0), Is.EqualTo(0.0).Within(HUE_TOLERANCE));
            Assert.That(TweeqColorLogic.NormalizeHue(-30.0), Is.EqualTo(330.0).Within(HUE_TOLERANCE));
            Assert.That(TweeqColorLogic.NormalizeHue(750.0), Is.EqualTo(30.0).Within(HUE_TOLERANCE));
            Assert.That(TweeqColorLogic.NormalizeHue(double.NaN), Is.EqualTo(0.0));
            Assert.That(TweeqColorLogic.NormalizeHue(double.PositiveInfinity), Is.EqualTo(0.0));
        }

        [Test]
        public void HsvaToRgbaWrapsHueAndClampsChannels()
        {
            AssertRgba(
                TweeqColorLogic.HsvaToRgba(new Hsva(-300.0, 1.0, 1.0, 1.0)),
                1.0, 1.0, 0.0, 1.0, "hue=-300 is equivalent to 60");

            AssertRgba(
                TweeqColorLogic.HsvaToRgba(new Hsva(0.0, 2.0, 5.0, 3.0)),
                1.0, 0.0, 0.0, 1.0, "out-of-range values saturate");

            AssertRgba(
                TweeqColorLogic.HsvaToRgba(new Hsva(0.0, -1.0, 0.5, -2.0)),
                0.5, 0.5, 0.5, 0.0, "negative values saturate to 0");
        }

        [Test]
        public void ChannelBytesRoundAwayFromZero()
        {
            Assert.That(TweeqColorLogic.ToByte(0.5), Is.EqualTo(128));
            Assert.That(TweeqColorLogic.ToByte(0.5 / 255.0), Is.EqualTo(1));
            Assert.That(TweeqColorLogic.ToByte(0.0), Is.EqualTo(0));
            Assert.That(TweeqColorLogic.ToByte(1.0), Is.EqualTo(255));
            Assert.That(TweeqColorLogic.ToByte(-1.0), Is.EqualTo(0));
            Assert.That(TweeqColorLogic.ToByte(2.0), Is.EqualTo(255));
            Assert.That(TweeqColorLogic.ToByte(double.NaN), Is.EqualTo(0));

            Assert.That(TweeqColorLogic.FromByte(0), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(TweeqColorLogic.FromByte(255), Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(TweeqColorLogic.FromByte(128), Is.EqualTo(128.0 / 255.0).Within(TOLERANCE));
            Assert.That(TweeqColorLogic.FromByte(-5), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(TweeqColorLogic.FromByte(300), Is.EqualTo(1.0).Within(TOLERANCE));
        }

        #endregion

        #region Hex parsing

        [Test]
        public void TryParseHexReadsSixDigits()
        {
            Assert.That(TweeqColorLogic.TryParseHex("#ff0080", out Rgba rgba), Is.True);
            AssertRgba(rgba, 1.0, 0.0, 128.0 / 255.0, 1.0, "#ff0080");
        }

        [Test]
        public void TryParseHexReadsEightDigitsAsAlpha()
        {
            Assert.That(TweeqColorLogic.TryParseHex("#33669980", out Rgba rgba), Is.True);
            AssertRgba(
                rgba, 51.0 / 255.0, 102.0 / 255.0, 153.0 / 255.0, 128.0 / 255.0, "#33669980");
        }

        // #RGB duplicates each 1-digit value into 2 digits (0x8 -> 0x88).
        [Test]
        public void TryParseHexExpandsThreeDigits()
        {
            Assert.That(TweeqColorLogic.TryParseHex("#f80", out Rgba rgba), Is.True);
            AssertRgba(rgba, 1.0, 136.0 / 255.0, 0.0, 1.0, "#f80");
        }

        [Test]
        public void TryParseHexAcceptsUpperCaseWhitespaceAndMissingHash()
        {
            Assert.That(TweeqColorLogic.TryParseHex("  #FF0080  ", out Rgba padded), Is.True);
            AssertRgba(padded, 1.0, 0.0, 128.0 / 255.0, 1.0, "padded");

            Assert.That(TweeqColorLogic.TryParseHex("FF0080", out Rgba bare), Is.True);
            AssertRgba(bare, 1.0, 0.0, 128.0 / 255.0, 1.0, "bare");
        }

        [Test]
        public void TryParseHexRejectsInvalidInput()
        {
            string[] invalid =
            {
                null,
                "",
                "   ",
                "#",
                "#GGG",
                "#xyz",
                "#12",
                "#1234",     // A 4-digit #RGBA shorthand is not accepted (the spec allows only 3/6/8 digits).
                "#12345",
                "#1234567",
                "#123456789",
                "#12345g",
                "#1234567z",
                "rgb(1,2,3)",
            };

            foreach (string text in invalid)
            {
                Assert.That(
                    TweeqColorLogic.TryParseHex(text, out Rgba rgba), Is.False, $"text={text ?? "null"}");

                // Contract: on failure, it's filled with opaque black.
                AssertRgba(rgba, 0.0, 0.0, 0.0, 1.0, $"text={text ?? "null"}");
            }
        }

        #endregion

        #region Hex formatting

        [Test]
        public void FormatHexUsesSixDigitsWhenOpaque()
        {
            Assert.That(TweeqColorLogic.FormatHex(new Rgba(1.0, 0.0, 0.5, 1.0)), Is.EqualTo("#ff0080"));
            Assert.That(TweeqColorLogic.FormatHex(new Rgba(0.0, 0.0, 0.0, 1.0)), Is.EqualTo("#000000"));
            Assert.That(TweeqColorLogic.FormatHex(new Rgba(1.0, 1.0, 1.0, 1.0)), Is.EqualTo("#ffffff"));
        }

        [Test]
        public void FormatHexUsesEightDigitsWhenTranslucent()
        {
            Assert.That(
                TweeqColorLogic.FormatHex(new Rgba(0.2, 0.4, 0.6, 128.0 / 255.0)),
                Is.EqualTo("#33669980"));
            Assert.That(
                TweeqColorLogic.FormatHex(new Rgba(0.0, 0.0, 0.0, 0.0)), Is.EqualTo("#00000000"));
        }

        // The digit-count decision is based on "less than 255 after quantization." An alpha that rounds to opaque stays at 6 digits.
        [Test]
        public void FormatHexKeepsSixDigitsWhenAlphaRoundsToOpaque()
        {
            Assert.That(
                TweeqColorLogic.FormatHex(new Rgba(1.0, 0.0, 0.5, 254.8 / 255.0)),
                Is.EqualTo("#ff0080"));
            Assert.That(
                TweeqColorLogic.FormatHex(new Rgba(1.0, 0.0, 0.5, 254.4 / 255.0)),
                Is.EqualTo("#ff0080fe"));
        }

        [Test]
        public void FormatHexIsLowerCase()
        {
            string text = TweeqColorLogic.FormatHex(new Rgba(0.67, 0.8, 0.93, 0.72));

            Assert.That(text, Is.EqualTo(text.ToLowerInvariant()));
            Assert.That(text.Length, Is.EqualTo(9));
        }

        [Test]
        public void FormatHexIsIdempotentThroughParse()
        {
            string[] samples = { "#ff0080", "#33669980", "#000000", "#ffffff", "#00000000" };

            foreach (string sample in samples)
            {
                Assert.That(TweeqColorLogic.TryParseHex(sample, out Rgba rgba), Is.True, sample);
                Assert.That(TweeqColorLogic.FormatHex(rgba), Is.EqualTo(sample), sample);
            }
        }

        [Test]
        public void FormatHexClampsOutOfRangeChannels()
        {
            Assert.That(
                TweeqColorLogic.FormatHex(new Rgba(-1.0, 2.0, double.NaN, 1.0)),
                Is.EqualTo("#00ff00"));
        }

        #endregion
    }
}
