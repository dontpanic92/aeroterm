// <copyright file="HyperlinkBehavior.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Diagnostics;
using AeroTerm.Diagnostics;
using AeroTerm.Pty;
using Microsoft.Extensions.Logging;

/// <summary>
/// Computes the contiguous OSC 8 hyperlink run under a pointer cell, validates
/// URI schemes before activation, and opens hyperlinks via the OS default
/// handler. This type is intentionally XAML-free so it can be unit tested.
/// </summary>
internal static class HyperlinkBehavior
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http",
        "https",
        "ftp",
        "mailto",
        "file",
    };

    /// <summary>
    /// Computes the contiguous run of cells that share a hyperlink with the
    /// cell under the pointer.
    /// </summary>
    /// <param name="cells">The current visible-grid snapshot.</param>
    /// <param name="row">The pointer's row (0-based).</param>
    /// <param name="col">The pointer's column (0-based).</param>
    /// <returns>
    /// The <see cref="HyperlinkRun"/> covering the hyperlinked cells on the
    /// same row as the pointer, or <see langword="null"/> if the pointer is
    /// outside the grid or the cell under the pointer has no hyperlink URI.
    /// </returns>
    public static HyperlinkRun? GetRunAt(Cell[,]? cells, int row, int col)
    {
        if (cells is null)
        {
            return null;
        }

        int rows = cells.GetLength(0);
        int cols = cells.GetLength(1);
        if (row < 0 || row >= rows || col < 0 || col >= cols)
        {
            return null;
        }

        var anchor = cells[row, col];
        if (string.IsNullOrEmpty(anchor.HyperlinkUri))
        {
            return null;
        }

        string uri = anchor.HyperlinkUri!;
        string? id = anchor.HyperlinkId;

        int startCol = col;
        while (startCol - 1 >= 0 && Matches(cells[row, startCol - 1], uri, id))
        {
            startCol--;
        }

        int endCol = col;
        while (endCol + 1 < cols && Matches(cells[row, endCol + 1], uri, id))
        {
            endCol++;
        }

        return new HyperlinkRun(row, startCol, endCol, uri, id);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the given URI uses a scheme that is
    /// safe to pass to the OS default handler.
    /// </summary>
    /// <param name="uri">The URI to validate.</param>
    /// <returns>True if the URI is allowed to be opened.</returns>
    public static bool IsAllowedUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        return AllowedSchemes.Contains(parsed.Scheme);
    }

    /// <summary>
    /// Opens the given hyperlink URI via the OS default handler after
    /// validating its scheme. Exceptions are logged through
    /// <see cref="AppLogger"/> rather than propagated.
    /// </summary>
    /// <param name="uri">The URI to open.</param>
    /// <returns>True if activation was attempted with an allowed scheme.</returns>
    public static bool Activate(string? uri)
    {
        if (!IsAllowedUri(uri))
        {
            AppLogger.For(nameof(HyperlinkBehavior)).LogWarning(
                "Refusing to open hyperlink with disallowed or invalid URI: {Uri}",
                uri ?? "<null>");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(uri!)
            {
                UseShellExecute = true,
            };
            using var process = Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.For(nameof(HyperlinkBehavior)).LogError(
                ex,
                "Failed to open hyperlink URI: {Uri}",
                uri);
            return false;
        }
    }

    private static bool Matches(Cell cell, string anchorUri, string? anchorId)
    {
        if (string.IsNullOrEmpty(cell.HyperlinkUri))
        {
            return false;
        }

        // When both sides carry a non-empty id, group by id so wrapped or
        // non-contiguous runs share highlight state.
        if (!string.IsNullOrEmpty(anchorId) && !string.IsNullOrEmpty(cell.HyperlinkId))
        {
            return string.Equals(cell.HyperlinkId, anchorId, StringComparison.Ordinal);
        }

        // Fall back to URI equality for unidentified links.
        return string.Equals(cell.HyperlinkUri, anchorUri, StringComparison.Ordinal);
    }
}
