using NUnit.Framework;
using Tweeq.Core;

namespace Tweeq.Core.Tests
{
    public class TweeqExpressionTests
    {
        const double TOLERANCE = 1e-9;

        static double Evaluate(string expression)
        {
            Assert.That(TweeqExpression.TryEvaluate(expression, out double result), Is.True,
                "failed to evaluate: " + expression);
            return result;
        }

        static void AssertRejected(string expression)
        {
            Assert.That(TweeqExpression.TryEvaluate(expression, out double result), Is.False,
                "unexpectedly evaluated: " + expression);
            Assert.That(result, Is.EqualTo(0.0), "result must stay untouched on failure");
        }

        #region Literals

        [Test]
        public void EvaluatesPlainNumbers()
        {
            Assert.That(Evaluate("0"), Is.EqualTo(0.0).Within(TOLERANCE));
            Assert.That(Evaluate("42"), Is.EqualTo(42.0).Within(TOLERANCE));
            Assert.That(Evaluate("1.5"), Is.EqualTo(1.5).Within(TOLERANCE));
            Assert.That(Evaluate(".5"), Is.EqualTo(0.5).Within(TOLERANCE));
            Assert.That(Evaluate("1."), Is.EqualTo(1.0).Within(TOLERANCE));
        }

        [Test]
        public void IgnoresSurroundingWhitespace()
        {
            Assert.That(Evaluate("  12  "), Is.EqualTo(12.0).Within(TOLERANCE));
            Assert.That(Evaluate("\t1 +\n2 "), Is.EqualTo(3.0).Within(TOLERANCE));
        }

        #endregion

        #region Arithmetic

        [Test]
        public void EvaluatesTheFourOperators()
        {
            Assert.That(Evaluate("1 + 2"), Is.EqualTo(3.0).Within(TOLERANCE));
            Assert.That(Evaluate("5 - 8"), Is.EqualTo(-3.0).Within(TOLERANCE));
            Assert.That(Evaluate("6 * 7"), Is.EqualTo(42.0).Within(TOLERANCE));
            Assert.That(Evaluate("7 / 2"), Is.EqualTo(3.5).Within(TOLERANCE));
        }

        [Test]
        public void MultiplicationBindsTighterThanAddition()
        {
            Assert.That(Evaluate("1 + 2 * 3"), Is.EqualTo(7.0).Within(TOLERANCE));
            Assert.That(Evaluate("2 * 3 + 1"), Is.EqualTo(7.0).Within(TOLERANCE));
            Assert.That(Evaluate("1 - 6 / 3"), Is.EqualTo(-1.0).Within(TOLERANCE));
        }

        [Test]
        public void OperatorsAreLeftAssociative()
        {
            Assert.That(Evaluate("8 - 3 - 2"), Is.EqualTo(3.0).Within(TOLERANCE));
            Assert.That(Evaluate("8 / 2 / 2"), Is.EqualTo(2.0).Within(TOLERANCE));
        }

        [Test]
        public void ParenthesesOverridePrecedence()
        {
            Assert.That(Evaluate("(1 + 2) * 3"), Is.EqualTo(9.0).Within(TOLERANCE));
            Assert.That(Evaluate("((1 + 2)) * (3 - 1)"), Is.EqualTo(6.0).Within(TOLERANCE));
            Assert.That(Evaluate("2 * (3 + (4 - 1) * 2)"), Is.EqualTo(18.0).Within(TOLERANCE));
        }

        [Test]
        public void HandlesUnarySigns()
        {
            Assert.That(Evaluate("-5"), Is.EqualTo(-5.0).Within(TOLERANCE));
            Assert.That(Evaluate("+4"), Is.EqualTo(4.0).Within(TOLERANCE));
            Assert.That(Evaluate("-(2 + 3)"), Is.EqualTo(-5.0).Within(TOLERANCE));
            Assert.That(Evaluate("2 * -3"), Is.EqualTo(-6.0).Within(TOLERANCE));
            Assert.That(Evaluate("2 - -3"), Is.EqualTo(5.0).Within(TOLERANCE));
            Assert.That(Evaluate("--3"), Is.EqualTo(3.0).Within(TOLERANCE));
            Assert.That(Evaluate("-0"), Is.EqualTo(0.0).Within(TOLERANCE));
        }

        #endregion

        #region Errors

        [Test]
        public void RejectsEmptyInput()
        {
            AssertRejected(null);
            AssertRejected("");
            AssertRejected("   ");
        }

        [Test]
        public void RejectsSyntaxErrors()
        {
            AssertRejected("1 +");
            AssertRejected("* 2");
            AssertRejected("(1 + 2");
            AssertRejected("1 + 2)");
            AssertRejected("()");
            AssertRejected("1 2");
            AssertRejected("1..2");
            AssertRejected(".");
        }

        [Test]
        public void RejectsUnsupportedGrammar()
        {
            // 意図的逸脱: 変数・関数・べき乗・指数表記は文法に無い
            AssertRejected("x * 2");
            AssertRejected("i + 1");
            AssertRejected("fps");
            AssertRejected("max(1, 2)");
            AssertRejected("2 ^ 3");
            AssertRejected("2 ** 3");
            AssertRejected("1e3");
            AssertRejected("1 % 2");
        }

        [Test]
        public void RejectsDivisionByZero()
        {
            AssertRejected("1 / 0");
            AssertRejected("1 / (2 - 2)");
            AssertRejected("0 / 0");
        }

        [Test]
        public void RejectsNonFiniteResults()
        {
            string huge = "1" + new string('0', 308);
            Assert.That(Evaluate(huge), Is.GreaterThan(0.0));
            AssertRejected(huge + " * 10");
        }

        [Test]
        public void RejectsRunawayNesting()
        {
            // スタックを守るための深さ制限。実用的な入れ子は通る
            string shallow = new string('(', 10) + "1" + new string(')', 10);
            Assert.That(Evaluate(shallow), Is.EqualTo(1.0).Within(TOLERANCE));

            string deep = new string('(', 200) + "1" + new string(')', 200);
            AssertRejected(deep);
        }

        #endregion

        #region Timecode integration

        [Test]
        public void EvaluatesReplacedTimecodeExpressions()
        {
            string[] sources = {"1:00 + 10f", "(2s - 12f) * 2", "-1:00 + 3s"};
            double[] expected = {34.0, 72.0, 48.0};

            for (int i = 0; i < sources.Length; i++)
            {
                string code = TimecodeLogic.ReplaceTimecodeWithFrames(sources[i], 24.0);
                Assert.That(TweeqExpression.TryEvaluate(code, out double value), Is.True,
                    sources[i] + " -> " + code);
                Assert.That(value, Is.EqualTo(expected[i]).Within(TOLERANCE), sources[i]);
            }
        }

        #endregion
    }
}
