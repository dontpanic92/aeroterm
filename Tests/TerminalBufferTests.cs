// <copyright file="TerminalBufferTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests terminal buffer state handling.
/// </summary>
public class TerminalBufferTests
{
    /// <summary>
    /// Resize should preserve overlapping cells.
    /// </summary>
    [Test]
    public void Resize_PreservesExistingCells()
    {
        var buffer = new TerminalBuffer(2, 2);
        buffer.PutChar('A');
        buffer.SetCursorPosition(1, 1);
        buffer.PutChar('Z');

        buffer.Resize(4, 3);

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("A"));
        Assert.That(screen.Cells[1, 1].Character, Is.EqualTo("Z"));
        Assert.That(screen.Cells[2, 3].Character, Is.EqualTo(" "));
    }

    /// <summary>
    /// Scroll should move lines up and clear the newly exposed row.
    /// </summary>
    [Test]
    public void ScrollUp_MovesContentAndClearsExposedRow()
    {
        var buffer = new TerminalBuffer(2, 2);
        buffer.PutChar('A');
        buffer.SetCursorPosition(1, 0);
        buffer.PutChar('B');

        buffer.ScrollUp(1);

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("B"));
        Assert.That(screen.Cells[1, 0].Character, Is.EqualTo(" "));
    }

    /// <summary>
    /// Wide characters should occupy their leading cell and reserve a continuation cell.
    /// </summary>
    [Test]
    public void PutChar_WideCharacter_UsesContinuationCell()
    {
        var buffer = new TerminalBuffer(3, 1);

        buffer.PutChar('中');

        var screen = buffer.GetScreen();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("中"));
        Assert.That(screen.Cells[0, 1].Character, Is.Null);
    }

    /// <summary>
    /// Switching between main and alternate buffers should preserve the main buffer state.
    /// </summary>
    [Test]
    public void AlternateBuffer_RestoresMainBufferContent()
    {
        var buffer = new TerminalBuffer(2, 1);
        buffer.PutChar('A');
        buffer.SwitchToAlternateBuffer();
        buffer.SetCursorPosition(0, 0);
        buffer.PutChar('B');

        var alternate = buffer.GetScreen();
        string? alternateCharacter = alternate!.Cells[0, 0].Character;

        buffer.SwitchToMainBuffer();
        var main = buffer.GetScreen();

        Assert.That(alternateCharacter, Is.EqualTo("B"));
        Assert.That(main, Is.Not.Null);
        Assert.That(main!.Cells[0, 0].Character, Is.EqualTo("A"));
    }

    /// <summary>
    /// Predominant background color should surface through the screen snapshot
    /// and foreground should be derived from it for readable chrome text.
    /// </summary>
    [Test]
    public void GetScreen_DetectsPredominantBackground_DerivesForeground()
    {
        var buffer = new TerminalBuffer(3, 2);
        buffer.SetForegroundColor(0x112233);
        buffer.SetBackgroundColor(0x445566);

        buffer.PutChar('A');
        buffer.PutChar('B');
        buffer.PutChar('C');
        buffer.PutChar('D');

        var screen = buffer.GetScreen();

        Assert.That(screen, Is.Not.Null);
        Assert.That(screen!.BackgroundColor, Is.EqualTo(0x445566));

        // Foreground is derived from bg, not detected from cell fg.
        Assert.That(screen.ForegroundColor, Is.EqualTo(ColorUtility.DeriveReadableForeground(0x445566)));
    }

    /// <summary>
    /// Scroll regions should only affect the configured rows.
    /// </summary>
    [Test]
    public void ScrollUp_WithScrollRegion_KeepsRowsOutsideRegionUntouched()
    {
        var buffer = new TerminalBuffer(3, 3);
        FillRow(buffer, 0, 'A');
        FillRow(buffer, 1, 'B');
        FillRow(buffer, 2, 'C');
        buffer.SetScrollRegion(1, 2);

        buffer.ScrollUp(1);

        var screen = buffer.GetScreen();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("A"));
        Assert.That(screen.Cells[1, 0].Character, Is.EqualTo("C"));
        Assert.That(screen.Cells[2, 0].Character, Is.EqualTo(" "));
    }

    /// <summary>
    /// Initial GetScreen should report all rows dirty.
    /// </summary>
    [Test]
    public void GetScreen_AfterConstruction_ReportsAllDirty()
    {
        var buffer = new TerminalBuffer(3, 2);
        buffer.PutChar('X');

        var screen = buffer.GetScreen();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen!.AllDirty, Is.True);
    }

    /// <summary>
    /// A partial update should report only the affected row as dirty.
    /// </summary>
    [Test]
    public void GetScreen_AfterPartialUpdate_ReportsOnlyDirtyRows()
    {
        var buffer = new TerminalBuffer(3, 3);
        FillRow(buffer, 0, 'A');
        FillRow(buffer, 1, 'B');
        FillRow(buffer, 2, 'C');

        // First snapshot consumes all dirty flags.
        buffer.GetScreen();

        // Update only row 1.
        buffer.SetCursorPosition(1, 0);
        buffer.PutChar('Z');

        var screen = buffer.GetScreen();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen!.AllDirty, Is.False);
        Assert.That(screen.DirtyRows, Is.Not.Null);
        Assert.That(screen.DirtyRows![0], Is.False);
        Assert.That(screen.DirtyRows[1], Is.True);
        Assert.That(screen.DirtyRows[2], Is.False);
    }

    /// <summary>
    /// A second GetScreen with no intervening changes should report nothing dirty.
    /// </summary>
    [Test]
    public void GetScreen_WithNoChanges_ReportsNothingDirty()
    {
        var buffer = new TerminalBuffer(2, 2);
        buffer.PutChar('A');

        buffer.GetScreen();

        var screen = buffer.GetScreen();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen!.AllDirty, Is.False);
        Assert.That(screen.DirtyRows, Is.Not.Null);
        Assert.That(screen.DirtyRows!.Any(d => d), Is.False);
    }

    /// <summary>
    /// Resize should report AllDirty in the subsequent snapshot.
    /// </summary>
    [Test]
    public void GetScreen_AfterResize_ReportsAllDirty()
    {
        var buffer = new TerminalBuffer(2, 2);
        buffer.PutChar('A');

        buffer.GetScreen();
        buffer.Resize(4, 3);

        var screen = buffer.GetScreen();
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen!.AllDirty, Is.True);
    }

    /// <summary>
    /// With auto-wrap on, pending wrap should defer the actual wrap until the next character.
    /// </summary>
    [Test]
    public void PutChar_PendingWrap_DefersWrapUntilNextChar()
    {
        var buffer = new TerminalBuffer(3, 2);
        buffer.PutChar('A');
        buffer.PutChar('B');
        buffer.PutChar('C'); // fills last column — should set pending wrap

        Assert.That(buffer.PendingWrap, Is.True);
        Assert.That(buffer.CursorRow, Is.EqualTo(0));

        buffer.PutChar('D'); // this triggers the actual wrap

        Assert.That(buffer.PendingWrap, Is.False);
        Assert.That(buffer.CursorRow, Is.EqualTo(1));
        Assert.That(buffer.CursorCol, Is.EqualTo(1));

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 2].Character, Is.EqualTo("C"));
        Assert.That(screen.Cells[1, 0].Character, Is.EqualTo("D"));
    }

    /// <summary>
    /// With auto-wrap off, writing at the last column should overwrite in place.
    /// </summary>
    [Test]
    public void PutChar_AutoWrapOff_OverwritesLastColumn()
    {
        var buffer = new TerminalBuffer(3, 2);
        buffer.AutoWrap = false;
        buffer.PutChar('A');
        buffer.PutChar('B');
        buffer.PutChar('C'); // at last column
        buffer.PutChar('D'); // should overwrite column 2

        Assert.That(buffer.CursorRow, Is.EqualTo(0));

        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 2].Character, Is.EqualTo("D"));
    }

    /// <summary>
    /// Cursor movement should clear pending wrap state.
    /// </summary>
    [Test]
    public void SetCursorPosition_ClearsPendingWrap()
    {
        var buffer = new TerminalBuffer(3, 2);
        buffer.PutChar('A');
        buffer.PutChar('B');
        buffer.PutChar('C');
        Assert.That(buffer.PendingWrap, Is.True);

        buffer.SetCursorPosition(0, 0);
        Assert.That(buffer.PendingWrap, Is.False);
    }

    /// <summary>
    /// A fresh buffer should have no scrollback retained.
    /// </summary>
    [Test]
    public void Scrollback_IsEmpty_OnConstruction()
    {
        var buffer = new TerminalBuffer(4, 3);

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(0));
        Assert.That(buffer.ScrollbackLimit, Is.EqualTo(TerminalBuffer.DefaultScrollbackLimit));
    }

    /// <summary>
    /// A line-feed that scrolls the full region should capture the evicted
    /// top row into the scrollback ring.
    /// </summary>
    [Test]
    public void Scrollback_LineFeedAtBottom_CapturesTopRow()
    {
        var buffer = new TerminalBuffer(3, 2);
        FillRow(buffer, 0, 'A');
        buffer.SetCursorPosition(1, 0);
        buffer.PutChar('B');

        // Cursor at bottom row; LineFeed scrolls row 0 off the top.
        buffer.SetCursorPosition(1, 0);
        buffer.LineFeed();

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(1));
        var row = buffer.GetScrollbackLine(0);
        Assert.That(row.Length, Is.EqualTo(3));
        Assert.That(row[0].Character, Is.EqualTo("A"));
        Assert.That(row[1].Character, Is.EqualTo("A"));
        Assert.That(row[2].Character, Is.EqualTo("A"));
    }

    /// <summary>
    /// An explicit multi-line <see cref="TerminalBuffer.ScrollUp"/> on the
    /// full region captures each evicted row in chronological order.
    /// </summary>
    [Test]
    public void Scrollback_ScrollUpFullRegion_CapturesLinesInOrder()
    {
        var buffer = new TerminalBuffer(3, 3);
        FillRow(buffer, 0, 'A');
        FillRow(buffer, 1, 'B');
        FillRow(buffer, 2, 'C');

        buffer.ScrollUp(2);

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(2));
        Assert.That(buffer.GetScrollbackLine(0)[0].Character, Is.EqualTo("A"));
        Assert.That(buffer.GetScrollbackLine(1)[0].Character, Is.EqualTo("B"));
    }

    /// <summary>
    /// Scrolling inside a constrained DEC scroll region must not push rows
    /// into scrollback — they belong to a pager/TUI and are internal state.
    /// </summary>
    [Test]
    public void Scrollback_ScrollUpInsideRegion_DoesNotCapture()
    {
        var buffer = new TerminalBuffer(3, 3);
        FillRow(buffer, 0, 'A');
        FillRow(buffer, 1, 'B');
        FillRow(buffer, 2, 'C');
        buffer.SetScrollRegion(1, 2);

        buffer.ScrollUp(1);

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(0));
    }

    /// <summary>
    /// The alternate screen buffer never contributes scrollback; switching
    /// away and back preserves the primary scrollback untouched.
    /// </summary>
    [Test]
    public void Scrollback_AltBuffer_DoesNotCaptureAndPreservesPrimary()
    {
        var buffer = new TerminalBuffer(3, 2);
        FillRow(buffer, 0, 'A');
        buffer.SetCursorPosition(1, 0);
        buffer.PutChar('B');
        buffer.SetCursorPosition(1, 0);
        buffer.LineFeed();
        Assume.That(buffer.ScrollbackCount, Is.EqualTo(1));

        buffer.SwitchToAlternateBuffer();
        buffer.SetCursorPosition(0, 0);
        buffer.PutChar('X');
        buffer.SetCursorPosition(1, 0);
        buffer.LineFeed();
        buffer.LineFeed();

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(1));
        Assert.That(buffer.GetScrollbackLine(0)[0].Character, Is.EqualTo("A"));

        buffer.SwitchToMainBuffer();
        Assert.That(buffer.ScrollbackCount, Is.EqualTo(1));
        Assert.That(buffer.GetScrollbackLine(0)[0].Character, Is.EqualTo("A"));
    }

    /// <summary>
    /// Exceeding the scrollback limit evicts the oldest lines (FIFO).
    /// </summary>
    [Test]
    public void Scrollback_ExceedsLimit_EvictsOldest()
    {
        var buffer = new TerminalBuffer(1, 2) { ScrollbackLimit = 3 };
        for (int i = 0; i < 5; i++)
        {
            buffer.SetCursorPosition(0, 0);
            buffer.PutChar((char)('0' + i));
            buffer.SetCursorPosition(1, 0);
            buffer.LineFeed();
        }

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(3));

        // The five captured lines were '0','1','2','3','4'; oldest two are
        // evicted, leaving '2','3','4' in oldest-first order.
        Assert.That(buffer.GetScrollbackLine(0)[0].Character, Is.EqualTo("2"));
        Assert.That(buffer.GetScrollbackLine(1)[0].Character, Is.EqualTo("3"));
        Assert.That(buffer.GetScrollbackLine(2)[0].Character, Is.EqualTo("4"));
    }

    /// <summary>
    /// Shrinking the limit drops the oldest lines and keeps the newest ones.
    /// </summary>
    [Test]
    public void Scrollback_ShrinkLimit_KeepsNewest()
    {
        var buffer = new TerminalBuffer(1, 2);
        for (int i = 0; i < 5; i++)
        {
            buffer.SetCursorPosition(0, 0);
            buffer.PutChar((char)('A' + i));
            buffer.SetCursorPosition(1, 0);
            buffer.LineFeed();
        }

        Assume.That(buffer.ScrollbackCount, Is.EqualTo(5));

        buffer.ScrollbackLimit = 2;

        Assert.That(buffer.ScrollbackLimit, Is.EqualTo(2));
        Assert.That(buffer.ScrollbackCount, Is.EqualTo(2));
        Assert.That(buffer.GetScrollbackLine(0)[0].Character, Is.EqualTo("D"));
        Assert.That(buffer.GetScrollbackLine(1)[0].Character, Is.EqualTo("E"));
    }

    /// <summary>
    /// Setting the limit to 0 disables capture and clears existing entries.
    /// </summary>
    [Test]
    public void Scrollback_LimitZero_DisablesAndClears()
    {
        var buffer = new TerminalBuffer(1, 2);
        buffer.SetCursorPosition(1, 0);
        buffer.LineFeed();
        Assume.That(buffer.ScrollbackCount, Is.EqualTo(1));

        buffer.ScrollbackLimit = 0;

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(0));

        // Further scrolls must not capture.
        buffer.SetCursorPosition(1, 0);
        buffer.LineFeed();
        Assert.That(buffer.ScrollbackCount, Is.EqualTo(0));
    }

    /// <summary>
    /// <see cref="TerminalBuffer.ClearScrollback"/> drops the ring but keeps capacity.
    /// </summary>
    [Test]
    public void Scrollback_Clear_EmptiesRingAndKeepsLimit()
    {
        var buffer = new TerminalBuffer(1, 2);
        buffer.SetCursorPosition(1, 0);
        buffer.LineFeed();

        buffer.ClearScrollback();

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(0));
        Assert.That(buffer.ScrollbackLimit, Is.EqualTo(TerminalBuffer.DefaultScrollbackLimit));
    }

    /// <summary>
    /// Growing the scrollback limit preserves existing entries and simply
    /// extends the retention window for subsequent scrolls.
    /// </summary>
    [Test]
    public void Scrollback_GrowLimit_PreservesExistingLines()
    {
        var buffer = new TerminalBuffer(1, 2) { ScrollbackLimit = 3 };
        for (int i = 0; i < 3; i++)
        {
            buffer.SetCursorPosition(0, 0);
            buffer.PutChar((char)('A' + i));
            buffer.SetCursorPosition(1, 0);
            buffer.LineFeed();
        }

        Assume.That(buffer.ScrollbackCount, Is.EqualTo(3));

        buffer.ScrollbackLimit = 10;

        Assert.That(buffer.ScrollbackLimit, Is.EqualTo(10));
        Assert.That(buffer.ScrollbackCount, Is.EqualTo(3));
        Assert.That(buffer.GetScrollbackLine(0)[0].Character, Is.EqualTo("A"));
        Assert.That(buffer.GetScrollbackLine(1)[0].Character, Is.EqualTo("B"));
        Assert.That(buffer.GetScrollbackLine(2)[0].Character, Is.EqualTo("C"));

        // Two more scrolls fit inside the new, larger window.
        for (int i = 0; i < 2; i++)
        {
            buffer.SetCursorPosition(0, 0);
            buffer.PutChar((char)('D' + i));
            buffer.SetCursorPosition(1, 0);
            buffer.LineFeed();
        }

        Assert.That(buffer.ScrollbackCount, Is.EqualTo(5));
        Assert.That(buffer.GetScrollbackLine(4)[0].Character, Is.EqualTo("E"));
    }

    /// <summary>
    /// Values passed to <see cref="TerminalBuffer.ScrollbackLimit"/> that
    /// exceed <see cref="TerminalBuffer.MaxScrollbackLimit"/> are clamped.
    /// </summary>
    [Test]
    public void Scrollback_LimitAboveMaximum_IsClampedToMax()
    {
        var buffer = new TerminalBuffer(1, 1);

        buffer.ScrollbackLimit = TerminalBuffer.MaxScrollbackLimit + 42;

        Assert.That(buffer.ScrollbackLimit, Is.EqualTo(TerminalBuffer.MaxScrollbackLimit));
    }

    /// <summary>
    /// <see cref="TerminalBuffer.GetScrollbackLine"/> must return a
    /// defensive copy; mutating it must not affect future reads.
    /// </summary>
    [Test]
    public void Scrollback_GetLine_ReturnsDefensiveCopy()
    {
        var buffer = new TerminalBuffer(2, 2);
        FillRow(buffer, 0, 'A');
        buffer.SetCursorPosition(1, 0);
        buffer.LineFeed();

        var row = buffer.GetScrollbackLine(0);
        row[0] = default;

        var fresh = buffer.GetScrollbackLine(0);
        Assert.That(fresh[0].Character, Is.EqualTo("A"));
    }

    /// <summary>
    /// <see cref="TerminalBuffer.GetScrollbackLine"/> rejects negative and
    /// out-of-range indices.
    /// </summary>
    /// <param name="invalid">An invalid index that should throw.</param>
    [TestCase(-1)]
    [TestCase(5)]
    public void Scrollback_GetLine_ThrowsOnInvalidIndex(int invalid)
    {
        var buffer = new TerminalBuffer(1, 2);
        buffer.SetCursorPosition(1, 0);
        buffer.LineFeed();
        Assume.That(buffer.ScrollbackCount, Is.EqualTo(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetScrollbackLine(invalid));
    }

    /// <summary>
    /// Scrollback rows re-enter the live grid when reflow is given enough
    /// rows to hold them. The captured content is preserved verbatim —
    /// this covers the small-history case where nothing needs to stay in
    /// scrollback after resize.
    /// </summary>
    [Test]
    public void Scrollback_ResizeLiveGrid_ReflowsHistoryIntoLiveGrid()
    {
        var buffer = new TerminalBuffer(3, 2);
        FillRow(buffer, 0, 'A');
        buffer.SetCursorPosition(1, 0);
        buffer.LineFeed();
        Assume.That(buffer.ScrollbackCount, Is.EqualTo(1));

        buffer.Resize(8, 4);

        // Scrollback row (AAA) + two live rows all fit in 4 rows — pulled back.
        Assert.That(buffer.ScrollbackCount, Is.EqualTo(0));
        var screen = buffer.GetScreen();
        Assert.That(screen!.Cells[0, 0].Character, Is.EqualTo("A"));
        Assert.That(screen.Cells[0, 1].Character, Is.EqualTo("A"));
        Assert.That(screen.Cells[0, 2].Character, Is.EqualTo("A"));
    }

    /// <summary>
    /// EraseInDisplay(2) issued immediately after a primary-buffer resize
    /// must clear the live grid. Skipping the clear (a previous "anti-flash"
    /// optimization) leaves stale cells from before the resize visible
    /// after a TUI repaints, which manifests as garbled box-drawing
    /// characters in apps that do clear+redraw on SIGWINCH.
    /// </summary>
    [Test]
    public void EraseInDisplay_AfterResize_ClearsLiveGrid()
    {
        var buffer = new TerminalBuffer(4, 3);
        FillRow(buffer, 0, 'A');
        FillRow(buffer, 1, 'B');
        FillRow(buffer, 2, 'C');

        buffer.Resize(5, 4);

        buffer.SetCursorPosition(0, 0);
        buffer.EraseInDisplay(2);

        var screen = buffer.GetScreen();
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                Assert.That(screen!.Cells[r, c].Character, Is.EqualTo(" "), $"cell ({r},{c}) should be blank after ED 2 post-resize");
            }
        }
    }

    /// <summary>
    /// Same as above but for the alternate buffer (truncate/pad path):
    /// a TUI like vim/htop sends ED 2 on SIGWINCH and the buffer must
    /// honour it.
    /// </summary>
    [Test]
    public void EraseInDisplay_AfterResize_ClearsAltBuffer()
    {
        var buffer = new TerminalBuffer(4, 3);
        buffer.SwitchToAlternateBuffer();
        FillRow(buffer, 0, 'X');
        FillRow(buffer, 1, 'Y');

        buffer.Resize(5, 4);

        buffer.SetCursorPosition(0, 0);
        buffer.EraseInDisplay(2);

        var screen = buffer.GetScreen();
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 5; c++)
            {
                Assert.That(screen!.Cells[r, c].Character, Is.EqualTo(" "), $"alt cell ({r},{c}) should be blank after ED 2 post-resize");
            }
        }
    }

    private static void FillRow(TerminalBuffer buffer, int row, char value)
    {
        buffer.SetCursorPosition(row, 0);
        buffer.PutChar(value);
        buffer.PutChar(value);
        buffer.PutChar(value);
    }
}
