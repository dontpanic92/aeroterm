// <copyright file="IPrimarySelectionService.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Threading.Tasks;

/// <summary>
/// Abstraction over the X11 PRIMARY selection. The PRIMARY selection is a
/// Linux-specific clipboard distinct from the regular system clipboard
/// (CLIPBOARD) that stores the most recent mouse-selected text and is
/// pasted by middle-clicking. On non-Linux or Wayland sessions, reads
/// return <c>null</c> and writes are no-ops.
/// </summary>
internal interface IPrimarySelectionService
{
    /// <summary>
    /// Gets a value indicating whether a functional PRIMARY selection
    /// backend was detected at construction time. Consumers can use this
    /// to decide whether to perform a PRIMARY round-trip or fall back to
    /// the regular clipboard directly.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Writes <paramref name="text"/> to the PRIMARY selection. Completes
    /// synchronously and silently on platforms without PRIMARY support.
    /// </summary>
    /// <param name="text">The text to publish.</param>
    /// <returns>A task that completes once the write finishes.</returns>
    Task WriteAsync(string text);

    /// <summary>
    /// Reads the current PRIMARY selection. Returns <c>null</c> when
    /// PRIMARY is unavailable, empty, or a backend error occurred.
    /// </summary>
    /// <returns>The selection text, or <c>null</c>.</returns>
    Task<string?> ReadAsync();
}
