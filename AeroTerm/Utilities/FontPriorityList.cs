// <copyright file="FontPriorityList.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Utilities;

using System.Runtime.InteropServices;

/// <summary>
/// Helpers for the unified font priority list stored in settings.
/// The list contains user font names interleaved with a sentinel
/// string that represents the platform system-monospace font list.
/// </summary>
public static class FontPriorityList
{
    /// <summary>
    /// Sentinel representing the platform system-monospace font list.
    /// </summary>
    public const string SystemMonoSentinel = "$SYSTEM_MONO";

    /// <summary>
    /// Returns <c>true</c> when the entry is a recognised sentinel
    /// (<see cref="SystemMonoSentinel"/>).
    /// The comparison is ordinal and case-sensitive so that misspelled
    /// entries are treated as ordinary (invalid) font names.
    /// </summary>
    /// <param name="entry">The font list entry to test.</param>
    /// <returns><c>true</c> if the entry is a sentinel.</returns>
    public static bool IsSentinel(string entry)
    {
        return string.Equals(entry, SystemMonoSentinel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the list contains exactly one <see cref="SystemMonoSentinel"/>.
    /// <list type="bullet">
    ///   <item>A missing sentinel is appended at the end.</item>
    ///   <item>Duplicate sentinels beyond the first occurrence are removed.</item>
    ///   <item>An empty or null list is treated as the default
    ///         <c>["$SYSTEM_MONO"]</c>.</item>
    /// </list>
    /// </summary>
    /// <param name="list">The raw list from settings (may be null).</param>
    /// <returns>A new list with exactly one sentinel.</returns>
    public static List<string> Normalize(IList<string>? list)
    {
        if (list is null || list.Count == 0)
        {
            return new List<string> { SystemMonoSentinel };
        }

        var result = new List<string>(list.Count + 1);
        bool hasSystemMono = false;

        foreach (var entry in list)
        {
            if (IsSentinel(entry))
            {
                if (!hasSystemMono)
                {
                    result.Add(SystemMonoSentinel);
                    hasSystemMono = true;
                }
            }
            else
            {
                result.Add(entry);
            }
        }

        if (!hasSystemMono)
        {
            result.Add(SystemMonoSentinel);
        }

        return result;
    }

    /// <summary>
    /// Returns the platform-specific default monospace font names.
    /// </summary>
    /// <returns>An ordered list of default monospace fonts for the current platform.</returns>
    public static IReadOnlyList<string> GetDefaultPlatformFonts()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new[] { "Cascadia Mono", "Consolas", "Courier New", "Lucida Console", "Segoe UI Emoji", "Segoe UI Symbol" };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new[] { "Menlo", "SF Mono", "Monaco", "Courier", "Apple Color Emoji" };
        }

        return new[] { "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", "Monospace", "Noto Color Emoji", "Noto Emoji" };
    }
}
