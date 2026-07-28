using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class TimecodeLogicTests
    {
        const double TOLERANCE = 1e-9;

        static double Parse(string text, double frameRate)
        {
            Assert.That(TimecodeLogic.TryParseTimecode(text, frameRate, out double frames), Is.True,
                "failed to parse: " + text);
            return frames;
        }

        #region FormatTimecode

        [Test]
        public void FormatTimecodeOmitsHoursBelowOneHour()
        {
            Assert.That(TimecodeLogic.FormatTimecode(0.0, 24.0), Is.EqualTo("00:00:00"));
            Assert.That(TimecodeLogic.FormatTimecode(24 * 61 + 3, 24.0), Is.EqualTo("01:01:03"));
            Assert.That(TimecodeLogic.FormatTimecode(24 * 3600 - 1, 24.0), Is.EqualTo("59:59:23"));
        }

        [Test]
        public void FormatTimecodeAddsUnpaddedHoursFromOneHour()
        {
            Assert.That(TimecodeLogic.FormatTimecode(24 * 3600, 24.0), Is.EqualTo("1:00:00:00"));
            Assert.That(TimecodeLogic.FormatTimecode(24 * 3600 * 10, 24.0), Is.EqualTo("10:00:00:00"));
            Assert.That(TimecodeLogic.FormatTimecode(24 * 3600 + 24 * 61 + 3, 24.0),
                Is.EqualTo("1:01:01:03"));
        }

        [Test]
        public void FormatTimecodeFollowsFrameRate()
        {
            Assert.That(TimecodeLogic.FormatTimecode(90.0, 30.0), Is.EqualTo("00:03:00"));
            Assert.That(TimecodeLogic.FormatTimecode(90.0, 24.0), Is.EqualTo("00:03:18"));
            Assert.That(TimecodeLogic.FormatTimecode(60.0, 60.0), Is.EqualTo("00:01:00"));
        }

        [Test]
        public void FormatTimecodePrefixesNegativeValues()
        {
            Assert.That(TimecodeLogic.FormatTimecode(-36.0, 24.0), Is.EqualTo("-00:01:12"));
            Assert.That(TimecodeLogic.FormatTimecode(-(24 * 3600 + 36), 24.0),
                Is.EqualTo("-1:00:01:12"));
            Assert.That(TimecodeLogic.FormatTimecode(-0.0, 24.0), Is.EqualTo("00:00:00"));
        }

        [Test]
        public void FormatTimecodeKeepsFractionalFramesAsIs()
        {
            // The original's pad is padStart(2,'0'), so anything 3 characters or longer comes out as-is
            Assert.That(TimecodeLogic.FormatTimecode(1.5, 24.0), Is.EqualTo("00:00:1.5"));
            Assert.That(TimecodeLogic.FormatTimecode(24.5, 24.0), Is.EqualTo("00:01:0.5"));
        }

        [Test]
        public void FormatTimecodeFallsBackOnUnusableInput()
        {
            Assert.That(TimecodeLogic.FormatTimecode(10.0, 0.0), Is.EqualTo("00:00:00"));
            Assert.That(TimecodeLogic.FormatTimecode(10.0, -24.0), Is.EqualTo("00:00:00"));
            Assert.That(TimecodeLogic.FormatTimecode(double.NaN, 24.0), Is.EqualTo("00:00:00"));
            Assert.That(TimecodeLogic.FormatTimecode(double.PositiveInfinity, 24.0),
                Is.EqualTo("00:00:00"));
        }

        #endregion

        #region TryParseTimecode (ported from the original utils.test.ts)

        [Test]
        public void ParsesTimecodeSplitByColon()
        {
            Assert.That(Parse("00:00:00", 24.0), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(Parse("00:00:00", 30.0), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(Parse("1:00", 24.0), Is.EqualTo(24.0).Within(TOLERANCE));
            Assert.That(Parse("1:00", 30.0), Is.EqualTo(30.0).Within(TOLERANCE));
            Assert.That(Parse("1:00:00", 60.0), Is.EqualTo(60.0 * 60.0).Within(TOLERANCE));
            Assert.That(Parse("120:00", 60.0), Is.EqualTo(60.0 * 120.0).Within(TOLERANCE));
        }

        [Test]
        public void ParsesFourDigitTimecodeAsHours()
        {
            Assert.That(Parse("1:00:00:00", 24.0), Is.EqualTo(24.0 * 3600.0).Within(TOLERANCE));
            Assert.That(Parse("2:01:01:03", 24.0),
                Is.EqualTo(2 * 24.0 * 3600.0 + 24 * 61 + 3).Within(TOLERANCE));
        }

        [Test]
        public void ParsesFrameSuffix()
        {
            Assert.That(Parse("100f", 24.0), Is.EqualTo(100.0).Within(TOLERANCE));
            Assert.That(Parse("100F", 24.0), Is.EqualTo(100.0).Within(TOLERANCE));
            Assert.That(Parse("100Frames", 24.0), Is.EqualTo(100.0).Within(TOLERANCE));
            Assert.That(Parse("100frame", 24.0), Is.EqualTo(100.0).Within(TOLERANCE));
        }

        [Test]
        public void ParsesSecondSuffix()
        {
            Assert.That(Parse("5s", 30.0), Is.EqualTo(150.0).Within(TOLERANCE));
            Assert.That(Parse("5sec", 30.0), Is.EqualTo(150.0).Within(TOLERANCE));
            Assert.That(Parse("5secs", 30.0), Is.EqualTo(150.0).Within(TOLERANCE));
            Assert.That(Parse("5second", 30.0), Is.EqualTo(150.0).Within(TOLERANCE));
            Assert.That(Parse("5seconds", 30.0), Is.EqualTo(150.0).Within(TOLERANCE));
            Assert.That(Parse("5SECONDS", 30.0), Is.EqualTo(150.0).Within(TOLERANCE));
        }

        [Test]
        public void ParsesMinuteSuffix()
        {
            Assert.That(Parse("10m", 30.0), Is.EqualTo(18000.0).Within(TOLERANCE));
            Assert.That(Parse("10min", 30.0), Is.EqualTo(18000.0).Within(TOLERANCE));
            Assert.That(Parse("10mins", 30.0), Is.EqualTo(18000.0).Within(TOLERANCE));
            Assert.That(Parse("10minute", 30.0), Is.EqualTo(18000.0).Within(TOLERANCE));
            Assert.That(Parse("10minutes", 30.0), Is.EqualTo(18000.0).Within(TOLERANCE));
        }

        [Test]
        public void ParsesHourSuffix()
        {
            Assert.That(Parse("10h", 30.0), Is.EqualTo(1080000.0).Within(TOLERANCE));
            Assert.That(Parse("10hr", 30.0), Is.EqualTo(1080000.0).Within(TOLERANCE));
            Assert.That(Parse("10hrs", 30.0), Is.EqualTo(1080000.0).Within(TOLERANCE));
            Assert.That(Parse("10hour", 30.0), Is.EqualTo(1080000.0).Within(TOLERANCE));
            Assert.That(Parse("10hours", 30.0), Is.EqualTo(1080000.0).Within(TOLERANCE));
        }

        [Test]
        public void ParsesNegativeTimecode()
        {
            Assert.That(Parse("-00:01:12", 24.0), Is.EqualTo(-36.0).Within(TOLERANCE));
            Assert.That(Parse("-100f", 24.0), Is.EqualTo(-100.0).Within(TOLERANCE));
            Assert.That(Parse("-100F", 24.0), Is.EqualTo(-100.0).Within(TOLERANCE));
            Assert.That(Parse("-100Frames", 24.0), Is.EqualTo(-100.0).Within(TOLERANCE));
            Assert.That(Parse("-1.5s", 24.0), Is.EqualTo(-36.0).Within(TOLERANCE));
        }

        [Test]
        public void ParsesBareFrameCount()
        {
            Assert.That(Parse("42", 24.0), Is.EqualTo(42.0).Within(TOLERANCE));
            Assert.That(Parse(" 42 ", 24.0), Is.EqualTo(42.0).Within(TOLERANCE));
            Assert.That(Parse("+42", 24.0), Is.EqualTo(42.0).Within(TOLERANCE));
        }

        [Test]
        public void TruncatesFractionalFrameLiterals()
        {
            // Since the original uses parseInt, fractional frame specifications are truncated toward 0
            Assert.That(Parse("1.9f", 24.0), Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(Parse("-1.9f", 24.0), Is.EqualTo(-1.0).Within(TOLERANCE));
            Assert.That(Parse("2.5", 24.0), Is.EqualTo(2.0).Within(TOLERANCE));
        }

        [Test]
        public void RoundsFractionalUnitLiterals()
        {
            // Seconds/minutes/hours use Math.round (rounds toward +infinity)
            Assert.That(Parse("1.5s", 24.0), Is.EqualTo(36.0).Within(TOLERANCE));
            Assert.That(Parse("0.5s", 25.0), Is.EqualTo(13.0).Within(TOLERANCE));
            Assert.That(Parse("1.5h", 24.0), Is.EqualTo(129600.0).Within(TOLERANCE));
            Assert.That(Parse("0.5m", 24.0), Is.EqualTo(720.0).Within(TOLERANCE));
        }

        [Test]
        public void FailsOnUnparsableText()
        {
            Assert.That(TimecodeLogic.TryParseTimecode(null, 24.0, out _), Is.False);
            Assert.That(TimecodeLogic.TryParseTimecode("", 24.0, out _), Is.False);
            Assert.That(TimecodeLogic.TryParseTimecode("   ", 24.0, out _), Is.False);
            Assert.That(TimecodeLogic.TryParseTimecode("abc", 24.0, out _), Is.False);
            Assert.That(TimecodeLogic.TryParseTimecode("f", 24.0, out _), Is.False);
            Assert.That(TimecodeLogic.TryParseTimecode("-", 24.0, out _), Is.False);
            Assert.That(TimecodeLogic.TryParseTimecode("1.2.3:00", 24.0, out _), Is.False);
        }

        [Test]
        public void FormatAndParseRoundTrip()
        {
            double[] frameRates = {24.0, 30.0, 60.0};
            double[] values = {0.0, 1.0, 23.0, 24.0, 30.0, 1439.0, 86400.0, 123456.0};

            foreach (double frameRate in frameRates)
            {
                foreach (double value in values)
                {
                    string forward = TimecodeLogic.FormatTimecode(value, frameRate);
                    Assert.That(Parse(forward, frameRate), Is.EqualTo(value).Within(TOLERANCE),
                        forward + " @" + frameRate);

                    string backward = TimecodeLogic.FormatTimecode(-value, frameRate);
                    Assert.That(Parse(backward, frameRate), Is.EqualTo(-value).Within(TOLERANCE),
                        backward + " @" + frameRate);
                }
            }
        }

        #endregion

        #region ReplaceTimecodeWithFrames (ported from the original utils.test.ts)

        [Test]
        public void ReplacesTimecodeWithFrames()
        {
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("00:24 + 1:00", 24.0),
                Is.EqualTo("24 + 24"));
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("{10f}", 24.0), Is.EqualTo("{10}"));
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames(" (20SEC) + 3min * 1:00 ", 24.0),
                Is.EqualTo(" (480) + 4320 * 24 "));
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("hr(1.5h)\n10s", 24.0),
                Is.EqualTo("hr(129600)\n240"));
        }

        [Test]
        public void ReplacesColonLiteralsWithTheGivenFrameRate()
        {
            // The original Vue hardcodes 24 here only (a bug). Since variable fps is a requirement, we use frameRate
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("1:00", 30.0), Is.EqualTo("30"));
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("00:01:00", 60.0),
                Is.EqualTo("60"));
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("1:00:00", 60.0),
                Is.EqualTo("3600"));
        }

        [Test]
        public void ReplacesEveryUnitSuffix()
        {
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("10f + 10frames", 30.0),
                Is.EqualTo("10 + 10"));
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("1s + 2sec + 3seconds", 30.0),
                Is.EqualTo("30 + 60 + 90"));
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("1m + 2min + 3minutes", 30.0),
                Is.EqualTo("1800 + 3600 + 5400"));
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("1h + 2hr + 3hours", 30.0),
                Is.EqualTo("108000 + 216000 + 324000"));
        }

        [Test]
        public void LeavesPlainExpressionsUntouched()
        {
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("1 + 2 * (3 - 4)", 24.0),
                Is.EqualTo("1 + 2 * (3 - 4)"));
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames("", 24.0), Is.EqualTo(""));
            Assert.That(TimecodeLogic.ReplaceTimecodeWithFrames(null, 24.0), Is.Null);
        }

        [Test]
        public void ReplacementFeedsTheExpressionEvaluator()
        {
            // The actual path on commit: replace -> evaluate
            string code = TimecodeLogic.ReplaceTimecodeWithFrames("1:00 + 10f", 24.0);
            Assert.That(TweeqExpression.TryEvaluate(code, out double value), Is.True);
            Assert.That(value, Is.EqualTo(34.0).Within(TOLERANCE));
        }

        #endregion

        #region ScaleSpeed / UnitFrames

        [Test]
        public void ScaleSpeedFollowsTheOriginalTable()
        {
            // frames has a fixed sensitivity independent of fps
            Assert.That(TimecodeLogic.ScaleSpeed(0, 24.0), Is.EqualTo(0.25).Within(TOLERANCE));
            Assert.That(TimecodeLogic.ScaleSpeed(0, 60.0), Is.EqualTo(0.25).Within(TOLERANCE));

            Assert.That(TimecodeLogic.ScaleSpeed(1, 24.0), Is.EqualTo(2.4).Within(TOLERANCE));
            Assert.That(TimecodeLogic.ScaleSpeed(2, 24.0), Is.EqualTo(144.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.ScaleSpeed(3, 24.0), Is.EqualTo(864.0).Within(TOLERANCE));

            Assert.That(TimecodeLogic.ScaleSpeed(1, 30.0), Is.EqualTo(3.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.ScaleSpeed(2, 30.0), Is.EqualTo(180.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.ScaleSpeed(3, 30.0), Is.EqualTo(1080.0).Within(TOLERANCE));
        }

        [Test]
        public void ScaleSpeedClampsOutOfRangeScales()
        {
            Assert.That(TimecodeLogic.ScaleSpeed(-5, 24.0), Is.EqualTo(0.25).Within(TOLERANCE));
            Assert.That(TimecodeLogic.ScaleSpeed(9, 24.0), Is.EqualTo(864.0).Within(TOLERANCE));
        }

        [Test]
        public void UnitFramesFollowsTheScaleTable()
        {
            Assert.That(TimecodeLogic.UnitFrames(0, 24.0), Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.UnitFrames(1, 24.0), Is.EqualTo(24.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.UnitFrames(2, 24.0), Is.EqualTo(1440.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.UnitFrames(3, 24.0), Is.EqualTo(86400.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.UnitFrames(-1, 30.0), Is.EqualTo(1.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.UnitFrames(4, 30.0), Is.EqualTo(108000.0).Within(TOLERANCE));
        }

        #endregion

        #region SnapToScale

        [Test]
        public void SnapToFramesQuantizesTheDragAccumulator()
        {
            Assert.That(TimecodeLogic.SnapToScale(24.25, 0, 24.0), Is.EqualTo(24.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.SnapToScale(24.75, 0, 24.0), Is.EqualTo(25.0).Within(TOLERANCE));
        }

        [Test]
        public void SnapToFramesRoundsHalvesTowardPositiveInfinity()
        {
            // Since frames has a sensitivity of 1/4px, .5 on the negative side comes up routinely. Round it the same way as JS Math.round
            Assert.That(TimecodeLogic.SnapToScale(24.5, 0, 24.0), Is.EqualTo(25.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.SnapToScale(-24.5, 0, 24.0),
                Is.EqualTo(-24.0).Within(TOLERANCE));
        }

        [Test]
        public void SnapToScaleWithoutOffsetLandsOnUnitBoundaries()
        {
            Assert.That(TimecodeLogic.SnapToScale(26.0, 1, 24.0), Is.EqualTo(24.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.SnapToScale(38.0, 1, 24.0), Is.EqualTo(48.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.SnapToScale(700.0, 2, 24.0), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.SnapToScale(800.0, 2, 24.0),
                Is.EqualTo(1440.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.SnapToScale(50000.0, 3, 24.0),
                Is.EqualTo(86400.0).Within(TOLERANCE));
        }

        [Test]
        public void SnapToScaleKeepsTheOffsetInsideTheUnit()
        {
            // Snapping to seconds from frame 25 lands on 1, 25, 49, ... (the original tweakSnapParams)
            Assert.That(TimecodeLogic.SnapToScale(26.0, 1, 24.0, 25.0),
                Is.EqualTo(25.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.SnapToScale(38.0, 1, 24.0, 25.0),
                Is.EqualTo(49.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.SnapToScale(700.0, 2, 24.0, 25.0),
                Is.EqualTo(25.0).Within(TOLERANCE));
            Assert.That(TimecodeLogic.SnapToScale(800.0, 2, 24.0, 25.0),
                Is.EqualTo(1465.0).Within(TOLERANCE));
        }

        [Test]
        public void SnapToScaleIsStableOnItsOwnResult()
        {
            // The original recomputes offset every time as model % step, so it keeps landing on the same grid even after snapping
            double snapped = TimecodeLogic.SnapToScale(38.0, 1, 24.0, 25.0);
            Assert.That(TimecodeLogic.SnapToScale(snapped, 1, 24.0, snapped),
                Is.EqualTo(snapped).Within(TOLERANCE));
        }

        [Test]
        public void SnapToScaleKeepsNegativeOffsets()
        {
            Assert.That(TimecodeLogic.SnapToScale(-26.0, 1, 24.0, -25.0),
                Is.EqualTo(-25.0).Within(TOLERANCE));
        }

        [Test]
        public void SnapToScalePassesThroughUnusableInput()
        {
            Assert.That(TimecodeLogic.SnapToScale(double.NaN, 1, 24.0), Is.NaN);
            Assert.That(TimecodeLogic.SnapToScale(10.0, 1, 0.0), Is.EqualTo(10.0).Within(TOLERANCE));
            // When offset can't be used, give up on preserving the remainder and fall to the unit boundary
            Assert.That(TimecodeLogic.SnapToScale(10.0, 1, 24.0, double.NaN),
                Is.EqualTo(0.0).Within(TOLERANCE));
        }

        #endregion
    }
}
