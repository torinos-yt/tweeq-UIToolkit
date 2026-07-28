using System.Globalization;

namespace Tweeq.Core
{
    /// <summary>
    /// 入力欄に打たれた式の評価。原典（tweeq）は置換後の文字列を JS の eval に渡すが、
    /// C# には等価物が無いので四則演算だけの再帰下降パーサへ縮小してある（意図的逸脱）。
    /// 対応: 数値リテラル・+ - * /・単項 +/-・括弧・空白。変数/関数/べき乗は非対応。
    /// </summary>
    public static class TweeqExpression
    {
        #region Constants

        // "((((..." のような入力でスタックを食い潰さないための上限。
        // 実用的な式の入れ子は数段なので、これを超えたら構文エラー扱いで十分
        const int MAX_DEPTH = 64;

        #endregion

        #region Evaluate

        /// <summary>
        /// 式を評価する。構文エラー・0 除算・非有限の結果は false（呼び出し側は現値維持）。
        /// </summary>
        public static bool TryEvaluate(string expression, out double result)
        {
            result = 0.0;

            if (string.IsNullOrEmpty(expression))
            {
                return false;
            }

            int index = 0;
            if (!TryParseSum(expression, ref index, 0, out double value))
            {
                return false;
            }

            SkipWhitespace(expression, ref index);
            if (index != expression.Length)
            {
                return false;
            }

            if (!TweeqMath.IsFinite(value))
            {
                return false;
            }

            result = TweeqMath.NormalizeZero(value);
            return true;
        }

        #endregion

        #region Grammar

        // sum := product (('+' | '-') product)*
        static bool TryParseSum(string text, ref int index, int depth, out double value)
        {
            if (!TryParseProduct(text, ref index, depth, out value))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    return true;
                }

                char op = text[index];
                if (op != '+' && op != '-')
                {
                    return true;
                }

                index++;
                if (!TryParseProduct(text, ref index, depth, out double right))
                {
                    return false;
                }

                value = op == '+' ? value + right : value - right;
            }
        }

        // product := unary (('*' | '/') unary)*
        static bool TryParseProduct(string text, ref int index, int depth, out double value)
        {
            if (!TryParseUnary(text, ref index, depth, out value))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    return true;
                }

                char op = text[index];
                if (op != '*' && op != '/')
                {
                    return true;
                }

                index++;
                if (!TryParseUnary(text, ref index, depth, out double right))
                {
                    return false;
                }

                if (op == '/' && right == 0.0)
                {
                    // JS なら Infinity になるが、値として使えないので構文エラーと同じ扱いにする
                    value = 0.0;
                    return false;
                }

                value = op == '*' ? value * right : value / right;
            }
        }

        // unary := ('+' | '-') unary | primary
        static bool TryParseUnary(string text, ref int index, int depth, out double value)
        {
            value = 0.0;

            if (depth >= MAX_DEPTH)
            {
                return false;
            }

            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                return false;
            }

            char sign = text[index];
            if (sign == '+' || sign == '-')
            {
                index++;
                if (!TryParseUnary(text, ref index, depth + 1, out double operand))
                {
                    return false;
                }

                value = sign == '-' ? -operand : operand;
                return true;
            }

            return TryParsePrimary(text, ref index, depth, out value);
        }

        // primary := number | '(' sum ')'
        static bool TryParsePrimary(string text, ref int index, int depth, out double value)
        {
            value = 0.0;

            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                return false;
            }

            if (text[index] == '(')
            {
                index++;
                if (!TryParseSum(text, ref index, depth + 1, out value))
                {
                    return false;
                }

                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ')')
                {
                    value = 0.0;
                    return false;
                }

                index++;
                return true;
            }

            return TryParseNumber(text, ref index, out value);
        }

        // number := digit* ('.' digit*) （数字が 1 文字以上あること。指数表記は非対応）
        static bool TryParseNumber(string text, ref int index, out double value)
        {
            value = 0.0;

            int start = index;
            int digits = 0;

            while (index < text.Length && text[index] >= '0' && text[index] <= '9')
            {
                index++;
                digits++;
            }

            if (index < text.Length && text[index] == '.')
            {
                index++;
                while (index < text.Length && text[index] >= '0' && text[index] <= '9')
                {
                    index++;
                    digits++;
                }
            }

            if (digits == 0)
            {
                index = start;
                return false;
            }

            // JS の "1." は 1。.NET の TryParse は末尾の小数点を通すとは限らないので落としておく
            int length = index - start;
            if (text[index - 1] == '.')
            {
                length--;
            }

            return double.TryParse(
                text.Substring(start, length),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value);
        }

        static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        #endregion
    }
}
