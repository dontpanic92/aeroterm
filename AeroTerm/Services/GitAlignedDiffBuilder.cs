// <copyright file="GitAlignedDiffBuilder.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// Expands parsed Git change rows into a complete, row-aligned full-file comparison.
/// </summary>
internal static class GitAlignedDiffBuilder
{
    /// <summary>
    /// Builds complete aligned rows from the old and new file contents and parsed change rows.
    /// </summary>
    /// <param name="oldText">Complete old-side file content.</param>
    /// <param name="newText">Complete new-side file content.</param>
    /// <param name="changeRows">Parsed changed rows in source order.</param>
    /// <returns>Complete aligned display rows.</returns>
    internal static IReadOnlyList<GitDiffRow> Build(
        string oldText,
        string newText,
        IReadOnlyList<GitDiffRow> changeRows)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);
        var rows = new List<GitDiffRow>();
        var oldCursor = 1;
        var newCursor = 1;

        foreach (var changeRow in changeRows)
        {
            var oldGap = changeRow.OldLineNumber is { } oldLine
                ? oldLine - oldCursor
                : changeRow.NewLineNumber!.Value - newCursor;
            var newGap = changeRow.NewLineNumber is { } newLine
                ? newLine - newCursor
                : changeRow.OldLineNumber!.Value - oldCursor;
            var commonCount = Math.Max(0, Math.Min(oldGap, newGap));

            for (var index = 0; index < commonCount; index++)
            {
                rows.Add(new GitDiffRow(
                    oldCursor,
                    GetLine(oldLines, oldCursor),
                    newCursor,
                    GetLine(newLines, newCursor),
                    GitDiffRowKind.Context));
                oldCursor++;
                newCursor++;
            }

            rows.Add(changeRow);
            if (changeRow.OldLineNumber is not null)
            {
                oldCursor++;
            }

            if (changeRow.NewLineNumber is not null)
            {
                newCursor++;
            }
        }

        while (oldCursor <= oldLines.Count && newCursor <= newLines.Count)
        {
            rows.Add(new GitDiffRow(
                oldCursor,
                GetLine(oldLines, oldCursor),
                newCursor,
                GetLine(newLines, newCursor),
                GitDiffRowKind.Context));
            oldCursor++;
            newCursor++;
        }

        while (oldCursor <= oldLines.Count)
        {
            rows.Add(new GitDiffRow(
                oldCursor,
                GetLine(oldLines, oldCursor),
                null,
                null,
                GitDiffRowKind.Removed));
            oldCursor++;
        }

        while (newCursor <= newLines.Count)
        {
            rows.Add(new GitDiffRow(
                null,
                null,
                newCursor,
                GetLine(newLines, newCursor),
                GitDiffRowKind.Added));
            newCursor++;
        }

        return rows;
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            Array.Resize(ref lines, lines.Length - 1);
        }

        return lines;
    }

    private static string GetLine(IReadOnlyList<string> lines, int lineNumber)
    {
        return lineNumber > 0 && lineNumber <= lines.Count ? lines[lineNumber - 1] : string.Empty;
    }
}
