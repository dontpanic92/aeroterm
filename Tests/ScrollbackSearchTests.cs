// <copyright file="ScrollbackSearchTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests the pure <see cref="ScrollbackSearch"/> matcher. Rows are
/// constructed directly as <see cref="Cell"/>[] arrays so the parser is
/// out of scope.
/// </summary>
public class ScrollbackSearchTests
{
    private static readonly SearchOptions Defaults = new(Regex: false, CaseSensitive: false, WholeWord: false);

    /// <summary>
    /// Literal match finds the substring at the right column.
    /// </summary>
    [Test]
    public void Literal_FindsSubstringAtColumnZero()
    {
        var rows = new[] { Row("foobar", width: 10) };

        var matches = ScrollbackSearch.FindMatches(rows, cols: 10, query: "foo", options: Defaults);

        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(matches[0], Is.EqualTo(new SearchMatch(0, 0, 3)));
    }

    /// <summary>
    /// Case-sensitive flag toggles whether uppercase queries match
    /// lowercase text.
    /// </summary>
    [Test]
    public void CaseSensitivity_RespectsToggle()
    {
        var rows = new[] { Row("foo", width: 8) };

        var caseSensitive = ScrollbackSearch.FindMatches(
            rows, 8, "FOO", new SearchOptions(false, CaseSensitive: true, false));
        var caseInsensitive = ScrollbackSearch.FindMatches(
            rows, 8, "FOO", new SearchOptions(false, CaseSensitive: false, false));

        Assert.That(caseSensitive, Is.Empty);
        Assert.That(caseInsensitive, Has.Count.EqualTo(1));
        Assert.That(caseInsensitive[0].StartCol, Is.EqualTo(0));
        Assert.That(caseInsensitive[0].CellLength, Is.EqualTo(3));
    }

    /// <summary>
    /// Regex mode: <c>\d+</c> locates a digit run within surrounding text.
    /// </summary>
    [Test]
    public void Regex_FindsDigitRun()
    {
        var rows = new[] { Row("abc 123 def", width: 16) };

        var matches = ScrollbackSearch.FindMatches(
            rows, 16, @"\d+", new SearchOptions(Regex: true, CaseSensitive: false, WholeWord: false));

        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(matches[0].StartCol, Is.EqualTo(4));
        Assert.That(matches[0].CellLength, Is.EqualTo(3));
    }

    /// <summary>
    /// Invalid regex patterns return an empty list rather than throwing.
    /// </summary>
    [Test]
    public void Regex_InvalidPattern_ReturnsEmpty()
    {
        var rows = new[] { Row("abc", width: 8) };

        var matches = ScrollbackSearch.FindMatches(
            rows, 8, "(unterminated", new SearchOptions(Regex: true, CaseSensitive: false, WholeWord: false));

        Assert.That(matches, Is.Empty);
    }

    /// <summary>
    /// Whole-word uses the terminal's custom word-char class: because
    /// <c>-</c> is a word character, "bar" is part of "foo-bar" and
    /// must not match.
    /// </summary>
    [Test]
    public void WholeWord_HyphenatedIdentifier_DoesNotMatchInnerWord()
    {
        var rows = new[] { Row("foo-bar", width: 10) };

        var whole = ScrollbackSearch.FindMatches(
            rows, 10, "bar", new SearchOptions(false, false, WholeWord: true));
        var loose = ScrollbackSearch.FindMatches(
            rows, 10, "bar", new SearchOptions(false, false, WholeWord: false));

        Assert.That(whole, Is.Empty);
        Assert.That(loose, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Whole-word matching succeeds when the query is space-delimited.
    /// </summary>
    [Test]
    public void WholeWord_SpaceDelimited_Matches()
    {
        var rows = new[] { Row("foo bar baz", width: 12) };

        var matches = ScrollbackSearch.FindMatches(
            rows, 12, "bar", new SearchOptions(false, false, WholeWord: true));

        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(matches[0].StartCol, Is.EqualTo(4));
        Assert.That(matches[0].CellLength, Is.EqualTo(3));
    }

    /// <summary>
    /// Empty or whitespace-only queries match nothing.
    /// </summary>
    /// <param name="query">The query string to try.</param>
    [TestCase("")]
    [TestCase("   ")]
    public void EmptyQuery_NoMatches(string query)
    {
        var rows = new[] { Row("hello", width: 8) };

        var matches = ScrollbackSearch.FindMatches(rows, 8, query, Defaults);

        Assert.That(matches, Is.Empty);
    }

    /// <summary>
    /// Multiple non-overlapping matches in a single row are all reported
    /// in column order.
    /// </summary>
    [Test]
    public void MultipleMatchesPerRow_OrderedByStartCol()
    {
        var rows = new[] { Row("ab ab ab", width: 12) };

        var matches = ScrollbackSearch.FindMatches(rows, 12, "ab", Defaults);

        Assert.That(matches.Select(m => m.StartCol), Is.EqualTo(new[] { 0, 3, 6 }));
        Assert.That(matches.All(m => m.CellLength == 2), Is.True);
    }

    /// <summary>
    /// A match that spans a wide glyph correctly reports the cell length
    /// covering both the lead cell and its continuation cell.
    /// </summary>
    [Test]
    public void WideGlyph_MatchSpansContinuationCell()
    {
        // Row:  [T][中][ ][A][X]...  with 中 at col 1 and continuation at col 2.
        var row = new Cell[10];
        SetChar(ref row[0], "T");
        SetChar(ref row[1], "中");
        SetChar(ref row[2], null); // continuation
        SetChar(ref row[3], " ");
        SetChar(ref row[4], "A");
        SetChar(ref row[5], "X");
        for (int i = 6; i < row.Length; i++)
        {
            SetChar(ref row[i], " ");
        }

        var rows = new[] { row };

        var matches = ScrollbackSearch.FindMatches(rows, 10, "中", Defaults);

        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(matches[0].StartCol, Is.EqualTo(1));
        Assert.That(matches[0].CellLength, Is.EqualTo(2));

        // A match that starts before and extends through the wide glyph
        // reports the full column span (T + 中's two cells).
        var spanning = ScrollbackSearch.FindMatches(rows, 10, "T中", Defaults);
        Assert.That(spanning, Has.Count.EqualTo(1));
        Assert.That(spanning[0].StartCol, Is.EqualTo(0));
        Assert.That(spanning[0].CellLength, Is.EqualTo(3));
    }

    /// <summary>
    /// Astral-plane characters (encoded as UTF-16 surrogate pairs) should
    /// match at the base cell column.
    /// </summary>
    [Test]
    public void Emoji_ReportedAtBaseCellColumn()
    {
        // Simulate a wide emoji occupying two cells; its string is a
        // surrogate pair stored in the lead cell.
        var row = new Cell[10];
        SetChar(ref row[0], "a");
        SetChar(ref row[1], "\U0001F600"); // 😀 — two UTF-16 units
        SetChar(ref row[2], null);
        SetChar(ref row[3], "b");
        for (int i = 4; i < row.Length; i++)
        {
            SetChar(ref row[i], " ");
        }

        var matches = ScrollbackSearch.FindMatches(new[] { row }, 10, "\U0001F600", Defaults);

        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(matches[0].StartCol, Is.EqualTo(1));
        Assert.That(matches[0].CellLength, Is.EqualTo(2));
    }

    /// <summary>
    /// Matches that start before <c>cols</c> but extend past it are
    /// clipped so <c>StartCol + CellLength &lt;= cols</c>.
    /// </summary>
    [Test]
    public void MatchExtendingPastCols_IsClipped()
    {
        // Row captured at width 12 with "hellothere" starting at col 5,
        // but cols at search time is 8.
        var row = new Cell[12];
        for (int i = 0; i < row.Length; i++)
        {
            SetChar(ref row[i], " ");
        }

        string text = "hellothere";
        for (int i = 0; i < text.Length; i++)
        {
            SetChar(ref row[2 + i], text[i].ToString());
        }

        var matches = ScrollbackSearch.FindMatches(new[] { row }, cols: 8, "hellothere", Defaults);

        Assert.That(matches, Has.Count.EqualTo(1));
        Assert.That(matches[0].StartCol, Is.EqualTo(2));
        Assert.That(matches[0].StartCol + matches[0].CellLength, Is.LessThanOrEqualTo(8));
        Assert.That(matches[0].CellLength, Is.EqualTo(6));
    }

    /// <summary>
    /// Matches whose <c>StartCol</c> is at or past the live <c>cols</c>
    /// width are excluded entirely.
    /// </summary>
    [Test]
    public void MatchStartingAtOrPastCols_IsExcluded()
    {
        var row = new Cell[12];
        for (int i = 0; i < row.Length; i++)
        {
            SetChar(ref row[i], " ");
        }

        // "zap" at col 9
        SetChar(ref row[9], "z");
        SetChar(ref row[10], "a");
        SetChar(ref row[11], "p");

        var matches = ScrollbackSearch.FindMatches(new[] { row }, cols: 8, "zap", Defaults);

        Assert.That(matches, Is.Empty);
    }

    /// <summary>
    /// Results are returned in ascending (AbsoluteRow, StartCol) order
    /// across multiple rows.
    /// </summary>
    [Test]
    public void Matches_AreOrderedByAbsoluteRowThenCol()
    {
        var rows = new[]
        {
            Row("  hit hit", width: 10),
            Row("hit    ", width: 10),
            Row("        hit ", width: 12),
        };

        var matches = ScrollbackSearch.FindMatches(rows, 12, "hit", Defaults);

        var keys = matches.Select(m => (m.AbsoluteRow, m.StartCol)).ToArray();
        Assert.That(keys, Is.EqualTo(new[] { (0, 2), (0, 6), (1, 0), (2, 8) }));
    }

    private static Cell[] Row(string text, int width)
    {
        var row = new Cell[width];
        for (int i = 0; i < width; i++)
        {
            if (i < text.Length)
            {
                SetChar(ref row[i], text[i].ToString());
            }
            else
            {
                SetChar(ref row[i], " ");
            }
        }

        return row;
    }

    private static void SetChar(ref Cell cell, string? character)
    {
        cell.Set(character, default);
    }
}
