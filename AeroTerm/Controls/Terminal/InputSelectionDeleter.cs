// <copyright file="InputSelectionDeleter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using System.Text;
using AeroTerm.Pty;

/// <summary>
/// Computes the keystroke sequence required to ask the shell to delete a
/// span of the user's current input that the user has selected with the
/// mouse. Strict mode: only acts when shell-integration prompt marks
/// (OSC 133 / 633) confirm the selection lies inside the active command
/// line; otherwise reports "ineligible" and the caller is expected to
/// no-op.
/// </summary>
/// <remarks>
/// The terminal does not own the input line buffer (readline, zle,
/// PSReadLine, etc.) — the shell does. This helper therefore emits a
/// stream of <c>Left</c> arrows followed by <c>Backspace</c> presses
/// that drives the shell's own line editor. Buffer cells are not
/// mutated; the shell's echo redraws the line.
/// </remarks>
internal static class InputSelectionDeleter
{
    /// <summary>
    /// Computes the byte sequence to write to the PTY in order to delete
    /// the (clipped-to-input) selection from the shell's current command
    /// line.
    /// </summary>
    /// <param name="selection">The active selection.</param>
    /// <param name="rows">A row source that exposes both scrollback and
    /// the live grid in absolute-row coordinates.</param>
    /// <param name="marks">The prompt marks registry.</param>
    /// <param name="scrollbackCount">Number of scrollback rows in the
    /// addressable buffer at snapshot time. The live grid starts at
    /// absolute row <paramref name="scrollbackCount"/>.</param>
    /// <param name="liveRows">Live grid row count.</param>
    /// <param name="liveCursorRow">0-based cursor row within the live
    /// grid.</param>
    /// <param name="liveCursorCol">0-based cursor column within the live
    /// grid.</param>
    /// <param name="isAltBuffer">True when the alternate screen buffer
    /// is active (full-screen TUIs); deletion is unsupported there.</param>
    /// <param name="applicationCursorKeys">DECCKM mode flag, used to
    /// pick the correct encoding for the <c>Left</c> arrow.</param>
    /// <param name="keystrokes">On success, the bytes to write to the
    /// PTY. Otherwise <see cref="System.Array.Empty{T}"/>.</param>
    /// <returns><see langword="true"/> if a delete is eligible and the
    /// resulting keystroke sequence is non-empty; <see langword="false"/>
    /// otherwise.</returns>
    public static bool TryBuildDeleteKeystrokes(
        TerminalSelection selection,
        ITerminalRowSource rows,
        PromptMarksRegistry marks,
        int scrollbackCount,
        int liveRows,
        int liveCursorRow,
        int liveCursorCol,
        bool isAltBuffer,
        bool applicationCursorKeys,
        out byte[] keystrokes)
    {
        keystrokes = Array.Empty<byte>();

        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(marks);

        if (isAltBuffer || selection.IsEmpty || liveRows <= 0)
        {
            return false;
        }

        // Find the most recent navigable mark (B/C/D). If it is not a
        // CommandStart (B) the user is not currently typing a command.
        // PromptStart (A) marks are ignored: the input span is rooted
        // at B, and B always follows the most recent A.
        PromptMark? cmdStart = FindActiveCommandStart(marks);
        if (cmdStart is null)
        {
            return false;
        }

        // CommandStart row must lie on the (live) grid that the selection
        // refers to. If the scrollback ring has evicted the row since
        // the mark was captured, AbsoluteRow is now stale and we cannot
        // trust it; bail out rather than guess.
        int cmdAbsRow = cmdStart.AbsoluteRow;
        int cmdCol = cmdStart.Column;
        if (cmdAbsRow < scrollbackCount || cmdAbsRow >= scrollbackCount + liveRows)
        {
            return false;
        }

        int cursorAbsRow = scrollbackCount + liveCursorRow;

        // Shell hasn't yet advanced past the prompt: empty input.
        if (LexCompare((cmdAbsRow, cmdCol), (cursorAbsRow, liveCursorCol)) >= 0)
        {
            return false;
        }

        var (sr, sc, er, ec) = selection.GetNormalizedRange();

        // Selection end is inclusive in TerminalSelection, but the input
        // span we walk is half-open [cmdStart .. cursor). Convert the
        // selection's inclusive end into an exclusive end for clipping.
        var selStart = (Row: sr, Col: sc);
        var selEndExcl = NextCellExclusive(rows, er, ec);

        var inputStart = (Row: cmdAbsRow, Col: cmdCol);
        var inputEndExcl = (Row: cursorAbsRow, Col: liveCursorCol);

        // Clip the selection to the input span.
        var effStart = LexMax(selStart, inputStart);
        var effEndExcl = LexMin(selEndExcl, inputEndExcl);
        if (LexCompare(effStart, effEndExcl) >= 0)
        {
            return false;
        }

        // Walk the input span in reading order and count grapheme steps.
        // A grapheme step is any cell with a non-null Character; wide-glyph
        // continuation cells (Character == null) are skipped because they
        // share a logical character with their lead cell.
        int inputBefore = 0;
        int selLen = 0;
        int afterSel = 0;

        for (int r = inputStart.Row; r <= inputEndExcl.Row; r++)
        {
            var rowCells = rows.GetRow(r);
            int width = rowCells.Length;
            int colStart = r == inputStart.Row ? Math.Clamp(inputStart.Col, 0, Math.Max(0, width)) : 0;
            int colEndExcl = r == inputEndExcl.Row ? Math.Clamp(inputEndExcl.Col, 0, width) : width;

            for (int c = colStart; c < colEndExcl; c++)
            {
                if (rowCells[c].Character is null)
                {
                    continue;
                }

                var pos = (Row: r, Col: c);
                int cmpStart = LexCompare(pos, effStart);
                int cmpEnd = LexCompare(pos, effEndExcl);

                if (cmpStart < 0)
                {
                    inputBefore++;
                }
                else if (cmpEnd < 0)
                {
                    selLen++;
                }
                else
                {
                    afterSel++;
                }
            }
        }

        if (selLen == 0)
        {
            return false;
        }

        keystrokes = BuildKeystrokes(afterSel, selLen, applicationCursorKeys);
        return keystrokes.Length > 0;
    }

    private static PromptMark? FindActiveCommandStart(PromptMarksRegistry marks)
    {
        var list = marks.Marks;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var m = list[i];
            switch (m.Kind)
            {
                case PromptMarkKind.CommandStart:
                    return m;
                case PromptMarkKind.OutputStart:
                case PromptMarkKind.CommandEnd:
                    return null;
            }

            // PromptStart (A) is transparent: the relevant span begins at
            // the next B. Keep walking back in case marks arrived in a
            // weird order; in practice we will hit B or run out.
        }

        return null;
    }

    private static (int Row, int Col) NextCellExclusive(ITerminalRowSource rows, int row, int col)
    {
        var rowCells = rows.GetRow(row);
        int width = rowCells.Length;
        if (col + 1 < width)
        {
            return (row, col + 1);
        }

        return (row + 1, 0);
    }

    private static int LexCompare((int Row, int Col) a, (int Row, int Col) b)
    {
        int d = a.Row - b.Row;
        return d != 0 ? d : a.Col - b.Col;
    }

    private static (int Row, int Col) LexMax((int Row, int Col) a, (int Row, int Col) b)
        => LexCompare(a, b) >= 0 ? a : b;

    private static (int Row, int Col) LexMin((int Row, int Col) a, (int Row, int Col) b)
        => LexCompare(a, b) <= 0 ? a : b;

    private static byte[] BuildKeystrokes(int leftCount, int backspaceCount, bool applicationCursorKeys)
    {
        string leftSeq = leftCount > 0
            ? TerminalInputEncoder.Encode("<Left>", applicationCursorKeys)
            : string.Empty;
        string bsSeq = TerminalInputEncoder.Encode("<BS>");

        // Pre-size: each repetition appends the corresponding sequence in
        // UTF-8. ASCII-only sequences so byte length == char length.
        var sb = new StringBuilder((leftSeq.Length * leftCount) + (bsSeq.Length * backspaceCount));
        for (int i = 0; i < leftCount; i++)
        {
            sb.Append(leftSeq);
        }

        for (int i = 0; i < backspaceCount; i++)
        {
            sb.Append(bsSeq);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
