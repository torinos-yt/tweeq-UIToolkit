using System;
using System.Globalization;
#if TWEEQ_ZSTRING
using Cysharp.Text;
#endif

namespace Tweeq.Core
{
    /// <summary>
    /// The single entry point for number-to-string conversion. Because this runs on the every-frame path while
    /// scrubbing, it's built to avoid rebuilding format-specifier strings or holding intermediate strings.
    /// If ZString (com.cysharp.zstring) is present, the asmdef's versionDefines sets TWEEQ_ZSTRING and
    /// switches to an implementation with intermediate allocations stripped out.
    /// </summary>
    public static class TweeqFormat
    {
        #region Constants

        /// <summary>
        /// Upper bound on the decimal digit count passed to the format specifier. "F16" and above tends to
        /// show .NET implementation differences, so it's capped here.
        /// </summary>
        public const int MAX_FORMAT_PRECISION = 15;

        // Angle display is fixed at 0.1°. Since the ZString side can only pass standard format strings, "F1" is kept alongside it
        const string ANGLE_FORMAT = "0.0";
        const string DEGREE_SIGN = "°";
        const string REVOLUTION_SEPARATOR = "x ";
        const double FULL_TURN = 360.0;

        // Scale for holding the display key in units of 0.1°
        const double ANGLE_DISPLAY_SCALE = 10.0;

        // Near the rounding boundary (.05°), ToString's rounding and this judgment can disagree, so that band is excluded from caching
        const double ANGLE_KEY_SAFE_BAND = 0.5 - 1e-6;

#if TWEEQ_ZSTRING
        // ZString's AppendFormat requires composite format strings, so a standard-format version is kept separately
        const string ANGLE_BRACED_FORMAT = "{0:F1}";
#endif

        #endregion

        #region Specifiers

        // "F" + digits.ToString() allocates twice on every call.
        // Since this is on the every-frame path, every pattern is pre-built at startup
        static readonly string[] FixedSpecifiers = BuildFixedSpecifiers();

#if TWEEQ_ZSTRING
        static readonly string[] FixedBracedSpecifiers = BuildFixedBracedSpecifiers();
#endif

        static string[] BuildFixedSpecifiers()
        {
            string[] specifiers = new string[MAX_FORMAT_PRECISION + 1];
            for (int i = 0; i <= MAX_FORMAT_PRECISION; i++)
            {
                specifiers[i] = "F" + i.ToString(CultureInfo.InvariantCulture);
            }

            return specifiers;
        }

#if TWEEQ_ZSTRING
        static string[] BuildFixedBracedSpecifiers()
        {
            string[] specifiers = new string[MAX_FORMAT_PRECISION + 1];
            for (int i = 0; i <= MAX_FORMAT_PRECISION; i++)
            {
                specifiers[i] = "{0:F" + i.ToString(CultureInfo.InvariantCulture) + "}";
            }

            return specifiers;
        }
#endif

        /// <summary>Clamps the digit count to [0, MAX_FORMAT_PRECISION].</summary>
        public static int ClampDigits(int digits)
        {
            if (digits < 0)
            {
                return 0;
            }

            return digits > MAX_FORMAT_PRECISION ? MAX_FORMAT_PRECISION : digits;
        }

        /// <summary>Pre-built format specifiers "F0".."F15". The digit count is clamped.</summary>
        public static string FixedSpecifier(int digits)
        {
            return FixedSpecifiers[ClampDigits(digits)];
        }

        #endregion

        #region Number

        /// <summary>
        /// While tweaking, fixed-point with trailing zeroes kept; when idle, trailing zeroes and a trailing dot
        /// are trimmed and -0 is normalized to "0".
        /// </summary>
        public static string Format(double value, int precision, bool tweaking)
        {
            if (!TweeqMath.IsFinite(value))
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            int digits = ClampDigits(precision);

#if TWEEQ_ZSTRING
            // When ZString is present, this avoids the two-stage ToString → Substring allocation
            // by trimming on the buffer and converting to a string only once
            using (Utf16ValueStringBuilder builder = ZString.CreateStringBuilder(true))
            {
                builder.AppendFormat(FixedBracedSpecifiers[digits], value);
                ReadOnlySpan<char> span = builder.AsSpan();

                if (tweaking)
                {
                    // Since the digit count is itself the feedback for drag sensitivity, keep the raw digits
                    return span.ToString();
                }

                int end = TrimmedLength(span);
                return IsNegativeZero(span, end) ? "0" : span.Slice(0, end).ToString();
            }
#else
            string text = value.ToString(FixedSpecifiers[digits], CultureInfo.InvariantCulture);

            if (tweaking)
            {
                // Since the digit count is itself the feedback for drag sensitivity, keep the raw digits
                return text;
            }

            int end = TrimmedLength(text.AsSpan());
            if (IsNegativeZero(text.AsSpan(), end))
            {
                return "0";
            }

            return end == text.Length ? text : text.Substring(0, end);
#endif
        }

        // A regex would allocate every frame, so trimming is done with a manual scan
        static int TrimmedLength(ReadOnlySpan<char> text)
        {
            if (text.IndexOf('.') < 0)
            {
                return text.Length;
            }

            int end = text.Length;
            while (end > 0 && text[end - 1] == '0')
            {
                end--;
            }

            if (end > 0 && text[end - 1] == '.')
            {
                end--;
            }

            return end;
        }

        static bool IsNegativeZero(ReadOnlySpan<char> text, int end)
        {
            return end == 2 && text[0] == '-' && text[1] == '0';
        }

        #endregion

        #region Angle

        /// <summary>
        /// Below ±360 this is "0.0°"; at or above that, the revolution count is prefixed as "Nx 0.0°".
        /// </summary>
        public static string FormatAngle(double value)
        {
#if TWEEQ_ZSTRING
            using (Utf16ValueStringBuilder builder = ZString.CreateStringBuilder(true))
            {
                if (Math.Abs(value) < FULL_TURN)
                {
                    builder.AppendFormat(ANGLE_BRACED_FORMAT, value);
                    builder.Append(DEGREE_SIGN);
                    return builder.ToString();
                }

                long turns = (long)Math.Truncate(value / FULL_TURN);
                builder.Append(turns);
                builder.Append(REVOLUTION_SEPARATOR);
                builder.AppendFormat(ANGLE_BRACED_FORMAT, value - turns * FULL_TURN);
                builder.Append(DEGREE_SIGN);
                return builder.ToString();
            }
#else
            if (Math.Abs(value) < FULL_TURN)
            {
                return value.ToString(ANGLE_FORMAT, CultureInfo.InvariantCulture) + DEGREE_SIGN;
            }

            long revolutions = (long)Math.Truncate(value / FULL_TURN);
            double rotation = value - revolutions * FULL_TURN;
            return revolutions.ToString(CultureInfo.InvariantCulture)
                + REVOLUTION_SEPARATOR
                + rotation.ToString(ANGLE_FORMAT, CultureInfo.InvariantCulture)
                + DEGREE_SIGN;
#endif
        }

        /// <summary>
        /// Key for determining whether FormatAngle's result would match. Since the display is in units of 0.1°,
        /// the string doesn't need to be rebuilt as long as the key matches.
        /// Returns false for non-finite values and near a rounding boundary (i.e. always rebuild in those cases).
        /// revolutions is always 0 on the branch below ±360, so the branch difference is captured in the key too.
        /// tenths can differ in display between -0.0 and 0.0, so use SameValueBits when comparing it.
        /// </summary>
        public static bool TryGetAngleDisplayKey(double value, out long revolutions, out double tenths)
        {
            revolutions = 0L;
            tenths = 0.0;

            if (!TweeqMath.IsFinite(value))
            {
                return false;
            }

            double rotation = value;
            if (Math.Abs(value) >= FULL_TURN)
            {
                revolutions = (long)Math.Truncate(value / FULL_TURN);
                rotation = value - revolutions * FULL_TURN;
            }

            double scaled = rotation * ANGLE_DISPLAY_SCALE;
            tenths = Math.Round(scaled, MidpointRounding.AwayFromZero);
            return Math.Abs(scaled - tenths) < ANGLE_KEY_SAFE_BAND;
        }

        #endregion

        #region Keys

        /// <summary>
        /// Bitwise equality check. Mixing up -0 and 0, or two NaNs, would cause the display cache to misfire,
        /// so use this instead of ==.
        /// </summary>
        public static bool SameValueBits(double left, double right)
        {
            return BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);
        }

        #endregion
    }
}
