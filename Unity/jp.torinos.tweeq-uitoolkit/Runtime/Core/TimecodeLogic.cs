using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tweeq.Core
{
    /// <summary>
    /// タイムコードの整形・解析・スクラブ量子化。UnityEngine 非依存・すべて double。
    /// 原典は tweeq/src/InputTime/utils.ts（整形・解析）と InputTime.vue（tweakSpeed / スナップ）。
    /// </summary>
    public static class TimecodeLogic
    {
        #region Constants

        /// <summary>tweak scale。原典 InputTime.vue の tweakScale と同じ 0..3。</summary>
        public const int SCALE_FRAMES = 0;
        public const int SCALE_SECONDS = 1;
        public const int SCALE_MINUTES = 2;
        public const int SCALE_HOURS = 3;

        const double SECONDS_PER_MINUTE = 60.0;
        const double SECONDS_PER_HOUR = 3600.0;

        // 原典の各正規表現。文字クラス内の '-' はエスケープ済みで、JS 版と同じ字面を保つ。
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

        // 単位サフィックスの判定は「末尾一致かつ直前が数字系」。原典 parseTimecode と同じ字面。
        static readonly Regex SecondsSuffixPattern = new Regex(@"[0-9+\-.]s(ec(ond)?s?)?$");
        static readonly Regex MinutesSuffixPattern = new Regex(@"[0-9+\-.]m(in(ute)?s?)?$");
        static readonly Regex HoursSuffixPattern = new Regex(@"[0-9+\-.]h((ou)?r)?s?$");

        #endregion

        #region Format

        /// <summary>
        /// "mm:ss:ff"、h&gt;0 のときだけ "h:mm:ss:ff"（h は 0 詰めしない）。負値は先頭に '-'。
        /// f は剰余そのままなので、小数フレームは "00:00:1.5" のように小数のまま出る（原典と同じ）。
        /// frameRate が 0・負・非有限のときはゼロ除算の文字列化を避けて "00:00:00" を返す
        /// （公演中の表示が例外や "∞" になるより無難なため。原典は NaN 混じりの文字列を出す）。
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

        // 原典の pad() は padStart(2,'0') なので、3 文字以上（小数フレーム等）はそのまま通す
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
        /// 原典 parseTimecode。"h:mm:ss:ff"（桁数任意）・単位サフィックス・裸のフレーム数を解釈する。
        /// 原典が null / NaN を返すケースは false（呼び出し側は現値維持）。
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

                // 原典は reverse() してから右端＝フレームとして重みを付ける
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

            // 裸の数値は原典が parseInt なので、小数フレームは 0 方向へ切り捨てられる
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
        /// 式中のタイムコードリテラルと単位サフィックスをフレーム数へ置換する（原典 replaceTimecodeWithFrames）。
        /// 置換順（コロン→f→s→m→h）は原典どおりで、これが単位の優先順位を決めている。
        /// 解釈できない部分は原典と同じく "0" に潰す。
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

        /// <summary>scale 1 単位あたりのフレーム数。0=1 / 1=fps / 2=fps*60 / 3 以上=fps*3600。</summary>
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
        /// 水平ドラッグ 1px あたりの増分（原典 InputTime.vue の tweakSpeed）。
        /// frames は fps に依らず固定 1/4px、秒・分は 1/10、時は 1/100 の粗さ。
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
        /// scale の単位境界へスナップする（余りは持たない版）。
        /// scale=0 は step=1、つまり連続値を整数フレームへ量子化する既定経路。
        /// </summary>
        public static double SnapToScale(double frames, int scale, double frameRate)
        {
            return Quantize(frames, UnitFrames(scale, frameRate), 0.0);
        }

        /// <summary>
        /// 原典方式のスナップ。offsetSource（Q 押下時の値）の単位内の余りを保持したまま
        /// 単位刻みで動かす。原典 tweakSnapParams の [step, model % step] に対応する。
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

        // linearly の scalar.quantize 相当。丸めは JS Math.round（=+∞ 方向）に合わせる必要がある：
        // frames スケールの速度が 1/4px なので、負側で .5 ちょうどの端数が日常的に発生する
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

        // 以下は原典が依存している JS 組み込みの挙動を必要な範囲だけ写したもの。
        // 文字列生成は確定時のみ通るので、素直な実装で足りる。

        /// <summary>JS の Math.round（.5 は +∞ 方向）。C# の既定は銀行家丸めなので使えない。</summary>
        static double JsRound(double value)
        {
            return Math.Floor(value + 0.5);
        }

        /// <summary>JS の Number()。空文字は 0、解釈できなければ NaN。</summary>
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

        /// <summary>JS の parseFloat()。先頭の数値部分だけを読む。</summary>
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

        /// <summary>JS の parseInt()。先頭の整数部分だけを読む（小数点以降は捨てる）。</summary>
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

        // 数字が 1 文字も無ければ 0 を返す（＝NaN 扱い）
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

                // "1." は JS でも 1。小数部が無いときは '.' を落として整数部だけ返す
                if (fractionDigits == 0)
                {
                    index = fractionStart;
                }

                digits += fractionDigits;
            }

            return digits == 0 ? 0 : index;
        }

        /// <summary>
        /// JS の String(number)。整数は小数点を付けない。
        /// 1e21 以上の指数表記は原典と字面が揃わないが、フレーム数の実用域から外れるため許容する。
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
