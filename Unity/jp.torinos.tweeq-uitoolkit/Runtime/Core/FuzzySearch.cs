using System;
using System.Collections.Generic;

namespace Tweeq.Core
{
    /// <summary>
    /// A simple fuzzy search used to filter DropdownInput. No UnityEngine dependency.
    ///
    /// The Vue original (InputDropdown) uses fast-fuzzy's search(), which runs Sellers' algorithm's
    /// edit distance against every candidate — an implementation that allocates a DP table per candidate
    /// in exchange for typo tolerance. This version instead prioritizes "never allocating even when every
    /// candidate is rescanned on every keystroke", replacing it with a weighted scorer that only passes
    /// subsequence matches (an intentional deviation, m6-wave2-spec.md §B). Because of this, rankings don't
    /// match fast-fuzzy's. Specifically, typos (an extra character, a swap) are never rescued here.
    ///
    /// The internal buffer is reused as a static, so this must only be called from the UI thread.
    /// </summary>
    public static class FuzzySearch
    {
        #region Constants

        // The raw points awarded per matching character. This only ever gets compared against scores from
        // the same query, so it doesn't affect ranking by itself — it just sets the baseline scale for the
        // bonuses/penalties below
        const int MATCH_SCORE = 16;

        // Adjacent to the previous match (a consecutive match)
        const int CONSECUTIVE_SCORE = 16;

        // A word-start match (the very start, right after a separator, or a camelCase boundary)
        const int WORD_START_SCORE = 24;

        // The entire query matched consecutively from the start = a prefix match.
        // Sized a full step above the others so that "a prefix match always beats a word-start match" holds
        // without having to stare at the formula
        const int PREFIX_SCORE = 64;

        // The gap for a skipped leading section and for gaps between matches. Left uncapped it would unfairly
        // sink long labels, so it's given a ceiling
        const int LEADING_PENALTY = 1;
        const int MAX_LEADING_SKIP = 8;
        const int GAP_PENALTY = 2;
        const int MAX_GAP = 4;

        #endregion

        #region Fields

        // Scores are kept as "options index -> score". Since the sort comparer only ever looks this up,
        // there's no need to rebuild an array paired with results every time
        static int[] ScoreByIndex = Array.Empty<int>();

        // Passing a Comparison as a lambda allocates a delegate on every call, so just one is built up front
        static readonly Comparison<int> RankComparison = CompareRank;

        #endregion

        #region Public API

        /// <summary>
        /// Fills results with the indices of labels matching query, in descending score order.
        /// Never allocates any strings. results can be a list the caller reuses (it's Cleared up front).
        ///
        /// The only match condition is subsequence (case-insensitive). Ties preserve labels' original order.
        /// An empty query returns every entry, in its original order.
        /// </summary>
        public static void Filter(string query, IReadOnlyList<string> labels, List<int> results)
        {
            if (results == null)
            {
                // There's no destination to write to, so there's nothing to do. Silently giving up beats crashing mid-show with an exception
                return;
            }

            results.Clear();

            if (labels == null)
            {
                return;
            }

            int count = labels.Count;
            if (count == 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(query))
            {
                for (int i = 0; i < count; i++)
                {
                    results.Add(i);
                }

                return;
            }

            EnsureScoreBuffer(count);

            for (int i = 0; i < count; i++)
            {
                if (!TryScore(query, labels[i], out int score))
                {
                    continue;
                }

                ScoreByIndex[i] = score;
                results.Add(i);
            }

            if (results.Count > 1)
            {
                // Since the comparer breaks ties by ascending index, the result is unique even with an unstable sort (equivalent to a stable one)
                results.Sort(RankComparison);
            }
        }

        /// <summary>
        /// Whether label contains query as a subsequence. Returns a score if it does (higher is better).
        /// An empty query always matches (score 0); a null label never matches.
        /// </summary>
        public static bool TryScore(string query, string label, out int score)
        {
            score = 0;

            if (label == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(query))
            {
                return true;
            }

            int cursor = 0;
            int previousMatch = -1;
            int firstMatch = -1;
            bool contiguous = true;
            int total = 0;

            for (int q = 0; q < query.Length; q++)
            {
                char target = char.ToLowerInvariant(query[q]);
                int found = -1;

                // A greedy leftmost-match approach. Finding the optimal assignment would need a DP of
                // candidate count x query length, but at the candidate counts a dropdown actually has, the
                // difference isn't noticeable, so simplicity wins out
                while (cursor < label.Length)
                {
                    bool hit = char.ToLowerInvariant(label[cursor]) == target;
                    cursor++;

                    if (hit)
                    {
                        found = cursor - 1;
                        break;
                    }
                }

                if (found < 0)
                {
                    return false;
                }

                total += MATCH_SCORE;

                if (IsWordStart(label, found))
                {
                    total += WORD_START_SCORE;
                }

                if (previousMatch < 0)
                {
                    firstMatch = found;
                    total -= Math.Min(found, MAX_LEADING_SKIP) * LEADING_PENALTY;
                }
                else if (found == previousMatch + 1)
                {
                    total += CONSECUTIVE_SCORE;
                }
                else
                {
                    contiguous = false;
                    total -= Math.Min(found - previousMatch - 1, MAX_GAP) * GAP_PENALTY;
                }

                previousMatch = found;
            }

            if (firstMatch == 0 && contiguous)
            {
                total += PREFIX_SCORE;
            }

            score = total;
            return true;
        }

        #endregion

        #region Helpers

        static void EnsureScoreBuffer(int count)
        {
            if (ScoreByIndex.Length >= count)
            {
                return;
            }

            ScoreByIndex = new int[count];
        }

        static int CompareRank(int left, int right)
        {
            int diff = ScoreByIndex[right] - ScoreByIndex[left];

            // Ties keep the original order. Always breaking ties here guarantees "stable sort" behavior without depending on List.Sort for it
            return diff != 0 ? diff : left - right;
        }

        static bool IsWordStart(string label, int index)
        {
            if (index == 0)
            {
                return true;
            }

            char previous = label[index - 1];
            if (IsSeparator(previous))
            {
                return true;
            }

            // camelCase boundary. Picks up the 'I' in "easeIn" as a word start
            return char.IsLower(previous) && char.IsUpper(label[index]);
        }

        static bool IsSeparator(char c)
        {
            return c == ' ' || c == '\t' || c == '_' || c == '-' || c == '/' || c == '.' || c == ':';
        }

        #endregion
    }
}
