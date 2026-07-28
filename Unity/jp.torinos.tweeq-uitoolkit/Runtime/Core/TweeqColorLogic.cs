using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>Hue, saturation, value, and alpha. No UnityEngine dependency; all double.</summary>
    /// <remarks>
    /// The Vue original (and another reference implementation) hold h in the 0-1 range, but here it's kept in degrees, 0-360.
    /// The picker's H field and Hue slider both display degrees, and keeping the internal representation in degrees
    /// leaves fewer places where rounding error can creep in than re-multiplying by 1/360 at the boundary.
    /// </remarks>
    public struct Hsva
    {
        /// <summary>Hue (degrees). [0, 360).</summary>
        public double H;

        /// <summary>Saturation. [0, 1].</summary>
        public double S;

        /// <summary>Value. [0, 1].</summary>
        public double V;

        /// <summary>Alpha. [0, 1].</summary>
        public double A;

        public Hsva(double h, double s, double v, double a)
        {
            H = h;
            S = s;
            V = v;
            A = a;
        }
    }

    /// <summary>RGBA with straight (non-premultiplied) alpha. Each channel [0, 1].</summary>
    public struct Rgba
    {
        public double R;

        public double G;

        public double B;

        public double A;

        public Rgba(double r, double g, double b, double a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    #endregion

    /// <summary>
    /// Color space conversion and HEX interconversion. Pure functions only; no UnityEngine dependency; all double.
    /// </summary>
    public static class TweeqColorLogic
    {
        #region Constants

        const double FULL_TURN = 360.0;
        const double SEXTANT = 60.0;
        const double BYTE_SCALE = 255.0;

        // '#' + RRGGBBAA. FormatHex builds within a single stack buffer of this length.
        const int HEX_MAX_LENGTH = 9;
        const int HEX_OPAQUE_LENGTH = 7;

        // Lowercase to match Vue's chroma output. Table lookup, so no branching or addition needed.
        const string HEX_DIGITS = "0123456789abcdef";

        #endregion

        #region Conversion

        /// <summary>HSVA to RGBA. h is auto-normalized to [0, 360); s/v/a are clamped to [0, 1].</summary>
        public static Rgba HsvaToRgba(Hsva hsva)
        {
            // Reduce to a continuous 0-6 quantity (sextant), then branch on its integer part. Same shape as the original.
            double sextant = NormalizeHue(hsva.H) / SEXTANT;
            double saturation = Clamp01(hsva.S);
            double value = Clamp01(hsva.V);

            double chroma = value * saturation;
            double secondary = chroma * (1.0 - Math.Abs(TweeqMath.UnsignedMod(sextant, 2.0) - 1.0));
            double matchValue = value - chroma;

            double red;
            double green;
            double blue;
            switch ((int)sextant)
            {
                case 0:
                    red = chroma;
                    green = secondary;
                    blue = 0.0;
                    break;
                case 1:
                    red = secondary;
                    green = chroma;
                    blue = 0.0;
                    break;
                case 2:
                    red = 0.0;
                    green = chroma;
                    blue = secondary;
                    break;
                case 3:
                    red = 0.0;
                    green = secondary;
                    blue = chroma;
                    break;
                case 4:
                    red = secondary;
                    green = 0.0;
                    blue = chroma;
                    break;
                default:
                    red = chroma;
                    green = 0.0;
                    blue = secondary;
                    break;
            }

            return new Rgba(
                red + matchValue, green + matchValue, blue + matchValue, Clamp01(hsva.A));
        }

        /// <summary>
        /// RGBA to HSVA. For achromatic colors (gray, black, white) hue can't be defined, so 0 is returned.
        /// </summary>
        public static Hsva RgbaToHsva(Rgba rgba)
        {
            return RgbaToHsva(rgba, default(Hsva));
        }

        /// <summary>
        /// RGBA to HSVA. Carries over previous's hue when achromatic, and previous's saturation when value is 0.
        /// </summary>
        /// <remarks>
        /// The original returns NaN when hue/saturation can't be defined, and the caller falls back to the previous value.
        /// Carrying NaN around would leak through every clamp and comparison, so this instead takes the carry-over source as an argument.
        /// This is also the path by which dragging the SV pad down to its bottom edge (v=0) doesn't let hue get swallowed by black.
        /// </remarks>
        public static Hsva RgbaToHsva(Rgba rgba, Hsva previous)
        {
            double red = Clamp01(rgba.R);
            double green = Clamp01(rgba.G);
            double blue = Clamp01(rgba.B);

            double maximum = Math.Max(red, Math.Max(green, blue));
            double minimum = Math.Min(red, Math.Min(green, blue));
            double delta = maximum - minimum;

            double hue;
            // Cutting off at machine epsilon rather than exact equality (delta == 0) is because dividing by a delta
            // at the denormal level would send hue flying off to infinity. Hue in that region is meaningless anyway.
            if (delta <= TweeqMath.MACHINE_EPSILON)
            {
                hue = NormalizeHue(previous.H);
            }
            else
            {
                double sector;
                if (maximum == red)
                {
                    sector = (green - blue) / delta;
                    if (sector < 0.0)
                    {
                        sector += 6.0;
                    }
                }
                else if (maximum == green)
                {
                    sector = (blue - red) / delta + 2.0;
                }
                else
                {
                    sector = (red - green) / delta + 4.0;
                }

                // sector already fits within [0, 6), so taking the remainder again here would only add rounding error.
                hue = sector * SEXTANT;
            }

            double saturation = maximum <= TweeqMath.MACHINE_EPSILON
                ? Clamp01(previous.S)
                : delta / maximum;

            return new Hsva(hue, saturation, maximum, Clamp01(rgba.A));
        }

        #endregion

        #region Hex

        /// <summary>
        /// Parses #RGB / #RRGGBB / #RRGGBBAA. The '#' is optional; leading/trailing whitespace is ignored.
        /// On failure returns false and sets rgba to opaque black.
        /// </summary>
        public static bool TryParseHex(string text, out Rgba rgba)
        {
            // AsSpan() returns an empty span for null too, so the null check is fully handled here.
            return TryParseHex(text.AsSpan(), out rgba);
        }

        /// <summary>
        /// Span version of <see cref="TryParseHex(string, out Rgba)"/>.
        /// An entry point for validating during text field editing without slicing out a substring.
        /// </summary>
        public static bool TryParseHex(ReadOnlySpan<char> text, out Rgba rgba)
        {
            rgba = new Rgba(0.0, 0.0, 0.0, 1.0);

            ReadOnlySpan<char> body = text.Trim();
            if (body.Length > 0 && body[0] == '#')
            {
                body = body.Slice(1);
            }

            int red;
            int green;
            int blue;
            int alpha = 255;

            if (body.Length == 3)
            {
                // #RGB duplicates each 1-digit value into 2 digits (0xA -> 0xAA). Multiplying by 17 performs that duplication.
                if (!TryReadDigit(body, 0, out red)
                    || !TryReadDigit(body, 1, out green)
                    || !TryReadDigit(body, 2, out blue))
                {
                    return false;
                }

                red *= 17;
                green *= 17;
                blue *= 17;
            }
            else if (body.Length == 6 || body.Length == 8)
            {
                if (!TryReadPair(body, 0, out red)
                    || !TryReadPair(body, 2, out green)
                    || !TryReadPair(body, 4, out blue))
                {
                    return false;
                }

                if (body.Length == 8 && !TryReadPair(body, 6, out alpha))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            rgba = new Rgba(FromByte(red), FromByte(green), FromByte(blue), FromByte(alpha));
            return true;
        }

        /// <summary>
        /// Lowercase HEX string. 8 digits if alpha is less than 255, otherwise 6 digits.
        /// </summary>
        /// <remarks>
        /// The digit-count decision is based on "less than 255 after quantization" rather than "alpha &lt; 1"
        /// so that FormatHex -> TryParseHex -> FormatHex returns to the same string (i.e. is idempotent).
        /// If alpha=0.999 produced 8 digits, it would write "...ff" and read back as 6 digits, making the HEX field wobble on every edit.
        /// </remarks>
        public static string FormatHex(Rgba rgba)
        {
            int red = ToByte(rgba.R);
            int green = ToByte(rgba.G);
            int blue = ToByte(rgba.B);
            int alpha = ToByte(rgba.A);

            // Assemble on the stack without creating an intermediate string; string conversion happens only once, at the end.
            Span<char> buffer = stackalloc char[HEX_MAX_LENGTH];
            buffer[0] = '#';
            WritePair(buffer, 1, red);
            WritePair(buffer, 3, green);
            WritePair(buffer, 5, blue);

            if (alpha >= 255)
            {
                return buffer.Slice(0, HEX_OPAQUE_LENGTH).ToString();
            }

            WritePair(buffer, 7, alpha);
            return buffer.ToString();
        }

        static void WritePair(Span<char> buffer, int index, int value)
        {
            buffer[index] = HEX_DIGITS[(value >> 4) & 0xF];
            buffer[index + 1] = HEX_DIGITS[value & 0xF];
        }

        static bool TryReadPair(ReadOnlySpan<char> text, int index, out int value)
        {
            value = 0;
            if (!TryReadDigit(text, index, out int high) || !TryReadDigit(text, index + 1, out int low))
            {
                return false;
            }

            value = (high << 4) | low;
            return true;
        }

        static bool TryReadDigit(ReadOnlySpan<char> text, int index, out int value)
        {
            char c = text[index];

            if (c >= '0' && c <= '9')
            {
                value = c - '0';
                return true;
            }

            if (c >= 'a' && c <= 'f')
            {
                value = c - 'a' + 10;
                return true;
            }

            if (c >= 'A' && c <= 'F')
            {
                value = c - 'A' + 10;
                return true;
            }

            value = 0;
            return false;
        }

        #endregion

        #region Channels

        /// <summary>Converts a [0, 1] channel to 0-255. Rounding uses AwayFromZero, same as TweeqMath.Quantize.</summary>
        public static int ToByte(double channel)
        {
            return (int)Math.Round(Clamp01(channel) * BYTE_SCALE, MidpointRounding.AwayFromZero);
        }

        /// <summary>Converts 0-255 to a [0, 1] channel. Out-of-range values are saturated.</summary>
        public static double FromByte(int value)
        {
            if (value <= 0)
            {
                return 0.0;
            }

            return value >= 255 ? 1.0 : value / BYTE_SCALE;
        }

        /// <summary>Normalizes hue to [0, 360). Non-finite values fall back to 0 (= red).</summary>
        public static double NormalizeHue(double hue)
        {
            if (!TweeqMath.IsFinite(hue))
            {
                return 0.0;
            }

            return TweeqMath.UnsignedMod(hue, FULL_TURN);
        }

        // NaN can flow in from the original as a marker of "achromatic," so it falls back to 0.
        // For ±infinity, leaving it to Clamp saturates it to 1 / 0.
        static double Clamp01(double value)
        {
            if (double.IsNaN(value))
            {
                return 0.0;
            }

            return TweeqMath.Clamp(value, 0.0, 1.0);
        }

        #endregion
    }
}
