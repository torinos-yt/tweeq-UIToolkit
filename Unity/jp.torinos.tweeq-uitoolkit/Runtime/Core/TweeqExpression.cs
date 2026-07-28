using System.Globalization;

namespace Tweeq.Core
{
    /// <summary>
    /// Evaluates an expression typed into an input field. The original (tweeq) passes the substituted string to JS's eval,
    /// but since C# has no equivalent, this is scaled down to a recursive-descent parser handling only the four arithmetic operations (an intentional deviation).
    /// Supported: numeric literals, + - * /, unary +/-, parentheses, whitespace. Not supported: variables/functions/exponentiation.
    /// </summary>
    public static class TweeqExpression
    {
        #region Constants

        // Upper bound so input like "((((..." can't exhaust the stack.
        // Practical expressions only nest a few levels deep, so treating anything beyond this as a syntax error is sufficient.
        const int MAX_DEPTH = 64;

        #endregion

        #region Evaluate

        /// <summary>
        /// Evaluates an expression. Syntax errors, division by zero, and non-finite results all return false (the caller keeps the current value).
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
                    // In JS this would become Infinity, but since it can't be used as a value, treat it the same as a syntax error.
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

        // number := digit* ('.' digit*) (at least one digit required; exponent notation not supported)
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

            // In JS, "1." is 1. .NET's TryParse doesn't reliably accept a trailing decimal point, so it's dropped here.
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
