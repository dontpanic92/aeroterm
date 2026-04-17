// <copyright file="FuzzyMatcher.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Globalization;

/// <summary>
/// Lightweight VS Code / Sublime-style fuzzy subsequence matcher. Given
/// a query and a candidate string, returns a score (lower is better;
/// 0 = empty-query "matches everything" sentinel) plus the matched
/// character positions so the UI can bold them.
/// </summary>
/// <remarks>
/// <para>The algorithm is a single-pass greedy subsequence scan: for
/// each query character we advance to the next case-insensitive
/// occurrence in the candidate. The match fails (returns
/// <see langword="null"/>) if any query character cannot be placed
/// monotonically.</para>
/// <para>Scoring combines penalties (leading gap, gaps between matches)
/// with bonuses (prefix, word-start, consecutive, case-matching) so
/// that a candidate where the query hits the head / word starts wins
/// over one where it hits mid-word characters.</para>
/// </remarks>
public static class FuzzyMatcher
{
    /// <summary>
    /// Scores <paramref name="query"/> against <paramref name="candidate"/>.
    /// </summary>
    /// <param name="query">The user's query. May be empty.</param>
    /// <param name="candidate">The candidate string.</param>
    /// <returns>A <see cref="Match"/> when every query character can be
    /// matched in order (case-insensitive); <see langword="null"/>
    /// otherwise. An empty or whitespace-only query always returns
    /// <c>new Match(0, Array.Empty&lt;int&gt;())</c> so every candidate
    /// surfaces in the default listing.</returns>
    public static Match? Score(string query, string candidate)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidate);

        if (query.Length == 0 || string.IsNullOrWhiteSpace(query))
        {
            return new Match(0, Array.Empty<int>());
        }

        if (candidate.Length == 0)
        {
            return null;
        }

        var positions = new int[query.Length];
        int ci = 0;
        for (int qi = 0; qi < query.Length; qi++)
        {
            char qc = query[qi];
            int found = -1;
            while (ci < candidate.Length)
            {
                if (EqualsIgnoreCase(candidate[ci], qc))
                {
                    found = ci;
                    ci++;
                    break;
                }

                ci++;
            }

            if (found < 0)
            {
                return null;
            }

            positions[qi] = found;
        }

        int score = ComputeScore(query, candidate, positions);
        return new Match(score, positions);
    }

    private static int ComputeScore(string query, string candidate, int[] positions)
    {
        int score = 0;

        // Leading gap: cheap, but a non-zero start loses the prefix bonus.
        score += positions[0];
        if (positions[0] == 0)
        {
            score -= 5;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            int pos = positions[i];
            if (i > 0)
            {
                int gap = pos - positions[i - 1] - 1;
                if (gap == 0)
                {
                    // Consecutive run.
                    score -= 3;
                }
                else
                {
                    score += gap * 2;
                }
            }

            // Word-start bonus (start of string or previous char is a
            // separator).
            if (pos > 0 && IsWordBoundary(candidate[pos - 1]))
            {
                score -= 5;
            }

            // Case-match bonus — only for letters where the case is
            // meaningful.
            if (candidate[pos] == query[i])
            {
                score -= 1;
            }
        }

        return score;
    }

    private static bool IsWordBoundary(char c)
    {
        if (char.IsLetterOrDigit(c))
        {
            return false;
        }

        return c == ' ' || c == '-' || c == '.' || c == '_' || c == '/' || c == '\\' || c == ':';
    }

    private static bool EqualsIgnoreCase(char a, char b)
    {
        if (a == b)
        {
            return true;
        }

        return char.ToLower(a, CultureInfo.InvariantCulture) == char.ToLower(b, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A successful fuzzy-match result.
    /// </summary>
    /// <param name="Score">The match score. Lower is better; 0 is the
    /// sentinel returned for an empty query.</param>
    /// <param name="Positions">The zero-based indices in the candidate
    /// string where each query character was matched. Monotonically
    /// increasing; empty when the query was empty.</param>
    public readonly record struct Match(int Score, int[] Positions);
}
