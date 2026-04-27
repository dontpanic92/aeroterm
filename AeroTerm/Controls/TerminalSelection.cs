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
/// Tracks a terminal text selection over the addressable buffer in
/// absolute-row coordinates. Absolute row 0 is the oldest scrollback
/// row when present, otherwise the top of the live grid; rows
/// <c>[ScrollbackCount, ScrollbackCount + LiveRows)</c> reference the
/// live screen.
/// </summary>
/// <remarks>
/// The class itself is pure — it knows nothing about Avalonia input,
/// the PTY, or rendering. Owners are responsible for providing a
/// suitable <see cref="ITerminalRowSource"/> snapshot for each call,
/// for shifting/clamping the selection when the scrollback ring evicts
/// rows (<see cref="Shift"/>, <see cref="ClampRows"/>), and for clearing
/// it on alt-buffer switch / full reset.
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
    /// Gets the fixed endpoint of the selection in absolute-row coords.
    /// </summary>
    public (int Row, int Col) Anchor { get; private set; }

    /// <summary>
    /// Gets the moving endpoint of the selection in absolute-row coords.
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
    /// <param name="absRow">Absolute row index.</param>
    /// <param name="col">Column index.</param>
    /// <param name="rows">Row source for wide-char canonicalization.</param>
    public void BeginCharacter(int absRow, int col, ITerminalRowSource rows)
    {
        var p = CanonicalizeEndpoint(rows, absRow, col);
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
    /// <param name="absRow">Absolute row index.</param>
    /// <param name="col">Column index.</param>
    /// <param name="rows">Row source.</param>
    public void BeginWord(int absRow, int col, ITerminalRowSource rows)
    {
        var p = CanonicalizeEndpoint(rows, absRow, col);
        var (s, e) = FindWordRange(rows, p.Row, p.Col);
        this.Mode = TerminalSelectionMode.Word;
        this.boundaryAnchorStart = s;
        this.boundaryAnchorEnd = e;
        this.Anchor = s;
        this.Active = e;
    }

    /// <summary>
    /// Begins a line-granularity selection covering the full row.
    /// </summary>
    /// <param name="absRow">Absolute row index.</param>
    /// <param name="rows">Row source.</param>
    public void BeginLine(int absRow, ITerminalRowSource rows)
    {
        int width = LineWidth(rows, absRow);
        var s = (absRow, 0);
        var e = (absRow, Math.Max(0, width - 1));
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
    /// <param name="absRow">Absolute row index.</param>
    /// <param name="col">Column index.</param>
    /// <param name="rows">Row source.</param>
    public void ExtendTo(int absRow, int col, ITerminalRowSource rows)
    {
        if (this.Mode == TerminalSelectionMode.None)
        {
            return;
        }

        var p = CanonicalizeEndpoint(rows, absRow, col);

        if (this.Mode == TerminalSelectionMode.Character)
        {
            this.Active = p;
            return;
        }

        if (this.Mode == TerminalSelectionMode.Word)
        {
            var (s, e) = FindWordRange(rows, p.Row, p.Col);
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
        int width = LineWidth(rows, p.Row);
        if (Compare((p.Row, 0), this.boundaryAnchorStart) < 0)
        {
            this.Anchor = this.boundaryAnchorEnd;
            this.Active = (p.Row, 0);
        }
        else
        {
            this.Anchor = this.boundaryAnchorStart;
            this.Active = (p.Row, Math.Max(0, width - 1));
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
    /// Shifts every absolute row reference in the selection by
    /// <paramref name="rowDelta"/>. Negative deltas move the selection
    /// upward (used to compensate for scrollback eviction). Caller is
    /// responsible for following up with <see cref="ClampRows"/> if the
    /// shift can drive endpoints out of range.
    /// </summary>
    /// <param name="rowDelta">Row delta to apply.</param>
    public void Shift(int rowDelta)
    {
        if (this.Mode == TerminalSelectionMode.None || rowDelta == 0)
        {
            return;
        }

        this.Anchor = (this.Anchor.Row + rowDelta, this.Anchor.Col);
        this.Active = (this.Active.Row + rowDelta, this.Active.Col);
        this.boundaryAnchorStart = (this.boundaryAnchorStart.Row + rowDelta, this.boundaryAnchorStart.Col);
        this.boundaryAnchorEnd = (this.boundaryAnchorEnd.Row + rowDelta, this.boundaryAnchorEnd.Col);
    }

    /// <summary>
    /// Clamps the selection to <c>[minRow, maxRow]</c>. Endpoints below
    /// <paramref name="minRow"/> snap to <paramref name="minRow"/> at
    /// column 0; endpoints above <paramref name="maxRow"/> snap to
    /// <paramref name="maxRow"/>. If the entire selection sits outside
    /// the range it is cleared.
    /// </summary>
    /// <param name="minRow">Inclusive lower bound on absolute rows.</param>
    /// <param name="maxRow">Inclusive upper bound on absolute rows.</param>
    public void ClampRows(int minRow, int maxRow)
    {
        if (this.Mode == TerminalSelectionMode.None)
        {
            return;
        }

        if (maxRow < minRow)
        {
            this.Clear();
            return;
        }

        int aRow = this.Anchor.Row;
        int bRow = this.Active.Row;
        int hi = Math.Max(aRow, bRow);
        int lo = Math.Min(aRow, bRow);
        if (hi < minRow || lo > maxRow)
        {
            this.Clear();
            return;
        }

        this.Anchor = ClampEndpoint(this.Anchor, minRow, maxRow);
        this.Active = ClampEndpoint(this.Active, minRow, maxRow);
        this.boundaryAnchorStart = ClampEndpoint(this.boundaryAnchorStart, minRow, maxRow);
        this.boundaryAnchorEnd = ClampEndpoint(this.boundaryAnchorEnd, minRow, maxRow);
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
    /// Tests whether the given absolute cell lies within the current
    /// selection. Wide glyph continuation cells (Character == null) are
    /// reported as selected when their leading cell is selected.
    /// </summary>
    /// <param name="absRow">Absolute row index.</param>
    /// <param name="col">Column index.</param>
    /// <returns>True if the cell is within the selection.</returns>
    public bool Contains(int absRow, int col)
    {
        if (this.Mode == TerminalSelectionMode.None)
        {
            return false;
        }

        var (sr, sc, er, ec) = this.GetNormalizedRange();
        if (absRow < sr || absRow > er)
        {
            return false;
        }

        if (sr == er)
        {
            return col >= sc && col <= ec;
        }

        if (absRow == sr)
        {
            return col >= sc;
        }

        if (absRow == er)
        {
            return col <= ec;
        }

        return true;
    }

    /// <summary>
    /// Extracts the selected text from the given row source. Fully
    /// selected rows are right-trimmed of blank cells and separated by
    /// <c>\n</c>; the final row preserves its trailing content up to the
    /// last selected column. Wide glyphs are emitted once.
    /// </summary>
    /// <param name="rows">The row source.</param>
    /// <returns>The plain-text selection content.</returns>
    public string CopyText(ITerminalRowSource rows)
    {
        if (this.Mode == TerminalSelectionMode.None || rows.RowCount == 0)
        {
            return string.Empty;
        }

        var (sr, sc, er, ec) = this.GetNormalizedRange();
        sr = Math.Clamp(sr, 0, rows.RowCount - 1);
        er = Math.Clamp(er, 0, rows.RowCount - 1);

        var sb = new StringBuilder();
        for (int r = sr; r <= er; r++)
        {
            var row = rows.GetRow(r);
            int width = row.Length;
            if (width == 0)
            {
                if (r < er)
                {
                    sb.Append('\n');
                }

                continue;
            }

            int cStart = r == sr ? Math.Clamp(sc, 0, width - 1) : 0;
            int cEnd = r == er ? Math.Clamp(ec, 0, width - 1) : width - 1;

            var rowBuilder = new StringBuilder();
            for (int c = cStart; c <= cEnd; c++)
            {
                string? ch = row[c].Character;

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

    // --------------------------------------------------------------------
    // Cell[,] adapter overloads. These wrap the supplied 2D grid in a
    // Cell2DRowSource so callers (notably the existing test suite) can
    // continue to express selections in plain visible-grid coordinates.
    // --------------------------------------------------------------------

    /// <summary>Cell[,] adapter for <see cref="BeginCharacter(int, int, ITerminalRowSource)"/>.</summary>
    /// <param name="row">Row index in the supplied grid.</param>
    /// <param name="col">Column index.</param>
    /// <param name="cells">Visible-grid cells.</param>
    public void BeginCharacter(int row, int col, Cell[,] cells)
        => this.BeginCharacter(row, col, new Cell2DRowSource(cells));

    /// <summary>Cell[,] adapter for <see cref="BeginWord(int, int, ITerminalRowSource)"/>.</summary>
    /// <param name="row">Row index in the supplied grid.</param>
    /// <param name="col">Column index.</param>
    /// <param name="cells">Visible-grid cells.</param>
    public void BeginWord(int row, int col, Cell[,] cells)
        => this.BeginWord(row, col, new Cell2DRowSource(cells));

    /// <summary>Cell[,] adapter for <see cref="BeginLine(int, ITerminalRowSource)"/>.</summary>
    /// <param name="row">Row index in the supplied grid.</param>
    /// <param name="cells">Visible-grid cells.</param>
    public void BeginLine(int row, Cell[,] cells)
        => this.BeginLine(row, new Cell2DRowSource(cells));

    /// <summary>Cell[,] adapter for <see cref="ExtendTo(int, int, ITerminalRowSource)"/>.</summary>
    /// <param name="row">Row index in the supplied grid.</param>
    /// <param name="col">Column index.</param>
    /// <param name="cells">Visible-grid cells.</param>
    public void ExtendTo(int row, int col, Cell[,] cells)
        => this.ExtendTo(row, col, new Cell2DRowSource(cells));

    /// <summary>Cell[,] adapter for <see cref="CopyText(ITerminalRowSource)"/>.</summary>
    /// <param name="cells">Visible-grid cells.</param>
    /// <returns>The selected text.</returns>
    public string CopyText(Cell[,] cells)
        => this.CopyText(new Cell2DRowSource(cells));

    private static (int Row, int Col) ClampEndpoint((int Row, int Col) p, int minRow, int maxRow)
    {
        if (p.Row < minRow)
        {
            return (minRow, 0);
        }

        if (p.Row > maxRow)
        {
            return (maxRow, p.Col);
        }

        return p;
    }

    private static int Compare((int Row, int Col) a, (int Row, int Col) b)
    {
        int d = a.Row - b.Row;
        return d != 0 ? d : a.Col - b.Col;
    }

    private static int LineWidth(ITerminalRowSource rows, int absRow)
    {
        var row = rows.GetRow(absRow);
        return row.Length > 0 ? row.Length : rows.Cols;
    }

    private static (int Row, int Col) CanonicalizeEndpoint(ITerminalRowSource rows, int absRow, int col)
    {
        if (rows.RowCount == 0)
        {
            return (absRow, col);
        }

        absRow = Math.Clamp(absRow, 0, rows.RowCount - 1);
        var row = rows.GetRow(absRow);
        int width = row.Length;
        if (width == 0)
        {
            return (absRow, Math.Max(0, col));
        }

        col = Math.Clamp(col, 0, width - 1);

        // If the pointer landed on the second half of a wide glyph, move
        // the endpoint back onto the leading cell so range math and copy
        // behave consistently.
        if (row[col].Character is null && col > 0)
        {
            col--;
        }

        return (absRow, col);
    }

    private static ((int Row, int Col) Start, (int Row, int Col) End) FindWordRange(
        ITerminalRowSource rows,
        int absRow,
        int col)
    {
        var row = rows.GetRow(absRow);
        int width = row.Length;
        if (width == 0)
        {
            return ((absRow, col), (absRow, col));
        }

        col = Math.Clamp(col, 0, width - 1);
        int startCategory = Category(row[col].Character);

        int s = col;
        while (s > 0)
        {
            int prev = s - 1;

            // Treat continuation cells as part of the same glyph as their lead.
            if (row[prev].Character is null)
            {
                if (prev == 0)
                {
                    break;
                }

                // Peek at the lead cell category.
                int cat = Category(row[prev - 1].Character);
                if (cat != startCategory)
                {
                    break;
                }

                s = prev - 1;
                continue;
            }

            if (Category(row[prev].Character) != startCategory)
            {
                break;
            }

            s = prev;
        }

        int e = col;
        while (e < width - 1)
        {
            int next = e + 1;

            // Continuation of the current glyph: keep going.
            if (row[next].Character is null)
            {
                e = next;
                continue;
            }

            if (Category(row[next].Character) != startCategory)
            {
                break;
            }

            e = next;
        }

        return ((absRow, s), (absRow, e));
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
