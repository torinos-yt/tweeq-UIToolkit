using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tweeq.Core
{
    /// <summary>
    /// Timecode formatting, parsing, and scrub quantization. No UnityEngine dependency, all double.
    /// The original is tweeq/src/InputTime/utils.ts (formatting/parsing) and InputTime.vue (tweakSpeed / snap).
    /// </summary>
    public static class TimecodeLogic
    {
        #region Constants

        /// <summary>tweak scale. Same 0..3 as tweakScale in the original InputTime.vue.</summary>
        public const int SCALE_FRAMES = 0;
        public const int SCALE_SECONDS = 1;
        public const int SCALE_MINUTES = 2;
        public const int SCALE_HOURS = 3;

        const double SECONDS_PER_MINUTE = 60.0;
        const double SECONDS_PER_HOUR = 3600.0;

        // Each regex from the original. The '-' inside the character class is escaped, keeping the same literal form as the JS version.
        static readonly Regex TimecodeLiteralPattern =
            new Regex(@"([0-9+\-.]+:)+[0-9+\-.]+", RegexOptions.IgnoreCase);

        static readonly Regex FramesLiteralPattern =
            new Regex(@"[0-9+\-.]+f(rames?)?", RegexOptions.IgnoreCase);

        static readonly Regex SecondsLiteralPattern =
            new Regex(@"[0-9+\-.]+s(ec(ond)?s?)?", RegexOptions.IgnoreCase);

        static readonly Regex MinutesLiteralPattern =
            new Regex(@"[0-9+\-.]+m(in(ute)?s?)?", RegexOptions.IgnoreCase);

        static readonly Regex HoursLiteralPattern =
            new Regex(@"[0-9+\-.]+h((ou)?r)?s?", RegexOptions.IgnoreCase);

        // A unit suffix is detected by "matches at the end, immediately preceded by a digit-like character." Same literal form as the original parseTimecode.
        static readonly Regex SecondsSuffixPattern = new Regex(@"[0-9+\-.]s(ec(ond)?s?)?$");
        static readonly Regex MinutesSuffixPattern = new Regex(@"[0-9+\-.]m(in(ute)?s?)?$");
        static readonly Regex HoursSuffixPattern = new Regex(@"[0-9+\-.]h((ou)?r)?s?$");

        #endregion

        #region Format

        /// <summary>
        /// "mm:ss:ff", or "h:mm:ss:ff" only when h&gt;0 (h is not zero-padded). Negative values get a leading '-'.
        /// f is the raw remainder, so fractional frames come out as-is, e.g. "00:00:1.5" (same as the original).
        /// When frameRate is 0, negative, or non-finite, returns "00:00:00" to avoid stringifying a division by zero
        /// (safer than an exception or "∞" showing up on stage during a performance; the original outputs a string mixed with NaN).
        /// </summary>
        public static string FormatTimecode(double frames, double frameRate)
        {
            if (!TweeqMath.IsFinite(frames) || !TweeqMath.IsFinite(frameRate) || frameRate <= 0.0)
            {
                return "00:00:00";
            }

            bool negative = frames < 0.0;
            if (negative)
            {
                frames = -frames;
            }

            double framesPerHour = frameRate * SECONDS_PER_HOUR;
            double framesPerMinute = frameRate * SECONDS_PER_MINUTE;

            double hours = Math.Floor(frames / framesPerHour);
            double minutes = Math.Floor(frames % framesPerHour / framesPerMinute);
            double seconds = Math.Floor(frames % framesPerMinute / frameRate);
            double frame = frames % frameRate;

            StringBuilder builder = new StringBuilder(negative ? 12 : 11);

            if (negative)
            {
                builder.Append('-');
            }

            if (hours > 0.0)
            {
                builder.Append(JsNumberToString(hours));
                builder.Append(':');
            }

            AppendPadded(builder, minutes);
            builder.Append(':');
            AppendPadded(builder, seconds);
            builder.Append(':');
            AppendPadded(builder, frame);

            return builder.ToString();
        }

        // The original pad() is padStart(2,'0'), so anything 3 characters or longer (e.g. fractional frames) passes through as-is
        static void AppendPadded(StringBuilder builder, double value)
        {
            string text = JsNumberToString(value);
            if (text.Length < 2)
            {
                builder.Append('0');
            }

            builder.Append(text);
        }

        #endregion

        #region Parse

        /// <summary>
        /// The original parseTimecode. Interprets "h:mm:ss:ff" (any number of digits), unit suffixes,
        /// and bare frame counts. Cases where the original returns null / NaN become false here (the caller keeps the current value).
        /// </summary>
        public static bool TryParseTimecode(string text, double frameRate, out double frames)
        {
            frames = 0.0;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string timecode = text.Trim().ToLowerInvariant();

            double sign = 1.0;
            if (timecode.Length > 0 && timecode[0] == '-')
            {
                sign = -1.0;
                timecode = timecode.Substring(1);
            }

            if (timecode.IndexOf(':') >= 0)
            {
                string[] parts = timecode.Split(':');
                double total = 0.0;

                // The original reverse()s first, then weights from the right end, i.e. treats it as frames
                for (int i = 0; i < parts.Length; i++)
                {
                    double digit = JsNumber(parts[parts.Length - 1 - i]);
                    double multiplier =
                        i == 0 ? 1.0
                        : i == 1 ? frameRate
                        : frameRate * Math.Pow(SECONDS_PER_MINUTE, i - 1);
                    total += digit * multiplier;
                }

                return Finish(sign * total, out frames);
            }

            if (SecondsSuffixPattern.IsMatch(timecode))
            {
                double seconds = JsParseFloat(timecode);
                return double.IsNaN(seconds)
                    ? false
                    : Finish(sign * JsRound(seconds * frameRate), out frames);
            }

            if (MinutesSuffixPattern.IsMatch(timecode))
            {
                double minutes = JsParseFloat(timecode);
                return double.IsNaN(minutes)
                    ? false
                    : Finish(sign * JsRound(minutes * frameRate * SECONDS_PER_MINUTE), out frames);
            }

            if (HoursSuffixPattern.IsMatch(timecode))
            {
                double hours = JsParseFloat(timecode);
                return double.IsNaN(hours)
                    ? false
                    : Finish(sign * JsRound(hours * frameRate * SECONDS_PER_HOUR), out frames);
            }

            // For bare numbers the original uses parseInt, so fractional frames are truncated toward 0
            double parsed = JsParseInt(timecode);
            return double.IsNaN(parsed) ? false : Finish(sign * parsed, out frames);
        }

        static bool Finish(double value, out double frames)
        {
            if (!TweeqMath.IsFinite(value))
            {
                frames = 0.0;
                return false;
            }

            frames = TweeqMath.NormalizeZero(value);
            return true;
        }

        /// <summary>
        /// Replaces timecode literals and unit suffixes in an expression with frame counts (the original replaceTimecodeWithFrames).
        /// The replacement order (colon → f → s → m → h) matches the original, and that order decides the units' priority.
        /// Parts that can't be interpreted collapse to "0", same as the original.
        /// </summary>
        public static string ReplaceTimecodeWithFrames(string text, double frameRate)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            MatchEvaluator toFrames = match =>
                TryParseTimecode(match.Value, frameRate, out double value)
                    ? JsNumberToString(value)
                    : "0";

            string result = TimecodeLiteralPattern.Replace(text, toFrames);
            result = FramesLiteralPattern.Replace(result, toFrames);
            result = SecondsLiteralPattern.Replace(result, toFrames);
            result = MinutesLiteralPattern.Replace(result, toFrames);
            result = HoursLiteralPattern.Replace(result, toFrames);
            return result;
        }

        #endregion

        #region Tweak

        /// <summary>Frame count per 1 unit of scale. 0=1 / 1=fps / 2=fps*60 / 3 and above=fps*3600.</summary>
        public static double UnitFrames(int scale, double frameRate)
        {
            if (scale <= SCALE_FRAMES)
            {
                return 1.0;
            }

            if (scale == SCALE_SECONDS)
            {
                return frameRate;
            }

            return scale == SCALE_MINUTES
                ? frameRate * SECONDS_PER_MINUTE
                : frameRate * SECONDS_PER_HOUR;
        }

        /// <summary>
        /// Increment per 1px of horizontal drag (tweakSpeed in the original InputTime.vue).
        /// frames is a fixed 1/4px regardless of fps; seconds/minutes are 1/10 coarse, hours are 1/100 coarse.
        /// </summary>
        public static double ScaleSpeed(int scale, double frameRate)
        {
            if (scale <= SCALE_FRAMES)
            {
                return 1.0 / 4.0;
            }

            if (scale == SCALE_SECONDS)
            {
                return frameRate / 10.0;
            }

            return scale == SCALE_MINUTES
                ? frameRate * SECONDS_PER_MINUTE / 10.0
                : frameRate * SECONDS_PER_HOUR / 100.0;
        }

        /// <summary>
        /// Snaps to the unit boundary of scale (the version that keeps no remainder).
        /// scale=0 means step=1, i.e. the default path that quantizes a continuous value to an integer frame.
        /// </summary>
        public static double SnapToScale(double frames, int scale, double frameRate)
        {
            return Quantize(frames, UnitFrames(scale, frameRate), 0.0);
        }

        /// <summary>
        /// Snapping in the original's style. Moves in unit steps while keeping the remainder within the unit
        /// from offsetSource (the value when Q was pressed). Corresponds to [step, model % step] in the original tweakSnapParams.
        /// </summary>
        public static double SnapToScale(double frames, int scale, double frameRate, double offsetSource)
        {
            double step = UnitFrames(scale, frameRate);
            if (!TweeqMath.IsFinite(step) || step <= 0.0 || !TweeqMath.IsFinite(offsetSource))
            {
                return Quantize(frames, step, 0.0);
            }

            return Quantize(frames, step, offsetSource % step);
        }

        // Equivalent to scalar.quantize from linearly. The rounding needs to match JS Math.round (rounds toward +infinity):
        // since the frames-scale speed is 1/4px, exact .5 fractions on the negative side occur routinely
        static double Quantize(double value, double step, double origin)
        {
            if (!TweeqMath.IsFinite(value) || !TweeqMath.IsFinite(step) || step <= 0.0
                || !TweeqMath.IsFinite(origin))
            {
                return value;
            }

            return TweeqMath.NormalizeZero(JsRound((value - origin) / step) * step + origin);
        }

        #endregion

        #region JS number semantics

        // The following reproduces, only to the extent needed, the JS built-in behavior the original depends on.
        // String generation only runs on commit, so a straightforward implementation is enough.

        /// <summary>JS's Math.round (.5 rounds toward +infinity). C#'s default is banker's rounding, so it can't be used as-is.</summary>
        static double JsRound(double value)
        {
            return Math.Floor(value + 0.5);
        }

        /// <summary>JS's Number(). Empty string is 0, and NaN if it can't be interpreted.</summary>
        static double JsNumber(string text)
        {
            if (text == null)
            {
                return double.NaN;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return 0.0;
            }

            return double.TryParse(
                trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : double.NaN;
        }

        /// <summary>JS's parseFloat(). Reads only the leading numeric portion.</summary>
        static double JsParseFloat(string text)
        {
            int length = NumericPrefixLength(text, true);
            if (length == 0)
            {
                return double.NaN;
            }

            return double.TryParse(
                text.Substring(0, length), NumberStyles.Float, CultureInfo.InvariantCulture,
                out double value)
                ? value
                : double.NaN;
        }

        /// <summary>JS's parseInt(). Reads only the leading integer portion (discards anything from the decimal point onward).</summary>
        static double JsParseInt(string text)
        {
            int length = NumericPrefixLength(text, false);
            if (length == 0)
            {
                return double.NaN;
            }

            return double.TryParse(
                text.Substring(0, length), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out double value)
                ? value
                : double.NaN;
        }

        // Returns 0 if there isn't a single digit (treated as NaN)
        static int NumericPrefixLength(string text, bool allowFraction)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int index = 0;
            if (text[index] == '+' || text[index] == '-')
            {
                index++;
            }

            int digits = 0;
            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                index++;
                digits++;
            }

            if (allowFraction && index < text.Length && text[index] == '.')
            {
                int fractionStart = index;
                index++;

                int fractionDigits = 0;
                while (index < text.Length && text[index] >= '0' && text[index] <= '9')
                {
                    index++;
                    fractionDigits++;
                }

                // "1." is 1 in JS too. When there's no fractional part, drop the '.' and return just the integer part
                if (fractionDigits == 0)
                {
                    index = fractionStart;
                }

                digits += fractionDigits;
            }

            return digits == 0 ? 0 : index;
        }

        /// <summary>
        /// JS's String(number). Integers get no decimal point.
        /// Exponential notation for 1e21 and above won't match the original's literal form, but that's acceptable
        /// since it's outside the practical range of frame counts.
        /// </summary>
        static string JsNumberToString(double value)
        {
            if (double.IsNaN(value))
            {
                return "NaN";
            }

            if (double.IsPositiveInfinity(value))
            {
                return "Infinity";
            }

            if (double.IsNegativeInfinity(value))
            {
                return "-Infinity";
            }

            if (value == 0.0)
            {
                return "0";
            }

            if (value == Math.Floor(value) && Math.Abs(value) < 1e21)
            {
                return value.ToString("F0", CultureInfo.InvariantCulture);
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
