// <copyright file="Cell2DRowSource.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using AeroTerm.Pty;

/// <summary>
/// <see cref="ITerminalRowSource"/> adapter over a <see cref="Cell"/>
/// rectangular grid. Used by tests and by code paths that already hold
/// a 2D snapshot of the visible screen.
/// </summary>
internal sealed class Cell2DRowSource : ITerminalRowSource
{
    private readonly Cell[,] cells;
    private readonly int rows;
    private readonly int cols;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cell2DRowSource"/> class.
    /// </summary>
    /// <param name="cells">The cell grid to expose.</param>
    public Cell2DRowSource(Cell[,] cells)
    {
        this.cells = cells ?? throw new ArgumentNullException(nameof(cells));
        this.rows = cells.GetLength(0);
        this.cols = cells.GetLength(1);
    }

    /// <inheritdoc />
    public int RowCount => this.rows;

    /// <inheritdoc />
    public int Cols => this.cols;

    /// <inheritdoc />
    public Cell[] GetRow(int absRow)
    {
        if (absRow < 0 || absRow >= this.rows)
        {
            return Array.Empty<Cell>();
        }

        var row = new Cell[this.cols];
        for (int c = 0; c < this.cols; c++)
        {
            row[c] = this.cells[absRow, c];
        }

        return row;
    }
}
