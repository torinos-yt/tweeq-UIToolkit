using System;
using System.Globalization;
#if TWEEQ_ZSTRING
using Cysharp.Text;
#endif

namespace Tweeq.Core
{
    /// <summary>
    /// 数値→文字列の唯一の窓口。スクラブ中の毎フレーム経路を通るので、
    /// 書式指定文字列の作り直しや中間文字列を持たない形に閉じ込めてある。
    /// ZString（com.cysharp.zstring）が入っていれば asmdef の versionDefines で
    /// TWEEQ_ZSTRING が立ち、中間アロケーションを削った実装に切り替わる。
    /// </summary>
    public static class TweeqFormat
    {
        #region Constants

        /// <summary>
        /// 書式指定に渡す小数桁数の上限。"F16" 以上は .NET の実装差が出やすいのでここで頭打ちにする。
        /// </summary>
        public const int MAX_FORMAT_PRECISION = 15;

        // 角度表示は 0.1° 固定。ZString 側は標準書式しか通せないので "F1" を併記してある
        const string ANGLE_FORMAT = "0.0";
        const string DEGREE_SIGN = "°";
        const string REVOLUTION_SEPARATOR = "x ";
        const double FULL_TURN = 360.0;

        // 表示キーを 0.1° 単位で持つためのスケール
        const double ANGLE_DISPLAY_SCALE = 10.0;

        // 丸めの境界（.05°）付近は ToString の丸めと判定がずれうるので、その帯はキャッシュ対象から外す
        const double ANGLE_KEY_SAFE_BAND = 0.5 - 1e-6;

#if TWEEQ_ZSTRING
        // ZString の AppendFormat は複合書式を要求するため、標準書式版を別に持つ
        const string ANGLE_BRACED_FORMAT = "{0:F1}";
#endif

        #endregion

        #region Specifiers

        // "F" + digits.ToString() は呼ぶたびに 2 回アロケーションする。
        // 毎フレーム経路なので全パターンを起動時に作り置きしておく
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

        /// <summary>桁数を [0, MAX_FORMAT_PRECISION] に丸める。</summary>
        public static int ClampDigits(int digits)
        {
            if (digits < 0)
            {
                return 0;
            }

            return digits > MAX_FORMAT_PRECISION ? MAX_FORMAT_PRECISION : digits;
        }

        /// <summary>"F0".."F15" の作り置き書式指定。桁数はクランプされる。</summary>
        public static string FixedSpecifier(int digits)
        {
            return FixedSpecifiers[ClampDigits(digits)];
        }

        #endregion

        #region Number

        /// <summary>
        /// tweaking 中は末尾ゼロを維持した固定小数、静止時は末尾ゼロ・末尾ドットをトリムし
        /// -0 を "0" に正規化する。
        /// </summary>
        public static string Format(double value, int precision, bool tweaking)
        {
            if (!TweeqMath.IsFinite(value))
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            int digits = ClampDigits(precision);

#if TWEEQ_ZSTRING
            // ZString があるときは ToString → Substring の 2 段アロケーションを避け、
            // バッファ上でトリムしてから 1 回だけ文字列化する
            using (Utf16ValueStringBuilder builder = ZString.CreateStringBuilder(true))
            {
                builder.AppendFormat(FixedBracedSpecifiers[digits], value);
                ReadOnlySpan<char> span = builder.AsSpan();

                if (tweaking)
                {
                    // 桁数がドラッグ感度のフィードバックそのものなので、生の桁を保つ
                    return span.ToString();
                }

                int end = TrimmedLength(span);
                return IsNegativeZero(span, end) ? "0" : span.Slice(0, end).ToString();
            }
#else
            string text = value.ToString(FixedSpecifiers[digits], CultureInfo.InvariantCulture);

            if (tweaking)
            {
                // 桁数がドラッグ感度のフィードバックそのものなので、生の桁を保つ
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

        // 正規表現だと毎フレームのアロケーションになるため手動スキャンでトリムする
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
        /// ±360 未満は "0.0°"、それ以上は回転数を前置して "Nx 0.0°"。
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
        /// FormatAngle の結果が一致するかを判定するためのキー。表示は 0.1° 単位なので、
        /// キーが一致する限り文字列を作り直さなくてよい。
        /// 非有限値と丸めの境界付近は false を返す（＝必ず作り直す）。
        /// revolutions は ±360 未満の枝では常に 0 になるので、枝の違いもキーに含まれる。
        /// tenths は -0.0 と 0.0 で表示が変わりうるため、比較には SameValueBits を使うこと。
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
        /// ビット単位の同値判定。-0 と 0、NaN 同士を取り違えると表示キャッシュが誤爆するので、
        /// == ではなくこちらを使う。
        /// </summary>
        public static bool SameValueBits(double left, double right)
        {
            return BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);
        }

        #endregion
    }
}
