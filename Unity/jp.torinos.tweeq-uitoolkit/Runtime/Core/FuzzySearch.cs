using System;
using System.Collections.Generic;

namespace Tweeq.Core
{
    /// <summary>
    /// DropdownInput の絞り込みに使う簡易ファジー検索。UnityEngine 非依存。
    ///
    /// 原典（Vue 版 InputDropdown）は fast-fuzzy の search() を使っているが、あれは Sellers 法の
    /// 編集距離を全候補に掛ける実装で、タイプミス耐性と引き換えに候補ごとの DP テーブルを確保する。
    /// こちらは「打鍵のたびに全候補を再走査してもアロケーションを出さない」ことを優先し、
    /// サブシーケンス一致だけを通す重み付けスコアラーに置き換えてある
    /// （m6-wave2-spec.md §B の意図的逸脱）。そのため fast-fuzzy とは順位が一致しない。
    /// 具体的には、タイプミス（余計な 1 文字・入れ替え）はここでは一切拾わない。
    ///
    /// 内部バッファは static に使い回すので、UI スレッドからのみ呼ぶこと。
    /// </summary>
    public static class FuzzySearch
    {
        #region Constants

        // 1 文字一致するたびの素点。同じクエリ同士の比較しかしないので順位には効かず、
        // ボーナス／ペナルティの基準スケールを決めるだけ
        const int MATCH_SCORE = 16;

        // 直前の一致の隣（連続一致）
        const int CONSECUTIVE_SCORE = 16;

        // 語頭一致（先頭・区切り文字の直後・camelCase 境界）
        const int WORD_START_SCORE = 24;

        // クエリ全体が先頭から連続で一致した＝前方一致。
        // 「前方一致は語頭一致に必ず勝つ」を式を睨まずに保証したいので、他より一段大きく取る
        const int PREFIX_SCORE = 64;

        // 先頭の取りこぼしと一致同士の隙間。青天井にすると長いラベルが不当に沈むので頭打ちにする
        const int LEADING_PENALTY = 1;
        const int MAX_LEADING_SKIP = 8;
        const int GAP_PENALTY = 2;
        const int MAX_GAP = 4;

        #endregion

        #region Fields

        // スコアは「options のインデックス → 点数」で持つ。並べ替えの比較子から引くだけなので、
        // results と対になる配列を毎回組み直す必要がない
        static int[] ScoreByIndex = Array.Empty<int>();

        // Comparison をラムダで渡すと呼び出しごとにデリゲートを確保するため、1 個だけ作り置きする
        static readonly Comparison<int> RankComparison = CompareRank;

        #endregion

        #region Public API

        /// <summary>
        /// query に一致する labels のインデックスをスコア降順で results へ詰める。
        /// 文字列は一切作らない。results は呼び出し側の使い回しリストでよい（先頭で Clear する）。
        ///
        /// 一致条件はサブシーケンス（大文字小文字無視）のみ。同点は labels の元順を保つ。
        /// 空クエリは全件をそのままの順で返す。
        /// </summary>
        public static void Filter(string query, IReadOnlyList<string> labels, List<int> results)
        {
            if (results == null)
            {
                // 出力先が無いので何もできない。公演中に例外で落とすより黙って諦める
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
                // 比較子が同点をインデックス昇順で割るので、不安定ソートでも結果は一意（＝安定と同じ）
                results.Sort(RankComparison);
            }
        }

        /// <summary>
        /// label が query のサブシーケンスを含むか。含むならスコアを返す（大きいほど良い）。
        /// 空クエリは常に一致（スコア 0）、null ラベルは常に不一致。
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

                // 最左一致の貪欲法。最適な割り当てを探すと候補数×クエリ長の DP が要るが、
                // ドロップダウンの候補数では体感差が出ないので単純さを取る
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

            // 同点は元順。ここで必ず割ることで「安定ソート」を List.Sort に依存せず保証する
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

            // camelCase 境界。"easeIn" の 'I' を語頭として拾う
            return char.IsLower(previous) && char.IsUpper(label[index]);
        }

        static bool IsSeparator(char c)
        {
            return c == ' ' || c == '\t' || c == '_' || c == '-' || c == '/' || c == '.' || c == ':';
        }

        #endregion
    }
}
