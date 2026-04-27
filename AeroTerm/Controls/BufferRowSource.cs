// <copyright file="BufferRowSource.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using AeroTerm.Pty;

/// <summary>
/// <see cref="ITerminalRowSource"/> backed by a <see cref="TerminalBuffer"/>
/// and a snapshot of its live <see cref="Screen"/>. Absolute rows
/// <c>[0, ScrollbackCount)</c> are scrollback lines (looked up via
/// <see cref="TerminalBuffer.GetScrollbackLine(int)"/>); rows
/// <c>[ScrollbackCount, ScrollbackCount + LiveRows)</c> are taken from
/// the live screen snapshot.
/// </summary>
internal sealed class BufferRowSource : ITerminalRowSource
{
    private readonly TerminalBuffer buffer;
    private readonly Screen liveScreen;
    private readonly int scrollbackCount;
    private readonly int liveRows;
    private readonly int liveCols;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferRowSource"/> class.
    /// </summary>
    /// <param name="buffer">The owning terminal buffer.</param>
    /// <param name="liveScreen">A snapshot of the live screen. The
    /// snapshot's row/col counts pin the view; later resizes do not
    /// affect this row source.</param>
    public BufferRowSource(TerminalBuffer buffer, Screen liveScreen)
    {
        this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        this.liveScreen = liveScreen ?? throw new ArgumentNullException(nameof(liveScreen));
        this.liveRows = liveScreen.Cells.GetLength(0);
        this.liveCols = liveScreen.Cells.GetLength(1);

        // Capture once so RowCount stays consistent across calls during
        // a selection gesture even if the reader thread is appending.
        this.scrollbackCount = buffer.ScrollbackCount;
    }

    /// <inheritdoc />
    public int RowCount => this.scrollbackCount + this.liveRows;

    /// <inheritdoc />
    public int Cols => this.liveCols;

    /// <inheritdoc />
    public Cell[] GetRow(int absRow)
    {
        if (absRow < 0 || absRow >= this.RowCount)
        {
            return Array.Empty<Cell>();
        }

        if (absRow < this.scrollbackCount)
        {
            // Defensive: ScrollbackCount may have decreased since the
            // snapshot was taken (e.g. ClearScrollback). Treat the row
            // as evicted in that case.
            try
            {
                return this.buffer.GetScrollbackLine(absRow);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Array.Empty<Cell>();
            }
        }

        int liveIdx = absRow - this.scrollbackCount;
        var row = new Cell[this.liveCols];
        for (int c = 0; c < this.liveCols; c++)
        {
            row[c] = this.liveScreen.Cells[liveIdx, c];
        }

        return row;
    }
}
