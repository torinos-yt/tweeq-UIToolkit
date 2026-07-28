using System.Collections.Generic;
using NUnit.Framework;

namespace Tweeq.Core.Tests
{
    /// <summary>
    /// Verifies FuzzySearch's match conditions and ranking (the FuzzySearch entry of m6-wave2-spec.md's "test contracts").
    ///
    /// The absolute score value is never looked at, since that's an implementation detail. What's
    /// checked is only "pass/drop" and the relative ranking of "prefix &gt; word-start &gt; contiguous &gt; sparse."
    /// </summary>
    public class FuzzySearchTests
    {
        // 4 entries that differ only in match kind. Leading position, word-startness, and contiguity are each peeled off one at a time.
        static readonly string[] Kinds = { "abc", "x ab", "xab", "xaxb" };

        static List<int> Filter(string query, params string[] labels)
        {
            List<int> results = new List<int>();
            FuzzySearch.Filter(query, labels, results);
            return results;
        }

        #region Subsequence

        [Test]
        public void OnlySubsequenceMatchesPass()
        {
            // "ba" is dropped since it can't be traced in the a->b order (typo recovery is not performed).
            Assert.AreEqual(new[] { 0 }, Filter("ab", "ab", "ba").ToArray());
        }

        [Test]
        public void MissingCharacterDropsTheCandidate()
        {
            Assert.AreEqual(0, Filter("abc", "ab").Count);
        }

        [Test]
        public void GapsAreAllowed()
        {
            Assert.AreEqual(new[] { 0 }, Filter("ab", "a-x-b").ToArray());
        }

        [Test]
        public void QueryLongerThanTheLabelNeverMatches()
        {
            Assert.AreEqual(0, Filter("abcd", "abc").Count);
        }

        #endregion

        #region Ranking

        [Test]
        public void PrefixBeatsWordStartBeatsConsecutiveBeatsSparse()
        {
            List<int> results = Filter("ab", Kinds);

            Assert.AreEqual(new[] { 0, 1, 2, 3 }, results.ToArray());
        }

        [Test]
        public void PrefixBeatsWordStart()
        {
            Assert.IsTrue(FuzzySearch.TryScore("ab", "abc", out int prefix));
            Assert.IsTrue(FuzzySearch.TryScore("ab", "x ab", out int wordStart));

            Assert.Greater(prefix, wordStart);
        }

        [Test]
        public void WordStartBeatsConsecutive()
        {
            Assert.IsTrue(FuzzySearch.TryScore("ab", "x ab", out int wordStart));
            Assert.IsTrue(FuzzySearch.TryScore("ab", "xab", out int consecutive));

            Assert.Greater(wordStart, consecutive);
        }

        [Test]
        public void ConsecutiveBeatsSparse()
        {
            Assert.IsTrue(FuzzySearch.TryScore("ab", "xab", out int consecutive));
            Assert.IsTrue(FuzzySearch.TryScore("ab", "xaxb", out int sparse));

            Assert.Greater(consecutive, sparse);
        }

        [Test]
        public void CamelCaseBoundaryCountsAsWordStart()
        {
            Assert.IsTrue(FuzzySearch.TryScore("in", "easeIn", out int camel));
            Assert.IsTrue(FuzzySearch.TryScore("in", "easein", out int flat));

            Assert.Greater(camel, flat);
        }

        [Test]
        public void SeparatorsCountAsWordStart()
        {
            // Any character treated as a separator produces a word-start bonus.
            Assert.IsTrue(FuzzySearch.TryScore("ab", "xab", out int plain));

            foreach (string label in new[] { "x ab", "x_ab", "x-ab", "x/ab", "x.ab", "x:ab" })
            {
                Assert.IsTrue(FuzzySearch.TryScore("ab", label, out int separated), label);
                Assert.Greater(separated, plain, label);
            }
        }

        #endregion

        #region Case

        [Test]
        public void MatchingIgnoresCase()
        {
            Assert.AreEqual(new[] { 0, 1 }, Filter("EASE", "Ease In", "ease out").ToArray());
            Assert.AreEqual(new[] { 0 }, Filter("ease", "EASE IN").ToArray());
        }

        [Test]
        public void CaseDoesNotChangeTheScore()
        {
            Assert.IsTrue(FuzzySearch.TryScore("AB", "abc", out int upperQuery));
            Assert.IsTrue(FuzzySearch.TryScore("ab", "ABC", out int upperLabel));

            Assert.AreEqual(upperQuery, upperLabel);
        }

        #endregion

        #region Stability

        [Test]
        public void TiesKeepTheOriginalOrder()
        {
            Assert.AreEqual(new[] { 0, 1, 2 }, Filter("ab", "ab", "ab", "ab").ToArray());
        }

        [Test]
        public void TiesKeepTheOriginalOrderWhenInterleavedWithBetterMatches()
        {
            // The tied "x ab" entries keep their original order; only the prefix match "abc" moves ahead.
            Assert.AreEqual(
                new[] { 2, 0, 1 },
                Filter("ab", "x ab", "x ab", "abc").ToArray());
        }

        #endregion

        #region Empty query

        [Test]
        public void EmptyQueryReturnsEveryIndexInOrder()
        {
            Assert.AreEqual(new[] { 0, 1, 2 }, Filter(string.Empty, "b", "a", "c").ToArray());
        }

        [Test]
        public void NullQueryIsTreatedAsEmpty()
        {
            Assert.AreEqual(new[] { 0, 1 }, Filter(null, "a", "b").ToArray());
        }

        [Test]
        public void EmptyQueryScoresZero()
        {
            Assert.IsTrue(FuzzySearch.TryScore(string.Empty, "anything", out int score));
            Assert.AreEqual(0, score);
        }

        #endregion

        #region Boundaries

        [Test]
        public void ResultsAreClearedBeforeFilling()
        {
            List<int> results = new List<int> { 99, 98 };

            FuzzySearch.Filter("ab", new[] { "abc" }, results);

            Assert.AreEqual(new[] { 0 }, results.ToArray());
        }

        [Test]
        public void ReusedListDoesNotLeakBetweenQueries()
        {
            List<int> results = new List<int>();
            string[] labels = { "abc", "zzz" };

            FuzzySearch.Filter("ab", labels, results);
            FuzzySearch.Filter("zz", labels, results);

            Assert.AreEqual(new[] { 1 }, results.ToArray());
        }

        [Test]
        public void NullLabelsClearTheResults()
        {
            List<int> results = new List<int> { 7 };

            Assert.DoesNotThrow(() => FuzzySearch.Filter("ab", null, results));
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void NullResultsIsIgnored()
        {
            Assert.DoesNotThrow(() => FuzzySearch.Filter("ab", new[] { "abc" }, null));
        }

        [Test]
        public void NullEntriesNeverMatch()
        {
            Assert.AreEqual(new[] { 1 }, Filter("ab", null, "abc").ToArray());
            Assert.IsFalse(FuzzySearch.TryScore("ab", null, out _));
        }

        [Test]
        public void NullEntriesAreStillCountedByAnEmptyQuery()
        {
            // An empty query means "no filtering," so every entry is returned regardless of content.
            Assert.AreEqual(new[] { 0, 1 }, Filter(string.Empty, null, "abc").ToArray());
        }

        [Test]
        public void EmptyLabelListReturnsNothing()
        {
            Assert.AreEqual(0, Filter("ab").Count);
        }

        #endregion
    }
}
