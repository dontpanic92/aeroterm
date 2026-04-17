// <copyright file="PaletteMruStore.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AeroTerm.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Tracks the most-recently-invoked command-palette command ids, with a
/// bounded in-memory list plus best-effort JSON persistence to
/// <c>${AppSettingsDir}/palette-mru.json</c>.
/// </summary>
/// <remarks>
/// Fault tolerant: missing or malformed files yield an empty MRU list
/// and log a warning. Write failures are logged and swallowed so a
/// read-only settings directory never crashes the palette.
/// </remarks>
public sealed class PaletteMruStore
{
    /// <summary>
    /// The maximum number of command ids retained in the MRU list.
    /// Older ids fall off the tail when the cap is exceeded.
    /// </summary>
    public const int MaxEntries = 20;

    private static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AeroTerm");

    private readonly string directory;
    private readonly List<string> order = new(MaxEntries);

    /// <summary>
    /// Initializes a new instance of the <see cref="PaletteMruStore"/>
    /// class bound to the default configuration directory.
    /// </summary>
    public PaletteMruStore()
        : this(DefaultDirectory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PaletteMruStore"/>
    /// class bound to a caller-supplied directory.
    /// </summary>
    /// <param name="directory">The directory that holds
    /// <c>palette-mru.json</c>.</param>
    public PaletteMruStore(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        this.directory = directory;
        this.Load();
    }

    /// <summary>
    /// Gets the effective path to <c>palette-mru.json</c>.
    /// </summary>
    public string FilePath => Path.Combine(this.directory, "palette-mru.json");

    /// <summary>
    /// Gets the current MRU ids, most-recent first.
    /// </summary>
    public IReadOnlyList<string> Order => this.order;

    /// <summary>
    /// Records an invocation: moves <paramref name="commandId"/> to the
    /// head of the list and, on a successful write, persists.
    /// </summary>
    /// <param name="commandId">The id that was just invoked.</param>
    public void Record(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        if (commandId.Length == 0)
        {
            return;
        }

        this.order.Remove(commandId);
        this.order.Insert(0, commandId);
        if (this.order.Count > MaxEntries)
        {
            this.order.RemoveRange(MaxEntries, this.order.Count - MaxEntries);
        }

        this.Save();
    }

    /// <summary>
    /// Returns the MRU rank of <paramref name="commandId"/> (0 = most
    /// recent) or <see cref="int.MaxValue"/> if it has never been
    /// invoked. Used as a tie-breaker when sorting the default
    /// (empty-query) palette list.
    /// </summary>
    /// <param name="commandId">The command id to look up.</param>
    /// <returns>The MRU rank.</returns>
    public int RankOf(string commandId)
    {
        for (int i = 0; i < this.order.Count; i++)
        {
            if (this.order[i] == commandId)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Removes all MRU entries, both in memory and on disk.
    /// </summary>
    public void Clear()
    {
        this.order.Clear();
        this.Save();
    }

    private void Load()
    {
        var log = AppLogger.For<PaletteMruStore>();
        if (!File.Exists(this.FilePath))
        {
            return;
        }

        string json;
        try
        {
            json = File.ReadAllText(this.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not read {Path}; starting with empty palette MRU.", this.FilePath);
            return;
        }

        List<string>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, PaletteMruJsonContext.Default.ListString);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Malformed {Path}; starting with empty palette MRU.", this.FilePath);
            return;
        }

        if (parsed is null)
        {
            return;
        }

        foreach (var id in parsed)
        {
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            if (!this.order.Contains(id))
            {
                this.order.Add(id);
            }

            if (this.order.Count >= MaxEntries)
            {
                break;
            }
        }
    }

    private void Save()
    {
        var log = AppLogger.For<PaletteMruStore>();
        try
        {
            Directory.CreateDirectory(this.directory);
            var json = JsonSerializer.Serialize(this.order, PaletteMruJsonContext.Default.ListString);
            File.WriteAllText(this.FilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            log.LogWarning(ex, "Failed to persist palette MRU to {Path}.", this.FilePath);
        }
    }
}
