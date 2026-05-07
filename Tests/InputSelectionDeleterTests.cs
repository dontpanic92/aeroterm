// <copyright file="InputSelectionDeleterTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Text;
using AeroTerm.Controls;
using AeroTerm.Controls.Terminal;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Unit tests for <see cref="InputSelectionDeleter"/>. The helper is
/// pure: it consumes a row source + selection + prompt marks and emits
/// the keystroke byte stream that must be sent to the shell to delete
/// the (clipped-to-input) selection.
/// </summary>
public class InputSelectionDeleterTests
{
    /// <summary>
    /// Selection in the middle of the active input emits N Left arrows
    /// followed by M backspaces, where N = chars after selection and
    /// M = selection length.
    /// </summary>
    [Test]
    public void Eligible_SelectionInMiddleOfInput_BuildsLeftThenBackspace()
    {
        // Layout (single live row, no scrollback):
        // "$ echo hello"
        //  0123456789...
        // CommandStart at col 2 ("e" of echo). Cursor at col 12 (just past 'o').
        var rows = MakeLive(0, "$ echo hello");
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, AbsoluteRow: 0, Column: 2, ExitCode: null, CurrentDirectory: null));

        var sel = new TerminalSelection();

        // Select "hello" (cols 7..11 inclusive). NB ExtendTo's Active is
        // inclusive in the selection model.
        sel.BeginCharacter(0, 7, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 11, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel,
            rows,
            marks,
            scrollbackCount: 0,
            liveRows: 1,
            liveCursorRow: 0,
            liveCursorCol: 12,
            isAltBuffer: false,
            applicationCursorKeys: false,
            out byte[] keys);

        Assert.That(ok, Is.True);

        // 5 chars selected ("hello"), 0 chars after the selection (selection
        // ends at cursor): expect 0 lefts + 5 backspaces.
        AssertSequence(keys, leftCount: 0, backspaceCount: 5, applicationCursorKeys: false);
    }

    /// <summary>
    /// Selection in the middle (with text after): emits Lefts equal to
    /// the input chars between selection-end and cursor, then backspaces.
    /// </summary>
    [Test]
    public void Eligible_SelectionFollowedByInput_EmitsLeftAndBackspace()
    {
        // "$ echo hello world", CommandStart at col 2, cursor at col 18.
        var rows = MakeLive(0, "$ echo hello world");
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();

        // Select "hello" at cols 7..11.
        sel.BeginCharacter(0, 7, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 11, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 18, false, false, out byte[] keys);

        Assert.That(ok, Is.True);

        // After "hello" there are 6 input chars (" world"). Selection
        // length = 5.
        AssertSequence(keys, leftCount: 6, backspaceCount: 5, applicationCursorKeys: false);
    }

    /// <summary>
    /// Selection at the very start of input: 0 Lefts before, full
    /// remainder must be moved over. Lefts = chars after selection end.
    /// </summary>
    [Test]
    public void Eligible_SelectionAtStartOfInput()
    {
        // "$ echo hello", CommandStart at col 2 ("e"), cursor at col 12.
        var rows = MakeLive(0, "$ echo hello");
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();

        // Select "echo" at cols 2..5.
        sel.BeginCharacter(0, 2, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 5, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 12, false, false, out byte[] keys);

        Assert.That(ok, Is.True);

        // 4 chars selected, 6 chars after (" hello").
        AssertSequence(keys, leftCount: 6, backspaceCount: 4, applicationCursorKeys: false);
    }

    /// <summary>
    /// Selection straddling the CommandStart boundary is clipped to the
    /// input portion only.
    /// </summary>
    [Test]
    public void Eligible_SelectionStraddlesCommandStart_ClipsToInput()
    {
        // "$ echo hello", CommandStart at col 2.
        var rows = MakeLive(0, "$ echo hello");
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();

        // Select cols 0..5 -> "$ echo" -> clipped to "echo" (4 input chars).
        sel.BeginCharacter(0, 0, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 5, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 12, false, false, out byte[] keys);

        Assert.That(ok, Is.True);
        AssertSequence(keys, leftCount: 6, backspaceCount: 4, applicationCursorKeys: false);
    }

    /// <summary>
    /// Selection that extends past the cursor is clipped at the cursor.
    /// </summary>
    [Test]
    public void Eligible_SelectionPastCursor_ClipsAtCursor()
    {
        // 16-col grid; "$ echo hello" then trailing blanks.
        var rows = MakeLive(0, PadRight("$ echo hello", 16));
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();

        // Select cols 7..15 -> "hello" + 4 trailing blanks; clipped to
        // cols 7..11 = "hello" (5 chars).
        sel.BeginCharacter(0, 7, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 15, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 12, false, false, out byte[] keys);

        Assert.That(ok, Is.True);
        AssertSequence(keys, leftCount: 0, backspaceCount: 5, applicationCursorKeys: false);
    }

    /// <summary>No prompt marks at all → no-op.</summary>
    [Test]
    public void NoMarks_NoOp()
    {
        var rows = MakeLive(0, "$ echo hello");
        var marks = new PromptMarksRegistry();
        var sel = new TerminalSelection();
        sel.BeginCharacter(0, 7, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 11, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 12, false, false, out byte[] keys);

        Assert.That(ok, Is.False);
        Assert.That(keys, Is.Empty);
    }

    /// <summary>
    /// Most recent navigable mark is OutputStart (command running, no
    /// active input) → no-op.
    /// </summary>
    [Test]
    public void OutputRunning_NoOp()
    {
        var rows = MakeLive(0, "$ echo hello");
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));
        marks.Add(new PromptMark(PromptMarkKind.OutputStart, 0, 12, null, null));

        var sel = new TerminalSelection();
        sel.BeginCharacter(0, 7, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 11, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 12, false, false, out byte[] keys);

        Assert.That(ok, Is.False);
    }

    /// <summary>
    /// Most recent navigable mark is CommandEnd (after a finished
    /// command, before the next prompt) → no-op.
    /// </summary>
    [Test]
    public void AfterCommandEnd_NoOp()
    {
        var rows = MakeLive(0, "$ echo hello");
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));
        marks.Add(new PromptMark(PromptMarkKind.OutputStart, 0, 12, null, null));
        marks.Add(new PromptMark(PromptMarkKind.CommandEnd, 0, 12, 0, null));

        var sel = new TerminalSelection();
        sel.BeginCharacter(0, 7, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 11, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 12, false, false, out byte[] keys);

        Assert.That(ok, Is.False);
    }

    /// <summary>Alt buffer active → no-op (TUIs own input differently).</summary>
    [Test]
    public void AltBuffer_NoOp()
    {
        var rows = MakeLive(0, "$ echo hello");
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();
        sel.BeginCharacter(0, 7, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 11, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 12, isAltBuffer: true, false, out byte[] keys);

        Assert.That(ok, Is.False);
    }

    /// <summary>
    /// Selection lies entirely in scrollback (above the live grid):
    /// clipped range becomes empty → no-op.
    /// </summary>
    [Test]
    public void SelectionInScrollback_NoOp()
    {
        // 1 scrollback row, then 1 live row. CommandStart on the live row.
        var rows = new ListRowSource(
            new[] { "old output line", "$ echo hello" },
            liveCols: 12);
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, AbsoluteRow: 1, Column: 2, null, null));

        var sel = new TerminalSelection();

        // Select something on the scrollback row (absRow 0).
        sel.BeginCharacter(0, 0, rows);
        sel.ExtendTo(0, 5, rows);

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel,
            rows,
            marks,
            scrollbackCount: 1,
            liveRows: 1,
            liveCursorRow: 0,
            liveCursorCol: 12,
            isAltBuffer: false,
            applicationCursorKeys: false,
            out byte[] keys);

        Assert.That(ok, Is.False);
    }

    /// <summary>
    /// CommandStart row has been evicted from scrollback (its absolute
    /// row is below the current scrollback floor). Defensive → no-op.
    /// </summary>
    [Test]
    public void StaleCommandStartBelowLiveGrid_NoOp()
    {
        // Pretend scrollback ring evicted the row that hosts the mark:
        // mark.AbsoluteRow=0 but scrollbackCount=5 (mark is "below" the
        // retained range).
        var rows = new ListRowSource(
            new[] { "$ echo hello" },
            liveCols: 12);

        // For this scenario rows reports a single live row at absRow 5
        // — but our row source is simple: pretend liveStart maps to
        // absRow 5 by claiming scrollbackCount=5. The row source itself
        // only has one row so we won't actually be asked about earlier
        // rows; the early eligibility check rejects first.
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();
        sel.BeginCharacter(0, 7, rows);
        sel.ExtendTo(0, 11, rows);

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel,
            rows,
            marks,
            scrollbackCount: 5,
            liveRows: 1,
            liveCursorRow: 0,
            liveCursorCol: 12,
            isAltBuffer: false,
            applicationCursorKeys: false,
            out byte[] keys);

        Assert.That(ok, Is.False);
    }

    /// <summary>
    /// A wide glyph in the input counts as exactly one grapheme step;
    /// its continuation cell is skipped during the walk and not double-
    /// counted toward backspace count.
    /// </summary>
    [Test]
    public void WideGlyphInSelection_CountedOnce()
    {
        // Build "$ a✓b": '$' ' ' 'a' '✓' (lead) (cont) 'b' across 7 cols.
        var cells = new Cell[1, 7];
        cells[0, 0] = new Cell("$", default);
        cells[0, 1] = new Cell(" ", default);
        cells[0, 2] = new Cell("a", default);
        cells[0, 3] = new Cell("✓", default); // wide lead
        cells[0, 4] = new Cell(null, default); // wide continuation
        cells[0, 5] = new Cell("b", default);
        cells[0, 6] = new Cell(" ", default);

        var rows = new Cell2DRowSource(cells);
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();

        // Select "a✓b" -> cols 2..5 (b at col 5; selection inclusive).
        sel.BeginCharacter(0, 2, cells);
        sel.ExtendTo(0, 5, cells);

        // Cursor just after 'b': col 6.
        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 6, false, false, out byte[] keys);

        Assert.That(ok, Is.True);

        // 3 input graphemes selected ("a", "✓", "b"); 0 chars after
        // selection -> 0 lefts + 3 backspaces.
        AssertSequence(keys, leftCount: 0, backspaceCount: 3, applicationCursorKeys: false);
    }

    /// <summary>
    /// Wrapped input that spans two live rows: walk crosses the row
    /// boundary correctly and counts one step per non-continuation cell
    /// across both rows.
    /// </summary>
    [Test]
    public void WrappedMultiLineInput_StepsCrossRows()
    {
        // 2 live rows, 6 cols: "$ abcd" / "efghij". CommandStart at row
        // 0 col 2 ("a"). Cursor at row 1 col 4 ("efgh|ij").
        var rows = new ListRowSource(
            new[]
            {
                "$ abcd",
                "efghij",
            },
            liveCols: 6);
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();

        // Select "cdef" -> from row 0 col 4 to row 1 col 1.
        sel.BeginCharacter(0, 4, rows);
        sel.ExtendTo(1, 1, rows);

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel,
            rows,
            marks,
            scrollbackCount: 0,
            liveRows: 2,
            liveCursorRow: 1,
            liveCursorCol: 4,
            isAltBuffer: false,
            applicationCursorKeys: false,
            out byte[] keys);

        Assert.That(ok, Is.True);

        // Input chars total: "abcdefgh" = 8. Selection "cdef" = 4 chars.
        // After selection: "gh" = 2 chars.
        AssertSequence(keys, leftCount: 2, backspaceCount: 4, applicationCursorKeys: false);
    }

    /// <summary>DECCKM (application cursor keys) on changes the Left encoding.</summary>
    [Test]
    public void ApplicationCursorKeys_UsesAlternateLeftEncoding()
    {
        var rows = MakeLive(0, "$ echo hello world");
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();
        sel.BeginCharacter(0, 7, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 11, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 18, false, applicationCursorKeys: true, out byte[] keys);

        Assert.That(ok, Is.True);
        AssertSequence(keys, leftCount: 6, backspaceCount: 5, applicationCursorKeys: true);
    }

    /// <summary>Empty selection: no-op.</summary>
    [Test]
    public void EmptySelection_NoOp()
    {
        var rows = MakeLive(0, "$ echo hello");
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 12, false, false, out byte[] keys);

        Assert.That(ok, Is.False);
        Assert.That(keys, Is.Empty);
    }

    /// <summary>Cursor at CommandStart (empty input): no-op.</summary>
    [Test]
    public void EmptyInput_NoOp()
    {
        // Cursor sits exactly at CommandStart -> no input chars.
        var rows = MakeLive(0, "$ ");
        var marks = new PromptMarksRegistry();
        marks.Add(new PromptMark(PromptMarkKind.CommandStart, 0, 2, null, null));

        var sel = new TerminalSelection();
        sel.BeginCharacter(0, 0, rows.GetUnderlyingCells());
        sel.ExtendTo(0, 1, rows.GetUnderlyingCells());

        bool ok = InputSelectionDeleter.TryBuildDeleteKeystrokes(
            sel, rows, marks, 0, 1, 0, 2, false, false, out byte[] keys);

        Assert.That(ok, Is.False);
    }

    private static GridRowSource MakeLive(int scrollbackCount, string row)
    {
        return new GridRowSource(scrollbackCount, row);
    }

    private static string PadRight(string s, int width) => s.PadRight(width, ' ');

    private static void AssertSequence(byte[] actual, int leftCount, int backspaceCount, bool applicationCursorKeys)
    {
        string leftSeq = TerminalInputEncoder.Encode("<Left>", applicationCursorKeys);
        string bsSeq = TerminalInputEncoder.Encode("<BS>");
        var sb = new StringBuilder();
        for (int i = 0; i < leftCount; i++)
        {
            sb.Append(leftSeq);
        }

        for (int i = 0; i < backspaceCount; i++)
        {
            sb.Append(bsSeq);
        }

        byte[] expected = Encoding.UTF8.GetBytes(sb.ToString());
        Assert.That(actual, Is.EqualTo(expected));
    }

    /// <summary>
    /// Single-live-row row source. Exposes the underlying Cell[,] grid
    /// so tests can drive <see cref="TerminalSelection"/>'s grid-typed
    /// overloads.
    /// </summary>
    private sealed class GridRowSource : ITerminalRowSource
    {
        private readonly Cell[,] cells;

        public GridRowSource(int scrollbackCount, string liveRow)
        {
            this.ScrollbackCount = scrollbackCount;
            this.cells = new Cell[1, liveRow.Length];
            for (int c = 0; c < liveRow.Length; c++)
            {
                this.cells[0, c] = new Cell(liveRow[c].ToString(), default);
            }
        }

        public int ScrollbackCount { get; }

        public int RowCount => 1;

        public int Cols => this.cells.GetLength(1);

        public Cell[,] GetUnderlyingCells() => this.cells;

        public Cell[] GetRow(int absRow)
        {
            if (absRow < 0 || absRow >= this.RowCount)
            {
                return Array.Empty<Cell>();
            }

            int width = this.cells.GetLength(1);
            var row = new Cell[width];
            for (int c = 0; c < width; c++)
            {
                row[c] = this.cells[absRow, c];
            }

            return row;
        }
    }

    /// <summary>
    /// Multi-row row source backed by a list of plain strings. All cells
    /// are single-width.
    /// </summary>
    private sealed class ListRowSource : ITerminalRowSource
    {
        private readonly Cell[][] rows;

        public ListRowSource(string[] rows, int liveCols)
        {
            this.rows = new Cell[rows.Length][];
            for (int r = 0; r < rows.Length; r++)
            {
                int width = Math.Max(rows[r].Length, liveCols);
                var arr = new Cell[width];
                for (int c = 0; c < width; c++)
                {
                    char ch = c < rows[r].Length ? rows[r][c] : ' ';
                    arr[c] = new Cell(ch.ToString(), default);
                }

                this.rows[r] = arr;
            }

            this.Cols = liveCols;
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
