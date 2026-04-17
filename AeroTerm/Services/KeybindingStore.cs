// <copyright file="KeybindingStore.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AeroTerm.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Persists user keybinding overrides to
/// <c>${AppSettingsDir}/keybindings.json</c>. Only overrides are written
/// so future default changes propagate to users.
/// </summary>
public sealed class KeybindingStore
{
    private static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AeroTerm");

    private readonly string directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeybindingStore"/> class
    /// bound to the default user configuration directory.
    /// </summary>
    public KeybindingStore()
        : this(DefaultDirectory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KeybindingStore"/> class
    /// with a caller-supplied directory (used by tests).
    /// </summary>
    /// <param name="directory">The directory that holds <c>keybindings.json</c>.</param>
    public KeybindingStore(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        this.directory = directory;
    }

    /// <summary>
    /// Gets the effective path to <c>keybindings.json</c>.
    /// </summary>
    public string FilePath => Path.Combine(this.directory, "keybindings.json");

    /// <summary>
    /// Loads the user's keybindings, merging any overrides on top of the
    /// platform defaults. Missing files and malformed JSON return
    /// <see cref="KeybindingSet.Defaults"/> (never throw).
    /// </summary>
    /// <returns>A resolved keybinding set.</returns>
    public KeybindingSet Load()
    {
        var log = AppLogger.For<KeybindingStore>();
        if (!File.Exists(this.FilePath))
        {
            return KeybindingSet.Defaults;
        }

        string json;
        try
        {
            json = File.ReadAllText(this.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not read {Path}; using default keybindings.", this.FilePath);
            return KeybindingSet.Defaults;
        }

        KeybindingsFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, KeybindingsJsonContext.Default.KeybindingsFile);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Malformed {Path}; using default keybindings.", this.FilePath);
            return KeybindingSet.Defaults;
        }

        if (parsed?.Bindings is null)
        {
            return KeybindingSet.Defaults;
        }

        var overrides = new List<Keybinding>();
        foreach (var entry in parsed.Bindings)
        {
            if (entry is null)
            {
                continue;
            }

            if (!Enum.TryParse<KeybindingAction>(entry.Action, ignoreCase: false, out var action))
            {
                log.LogWarning("Skipping keybinding with unknown action '{Action}'.", entry.Action);
                continue;
            }

            if (!KeyChordParser.TryParse(entry.Chord, out var chord) || chord is null)
            {
                log.LogWarning(
                    "Skipping keybinding for {Action}: unparseable chord '{Chord}'.",
                    entry.Action,
                    entry.Chord);
                continue;
            }

            overrides.Add(new Keybinding(action, chord));
        }

        return KeybindingSet.Defaults.Merge(overrides);
    }

    /// <summary>
    /// Writes the given overrides to disk, replacing existing file
    /// contents.
    /// </summary>
    /// <param name="overrides">The user-specified overrides.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public bool Save(IEnumerable<Keybinding> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var log = AppLogger.For<KeybindingStore>();
        try
        {
            Directory.CreateDirectory(this.directory);
            var file = new KeybindingsFile
            {
                Version = 1,
                Bindings = overrides.Select(o => new KeybindingEntry
                {
                    Action = o.Action.ToString(),
                    Chord = KeyChordParser.Serialize(o.Chord),
                }).ToList(),
            };
            var json = JsonSerializer.Serialize(file, KeybindingsJsonContext.Default.KeybindingsFile);
            File.WriteAllText(this.FilePath, json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.LogError(ex, "Failed to save keybindings to {Path}.", this.FilePath);
            return false;
        }
    }
}
