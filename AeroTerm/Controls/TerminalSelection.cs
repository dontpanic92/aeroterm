// <copyright file="TerminalSelection.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Text;
using AeroTerm.Pty;

/// <summary>
/// Selection mode driven by click count: single click starts character
/// selection, double click selects the word under the pointer, triple click
/// selects the entire line.
/// </summary>
internal enum TerminalSelectionMode
{
    /// <summary>No active selection.</summary>
    None,

    /// <summary>Character-granularity selection.</summary>
    Character,

    /// <summary>Word-granularity selection (double click).</summary>
    Word,

    /// <summary>Line-granularity selection (triple click).</summary>
    Line,
}

/// <summary>
/// Tracks a terminal text selection over the visible screen grid. Coordinates
/// are in (row, column) cell space; (0, 0) is the top-left visible cell.
/// The type is pure — it knows nothing about Avalonia input, the PTY, or
/// rendering — so it is cheap to unit test.
/// </summary>
/// <remarks>
/// AeroTerm has no scrollback ring today, so a selection is valid only for
/// the current visible grid snapshot. The owning control is responsible for
/// invalidating the selection when the buffer scrolls, the screen is cleared,
/// or the alt-buffer is switched.
/// </remarks>
internal sealed class TerminalSelection
{
    // The range that was snapped to word/line boundaries when the gesture
    // began. Used to expand outward on subsequent drag updates without
    // losing the original anchor.
    private (int Row, int Col) boundaryAnchorStart;
    private (int Row, int Col) boundaryAnchorEnd;

    /// <summary>
    /// Gets the current selection mode.
    /// </summary>
    public TerminalSelectionMode Mode { get; private set; }

    /// <summary>
    /// Gets the fixed endpoint of the selection.
    /// </summary>
    public (int Row, int Col) Anchor { get; private set; }

    /// <summary>
    /// Gets the moving endpoint of the selection.
    /// </summary>
    public (int Row, int Col) Active { get; private set; }

    /// <summary>
    /// Gets a value indicating whether no selection is active or the
    /// selection collapses to a single cell (empty content).
    /// </summary>
    public bool IsEmpty => this.Mode == TerminalSelectionMode.None
        || (this.Mode == TerminalSelectionMode.Character && this.Anchor == this.Active);

    /// <summary>
    /// Begins a character-granularity selection at the given cell.
    /// </summary>
    /// <param name="row">Grid row.</param>
    /// <param name="col">Grid column.</param>
    /// <param name="cells">Current visible cells for wide-char canonicalization.</param>
    public void BeginCharacter(int row, int col, Cell[,] cells)
    {
        var p = CanonicalizeEndpoint(cells, row, col);
        this.Mode = TerminalSelectionMode.Character;
        this.Anchor = p;
        this.Active = p;
        this.boundaryAnchorStart = p;
        this.boundaryAnchorEnd = p;
    }

    /// <summary>
    /// Begins a word-granularity selection by snapping to the word boundaries
    /// around the given cell.
    /// </summary>
    /// <param name="row">Grid row.</param>
    /// <param name="col">Grid column.</param>
    /// <param name="cells">Current visible cells.</param>
    public void BeginWord(int row, int col, Cell[,] cells)
    {
        var p = CanonicalizeEndpoint(cells, row, col);
        var (s, e) = FindWordRange(cells, p.Row, p.Col);
        this.Mode = TerminalSelectionMode.Word;
        this.boundaryAnchorStart = s;
        this.boundaryAnchorEnd = e;
        this.Anchor = s;
        this.Active = e;
    }

    /// <summary>
    /// Begins a line-granularity selection covering the full row.
    /// </summary>
    /// <param name="row">Grid row.</param>
    /// <param name="cells">Current visible cells.</param>
    public void BeginLine(int row, Cell[,] cells)
    {
        int cols = cells.GetLength(1);
        var s = (row, 0);
        var e = (row, Math.Max(0, cols - 1));
        this.Mode = TerminalSelectionMode.Line;
        this.boundaryAnchorStart = s;
        this.boundaryAnchorEnd = e;
        this.Anchor = s;
        this.Active = e;
    }

    /// <summary>
    /// Extends the active endpoint to the given cell. For word/line modes
    /// the endpoint snaps to the corresponding boundary; the selection
    /// always grows outward from the original anchor.
    /// </summary>
    /// <param name="row">Grid row.</param>
    /// <param name="col">Grid column.</param>
    /// <param name="cells">Current visible cells.</param>
    public void ExtendTo(int row, int col, Cell[,] cells)
    {
        if (this.Mode == TerminalSelectionMode.None)
        {
            return;
        }

        var p = CanonicalizeEndpoint(cells, row, col);

        if (this.Mode == TerminalSelectionMode.Character)
        {
            this.Active = p;
            return;
        }

        if (this.Mode == TerminalSelectionMode.Word)
        {
            var (s, e) = FindWordRange(cells, p.Row, p.Col);
            if (Compare(s, this.boundaryAnchorStart) < 0)
            {
                this.Anchor = this.boundaryAnchorEnd;
                this.Active = s;
            }
            else
            {
                this.Anchor = this.boundaryAnchorStart;
                this.Active = e;
            }

            return;
        }

        // Line mode.
        int cols = cells.GetLength(1);
        if (p.Row < this.boundaryAnchorStart.Row)
        {
            this.Anchor = this.boundaryAnchorEnd;
            this.Active = (p.Row, 0);
        }
        else
        {
            this.Anchor = this.boundaryAnchorStart;
            this.Active = (p.Row, Math.Max(0, cols - 1));
        }
    }

    /// <summary>
    /// Clears the selection.
    /// </summary>
    public void Clear()
    {
        this.Mode = TerminalSelectionMode.None;
        this.Anchor = default;
        this.Active = default;
        this.boundaryAnchorStart = default;
        this.boundaryAnchorEnd = default;
    }

    /// <summary>
    /// Returns the selection as a normalized range where (startRow, startCol)
    /// precedes (endRow, endCol) in reading order. Both endpoints are
    /// inclusive.
    /// </summary>
    /// <returns>The normalized range.</returns>
    public (int StartRow, int StartCol, int EndRow, int EndCol) GetNormalizedRange()
    {
        var a = this.Anchor;
        var b = this.Active;
        if (Compare(a, b) <= 0)
        {
            return (a.Row, a.Col, b.Row, b.Col);
        }

        return (b.Row, b.Col, a.Row, a.Col);
    }

    /// <summary>
    /// Tests whether the given cell lies within the current selection. Wide
    /// glyph continuation cells (Character == null) are reported as selected
    /// when their leading cell is selected.
    /// </summary>
    /// <param name="row">Grid row.</param>
    /// <param name="col">Grid column.</param>
    /// <returns>True if the cell is within the selection.</returns>
    public bool Contains(int row, int col)
    {
        if (this.Mode == TerminalSelectionMode.None)
        {
            return false;
        }

        var (sr, sc, er, ec) = this.GetNormalizedRange();
        if (row < sr || row > er)
        {
            return false;
        }

        if (sr == er)
        {
            return col >= sc && col <= ec;
        }

        if (row == sr)
        {
            return col >= sc;
        }

        if (row == er)
        {
            return col <= ec;
        }

        return true;
    }

    /// <summary>
    /// Extracts the selected text from the given visible cells. Fully
    /// selected rows are right-trimmed of blank cells and separated by
    /// <c>\n</c>; the final row preserves its trailing content up to the
    /// last selected column. Wide glyphs are emitted once.
    /// </summary>
    /// <param name="cells">The visible cells.</param>
    /// <returns>The plain-text selection content.</returns>
    public string CopyText(Cell[,] cells)
    {
        if (this.Mode == TerminalSelectionMode.None)
        {
            return string.Empty;
        }

        int rows = cells.GetLength(0);
        int cols = cells.GetLength(1);
        if (rows == 0 || cols == 0)
        {
            return string.Empty;
        }

        var (sr, sc, er, ec) = this.GetNormalizedRange();
        sr = Math.Clamp(sr, 0, rows - 1);
        er = Math.Clamp(er, 0, rows - 1);
        sc = Math.Clamp(sc, 0, cols - 1);
        ec = Math.Clamp(ec, 0, cols - 1);

        var sb = new StringBuilder();
        for (int r = sr; r <= er; r++)
        {
            int cStart = r == sr ? sc : 0;
            int cEnd = r == er ? ec : cols - 1;

            var rowBuilder = new StringBuilder();
            for (int c = cStart; c <= cEnd; c++)
            {
                string? ch = cells[r, c].Character;

                // Skip continuation cells of wide glyphs.
                if (ch is null)
                {
                    continue;
                }

                rowBuilder.Append(ch);
            }

            if (r < er)
            {
                // Strip trailing blank cells on fully-selected rows.
                string rowText = rowBuilder.ToString();
                int end = rowText.Length;
                while (end > 0 && rowText[end - 1] == ' ')
                {
                    end--;
                }

                sb.Append(rowText, 0, end);
                sb.Append('\n');
            }
            else
            {
                sb.Append(rowBuilder);
            }
        }

        return sb.ToString();
    }

    private static int Compare((int Row, int Col) a, (int Row, int Col) b)
    {
        int d = a.Row - b.Row;
        return d != 0 ? d : a.Col - b.Col;
    }

    private static (int Row, int Col) CanonicalizeEndpoint(Cell[,] cells, int row, int col)
    {
        int rows = cells.GetLength(0);
        int cols = cells.GetLength(1);
        if (rows == 0 || cols == 0)
        {
            return (row, col);
        }

        row = Math.Clamp(row, 0, rows - 1);
        col = Math.Clamp(col, 0, cols - 1);

        // If the pointer landed on the second half of a wide glyph, move
        // the endpoint back onto the leading cell so range math and copy
        // behave consistently.
        if (cells[row, col].Character is null && col > 0)
        {
            col--;
        }

        return (row, col);
    }

    private static ((int Row, int Col) Start, (int Row, int Col) End) FindWordRange(
        Cell[,] cells,
        int row,
        int col)
    {
        int cols = cells.GetLength(1);
        int startCategory = Category(cells[row, col].Character);

        int s = col;
        while (s > 0)
        {
            int prev = s - 1;

            // Treat continuation cells as part of the same glyph as their lead.
            if (cells[row, prev].Character is null)
            {
                if (prev == 0)
                {
                    break;
                }

                // Peek at the lead cell category.
                int cat = Category(cells[row, prev - 1].Character);
                if (cat != startCategory)
                {
                    break;
                }

                s = prev - 1;
                continue;
            }

            if (Category(cells[row, prev].Character) != startCategory)
            {
                break;
            }

            s = prev;
        }

        int e = col;
        while (e < cols - 1)
        {
            int next = e + 1;

            // Continuation of the current glyph: keep going.
            if (cells[row, next].Character is null)
            {
                e = next;
                continue;
            }

            if (Category(cells[row, next].Character) != startCategory)
            {
                break;
            }

            e = next;
        }

        return ((row, s), (row, e));
    }

    private static int Category(string? ch)
    {
        if (string.IsNullOrEmpty(ch) || ch == " ")
        {
            return 0;
        }

        char c = ch[0];
        if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '/' || c == ':' || c > 0x7F)
        {
            return 1;
        }

        return 2;
    }
}
