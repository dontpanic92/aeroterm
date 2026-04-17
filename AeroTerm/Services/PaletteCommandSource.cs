// <copyright file="PaletteCommandSource.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AeroTerm.Models;
using AeroTerm.WindowEffects;

/// <summary>
/// Aggregates the current set of <see cref="PaletteCommand"/> entries
/// from the host window, profiles, color-scheme presets, and
/// window-level actions. Rebuilt on every palette open so the list
/// reflects fresh tab counts, profile edits, and scheme additions
/// without explicit invalidation.
/// </summary>
public static class PaletteCommandSource
{
    /// <summary>
    /// Sentinel stashed so "Toggle transparency" can restore the
    /// previous blur type on a second invocation. Process-global — one
    /// user-facing toggle is sufficient, and the palette surfaces the
    /// command per-window via the <see cref="IPaletteHost"/>.
    /// </summary>
    private static BlurType? previousBlurType;

    /// <summary>
    /// Builds the full command list for the supplied host, profile
    /// snapshot, and color-scheme preset list.
    /// </summary>
    /// <param name="host">The host window abstraction.</param>
    /// <param name="profiles">Profile snapshot, typically
    /// <c>App.Profiles.Profiles</c>.</param>
    /// <param name="colorSchemes">Color-scheme presets, typically
    /// <c>ColorSchemePresets.All</c>.</param>
    /// <returns>The command list in declaration order; the palette
    /// applies its own scoring / MRU ordering.</returns>
    internal static IReadOnlyList<PaletteCommand> Build(
        IPaletteHost host,
        IReadOnlyList<Profile> profiles,
        IReadOnlyList<ColorScheme> colorSchemes)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(colorSchemes);

        var list = new List<PaletteCommand>();

        // --- Tab commands ----------------------------------------------------
        list.Add(new PaletteCommand(
            Id: "tab.new",
            Title: "New tab",
            Subtitle: "Default profile",
            Category: "Tab",
            Execute: () =>
            {
                host.NewTab();
                return ValueTask.CompletedTask;
            }));

        list.Add(new PaletteCommand(
            Id: "tab.close",
            Title: "Close active tab",
            Subtitle: null,
            Category: "Tab",
            Execute: () =>
            {
                host.CloseActiveTab();
                return ValueTask.CompletedTask;
            }));

        list.Add(new PaletteCommand(
            Id: "tab.duplicate",
            Title: "Duplicate tab",
            Subtitle: null,
            Category: "Tab",
            Execute: () =>
            {
                host.DuplicateActiveTab();
                return ValueTask.CompletedTask;
            }));

        list.Add(new PaletteCommand(
            Id: "tab.next",
            Title: "Next tab",
            Subtitle: null,
            Category: "Tab",
            Execute: () =>
            {
                host.ActivateNextTab();
                return ValueTask.CompletedTask;
            }));

        list.Add(new PaletteCommand(
            Id: "tab.prev",
            Title: "Previous tab",
            Subtitle: null,
            Category: "Tab",
            Execute: () =>
            {
                host.ActivatePreviousTab();
                return ValueTask.CompletedTask;
            }));

        for (int i = 0; i < host.TabTitles.Count; i++)
        {
            int index = i;
            string label = host.TabTitles[i];
            list.Add(new PaletteCommand(
                Id: $"tab.jump.{index}",
                Title: $"Jump to tab {index + 1}",
                Subtitle: string.IsNullOrWhiteSpace(label) ? null : label,
                Category: "Tab",
                Execute: () =>
            {
                host.ActivateTabByIndex(index);
                return ValueTask.CompletedTask;
            }));
        }

        // --- Profile commands ------------------------------------------------
        foreach (var profile in profiles)
        {
            var captured = profile;
            list.Add(new PaletteCommand(
                Id: $"profile.activate.{captured.Id}",
                Title: $"New tab: {captured.Name}",
                Subtitle: captured.Command,
                Category: "Profile",
                Execute: () =>
            {
                host.NewTabFromProfile(captured);
                return ValueTask.CompletedTask;
            }));
        }

        // --- Color scheme commands ------------------------------------------
        var settings = host.Settings;
        foreach (var scheme in colorSchemes)
        {
            var captured = scheme;
            list.Add(new PaletteCommand(
                Id: $"scheme.activate.{captured.Name}",
                Title: $"Switch color scheme: {captured.Name}",
                Subtitle: captured.Name == settings.ColorSchemeName ? "Current" : null,
                Category: "Color scheme",
                Execute: () =>
                {
                    settings.ColorSchemeName = captured.Name;
                    settings.ForegroundColor = captured.Foreground;
                    settings.BackgroundColor = captured.Background;
                    return ValueTask.CompletedTask;
                }));
        }

        // --- Window commands -------------------------------------------------
        list.Add(new PaletteCommand(
            Id: "window.toggle-transparency",
            Title: "Toggle transparency",
            Subtitle: "Swap the current window effect with transparent (or restore it).",
            Category: "Window",
            Execute: () =>
            {
                if (settings.BlurType == BlurType.Transparent)
                {
                    settings.BlurType = previousBlurType ?? BlurType.Acrylic;
                }
                else
                {
                    previousBlurType = settings.BlurType;
                    settings.BlurType = BlurType.Transparent;
                }

                return ValueTask.CompletedTask;
            }));

        list.Add(new PaletteCommand(
            Id: "window.open-settings",
            Title: "Open Settings",
            Subtitle: null,
            Category: "Window",
            Execute: () =>
            {
                host.OpenSettings();
                return ValueTask.CompletedTask;
            }));

        list.Add(new PaletteCommand(
            Id: "window.new",
            Title: "New window",
            Subtitle: null,
            Category: "Window",
            Execute: () =>
            {
                host.NewWindow();
                return ValueTask.CompletedTask;
            }));

        list.Add(new PaletteCommand(
            Id: "window.close",
            Title: "Close window",
            Subtitle: null,
            Category: "Window",
            Execute: () =>
            {
                host.CloseHostWindow();
                return ValueTask.CompletedTask;
            }));

        list.Add(new PaletteCommand(
            Id: "keybindings.reload",
            Title: "Reload keybindings",
            Subtitle: "Re-read keybindings.json from disk.",
            Category: "Window",
            Execute: () =>
            {
                host.ReloadKeybindings();
                return ValueTask.CompletedTask;
            }));

        return list;
    }
}
