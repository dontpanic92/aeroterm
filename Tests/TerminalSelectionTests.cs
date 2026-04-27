// <copyright file="TerminalSelectionTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Unit tests for <see cref="TerminalSelection"/>.
/// </summary>
public class TerminalSelectionTests
{
    /// <summary>
    /// A fresh selection is empty; a single-cell character selection is also
    /// reported empty until the gesture extends to another cell.
    /// </summary>
    [Test]
    public void BeginCharacter_EmptyByDefault_NotEmptyAfterDrag()
    {
        var s = new TerminalSelection();
        var cells = Grid("hello", "world");

        Assert.That(s.IsEmpty, Is.True);

        s.BeginCharacter(0, 2, cells);
        Assert.That(s.IsEmpty, Is.True);

        s.ExtendTo(0, 4, cells);
        Assert.That(s.IsEmpty, Is.False);
    }

    /// <summary>
    /// Character-granularity copy returns the substring of the selected row.
    /// </summary>
    [Test]
    public void CopyText_CharacterRange_WithinSingleRow()
    {
        var s = new TerminalSelection();
        var cells = Grid("hello world");
        s.BeginCharacter(0, 0, cells);
        s.ExtendTo(0, 4, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo("hello"));
    }

    /// <summary>
    /// Dragging right-to-left produces the same text as left-to-right.
    /// </summary>
    [Test]
    public void CopyText_ReverseDrag_NormalizesRange()
    {
        var s = new TerminalSelection();
        var cells = Grid("hello world");
        s.BeginCharacter(0, 4, cells);
        s.ExtendTo(0, 0, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo("hello"));
    }

    /// <summary>
    /// Multi-row selection rtrims fully-selected rows and joins with
    /// line-feeds; the last row preserves its partial trailing content.
    /// </summary>
    [Test]
    public void CopyText_AcrossRows_TrimsFullRows_AndJoinsWithNewline()
    {
        var s = new TerminalSelection();
        var cells = Grid(
            "line one   ",
            "line two   ",
            "line three ");
        s.BeginCharacter(0, 0, cells);
        s.ExtendTo(2, 3, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo("line one\nline two\nline"));
    }

    /// <summary>
    /// Trailing spaces inside a partial selection on the final row are
    /// preserved (only fully-selected rows get rtrimmed).
    /// </summary>
    [Test]
    public void CopyText_LastRowPreservesTrailingSpaces_UpToEndColumn()
    {
        var s = new TerminalSelection();
        var cells = Grid("abc   def");
        s.BeginCharacter(0, 0, cells);
        s.ExtendTo(0, 5, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo("abc   "));
    }

    /// <summary>
    /// Word selection snaps to the run of word characters around the click.
    /// Hyphens, underscores and a few path-y punctuators stay in the word.
    /// </summary>
    [Test]
    public void BeginWord_SelectsWordBoundaries()
    {
        var s = new TerminalSelection();
        var cells = Grid("foo bar-baz qux");
        s.BeginWord(0, 5, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo("bar-baz"));
    }

    /// <summary>
    /// Word selection on a punctuation cluster selects the punctuation run.
    /// </summary>
    [Test]
    public void BeginWord_OnPunctuation_SelectsPunctuationRun()
    {
        var s = new TerminalSelection();
        var cells = Grid("a ;; b");
        s.BeginWord(0, 2, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo(";;"));
    }

    /// <summary>
    /// Line selection covers the full row, with rtrimming on fully-selected rows.
    /// </summary>
    [Test]
    public void BeginLine_SelectsEntireRow_RtrimmedOnFullRow()
    {
        var s = new TerminalSelection();
        var cells = Grid(
            "hello        ",
            "world        ");
        s.BeginLine(0, cells);
        s.ExtendTo(1, 0, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo("hello\nworld        "));
    }

    /// <summary>
    /// <see cref="TerminalSelection.Contains"/> returns true exactly for
    /// cells inside the normalized range.
    /// </summary>
    [Test]
    public void Contains_ReportsCellsInRange()
    {
        var s = new TerminalSelection();
        var cells = Grid("hello", "world");
        s.BeginCharacter(0, 1, cells);
        s.ExtendTo(1, 2, cells);

        Assert.That(s.Contains(0, 0), Is.False);
        Assert.That(s.Contains(0, 1), Is.True);
        Assert.That(s.Contains(0, 4), Is.True);
        Assert.That(s.Contains(1, 0), Is.True);
        Assert.That(s.Contains(1, 2), Is.True);
        Assert.That(s.Contains(1, 3), Is.False);
    }

    /// <summary>
    /// Clicking the second half of a wide glyph canonicalizes the endpoint
    /// onto the leading cell so range math and copy stay consistent.
    /// </summary>
    [Test]
    public void BeginCharacter_OnWideGlyphContinuation_CanonicalizesToLead()
    {
        var cells = GridWithWide("A##B", leadCol: 1);
        var s = new TerminalSelection();
        s.BeginCharacter(0, 2, cells);
        s.ExtendTo(0, 3, cells);

        Assert.That(s.Anchor.Col, Is.EqualTo(1));
        Assert.That(s.CopyText(cells), Is.EqualTo("#B"));
    }

    /// <summary>
    /// Wide glyph continuation cells are skipped during copy so the glyph
    /// appears exactly once in the output.
    /// </summary>
    [Test]
    public void ExtendTo_AcrossWideGlyph_SkipsContinuationInCopy()
    {
        var cells = GridWithWide("A##B", leadCol: 1);
        var s = new TerminalSelection();
        s.BeginCharacter(0, 0, cells);
        s.ExtendTo(0, 3, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo("A#B"));
    }

    /// <summary>
    /// <see cref="TerminalSelection.Clear"/> resets state entirely.
    /// </summary>
    [Test]
    public void Clear_ResetsSelection()
    {
        var s = new TerminalSelection();
        var cells = Grid("abc");
        s.BeginCharacter(0, 0, cells);
        s.ExtendTo(0, 2, cells);
        s.Clear();

        Assert.That(s.IsEmpty, Is.True);
        Assert.That(s.Contains(0, 1), Is.False);
        Assert.That(s.CopyText(cells), Is.EqualTo(string.Empty));
    }

    /// <summary>
    /// In word mode the selection grows outward to whole-word boundaries in
    /// either direction from the originally-snapped anchor.
    /// </summary>
    [Test]
    public void BeginWord_ThenExtend_OnlyGrowsOutward()
    {
        var s = new TerminalSelection();
        var cells = Grid("alpha beta gamma");

        s.BeginWord(0, 7, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo("beta"));

        s.ExtendTo(0, 13, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo("beta gamma"));

        s.ExtendTo(0, 1, cells);
        Assert.That(s.CopyText(cells), Is.EqualTo("alpha beta"));
    }

    /// <summary>
    /// <see cref="TerminalSelection.GetNormalizedRange"/> returns endpoints
    /// in reading order regardless of drag direction.
    /// </summary>
    [Test]
    public void GetNormalizedRange_OrdersEndpoints()
    {
        var s = new TerminalSelection();
        var cells = Grid("abcde", "fghij");
        s.BeginCharacter(1, 3, cells);
        s.ExtendTo(0, 1, cells);

        var (sr, sc, er, ec) = s.GetNormalizedRange();
        Assert.That((sr, sc), Is.EqualTo((0, 1)));
        Assert.That((er, ec), Is.EqualTo((1, 3)));
    }

    /// <summary>
    /// <see cref="TerminalSelection.Shift"/> moves all stored row indices
    /// uniformly. Used by the owner to compensate for scrollback ring
    /// eviction without losing the user's selection.
    /// </summary>
    [Test]
    public void Shift_TranslatesAllRowsByDelta()
    {
        var rows = new ListRowSource(new[] { "alpha", "beta-", "gamma" });
        var s = new TerminalSelection();
        s.BeginCharacter(1, 0, rows);
        s.ExtendTo(2, 4, rows);

        s.Shift(-1);

        var (sr, sc, er, ec) = s.GetNormalizedRange();
        Assert.That((sr, sc, er, ec), Is.EqualTo((0, 0, 1, 4)));
    }

    /// <summary>
    /// <see cref="TerminalSelection.ClampRows"/> snaps endpoints into the
    /// supplied range. After clamping, copy text reflects the surviving
    /// portion of the selection.
    /// </summary>
    [Test]
    public void ClampRows_TrimsEndpointsToRange()
    {
        var rows = new ListRowSource(new[] { "alpha", "beta", "gamma" });
        var s = new TerminalSelection();
        s.BeginCharacter(0, 0, rows);
        s.ExtendTo(2, 4, rows);

        s.ClampRows(1, 2);

        Assert.That(s.IsEmpty, Is.False);
        Assert.That(s.CopyText(rows), Is.EqualTo("beta\ngamma"));
    }

    /// <summary>
    /// When the entire selection range falls outside the clamping window
    /// the selection is cleared (full eviction).
    /// </summary>
    [Test]
    public void ClampRows_FullyOutOfRange_ClearsSelection()
    {
        var rows = new ListRowSource(new[] { "alpha", "beta", "gamma" });
        var s = new TerminalSelection();
        s.BeginCharacter(0, 0, rows);
        s.ExtendTo(0, 4, rows);

        s.ClampRows(2, 2);

        Assert.That(s.IsEmpty, Is.True);
        Assert.That(s.Mode, Is.EqualTo(TerminalSelectionMode.None));
    }

    /// <summary>
    /// Cross-row character selection over a row source returns the joined
    /// text with rtrimmed full rows, matching the visible-grid behavior.
    /// </summary>
    [Test]
    public void CopyText_ITerminalRowSource_AcrossRows()
    {
        var rows = new ListRowSource(new[] { "line one   ", "line two   ", "line three " });
        var s = new TerminalSelection();
        s.BeginCharacter(0, 0, rows);
        s.ExtendTo(2, 3, rows);

        Assert.That(s.CopyText(rows), Is.EqualTo("line one\nline two\nline"));
    }

    /// <summary>
    /// Word selection on a scrollback-backed row addresses the row's full
    /// width, even when scrollback rows happen to be narrower than the
    /// live grid (post-shrink).
    /// </summary>
    [Test]
    public void BeginWord_OnScrollbackRow_UsesRowOwnWidth()
    {
        var rows = new ListRowSource(new[] { "scroll-old" }, liveCols: 80);
        var s = new TerminalSelection();
        s.BeginWord(0, 3, rows);

        Assert.That(s.CopyText(rows), Is.EqualTo("scroll-old"));
    }

    /// <summary>
    /// Line selection on a row whose width differs from <c>Cols</c> uses
    /// the row's own length for the trailing endpoint.
    /// </summary>
    [Test]
    public void BeginLine_OnNarrowScrollbackRow_CoversRowWidth()
    {
        var rows = new ListRowSource(new[] { "abc" }, liveCols: 80);
        var s = new TerminalSelection();
        s.BeginLine(0, rows);

        var (sr, sc, er, ec) = s.GetNormalizedRange();
        Assert.That((sr, sc, er, ec), Is.EqualTo((0, 0, 0, 2)));
    }

    private static Cell[,] Grid(params string[] rows)
    {
        int h = rows.Length;
        int w = rows.Length == 0 ? 0 : rows[0].Length;
        var cells = new Cell[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                cells[r, c] = new Cell(rows[r][c].ToString(), default);
            }
        }

        return cells;
    }

    private static Cell[,] GridWithWide(string row, int leadCol)
    {
        int w = row.Length;
        var cells = new Cell[1, w];
        for (int c = 0; c < w; c++)
        {
            cells[0, c] = new Cell(row[c].ToString(), default);
        }

        cells[0, leadCol + 1] = new Cell(null, default);
        return cells;
    }

    /// <summary>
    /// In-test <see cref="ITerminalRowSource"/> implementation backed by a
    /// list of strings (one per absolute row). Lets tests address rows
    /// of varying widths and pick a live-grid <c>Cols</c> independently
    /// (mimicking a scrollback row narrower than the current viewport).
    /// </summary>
    private sealed class ListRowSource : ITerminalRowSource
    {
        private readonly Cell[][] rows;

        public ListRowSource(string[] rows, int? liveCols = null)
        {
            this.rows = new Cell[rows.Length][];
            int maxWidth = 0;
            for (int r = 0; r < rows.Length; r++)
            {
                var s = rows[r];
                this.rows[r] = new Cell[s.Length];
                for (int c = 0; c < s.Length; c++)
                {
                    this.rows[r][c] = new Cell(s[c].ToString(), default);
                }

                if (s.Length > maxWidth)
                {
                    maxWidth = s.Length;
                }
            }

            this.Cols = liveCols ?? maxWidth;
        }

        public int RowCount => this.rows.Length;

        public int Cols { get; }

        public Cell[] GetRow(int absRow)
        {
            if (absRow < 0 || absRow >= this.rows.Length)
            {
                return Array.Empty<Cell>();
            }

            return this.rows[absRow];
        }
    }
}
