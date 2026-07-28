using System;
using NUnit.Framework;

namespace Tweeq.Core.Tests
{
    public class TweeqOklchTests
    {
        #region Helpers

        const double TOLERANCE = 1e-12;

        // Hue is on a 0-360 scale, so it's checked with a looser tolerance than the [0,1] channels.
        const double HUE_TOLERANCE = 1e-9;

        static Oklch FromHex(string hex)
        {
            Assert.That(TweeqColorLogic.TryParseHex(hex, out Rgba rgba), Is.True, hex);
            return TweeqOklch.SrgbToOklch(rgba.R, rgba.G, rgba.B);
        }

        static string ToHex(Rgba32 color)
        {
            return TweeqColorLogic.FormatHex(color.ToRgba());
        }

        #endregion

        #region Conversion

        [Test]
        public void SrgbToOklch_MatchesReferenceValues()
        {
            // Measured values from colorjs.io 0.5.2 (the version used by the porting source radix.ts).
            Oklch gray = FromHex("#8B8D98");
            Assert.That(gray.L, Is.EqualTo(0.645313935869397).Within(TOLERANCE));
            Assert.That(gray.C, Is.EqualTo(0.016454192211054913).Within(TOLERANCE));
            Assert.That(gray.H, Is.EqualTo(277.69978370707054).Within(HUE_TOLERANCE));

            Oklch blue = FromHex("#0000ff");
            Assert.That(blue.L, Is.EqualTo(0.45201371817442365).Within(TOLERANCE));
            Assert.That(blue.C, Is.EqualTo(0.3132143886344849).Within(TOLERANCE));
            Assert.That(blue.H, Is.EqualTo(264.0520226163699).Within(HUE_TOLERANCE));
        }

        [Test]
        public void SrgbToOklch_AchromaticHasUndefinedHue()
        {
            // An achromatic color's hue is NaN. getButtonHoverColor looks at this NaN to change how it
            // handles saturation, so it must never be collapsed to 0.
            Assert.That(double.IsNaN(FromHex("#ffffff").H), Is.True, "white");
            Assert.That(double.IsNaN(FromHex("#000000").H), Is.True, "black");
            Assert.That(double.IsNaN(FromHex("#808080").H), Is.True, "mid gray");
            Assert.That(double.IsNaN(FromHex("#8B8D98").H), Is.False, "tinted gray keeps its hue");
        }

        [Test]
        public void OklchRoundTrip_ReturnsSameBytes()
        {
            string[] samples =
            {
                "#000000", "#ffffff", "#0000ff", "#ff0000", "#00ff00", "#8B8D98",
                "#123456", "#e5484d", "#ffc53d", "#46a758", "#3e63dd", "#010203",
            };

            foreach (string hex in samples)
            {
                Rgba32 bytes = TweeqOklch.OklchToBytes(FromHex(hex));
                Assert.That(ToHex(bytes), Is.EqualTo(hex.ToLowerInvariant()), hex);
            }
        }

        [Test]
        public void OklabRoundTrip_IsStable()
        {
            Oklch source = FromHex("#3e63dd");
            Oklch restored = TweeqOklch.OklabToOklch(TweeqOklch.OklchToOklab(source));

            Assert.That(restored.L, Is.EqualTo(source.L).Within(TOLERANCE));
            Assert.That(restored.C, Is.EqualTo(source.C).Within(TOLERANCE));
            Assert.That(restored.H, Is.EqualTo(source.H).Within(HUE_TOLERANCE));
        }

        [Test]
        public void LabD50RoundTrip_IsStable()
        {
            // The path scale mixing goes through (OKLab -> CIE Lab(D50) -> OKLab) must be reversible.
            Oklch source = FromHex("#46a758");
            Oklab oklab = TweeqOklch.OklchToOklab(source);
            Oklab restored = TweeqOklch.LabD50ToOklab(TweeqOklch.OklabToLabD50(oklab));

            Assert.That(restored.L, Is.EqualTo(oklab.L).Within(1e-9));
            Assert.That(restored.A, Is.EqualTo(oklab.A).Within(1e-9));
            Assert.That(restored.B, Is.EqualTo(oklab.B).Within(1e-9));
        }

        [Test]
        public void P3ToOklch_MatchesEmbeddedPaletteData()
        {
            // RadixPaletteData bakes in @radix-ui/colors's display-p3 values through this same conversion.
            // This is kept in place so a mismatch becomes noticeable if either side ever gets swapped out.
            const int BLUE = 16;
            Assert.That(RadixPaletteData.ScaleNames[BLUE], Is.EqualTo("blue"));

            // light.ts: blue9 = color(display-p3 0.247 0.556 0.969)
            Oklch expected = TweeqOklch.P3ToOklch(0.247, 0.556, 0.969);
            Oklch actual = RadixPaletteData.Get(false, BLUE, 8);

            Assert.That(actual.L, Is.EqualTo(expected.L).Within(TOLERANCE));
            Assert.That(actual.C, Is.EqualTo(expected.C).Within(TOLERANCE));
            Assert.That(actual.H, Is.EqualTo(expected.H).Within(HUE_TOLERANCE));
        }

        #endregion

        #region Gamut

        [Test]
        public void OklchToBytes_MapsOutOfGamutIntoSrgb()
        {
            // A blue with 0.4 chroma doesn't fit within sRGB. This is the result after passing it through CSS Color 4's Gamut Mapping.
            Assert.That(ToHex(TweeqOklch.OklchToBytes(new Oklch(0.5, 0.4, 264.0))),
                Is.EqualTo("#0033ff"));
            Assert.That(ToHex(TweeqOklch.OklchToBytes(new Oklch(0.65, 0.35, 30.0))),
                Is.EqualTo("#ff1300"));
        }

        [Test]
        public void OklchToBytes_ClampsExtremeLightness()
        {
            Assert.That(ToHex(TweeqOklch.OklchToBytes(new Oklch(1.5, 0.3, 120.0))), Is.EqualTo("#ffffff"));
            Assert.That(ToHex(TweeqOklch.OklchToBytes(new Oklch(-0.2, 0.3, 120.0))), Is.EqualTo("#000000"));
        }

        #endregion

        #region DeltaEOK

        [Test]
        public void DeltaEOK_IsZeroForIdenticalColors()
        {
            Oklch color = FromHex("#46a758");
            Assert.That(TweeqOklch.DeltaEOK(color, color), Is.EqualTo(0.0).Within(TOLERANCE));
        }

        [Test]
        public void DeltaEOK_IsSymmetric()
        {
            string[] samples = { "#000000", "#ffffff", "#0000ff", "#8B8D98", "#e5484d", "#46a758" };

            for (int i = 0; i < samples.Length; i++)
            {
                for (int j = 0; j < samples.Length; j++)
                {
                    Oklch left = FromHex(samples[i]);
                    Oklch right = FromHex(samples[j]);
                    Assert.That(TweeqOklch.DeltaEOK(left, right),
                        Is.EqualTo(TweeqOklch.DeltaEOK(right, left)).Within(TOLERANCE),
                        samples[i] + " / " + samples[j]);
                }
            }
        }

        [Test]
        public void DeltaEOK_MatchesReferenceValue()
        {
            Assert.That(TweeqOklch.DeltaEOK(FromHex("#0000ff"), FromHex("#8B8D98")),
                Is.EqualTo(0.35457385802938474).Within(TOLERANCE));
        }

        [Test]
        public void DeltaEOK_SatisfiesTriangleInequality()
        {
            Oklch a = FromHex("#0000ff");
            Oklch b = FromHex("#8B8D98");
            Oklch c = FromHex("#e5484d");

            double ab = TweeqOklch.DeltaEOK(a, b);
            double bc = TweeqOklch.DeltaEOK(b, c);
            double ac = TweeqOklch.DeltaEOK(a, c);

            Assert.That(ac, Is.LessThanOrEqualTo(ab + bc + TOLERANCE));
        }

        #endregion

        #region APCA

        [Test]
        public void ContrastApca_MatchesReferenceValues()
        {
            Assert.That(TweeqOklch.ContrastApca(1.0, 1.0, 1.0, 0.0, 0.0, 1.0),
                Is.EqualTo(85.82083364925676).Within(1e-9), "white over pure blue");

            Assert.That(
                TweeqOklch.ContrastApca(1.0, 1.0, 1.0, 0xFF / 255.0, 0xC5 / 255.0, 0x3D / 255.0),
                Is.EqualTo(26.100318322515825).Within(1e-9), "white over amber");
        }

        [Test]
        public void ContrastApca_IsZeroForIdenticalColors()
        {
            Assert.That(TweeqOklch.ContrastApca(1.0, 1.0, 1.0, 1.0, 1.0, 1.0),
                Is.EqualTo(0.0).Within(TOLERANCE));
        }

        [Test]
        public void ContrastApca_ThresholdSplitsWhiteTextReadability()
        {
            // radix.ts's getTextColor falls back to "unreadable in white" when |Lc| < 40.
            // A dark blue should end up on the side that gets white text, and a light amber on the side that doesn't.
            double onBlue = Math.Abs(TweeqOklch.ContrastApca(1.0, 1.0, 1.0, 0.0, 0.0, 1.0));
            double onAmber = Math.Abs(
                TweeqOklch.ContrastApca(1.0, 1.0, 1.0, 0xFF / 255.0, 0xC5 / 255.0, 0x3D / 255.0));

            Assert.That(onBlue, Is.GreaterThan(40.0));
            Assert.That(onAmber, Is.LessThan(40.0));
        }

        #endregion

        #region Bezier

        [Test]
        public void CubicBezier_PinsEndpoints()
        {
            CubicBezierEasing easing = new CubicBezierEasing(0.0, 2.0, 0.0, 2.0);

            Assert.That(easing.Evaluate(0.0), Is.EqualTo(0.0));
            Assert.That(easing.Evaluate(1.0), Is.EqualTo(1.0));
        }

        [Test]
        public void CubicBezier_MatchesReferenceValues()
        {
            // Measured values from bezier-easing (gre/bezier-easing). The two curves Radix uses.
            CubicBezierEasing light = new CubicBezierEasing(0.0, 2.0, 0.0, 2.0);
            Assert.That(light.Evaluate(0.25), Is.EqualTo(1.6486615717323203).Within(1e-9));
            Assert.That(light.Evaluate(0.5), Is.EqualTo(1.482440006219979).Within(1e-9));

            CubicBezierEasing dark = new CubicBezierEasing(1.0, 0.0, 1.0, 0.0);
            Assert.That(dark.Evaluate(0.25), Is.EqualTo(0.0007645474227605245).Within(1e-9));
            Assert.That(dark.Evaluate(0.5), Is.EqualTo(0.008779996890010529).Within(1e-9));
            Assert.That(dark.Evaluate(0.75), Is.EqualTo(0.05066921413383986).Within(1e-9));
        }

        [Test]
        public void CubicBezier_IsIdentityWhenLinear()
        {
            // The dark ease collapses to [0,0,0,0] when the background is light. It must be treated as linear in that case.
            CubicBezierEasing easing = new CubicBezierEasing(0.0, 0.0, 0.0, 0.0);

            for (int i = 0; i <= 10; i++)
            {
                double x = i / 10.0;
                Assert.That(easing.Evaluate(x), Is.EqualTo(x).Within(TOLERANCE), x.ToString());
            }
        }

        [Test]
        public void CubicBezier_IsMonotonicForDarkEasing()
        {
            CubicBezierEasing easing = new CubicBezierEasing(1.0, 0.0, 1.0, 0.0);

            double previous = easing.Evaluate(0.0);
            for (int i = 1; i <= 100; i++)
            {
                double value = easing.Evaluate(i / 100.0);
                Assert.That(value, Is.GreaterThanOrEqualTo(previous), "step " + i);
                previous = value;
            }
        }

        [Test]
        public void CubicBezier_RejectsOutOfRangeControlX()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CubicBezierEasing(-0.1, 0.0, 1.0, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CubicBezierEasing(0.0, 0.0, 1.5, 1.0));
        }

        #endregion
    }
}
