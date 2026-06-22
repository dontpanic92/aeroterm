// <copyright file="GitDiffHighlightParser.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Parses unified diff hunks into side-specific full-file highlight ranges.
/// </summary>
internal static class GitDiffHighlightParser
{
    /// <summary>
    /// Parses unified diff text into old-side and new-side highlight ranges.
    /// </summary>
    /// <param name="unifiedDiff">Raw unified diff output.</param>
    /// <returns>Side-specific highlight ranges.</returns>
    internal static GitDiffHighlightSet Parse(string? unifiedDiff)
    {
        var oldRanges = new List<GitDiffHighlightRange>();
        var newRanges = new List<GitDiffHighlightRange>();
        if (string.IsNullOrEmpty(unifiedDiff))
        {
            return new GitDiffHighlightSet(oldRanges, newRanges);
        }

        var oldLine = 0;
        var newLine = 0;
        var inHunk = false;
        int? pendingOldStart = null;
        var pendingOldCount = 0;
        int? pendingNewStart = null;
        var pendingNewCount = 0;

        void FlushPending()
        {
            if (pendingOldCount > 0 && pendingNewCount > 0)
            {
                oldRanges.Add(new GitDiffHighlightRange(pendingOldStart!.Value, pendingOldCount, GitDiffHighlightKind.Modified));
                newRanges.Add(new GitDiffHighlightRange(pendingNewStart!.Value, pendingNewCount, GitDiffHighlightKind.Modified));
            }
            else if (pendingOldCount > 0)
            {
                oldRanges.Add(new GitDiffHighlightRange(pendingOldStart!.Value, pendingOldCount, GitDiffHighlightKind.Removed));
            }
            else if (pendingNewCount > 0)
            {
                newRanges.Add(new GitDiffHighlightRange(pendingNewStart!.Value, pendingNewCount, GitDiffHighlightKind.Added));
            }

            pendingOldStart = null;
            pendingOldCount = 0;
            pendingNewStart = null;
            pendingNewCount = 0;
        }

        foreach (var line in unifiedDiff.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                FlushPending();
                ParseHunkHeader(line, ref oldLine, ref newLine);
                inHunk = true;
                continue;
            }

            if (!inHunk || line.Length == 0 || line.StartsWith("\\", StringComparison.Ordinal))
            {
                continue;
            }

            switch (line[0])
            {
                case ' ':
                    FlushPending();
                    oldLine++;
                    newLine++;
                    break;
                case '-':
                    pendingOldStart ??= oldLine;
                    pendingOldCount++;
                    oldLine++;
                    break;
                case '+':
                    pendingNewStart ??= newLine;
                    pendingNewCount++;
                    newLine++;
                    break;
                default:
                    FlushPending();
                    inHunk = false;
                    break;
            }
        }

        FlushPending();
        return new GitDiffHighlightSet(oldRanges, newRanges);
    }

    private static void ParseHunkHeader(string line, ref int oldLine, ref int newLine)
    {
        var firstAt = line.IndexOf("@@", StringComparison.Ordinal);
        var secondAt = line.IndexOf("@@", firstAt + 2, StringComparison.Ordinal);
        if (secondAt < 0)
        {
            return;
        }

        var ranges = line[(firstAt + 2)..secondAt].Trim();
        foreach (var part in ranges.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 2)
            {
                continue;
            }

            var start = ParseRangeStart(part[1..]);
            if (part[0] == '-')
            {
                oldLine = start;
            }
            else if (part[0] == '+')
            {
                newLine = start;
            }
        }
    }

    private static int ParseRangeStart(string range)
    {
        var commaIndex = range.IndexOf(',', StringComparison.Ordinal);
        var startText = commaIndex >= 0 ? range[..commaIndex] : range;
        return int.TryParse(startText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(1, value)
            : 1;
    }
}
