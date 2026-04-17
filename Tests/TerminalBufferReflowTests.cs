// <copyright file="TerminalBufferReflowTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests the logical-line reflow behavior of <see cref="TerminalBuffer.Resize(int, int)"/>
/// on the primary screen. The alternate screen keeps the legacy
/// truncate/pad semantics; both paths are exercised here.
/// </summary>
[TestFixture]
public class TerminalBufferReflowTests
{
    /// <summary>
    /// A no-op resize should not mutate any cell, the cursor, or the
    /// scrollback.
    /// </summary>
    [Test]
    public void Resize_SameDimensions_IsNoOp()
    {
        var buffer = new TerminalBuffer(10, 4);
        PrintString(buffer, "hello");
        var before = ScreenDump(buffer);

        buffer.Resize(10, 4);

        var after = ScreenDump(buffer);
        Assert.That(after, Is.EqualTo(before));
        Assert.That(buffer.CursorCol, Is.EqualTo(5));
        Assert.That(buffer.CursorRow, Is.EqualTo(0));
    }

    /// <summary>
    /// Shrinking columns should re-wrap a long logical line and set the
    /// wrap flag on every row except the last.
    /// </summary>
    [Test]
    public void Resize_ShrinkCols_RewrapsLogicalLine()
    {
        var buffer = new TerminalBuffer(20, 4);
        PrintString(buffer, "ABCDEFGHIJ");

        buffer.Resize(5, 4);

        Assert.That(buffer.Cols, Is.EqualTo(5));
        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("A"));
        Assert.That(screen.Cells[0, 4].Character, Is.EqualTo("E"));
        Assert.That(screen.Cells[1, 0].Character, Is.EqualTo("F"));
        Assert.That(screen.Cells[1, 4].Character, Is.EqualTo("J"));
        Assert.That(buffer.IsRowWrapped(0), Is.True, "row 0 should be marked as wrapped");
        Assert.That(buffer.IsRowWrapped(1), Is.False, "row 1 is the last physical row of the logical line");
    }

    /// <summary>
    /// Growing columns should unwrap a previously wrapped logical line
    /// back into a single physical row.
    /// </summary>
    [Test]
    public void Resize_GrowCols_UnwrapsLogicalLine()
    {
        var buffer = new TerminalBuffer(5, 4);
        PrintString(buffer, "ABCDEFGHIJ");

        // Expect initial wrap.
        Assume.That(buffer.IsRowWrapped(0), Is.True);

        buffer.Resize(10, 4);

        var screen = buffer.GetScreen();
        for (int i = 0; i < 10; i++)
        {
            Assert.That(screen!.Cells[0, i].Character, Is.EqualTo(((char)('A' + i)).ToString()), $"col {i}");
        }

        Assert.That(buffer.IsRowWrapped(0), Is.False, "no longer wrapped after growth");
    }

    /// <summary>
    /// Shrinking the row count should push the oldest rows into
    /// scrollback in FIFO order when the limit is positive.
    /// </summary>
    [Test]
    public void Resize_ShrinkRows_PushesTopContentIntoScrollback()
    {
        var buffer = new TerminalBuffer(5, 5);
        PrintString(buffer, "AAAAA");
        buffer.LineFeed();
        buffer.CarriageReturn();
        PrintString(buffer, "BBBBB");
        buffer.LineFeed();
        buffer.CarriageReturn();
        PrintString(buffer, "CCCCC");
        buffer.LineFeed();
        buffer.CarriageReturn();
        PrintString(buffer, "DDDDD");
        buffer.LineFeed();
        buffer.CarriageReturn();
        PrintString(buffer, "EEEEE");

        buffer.Resize(5, 3);

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(2));
        var oldest = buffer.GetScrollbackLine(0);
        var next = buffer.GetScrollbackLine(1);
        Assert.That(oldest[0].Character, Is.EqualTo("A"));
        Assert.That(next[0].Character, Is.EqualTo("B"));

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("C"));
        Assert.That(screen.Cells[1, 0].Character, Is.EqualTo("D"));
        Assert.That(screen.Cells[2, 0].Character, Is.EqualTo("E"));
    }

    /// <summary>
    /// When the scrollback limit is zero, content that no longer fits
    /// in the live grid should simply be discarded.
    /// </summary>
    [Test]
    public void Resize_ShrinkRows_WithZeroScrollbackLimit_DiscardsTopContent()
    {
        var buffer = new TerminalBuffer(5, 5);
        buffer.ScrollbackLimit = 0;
        PrintString(buffer, "AAAAA");
        buffer.LineFeed();
        buffer.CarriageReturn();
        PrintString(buffer, "BBBBB");
        buffer.LineFeed();
        buffer.CarriageReturn();
        PrintString(buffer, "CCCCC");

        buffer.Resize(5, 2);

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(0));
        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("B"));
        Assert.That(screen.Cells[1, 0].Character, Is.EqualTo("C"));
    }

    /// <summary>
    /// The cursor's logical position should be preserved across a
    /// column-shrinking reflow.
    /// </summary>
    [Test]
    public void Resize_PreservesCursorLogicalPosition()
    {
        var buffer = new TerminalBuffer(80, 5);
        PrintString(buffer, "hello world");

        // cursor now at col 11, row 0.
        Assume.That(buffer.CursorCol, Is.EqualTo(11));

        buffer.Resize(5, 5);

        // "hello world" = 11 chars. 11 / 5 = 2 remainder 1 → row 2, col 1.
        Assert.That(buffer.CursorRow, Is.EqualTo(2));
        Assert.That(buffer.CursorCol, Is.EqualTo(1));
    }

    /// <summary>
    /// A wide glyph that would straddle the new right margin should
    /// wrap to the next physical row, leaving a blank pad cell.
    /// </summary>
    [Test]
    public void Resize_WideGlyph_DoesNotStraddleMargin()
    {
        var buffer = new TerminalBuffer(3, 2);
        buffer.PutChar('A');
        buffer.PutChar('あ');

        // Row 0 is now: A あ (pad). 3 cols: "A","あ",null
        buffer.Resize(2, 3);

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("A"));

        // Col 1 is padding (blank or not a wide lead).
        Assert.That(buffer.IsRowWrapped(0), Is.True);
        Assert.That(screen.Cells[1, 0].Character, Is.EqualTo("あ"));
        Assert.That(screen.Cells[1, 1].Character, Is.Null, "wide continuation");
    }

    /// <summary>
    /// The alternate buffer is deliberately not reflowed; resizing it
    /// should perform a plain truncate/pad, and the primary scrollback
    /// should remain untouched by the operation.
    /// </summary>
    [Test]
    public void Resize_AltBuffer_UsesTruncatePadAndLeavesPrimaryScrollbackAlone()
    {
        var buffer = new TerminalBuffer(6, 3);
        PrintString(buffer, "AAAAAA");
        buffer.LineFeed();
        buffer.CarriageReturn();
        PrintString(buffer, "BBBBBB");
        buffer.LineFeed();
        buffer.CarriageReturn();
        PrintString(buffer, "CCCCCC");
        buffer.LineFeed();
        buffer.CarriageReturn();
        PrintString(buffer, "DDDDDD");

        int scrollbackBefore = buffer.ScrollbackCount;
        Assume.That(scrollbackBefore, Is.GreaterThan(0));

        buffer.SwitchToAlternateBuffer();
        buffer.SetCursorPosition(0, 0);
        PrintString(buffer, "XYZXYZ");

        // Shrink cols on the alt buffer — no reflow, just truncate.
        buffer.Resize(3, 3);

        Assert.That(buffer.IsUsingAltBuffer, Is.True);
        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("X"));
        Assert.That(screen.Cells[0, 1].Character, Is.EqualTo("Y"));
        Assert.That(screen.Cells[0, 2].Character, Is.EqualTo("Z"));

        // Primary's scrollback must not have been reflowed away.
        Assert.That(buffer.ScrollbackCount, Is.EqualTo(scrollbackBefore));
    }

    /// <summary>
    /// When a wrapped row has already been evicted into scrollback, a
    /// reflow on grow should still reconstruct the single logical line
    /// from scrollback + live rows.
    /// </summary>
    [Test]
    public void Resize_PreservesScrollbackWrapFlagAcrossReflow()
    {
        var buffer = new TerminalBuffer(5, 2);
        PrintString(buffer, "ABCDEFGHIJ"); // wraps: "ABCDE" (wrap) / "FGHIJ"

        Assume.That(buffer.IsRowWrapped(0), Is.True);

        // Push first row into scrollback by inserting more newlines.
        buffer.LineFeed();
        buffer.CarriageReturn();
        buffer.LineFeed();
        buffer.CarriageReturn();

        // Now "ABCDE" (wrap) should be the oldest scrollback entry.
        Assume.That(buffer.ScrollbackCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(buffer.IsScrollbackRowWrapped(0), Is.True, "wrap flag preserved into scrollback");

        buffer.Resize(10, 5);

        // After reflow the 10-char logical line should be visible on a
        // single 10-col row in either scrollback or the live grid.
        bool foundSingleRow = false;
        for (int i = 0; i < buffer.ScrollbackCount; i++)
        {
            var row = buffer.GetScrollbackLine(i);
            if (row.Length >= 10 && row[0].Character == "A" && row[9].Character == "J")
            {
                foundSingleRow = true;
                break;
            }
        }

        if (!foundSingleRow)
        {
            var screen = buffer.GetScreen();
            for (int r = 0; r < buffer.Rows; r++)
            {
                if (screen!.Cells[r, 0].Character == "A" && screen.Cells[r, 9].Character == "J")
                {
                    foundSingleRow = true;
                    break;
                }
            }
        }

        Assert.That(foundSingleRow, Is.True, "reflow reconstructs ABCDEFGHIJ as a single 10-col row");
    }

    /// <summary>
    /// Resizing to the same column count but different row count should
    /// not change any wrap-flag state.
    /// </summary>
    [Test]
    public void Resize_SameColsDifferentRows_PreservesWrapFlags()
    {
        var buffer = new TerminalBuffer(5, 4);
        PrintString(buffer, "ABCDEFGHIJ");

        Assume.That(buffer.IsRowWrapped(0), Is.True);
        Assume.That(buffer.IsRowWrapped(1), Is.False);

        buffer.Resize(5, 6);

        Assert.That(buffer.Cols, Is.EqualTo(5));
        Assert.That(buffer.IsRowWrapped(0), Is.True);
        Assert.That(buffer.IsRowWrapped(1), Is.False);

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("A"));
        Assert.That(screen.Cells[1, 4].Character, Is.EqualTo("J"));
    }

    /// <summary>
    /// Explicit newlines should prevent logical-line merging: two
    /// separate LF-terminated lines must remain separate after a
    /// col-shrinking reflow that could otherwise have merged them.
    /// </summary>
    [Test]
    public void Resize_ExplicitNewline_DoesNotMergeIntoWrappedLine()
    {
        var buffer = new TerminalBuffer(10, 4);
        PrintString(buffer, "foo");
        buffer.LineFeed();
        buffer.CarriageReturn();
        PrintString(buffer, "bar");

        buffer.Resize(5, 4);

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("f"));
        Assert.That(screen.Cells[0, 1].Character, Is.EqualTo("o"));
        Assert.That(screen.Cells[0, 2].Character, Is.EqualTo("o"));
        Assert.That(screen.Cells[1, 0].Character, Is.EqualTo("b"));
        Assert.That(screen.Cells[1, 1].Character, Is.EqualTo("a"));
        Assert.That(screen.Cells[1, 2].Character, Is.EqualTo("r"));
        Assert.That(buffer.IsRowWrapped(0), Is.False);
    }

    /// <summary>
    /// An empty buffer (no content, no scrollback) should still
    /// produce a correctly-sized grid after any resize.
    /// </summary>
    [Test]
    public void Resize_EmptyBuffer_BehavesLikeTruncatePad()
    {
        var buffer = new TerminalBuffer(10, 5);
        buffer.Resize(4, 3);

        Assert.That(buffer.Rows, Is.EqualTo(3));
        Assert.That(buffer.Cols, Is.EqualTo(4));
        Assert.That(buffer.ScrollbackCount, Is.EqualTo(0));
        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo(" "));
    }

    /// <summary>
    /// When scrollback is already at its limit, a reflow that would
    /// push more rows above the live grid must not exceed the limit.
    /// </summary>
    [Test]
    public void Resize_ScrollbackAtLimit_FifoEviction()
    {
        var buffer = new TerminalBuffer(5, 2);
        buffer.ScrollbackLimit = 2;

        // Scroll 4 lines off the top so the ring overflows.
        for (int i = 0; i < 4; i++)
        {
            PrintString(buffer, new string((char)('A' + i), 5));
            buffer.LineFeed();
            buffer.CarriageReturn();
        }

        Assume.That(buffer.ScrollbackCount, Is.EqualTo(2));

        // Resize so that more rows would want to go into scrollback.
        buffer.Resize(5, 1);

        Assert.That(buffer.ScrollbackCount, Is.LessThanOrEqualTo(2));
    }

    private static void PrintString(TerminalBuffer buffer, string text)
    {
        foreach (var ch in text)
        {
            buffer.PutChar(ch);
        }
    }

    private static string ScreenDump(TerminalBuffer buffer)
    {
        var screen = buffer.GetScreen();
        if (screen is null)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < buffer.Rows; r++)
        {
            for (int c = 0; c < buffer.Cols; c++)
            {
                sb.Append(screen.Cells[r, c].Character ?? "·");
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }
}
