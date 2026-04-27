// <copyright file="ITerminalRowSource.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using AeroTerm.Pty;

/// <summary>
/// Read-only random-access view over a contiguous sequence of terminal
/// rows in absolute-row coordinates. Absolute row 0 is the top of the
/// addressable buffer (oldest scrollback row when present, otherwise the
/// top of the live grid). The source is consumed by
/// <see cref="TerminalSelection"/> so it can address scrollback and live
/// rows uniformly without depending on <see cref="TerminalBuffer"/>.
/// </summary>
internal interface ITerminalRowSource
{
    /// <summary>
    /// Gets the total number of addressable rows
    /// (<c>ScrollbackCount + LiveRows</c> for buffer-backed sources).
    /// </summary>
    int RowCount { get; }

    /// <summary>
    /// Gets the live grid column count. Used by line-mode selection to
    /// pick the rightmost endpoint when the selected row may be wider
    /// than the visible grid (post-shrink scrollback rows).
    /// </summary>
    int Cols { get; }

    /// <summary>
    /// Returns the cells of the requested absolute row, or an empty
    /// array if the index is out of range. Implementations may return a
    /// shared reference; callers must not mutate the result.
    /// </summary>
    /// <param name="absRow">The absolute row index.</param>
    /// <returns>The row's cell array.</returns>
    Cell[] GetRow(int absRow);
}
