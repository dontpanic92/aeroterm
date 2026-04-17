// <copyright file="TabGroupStore.cs">
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
/// In-memory, persistence-backed registry of <see cref="TabGroup"/>
/// definitions. Mirrors the fault-tolerance pattern of
/// <see cref="ProfileStore"/>: malformed or missing <c>groups.json</c>
/// yields an empty store (never throws). Mutating operations
/// (<see cref="CreateGroup"/>, <see cref="RemoveGroup"/>,
/// <see cref="RenameGroup"/>, <see cref="SetGroupColor"/>) auto-save
/// and raise <see cref="GroupsChanged"/>.
/// </summary>
public sealed class TabGroupStore
{
    private static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AeroTerm");

    private readonly string directory;
    private readonly List<TabGroup> groups = new();
    private bool loaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabGroupStore"/>
    /// class bound to the default user configuration directory. The
    /// initial load happens on first access to <see cref="Groups"/> or
    /// the first mutation.
    /// </summary>
    public TabGroupStore()
        : this(DefaultDirectory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TabGroupStore"/>
    /// class with a caller-supplied directory (used by tests).
    /// </summary>
    /// <param name="directory">The directory that holds <c>groups.json</c>.</param>
    public TabGroupStore(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        this.directory = directory;
    }

    /// <summary>
    /// Raised whenever the group list changes through any mutation or
    /// a call to <see cref="Reload"/>.
    /// </summary>
    public event Action? GroupsChanged;

    /// <summary>
    /// Gets the effective path to <c>groups.json</c>.
    /// </summary>
    public string FilePath => Path.Combine(this.directory, "groups.json");

    /// <summary>
    /// Gets the current group list as a read-only snapshot. The list
    /// is ordered by creation / load order.
    /// </summary>
    public IReadOnlyList<TabGroup> Groups
    {
        get
        {
            this.EnsureLoaded();
            return this.groups.ToList();
        }
    }

    /// <summary>
    /// Discards any in-memory state and re-loads from disk. Missing /
    /// malformed files yield an empty list. Always raises
    /// <see cref="GroupsChanged"/>.
    /// </summary>
    public void Reload()
    {
        this.groups.Clear();
        this.groups.AddRange(LoadFromDisk(this.FilePath));
        this.loaded = true;
        this.GroupsChanged?.Invoke();
    }

    /// <summary>
    /// Looks up a group by its stable id. Returns <see langword="null"/>
    /// when the id does not match any known group.
    /// </summary>
    /// <param name="id">The group id.</param>
    /// <returns>The matched group, or <see langword="null"/>.</returns>
    public TabGroup? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        this.EnsureLoaded();
        foreach (var g in this.groups)
        {
            if (g.Id == id)
            {
                return g;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates and persists a new group with the supplied name. The
    /// color is picked from <see cref="TabGroup"/>'s default palette
    /// cycled by the current group count, ensuring newly-created groups
    /// get visually distinct pills out of the box.
    /// </summary>
    /// <param name="name">The display name. Trimmed; empty falls back
    /// to <c>"Group"</c>.</param>
    /// <returns>The newly-created group.</returns>
    public TabGroup CreateGroup(string name)
    {
        this.EnsureLoaded();
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Group" : name.Trim();
        var palette = TabGroup.DefaultPalette;
        var color = palette[this.groups.Count % palette.Count];
        var group = new TabGroup
        {
            Name = trimmed,
            Color = color,
        };
        this.groups.Add(group);
        this.SaveToDisk();
        this.GroupsChanged?.Invoke();
        return group;
    }

    /// <summary>
    /// Removes the group with the supplied id. No-op when the id is
    /// unknown. Callers are responsible for scrubbing any dangling
    /// <c>TabSession.GroupId</c> references; the store intentionally
    /// does not know about sessions.
    /// </summary>
    /// <param name="id">The id of the group to remove.</param>
    /// <returns><see langword="true"/> if a group was removed.</returns>
    public bool RemoveGroup(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        this.EnsureLoaded();
        int idx = this.groups.FindIndex(g => g.Id == id);
        if (idx < 0)
        {
            return false;
        }

        this.groups.RemoveAt(idx);
        this.SaveToDisk();
        this.GroupsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Renames the group with the supplied id. The group's
    /// <see cref="TabGroup.Color"/> is preserved.
    /// </summary>
    /// <param name="id">The id of the group to rename.</param>
    /// <param name="newName">The new display name.</param>
    /// <returns><see langword="true"/> if a group was renamed.</returns>
    public bool RenameGroup(string id, string newName)
    {
        ArgumentNullException.ThrowIfNull(id);
        this.EnsureLoaded();
        var g = this.groups.FirstOrDefault(x => x.Id == id);
        if (g is null)
        {
            return false;
        }

        g.Name = string.IsNullOrWhiteSpace(newName) ? "Group" : newName.Trim();
        this.SaveToDisk();
        this.GroupsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Updates the <see cref="TabGroup.Color"/> of a group in place.
    /// The id and name are preserved.
    /// </summary>
    /// <param name="id">The id of the group to recolor.</param>
    /// <param name="color">The new 24-bit RGB color.</param>
    /// <returns><see langword="true"/> if a group was recolored.</returns>
    public bool SetGroupColor(string id, int color)
    {
        ArgumentNullException.ThrowIfNull(id);
        this.EnsureLoaded();
        var g = this.groups.FirstOrDefault(x => x.Id == id);
        if (g is null)
        {
            return false;
        }

        g.Color = color;
        this.SaveToDisk();
        this.GroupsChanged?.Invoke();
        return true;
    }

    private static List<TabGroup> LoadFromDisk(string path)
    {
        var log = AppLogger.For<TabGroupStore>();
        if (!File.Exists(path))
        {
            return new List<TabGroup>();
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not read {Path}; using empty group list.", path);
            return new List<TabGroup>();
        }

        TabGroupsFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, TabGroupsJsonContext.Default.TabGroupsFile);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Malformed {Path}; using empty group list.", path);
            return new List<TabGroup>();
        }

        var result = new List<TabGroup>();
        if (parsed?.Groups is null)
        {
            return result;
        }

        foreach (var entry in parsed.Groups)
        {
            if (entry is null || string.IsNullOrEmpty(entry.Id))
            {
                continue;
            }

            result.Add(new TabGroup
            {
                Id = entry.Id,
                Name = string.IsNullOrWhiteSpace(entry.Name) ? "Group" : entry.Name!.Trim(),
                Color = entry.Color,
            });
        }

        return result;
    }

    private void EnsureLoaded()
    {
        if (this.loaded)
        {
            return;
        }

        this.groups.AddRange(LoadFromDisk(this.FilePath));
        this.loaded = true;
    }

    private void SaveToDisk()
    {
        var log = AppLogger.For<TabGroupStore>();
        try
        {
            Directory.CreateDirectory(this.directory);
            var file = new TabGroupsFile
            {
                Version = 1,
                Groups = this.groups.Select(g => new TabGroupEntry
                {
                    Id = g.Id,
                    Name = g.Name,
                    Color = g.Color,
                }).ToList(),
            };
            var json = JsonSerializer.Serialize(file, TabGroupsJsonContext.Default.TabGroupsFile);
            File.WriteAllText(this.FilePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            log.LogError(ex, "Failed to save groups to {Path}.", this.FilePath);
        }
    }
}
