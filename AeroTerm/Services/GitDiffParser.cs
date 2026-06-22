// <copyright file="GitDiffParser.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Parses unified <c>git diff</c> output into an aligned side-by-side model.
/// </summary>
internal static class GitDiffParser
{
    /// <summary>
    /// Parses unified diff text into one model per file.
    /// </summary>
    /// <param name="unifiedDiff">Raw unified diff output from Git.</param>
    /// <returns>The parsed files, in the order they appear in the diff.</returns>
    internal static IReadOnlyList<GitDiffFile> Parse(string? unifiedDiff)
    {
        var files = new List<GitDiffFile>();
        if (string.IsNullOrEmpty(unifiedDiff))
        {
            return files;
        }

        var lines = unifiedDiff.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        string path = string.Empty;
        var isBinary = false;
        var rows = new List<GitDiffRow>();
        var hasFile = false;
        var oldLine = 0;
        var newLine = 0;

        void FlushFile()
        {
            if (hasFile)
            {
                files.Add(new GitDiffFile(path, rows.ToArray(), isBinary));
            }
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushFile();
                hasFile = true;
                path = ExtractPathFromGitHeader(line);
                isBinary = false;
                rows = new List<GitDiffRow>();
                oldLine = 0;
                newLine = 0;
                continue;
            }

            if (!hasFile)
            {
                continue;
            }

            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                var candidate = StripDiffPathPrefix(line[4..]);
                if (candidate.Length > 0)
                {
                    path = candidate;
                }

                continue;
            }

            if (line.StartsWith("--- ", StringComparison.Ordinal) ||
                line.StartsWith("index ", StringComparison.Ordinal) ||
                line.StartsWith("old mode ", StringComparison.Ordinal) ||
                line.StartsWith("new mode ", StringComparison.Ordinal) ||
                line.StartsWith("deleted file mode ", StringComparison.Ordinal) ||
                line.StartsWith("new file mode ", StringComparison.Ordinal) ||
                line.StartsWith("similarity index ", StringComparison.Ordinal) ||
                line.StartsWith("rename from ", StringComparison.Ordinal) ||
                line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("Binary files ", StringComparison.Ordinal) ||
                line.StartsWith("GIT binary patch", StringComparison.Ordinal))
            {
                isBinary = true;
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                ParseHunkHeader(line, ref oldLine, ref newLine);
                continue;
            }

            if (line.StartsWith("\\", StringComparison.Ordinal))
            {
                // "\ No newline at end of file" — informational only.
                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            var marker = line[0];
            var content = line[1..];
            switch (marker)
            {
                case ' ':
                    rows.Add(new GitDiffRow(oldLine, content, newLine, content, GitDiffRowKind.Context));
                    oldLine++;
                    newLine++;
                    break;
                case '-':
                    rows.Add(new GitDiffRow(oldLine, content, null, null, GitDiffRowKind.Removed));
                    oldLine++;
                    break;
                case '+':
                    rows.Add(new GitDiffRow(null, null, newLine, content, GitDiffRowKind.Added));
                    newLine++;
                    break;
                default:
                    // Unrecognized line outside a hunk; ignore.
                    break;
            }
        }

        FlushFile();
        return files;
    }

    private static void ParseHunkHeader(string line, ref int oldLine, ref int newLine)
    {
        // Format: @@ -oldStart,oldCount +newStart,newCount @@ optional section heading
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
            ? value
            : 1;
    }

    private static string ExtractPathFromGitHeader(string line)
    {
        // diff --git a/path b/path
        var body = line["diff --git ".Length..];
        var separator = body.IndexOf(" b/", StringComparison.Ordinal);
        if (separator >= 0)
        {
            return StripDiffPathPrefix(body[(separator + 1)..]);
        }

        return StripDiffPathPrefix(body);
    }

    private static string StripDiffPathPrefix(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed == "/dev/null")
        {
            return string.Empty;
        }

        if (trimmed.StartsWith("a/", StringComparison.Ordinal) ||
            trimmed.StartsWith("b/", StringComparison.Ordinal))
        {
            return trimmed[2..];
        }

        return trimmed;
    }
}
