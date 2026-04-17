// <copyright file="BufferSnapshot.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// An immutable point-in-time copy of a <see cref="TerminalBuffer"/>'s
/// observable state suitable for safe consumption from threads other than
/// the one mutating the buffer. Captured atomically under the buffer's
/// internal lock so scrollback rows, the live grid, and the alt-buffer
/// indicator are mutually consistent.
/// </summary>
/// <remarks>
/// All cell arrays are defensive copies — mutating them does not affect
/// the source buffer, and subsequent buffer mutations do not affect the
/// snapshot.
/// </remarks>
public sealed class BufferSnapshot
{
    /// <summary>
    /// Gets a value indicating whether the alternate screen buffer was
    /// active at capture time.
    /// </summary>
    public required bool IsUsingAltBuffer { get; init; }

    /// <summary>
    /// Gets the number of scrollback rows captured. Equal to the length of
    /// <see cref="ScrollbackRows"/>.
    /// </summary>
    public required int ScrollbackCount { get; init; }

    /// <summary>
    /// Gets the scrollback rows, oldest-first. Each row is a defensive
    /// copy and preserves its capture-time column count (not reflowed).
    /// </summary>
    public required Cell[][] ScrollbackRows { get; init; }

    /// <summary>
    /// Gets a defensive copy of the live screen at capture time. Includes
    /// <see cref="Screen.Cells"/>, cursor position, and detected colors.
    /// </summary>
    public required Screen LiveScreen { get; init; }

    /// <summary>
    /// Gets the live grid row count (<see cref="Screen.Cells"/> first dimension).
    /// </summary>
    public required int Rows { get; init; }

    /// <summary>
    /// Gets the live grid column count (<see cref="Screen.Cells"/> second dimension).
    /// </summary>
    public required int Cols { get; init; }
}
