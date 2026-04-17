// <copyright file="ScrollbackSearch.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Text;
using System.Text.RegularExpressions;
using AeroTerm.Pty;

/// <summary>
/// Pure, thread-safe matcher for <see cref="TerminalControl"/>'s scrollback
/// search overlay. All inputs are by-value snapshots; no live terminal
/// state is touched.
/// </summary>
internal static class ScrollbackSearch
{
    /// <summary>
    /// Same word-character definition as <c>TerminalSelection.Category</c>:
    /// Unicode letters/digits, <c>_</c>, <c>-</c>, <c>.</c>, <c>/</c>,
    /// <c>:</c>, and any code unit above U+007F. Used only for whole-word
    /// lookarounds so search semantics match double-click selection.
    /// </summary>
    private const string WordCharClass = @"[\p{L}\p{N}_\-./:\u0080-\uFFFF]";

    /// <summary>
    /// Finds all matches for <paramref name="query"/> across the given rows,
    /// returning results ordered by <c>(AbsoluteRow, StartCol)</c>.
    /// </summary>
    /// <param name="rows">The row corpus in absolute-row order. Callers
    /// assemble this as scrollback rows (oldest-first) followed by the
    /// live screen's rows so <see cref="SearchMatch.AbsoluteRow"/> lines
    /// up with the scrollback ring index scheme.</param>
    /// <param name="cols">The current live-grid column count. Matches whose
    /// <see cref="SearchMatch.StartCol"/> is at or past this value are
    /// excluded (a row may be wider if it was captured before a resize).
    /// <see cref="SearchMatch.CellLength"/> is clipped so
    /// <c>StartCol + CellLength &lt;= cols</c>.</param>
    /// <param name="query">The search query. Empty or whitespace-only
    /// queries produce no matches.</param>
    /// <param name="options">Toggles for regex / case-sensitivity / whole-word.</param>
    /// <returns>The matches in absolute-row order, or an empty list.</returns>
    public static IReadOnlyList<SearchMatch> FindMatches(
        IReadOnlyList<Cell[]> rows,
        int cols,
        string query,
        SearchOptions options)
    {
        if (rows is null || string.IsNullOrWhiteSpace(query) || cols <= 0)
        {
            return Array.Empty<SearchMatch>();
        }

        Regex regex;
        try
        {
            regex = CompilePattern(query, options);
        }
        catch (ArgumentException)
        {
            // Invalid regex: return empty rather than propagate.
            return Array.Empty<SearchMatch>();
        }
        catch (RegexMatchTimeoutException)
        {
            return Array.Empty<SearchMatch>();
        }

        var results = new List<SearchMatch>();
        var sb = new StringBuilder();
        var utf16ToCol = new List<int>();

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row is null || row.Length == 0)
            {
                continue;
            }

            sb.Clear();
            utf16ToCol.Clear();
            BuildRowText(row, sb, utf16ToCol);
            if (sb.Length == 0)
            {
                continue;
            }

            string text = sb.ToString();
            Match m;
            try
            {
                m = regex.Match(text);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            while (m.Success)
            {
                int s = m.Index;
                int e = m.Index + m.Length;
                if (m.Length > 0 && s < utf16ToCol.Count)
                {
                    int startCol = utf16ToCol[s];
                    int endCol = e < utf16ToCol.Count ? utf16ToCol[e] : cols;
                    int cellLength = endCol - startCol;
                    if (startCol < cols && cellLength > 0)
                    {
                        if (startCol + cellLength > cols)
                        {
                            cellLength = cols - startCol;
                        }

                        results.Add(new SearchMatch(rowIndex, startCol, cellLength));
                    }
                }

                int next = m.Index + Math.Max(1, m.Length);
                if (next > text.Length)
                {
                    break;
                }

                try
                {
                    m = regex.Match(text, next);
                }
                catch (RegexMatchTimeoutException)
                {
                    break;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Walks <paramref name="row"/>, appending each non-continuation cell's
    /// <see cref="Cell.Character"/> to <paramref name="sb"/> while recording
    /// the originating column in <paramref name="utf16ToCol"/> — one entry
    /// per UTF-16 code unit appended. Continuation cells (null/empty
    /// <see cref="Cell.Character"/> — the second half of a wide glyph)
    /// contribute nothing to either structure, so UTF-16 offsets of
    /// <c>sb.ToString()</c> map back cleanly to the lead cell column.
    /// </summary>
    private static void BuildRowText(Cell[] row, StringBuilder sb, List<int> utf16ToCol)
    {
        for (int col = 0; col < row.Length; col++)
        {
            string? ch = row[col].Character;
            if (string.IsNullOrEmpty(ch))
            {
                continue;
            }

            for (int k = 0; k < ch.Length; k++)
            {
                sb.Append(ch[k]);
                utf16ToCol.Add(col);
            }
        }
    }

    private static Regex CompilePattern(string query, SearchOptions options)
    {
        string core = options.Regex ? query : Regex.Escape(query);
        if (options.WholeWord)
        {
            core = $"(?<!{WordCharClass}){core}(?!{WordCharClass})";
        }

        var flags = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        if (!options.CaseSensitive)
        {
            flags |= RegexOptions.IgnoreCase;
        }

        return new Regex(core, flags, TimeSpan.FromMilliseconds(250));
    }
}
