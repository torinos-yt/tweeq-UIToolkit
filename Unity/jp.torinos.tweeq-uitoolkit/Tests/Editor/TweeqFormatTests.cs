using System;
using System.Globalization;
using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class TweeqFormatTests
    {
        #region Reference implementation

        // A direct copy of NumberLogic.Format as it was before the move.
        // Serves as the baseline for confirming TweeqFormat (or, with ZString installed, that implementation) hasn't changed behavior by even one character
        const int REFERENCE_MAX_PRECISION = 15;

        static string ReferenceFormat(double value, int precision, bool tweaking)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            int digits = precision < 0
                ? 0
                : (precision > REFERENCE_MAX_PRECISION ? REFERENCE_MAX_PRECISION : precision);
            string text = value.ToString(
                "F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

            if (tweaking)
            {
                return text;
            }

            if (text.IndexOf('.') >= 0)
            {
                int end = text.Length;
                while (end > 0 && text[end - 1] == '0')
                {
                    end--;
                }

                if (end > 0 && text[end - 1] == '.')
                {
                    end--;
                }

                if (end != text.Length)
                {
                    text = text.Substring(0, end);
                }
            }

            return text == "-0" ? "0" : text;
        }

        static readonly double[] SampleValues =
        {
            0.0,
            -0.0,
            1.0,
            -1.0,
            2.0,
            0.5,
            -0.5,
            1.25,
            100.0,
            0.1 + 0.2,
            1234.5,
            -1234.5,
            -0.0000001,
            0.0000001,
            1e-9,
            -1e-9,
            9.9999999,
            -9.9999999,
            123456789.987654321,
            -123456789.987654321,
            1e15,
            -1e15,
            double.Epsilon,
            -double.Epsilon,
        };

        static readonly double[] NonFiniteValues =
        {
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
        };

        static readonly int[] SamplePrecisions = { -3, -1, 0, 1, 2, 3, 4, 5, 8, 14, 15, 16, 20 };

        #endregion

        #region Format parity

        [Test]
        public void FormatMatchesReferenceForAllSamples()
        {
            foreach (double value in SampleValues)
            {
                foreach (int precision in SamplePrecisions)
                {
                    Assert.That(
                        TweeqFormat.Format(value, precision, false),
                        Is.EqualTo(ReferenceFormat(value, precision, false)),
                        $"value={value} precision={precision} tweaking=false");

                    Assert.That(
                        TweeqFormat.Format(value, precision, true),
                        Is.EqualTo(ReferenceFormat(value, precision, true)),
                        $"value={value} precision={precision} tweaking=true");
                }
            }
        }

        [Test]
        public void FormatMatchesReferenceForNonFiniteValues()
        {
            foreach (double value in NonFiniteValues)
            {
                Assert.That(
                    TweeqFormat.Format(value, 4, false),
                    Is.EqualTo(ReferenceFormat(value, 4, false)),
                    $"value={value} tweaking=false");

                Assert.That(
                    TweeqFormat.Format(value, 4, true),
                    Is.EqualTo(ReferenceFormat(value, 4, true)),
                    $"value={value} tweaking=true");
            }
        }

        [Test]
        public void NumberLogicFormatForwardsToTweeqFormat()
        {
            foreach (double value in SampleValues)
            {
                foreach (int precision in SamplePrecisions)
                {
                    Assert.That(
                        NumberLogic.Format(value, precision, false),
                        Is.EqualTo(TweeqFormat.Format(value, precision, false)));

                    Assert.That(
                        NumberLogic.Format(value, precision, true),
                        Is.EqualTo(TweeqFormat.Format(value, precision, true)));
                }
            }
        }

        [Test]
        public void FormatNormalizesNegativeZero()
        {
            Assert.That(TweeqFormat.Format(-0.0, 4, false), Is.EqualTo("0"));
            Assert.That(TweeqFormat.Format(-0.0, 0, false), Is.EqualTo("0"));
            Assert.That(TweeqFormat.Format(-0.0000001, 4, false), Is.EqualTo("0"));
            Assert.That(TweeqFormat.Format(-0.0000001, 4, false), Does.Not.StartWith("-"));
        }

        [Test]
        public void FormatTrimsTrailingZerosAndDot()
        {
            Assert.That(TweeqFormat.Format(1.25, 4, false), Is.EqualTo("1.25"));
            Assert.That(TweeqFormat.Format(100.0, 3, false), Is.EqualTo("100"));
            Assert.That(TweeqFormat.Format(0.1 + 0.2, 4, false), Is.EqualTo("0.3"));
            Assert.That(TweeqFormat.Format(2.0, 0, false), Is.EqualTo("2"));
            Assert.That(TweeqFormat.Format(1234.5, 2, false), Is.EqualTo("1234.5"));
        }

        [Test]
        public void FormatKeepsTrailingZerosWhileTweaking()
        {
            Assert.That(TweeqFormat.Format(1.25, 4, true), Is.EqualTo("1.2500"));
            Assert.That(TweeqFormat.Format(100.0, 3, true), Is.EqualTo("100.000"));
            Assert.That(TweeqFormat.Format(2.0, 0, true), Is.EqualTo("2"));
            Assert.That(TweeqFormat.Format(1234.5, 2, true), Is.EqualTo("1234.50"));
        }

        #endregion

        #region Specifiers

        [Test]
        public void FixedSpecifierCoversEveryPrecision()
        {
            for (int i = 0; i <= TweeqFormat.MAX_FORMAT_PRECISION; i++)
            {
                Assert.That(
                    TweeqFormat.FixedSpecifier(i),
                    Is.EqualTo("F" + i.ToString(CultureInfo.InvariantCulture)));
            }
        }

        [Test]
        public void FixedSpecifierClampsOutOfRange()
        {
            Assert.That(TweeqFormat.FixedSpecifier(-5), Is.EqualTo("F0"));
            Assert.That(TweeqFormat.FixedSpecifier(99), Is.EqualTo("F15"));
            Assert.That(TweeqFormat.ClampDigits(-5), Is.EqualTo(0));
            Assert.That(TweeqFormat.ClampDigits(99), Is.EqualTo(TweeqFormat.MAX_FORMAT_PRECISION));
        }

        // The pre-building only reduces allocations if the reference is actually the same instance
        [Test]
        public void FixedSpecifierReturnsCachedInstance()
        {
            Assert.That(
                ReferenceEquals(TweeqFormat.FixedSpecifier(4), TweeqFormat.FixedSpecifier(4)),
                Is.True);
        }

        #endregion

        #region Angle

        static string ReferenceFormatAngle(double value)
        {
            if (Math.Abs(value) < 360.0)
            {
                return value.ToString("0.0", CultureInfo.InvariantCulture) + "°";
            }

            long revolutions = (long)Math.Truncate(value / 360.0);
            double rotation = value - revolutions * 360.0;
            return revolutions.ToString(CultureInfo.InvariantCulture)
                + "x "
                + rotation.ToString("0.0", CultureInfo.InvariantCulture)
                + "°";
        }

        static readonly double[] SampleAngles =
        {
            0.0,
            0.04,
            -0.04,
            12.34,
            -12.34,
            359.9,
            359.94,
            -359.9,
            360.0,
            -360.0,
            360.1,
            719.9,
            720.0,
            -720.0,
            3000.0,
            -3000.0,
            -1234.5,
        };

        [Test]
        public void FormatAngleMatchesReference()
        {
            foreach (double value in SampleAngles)
            {
                Assert.That(
                    TweeqFormat.FormatAngle(value),
                    Is.EqualTo(ReferenceFormatAngle(value)),
                    $"value={value}");
            }
        }

        [Test]
        public void FormatAngleKeepsUnitAndRevolutions()
        {
            Assert.That(TweeqFormat.FormatAngle(0.0), Is.EqualTo("0.0°"));
            Assert.That(TweeqFormat.FormatAngle(359.9), Is.EqualTo("359.9°"));
            Assert.That(TweeqFormat.FormatAngle(-359.9), Is.EqualTo("-359.9°"));
            Assert.That(TweeqFormat.FormatAngle(360.0), Is.EqualTo("1x 0.0°"));
            Assert.That(TweeqFormat.FormatAngle(-360.0), Is.EqualTo("-1x 0.0°"));
            Assert.That(TweeqFormat.FormatAngle(3000.0), Is.EqualTo("8x 120.0°"));
            Assert.That(TweeqFormat.FormatAngle(-3000.0), Is.EqualTo("-8x -120.0°"));
            Assert.That(TweeqFormat.FormatAngle(-1234.5), Is.EqualTo("-3x -154.5°"));
        }

        // The ZString version can only pass standard format strings, so "F1" is used. Guarantee the two don't disagree.
        // Negative values below ±0.05 are excluded, since "whether the negative zero survives into the display" can differ by runtime implementation
        static readonly double[] FixedFormatAgreementAngles =
        {
            0.0, 0.04, 12.34, 120.0, 154.5, 359.9, 359.94, 719.9, 3000.0,
            -12.34, -120.0, -154.5, -359.9, -1234.5, -3000.0,
        };

        [Test]
        public void AngleFormatAgreesWithFixedOnePrecision()
        {
            foreach (double value in FixedFormatAgreementAngles)
            {
                Assert.That(
                    value.ToString("F1", CultureInfo.InvariantCulture),
                    Is.EqualTo(value.ToString("0.0", CultureInfo.InvariantCulture)),
                    $"value={value}");
            }
        }

        #endregion

        #region Angle display key

        // If "key matches ⇒ display string also matches" doesn't hold, the label would freeze on a stale value
        [Test]
        public void AngleDisplayKeyImpliesSameText()
        {
            for (int i = 0; i < 4000; i++)
            {
                double a = -800.0 + i * 0.4;
                double b = a + 0.03;

                if (!TweeqFormat.TryGetAngleDisplayKey(a, out long revA, out double tenthsA))
                {
                    continue;
                }

                if (!TweeqFormat.TryGetAngleDisplayKey(b, out long revB, out double tenthsB))
                {
                    continue;
                }

                if (revA != revB || !TweeqFormat.SameValueBits(tenthsA, tenthsB))
                {
                    continue;
                }

                Assert.That(
                    TweeqFormat.FormatAngle(a),
                    Is.EqualTo(TweeqFormat.FormatAngle(b)),
                    $"a={a} b={b}");
            }
        }

        // Crossing 360° changes the display's shape itself, so the key must also always distinguish it
        [Test]
        public void AngleDisplayKeySeparatesRevolutionBoundary()
        {
            TweeqFormat.TryGetAngleDisplayKey(359.96, out long belowRevolutions, out double belowTenths);
            TweeqFormat.TryGetAngleDisplayKey(360.04, out long aboveRevolutions, out double aboveTenths);

            Assert.That(belowRevolutions, Is.EqualTo(0L));
            Assert.That(aboveRevolutions, Is.EqualTo(1L));
            Assert.That(belowTenths, Is.Not.EqualTo(aboveTenths));
            Assert.That(TweeqFormat.FormatAngle(359.96), Is.EqualTo("360.0°"));
            Assert.That(TweeqFormat.FormatAngle(360.04), Is.EqualTo("1x 0.0°"));
        }

        [Test]
        public void AngleDisplayKeyRejectsNonFinite()
        {
            Assert.That(TweeqFormat.TryGetAngleDisplayKey(double.NaN, out _, out _), Is.False);
            Assert.That(
                TweeqFormat.TryGetAngleDisplayKey(double.PositiveInfinity, out _, out _), Is.False);
            Assert.That(
                TweeqFormat.TryGetAngleDisplayKey(double.NegativeInfinity, out _, out _), Is.False);
        }

        // The rounding boundary (.05°) is excluded from caching, forcing a rebuild every time
        [Test]
        public void AngleDisplayKeyRejectsRoundingTies()
        {
            Assert.That(TweeqFormat.TryGetAngleDisplayKey(12.35, out _, out _), Is.False);
            Assert.That(TweeqFormat.TryGetAngleDisplayKey(-12.35, out _, out _), Is.False);
            Assert.That(TweeqFormat.TryGetAngleDisplayKey(12.30, out _, out _), Is.True);
        }

        [Test]
        public void AngleDisplayKeyIsStableWithinSameTenth()
        {
            Assert.That(TweeqFormat.TryGetAngleDisplayKey(12.31, out long r1, out double t1), Is.True);
            Assert.That(TweeqFormat.TryGetAngleDisplayKey(12.33, out long r2, out double t2), Is.True);

            Assert.That(r1, Is.EqualTo(r2));
            Assert.That(TweeqFormat.SameValueBits(t1, t2), Is.True);
            Assert.That(TweeqFormat.FormatAngle(12.31), Is.EqualTo(TweeqFormat.FormatAngle(12.33)));
        }

        #endregion

        #region Value bits

        [Test]
        public void SameValueBitsSeparatesSignedZero()
        {
            Assert.That(TweeqFormat.SameValueBits(0.0, -0.0), Is.False);
            Assert.That(TweeqFormat.SameValueBits(0.0, 0.0), Is.True);
            Assert.That(TweeqFormat.SameValueBits(-0.0, -0.0), Is.True);
        }

        [Test]
        public void SameValueBitsTreatsNaNAsEqual()
        {
            Assert.That(TweeqFormat.SameValueBits(double.NaN, double.NaN), Is.True);
            Assert.That(TweeqFormat.SameValueBits(double.NaN, 1.0), Is.False);
        }

        #endregion
    }
}
