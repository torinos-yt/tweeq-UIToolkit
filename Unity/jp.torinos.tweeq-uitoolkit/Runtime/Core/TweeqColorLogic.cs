using System;

namespace Tweeq.Core
{
    #region Data

    /// <summary>色相・彩度・明度・不透明度。UnityEngine 非依存・すべて double。</summary>
    /// <remarks>
    /// 原典（Vue / egui）は h を 0〜1 で持つが、ここは 0〜360 の度数にしてある。
    /// ピッカーの H フィールドと Hue スライダーが度数表示で、境界で 1/360 を掛け直すより
    /// 内部を度数に揃えたほうが丸め誤差の入る箇所が減るため。
    /// </remarks>
    public struct Hsva
    {
        /// <summary>色相（度）。[0, 360)。</summary>
        public double H;

        /// <summary>彩度。[0, 1]。</summary>
        public double S;

        /// <summary>明度。[0, 1]。</summary>
        public double V;

        /// <summary>不透明度。[0, 1]。</summary>
        public double A;

        public Hsva(double h, double s, double v, double a)
        {
            H = h;
            S = s;
            V = v;
            A = a;
        }
    }

    /// <summary>ストレート（非乗算済み）アルファの RGBA。各成分 [0, 1]。</summary>
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
    /// 色空間変換と HEX の相互変換。純関数のみで、UnityEngine 非依存・すべて double。
    /// </summary>
    public static class TweeqColorLogic
    {
        #region Constants

        const double FULL_TURN = 360.0;
        const double SEXTANT = 60.0;
        const double BYTE_SCALE = 255.0;

        // '#' + RRGGBBAA。FormatHex はこの長さのスタックバッファ 1 枚で完結させる
        const int HEX_MAX_LENGTH = 9;
        const int HEX_OPAQUE_LENGTH = 7;

        // Vue の chroma 出力に合わせて小文字。テーブル引きなので分岐も加算も要らない
        const string HEX_DIGITS = "0123456789abcdef";

        #endregion

        #region Conversion

        /// <summary>HSVA → RGBA。h は自動で [0, 360) に、s/v/a は [0, 1] に丸め込む。</summary>
        public static Rgba HsvaToRgba(Hsva hsva)
        {
            // 0〜6 の連続量（sextant）に落としてから整数部で枝分かれする。原典と同じ形
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
        /// RGBA → HSVA。無彩色（灰色・黒・白）は色相が定義できないので 0 を返す。
        /// </summary>
        public static Hsva RgbaToHsva(Rgba rgba)
        {
            return RgbaToHsva(rgba, default(Hsva));
        }

        /// <summary>
        /// RGBA → HSVA。無彩色のとき previous の色相を、明度 0 のとき previous の彩度を引き継ぐ。
        /// </summary>
        /// <remarks>
        /// 原典は色相・彩度が定義できない場合に NaN を返し、呼び出し側で直前の値に差し戻している。
        /// NaN を持ち回るとクランプや比較のたびに漏れるので、引き継ぎ元を引数で受け取る形にした。
        /// SV パッドを下端（v=0）まで引いても色相が黒に飲まれないのはこの経路。
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
            // 完全一致（delta == 0）ではなく機械イプシロンで切るのは、非正規化数レベルの
            // delta で割ると色相が無限大に飛ぶため。その領域の色相はどのみち意味を持たない
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

                // sector は [0, 6) に収まるので、ここで剰余を取り直すと丸め誤差が増えるだけ
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
        /// #RGB / #RRGGBB / #RRGGBBAA を解釈する。'#' は省略可、前後の空白は無視。
        /// 失敗時は false を返し、rgba には不透明な黒を入れる。
        /// </summary>
        public static bool TryParseHex(string text, out Rgba rgba)
        {
            // AsSpan() は null でも空スパンを返すので、null チェックはここで完結する
            return TryParseHex(text.AsSpan(), out rgba);
        }

        /// <summary>
        /// <see cref="TryParseHex(string, out Rgba)"/> のスパン版。
        /// テキストフィールドの編集中に部分文字列を切り出さずに判定するための入口。
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
                // #RGB は 1 桁を 2 桁へ複製する（0xA → 0xAA）。×17 がその複製にあたる
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
        /// 小文字の HEX 文字列。α が 255 未満なら 8 桁、それ以外は 6 桁。
        /// </summary>
        /// <remarks>
        /// 桁数の判定を「α&lt;1」ではなく量子化後の 255 未満で行うのは、
        /// FormatHex → TryParseHex → FormatHex が同じ文字列に戻る（冪等になる）ようにするため。
        /// α=0.999 を 8 桁にすると "…ff" と書いて読み戻すと 6 桁になり、HEX 欄が編集のたびに揺れる。
        /// </remarks>
        public static string FormatHex(Rgba rgba)
        {
            int red = ToByte(rgba.R);
            int green = ToByte(rgba.G);
            int blue = ToByte(rgba.B);
            int alpha = ToByte(rgba.A);

            // 中間文字列を作らずスタック上で組み立て、string 化は最後の 1 回だけ
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

        /// <summary>[0, 1] のチャンネルを 0〜255 へ。丸めは TweeqMath.Quantize と同じ AwayFromZero。</summary>
        public static int ToByte(double channel)
        {
            return (int)Math.Round(Clamp01(channel) * BYTE_SCALE, MidpointRounding.AwayFromZero);
        }

        /// <summary>0〜255 を [0, 1] のチャンネルへ。範囲外は飽和させる。</summary>
        public static double FromByte(int value)
        {
            if (value <= 0)
            {
                return 0.0;
            }

            return value >= 255 ? 1.0 : value / BYTE_SCALE;
        }

        /// <summary>色相を [0, 360) へ。非有限値は 0（＝赤）に倒す。</summary>
        public static double NormalizeHue(double hue)
        {
            if (!TweeqMath.IsFinite(hue))
            {
                return 0.0;
            }

            return TweeqMath.UnsignedMod(hue, FULL_TURN);
        }

        // NaN は「無彩色」の印として原典から流れてくることがあるので 0 に倒す。
        // ±∞ は Clamp に任せれば 1 / 0 に飽和する
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
