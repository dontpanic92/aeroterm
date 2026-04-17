// <copyright file="TerminalBufferSnapshotTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Tests <see cref="TerminalBuffer.CreateSnapshot"/>.
/// </summary>
public class TerminalBufferSnapshotTests
{
    /// <summary>
    /// A snapshot of a fresh buffer reports zero scrollback and a live
    /// screen sized to match the buffer.
    /// </summary>
    [Test]
    public void Snapshot_FreshBuffer_IsEmpty()
    {
        var buffer = new TerminalBuffer(6, 3);

        var snapshot = buffer.CreateSnapshot();

        Assert.That(snapshot.IsUsingAltBuffer, Is.False);
        Assert.That(snapshot.ScrollbackCount, Is.EqualTo(0));
        Assert.That(snapshot.ScrollbackRows, Has.Length.EqualTo(0));
        Assert.That(snapshot.Rows, Is.EqualTo(3));
        Assert.That(snapshot.Cols, Is.EqualTo(6));
        Assert.That(snapshot.LiveScreen.Cells.GetLength(0), Is.EqualTo(3));
        Assert.That(snapshot.LiveScreen.Cells.GetLength(1), Is.EqualTo(6));
    }

    /// <summary>
    /// After scrolling off the full grid, the snapshot exposes the
    /// evicted rows in oldest-first order, each a defensive copy that
    /// can be mutated without corrupting the buffer.
    /// </summary>
    [Test]
    public void Snapshot_AfterScroll_ExposesScrollbackRows()
    {
        var buffer = new TerminalBuffer(3, 2);
        FillRow(buffer, 0, 'A');
        FillRow(buffer, 1, 'B');
        buffer.ScrollUp(2);

        var snapshot = buffer.CreateSnapshot();

        Assert.That(snapshot.ScrollbackCount, Is.EqualTo(2));
        Assert.That(snapshot.ScrollbackRows.Length, Is.EqualTo(2));
        Assert.That(snapshot.ScrollbackRows[0][0].Character, Is.EqualTo("A"));
        Assert.That(snapshot.ScrollbackRows[1][0].Character, Is.EqualTo("B"));

        // Defensive-copy invariant: overwriting the snapshot must not
        // affect the buffer's ring.
        snapshot.ScrollbackRows[0][0].Set("Z", default);
        Assert.That(buffer.GetScrollbackLine(0)[0].Character, Is.EqualTo("A"));
    }

    /// <summary>
    /// A snapshot taken while the alt buffer is active reports
    /// <see cref="BufferSnapshot.IsUsingAltBuffer"/> true, and the
    /// primary buffer's scrollback is preserved in the snapshot.
    /// </summary>
    [Test]
    public void Snapshot_AltBuffer_ReportsFlagAndPreservesPrimaryScrollback()
    {
        var buffer = new TerminalBuffer(3, 2);
        FillRow(buffer, 0, 'A');
        buffer.SetCursorPosition(1, 0);
        buffer.LineFeed(); // captures row of 'A'
        Assume.That(buffer.ScrollbackCount, Is.EqualTo(1));

        buffer.SwitchToAlternateBuffer();

        var snapshot = buffer.CreateSnapshot();

        Assert.That(snapshot.IsUsingAltBuffer, Is.True);
        Assert.That(snapshot.ScrollbackCount, Is.EqualTo(1));
        Assert.That(snapshot.ScrollbackRows[0][0].Character, Is.EqualTo("A"));
    }

    private static void FillRow(TerminalBuffer buffer, int row, char value)
    {
        buffer.SetCursorPosition(row, 0);
        buffer.PutChar(value);
        buffer.PutChar(value);
        buffer.PutChar(value);
    }
}
