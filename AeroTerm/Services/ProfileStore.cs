// <copyright file="ProfileStore.cs">
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
/// Persists the user's profile list and default-profile pointer to
/// <c>${AppSettingsDir}/profiles.json</c>. Mirrors the fault-tolerance
/// pattern of <see cref="KeybindingStore"/>: missing file yields an
/// empty list plus a synthesized "Default" profile; malformed JSON
/// yields an empty list and a warning (never throws to the caller).
/// </summary>
public sealed class ProfileStore
{
    private static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AeroTerm");

    private readonly string directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileStore"/> class
    /// bound to the default user configuration directory.
    /// </summary>
    public ProfileStore()
        : this(DefaultDirectory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileStore"/> class
    /// with a caller-supplied directory (used by tests).
    /// </summary>
    /// <param name="directory">The directory that holds <c>profiles.json</c>.</param>
    public ProfileStore(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        this.directory = directory;
    }

    /// <summary>
    /// Raised after a successful <see cref="Save"/> so subscribers (tab
    /// dropdown, keybinding lookup) can refresh their cached view.
    /// </summary>
    public event Action? ProfilesChanged;

    /// <summary>
    /// Gets the effective path to <c>profiles.json</c>.
    /// </summary>
    public string FilePath => Path.Combine(this.directory, "profiles.json");

    /// <summary>
    /// Synthesizes the canonical default profile (null command → platform
    /// default shell). Exposed for tests and for the new-tab fallback path.
    /// </summary>
    /// <returns>A fresh default profile.</returns>
    public static Profile CreateSynthesizedDefault()
    {
        return new Profile
        {
            Name = "Default",
        };
    }

    /// <summary>
    /// Loads the user's profile list.
    /// Fault-tolerant: a missing file returns a single synthesized
    /// default profile; malformed JSON returns an empty list plus a
    /// synthesized default and logs a warning.
    /// </summary>
    /// <returns>The loaded profile data.</returns>
    public ProfileStoreData Load()
    {
        var log = AppLogger.For<ProfileStore>();
        if (!File.Exists(this.FilePath))
        {
            var synth = CreateSynthesizedDefault();
            return new ProfileStoreData(new List<Profile> { synth }, synth.Id);
        }

        string json;
        try
        {
            json = File.ReadAllText(this.FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not read {Path}; using synthesized default profile.", this.FilePath);
            var synth = CreateSynthesizedDefault();
            return new ProfileStoreData(new List<Profile> { synth }, synth.Id);
        }

        ProfilesFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, ProfilesJsonContext.Default.ProfilesFile);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Malformed {Path}; using synthesized default profile.", this.FilePath);
            var synth = CreateSynthesizedDefault();
            return new ProfileStoreData(new List<Profile> { synth }, synth.Id);
        }

        var profiles = new List<Profile>();
        if (parsed?.Profiles is not null)
        {
            foreach (var entry in parsed.Profiles)
            {
                if (entry is null)
                {
                    continue;
                }

                var profile = FromEntry(entry);
                if (profile is null)
                {
                    log.LogWarning("Skipping profile with missing id/name in {Path}.", this.FilePath);
                    continue;
                }

                profiles.Add(profile);
            }
        }

        // If the file had zero valid profiles, synthesize a default so the
        // app always has *something* to launch.
        if (profiles.Count == 0)
        {
            var synth = CreateSynthesizedDefault();
            profiles.Add(synth);
            return new ProfileStoreData(profiles, synth.Id);
        }

        // Resolve the default: honour the persisted id when it points at a
        // known profile, otherwise fall back to the first entry.
        string? defaultId = parsed?.DefaultProfileId;
        if (defaultId is null || !profiles.Any(p => p.Id == defaultId))
        {
            defaultId = profiles[0].Id;
        }

        return new ProfileStoreData(profiles, defaultId);
    }

    /// <summary>
    /// Writes the supplied profile list (and default pointer) to disk.
    /// Returns <see langword="true"/> on success; failures are logged and
    /// swallowed so the UI never crashes on a read-only settings
    /// directory.
    /// </summary>
    /// <param name="profiles">The profile list to persist.</param>
    /// <param name="defaultProfileId">The default profile id, or <c>null</c>.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public bool Save(IEnumerable<Profile> profiles, string? defaultProfileId)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var log = AppLogger.For<ProfileStore>();
        try
        {
            Directory.CreateDirectory(this.directory);
            var file = new ProfilesFile
            {
                Version = 1,
                DefaultProfileId = defaultProfileId,
                Profiles = profiles.Select(ToEntry).ToList(),
            };
            var json = JsonSerializer.Serialize(file, ProfilesJsonContext.Default.ProfilesFile);
            File.WriteAllText(this.FilePath, json);
            this.ProfilesChanged?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            log.LogError(ex, "Failed to save profiles to {Path}.", this.FilePath);
            return false;
        }
    }

    /// <summary>
    /// Builds a baseline <see cref="LaunchSpec"/> from the current process
    /// environment (default shell, user home, inherited env vars). Used
    /// when a profile has partial launch overrides but the caller didn't
    /// supply an explicit fallback spec.
    /// </summary>
    /// <returns>A complete <see cref="LaunchSpec"/>.</returns>
    internal static LaunchSpec BuildEnvironmentFallback()
    {
        string shell = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows)
            ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe"
            : Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";

        string[] args = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows)
            ? Array.Empty<string>()
            : new[] { "-l" };

        string cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
            }
        }

        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";
        return new LaunchSpec(cwd, shell, args, env);
    }

    /// <summary>
    /// Merges a <see cref="Profile"/> onto a fallback <see cref="LaunchSpec"/>:
    /// any field set on the profile wins; unset fields inherit from the
    /// fallback. Environment overrides are layered (profile wins on key
    /// collisions).
    /// </summary>
    /// <param name="profile">The profile providing overrides.</param>
    /// <param name="fallback">The baseline spec.</param>
    /// <returns>A newly-constructed <see cref="LaunchSpec"/>.</returns>
    internal static LaunchSpec BuildLaunchSpec(Profile profile, LaunchSpec fallback)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(fallback);

        string command = profile.Command is { Length: > 0 } cmd ? cmd : fallback.Command;
        string cwd = profile.WorkingDirectory is { Length: > 0 } wd ? wd : fallback.Cwd;
        string[] args = profile.Args is not null ? (string[])profile.Args.Clone() : fallback.Args.ToArray();

        var env = new Dictionary<string, string>(fallback.Env);
        if (profile.EnvironmentOverrides is not null)
        {
            foreach (var kv in profile.EnvironmentOverrides)
            {
                env[kv.Key] = kv.Value;
            }
        }

        return new LaunchSpec(cwd, command, args, env);
    }

    private static ProfileEntry ToEntry(Profile p)
    {
        Dictionary<string, string>? env = null;
        if (p.EnvironmentOverrides is not null && p.EnvironmentOverrides.Count > 0)
        {
            env = new Dictionary<string, string>(p.EnvironmentOverrides.Count);
            foreach (var kv in p.EnvironmentOverrides)
            {
                env[kv.Key] = kv.Value;
            }
        }

        return new ProfileEntry
        {
            Id = p.Id,
            Name = p.Name,
            Command = p.Command,
            Args = p.Args is null ? null : (string[])p.Args.Clone(),
            WorkingDirectory = p.WorkingDirectory,
            EnvironmentOverrides = env,
            ColorSchemeName = p.ColorSchemeName,
            FontFamilies = p.FontFamilies is null ? null : (string[])p.FontFamilies.Clone(),
            FontSize = p.FontSize,
            WindowEffect = p.WindowEffect,
        };
    }

    private static Profile? FromEntry(ProfileEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Name))
        {
            return null;
        }

        return new Profile
        {
            Id = entry.Id,
            Name = entry.Name,
            Command = entry.Command,
            Args = entry.Args is null ? null : (string[])entry.Args.Clone(),
            WorkingDirectory = entry.WorkingDirectory,
            EnvironmentOverrides = entry.EnvironmentOverrides is null
                ? null
                : new Dictionary<string, string>(entry.EnvironmentOverrides),
            ColorSchemeName = entry.ColorSchemeName,
            FontFamilies = entry.FontFamilies is null ? null : (string[])entry.FontFamilies.Clone(),
            FontSize = entry.FontSize,
            WindowEffect = entry.WindowEffect,
        };
    }
}
