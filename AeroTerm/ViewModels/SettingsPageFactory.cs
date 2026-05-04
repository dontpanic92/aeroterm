// <copyright file="SettingsPageFactory.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.Collections.Generic;
using AeroTerm.Services;

/// <summary>
/// Constructs the ordered settings page list used by the settings dialog.
/// </summary>
internal static class SettingsPageFactory
{
    /// <summary>
    /// Creates the complete set of pages for the running application.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    /// <param name="keybindingStore">The keybinding store.</param>
    /// <param name="profileStore">The profile store.</param>
    /// <param name="updateService">The update service.</param>
    /// <returns>The ordered settings pages.</returns>
    public static IReadOnlyList<SettingsPageViewModel> CreateApplicationPages(
        AppSettings settings,
        KeybindingStore keybindingStore,
        ProfileStore profileStore,
        IUpdateService updateService)
    {
        return new SettingsPageViewModel[]
        {
            new AppearancePageViewModel(settings),
            new TerminalPageViewModel(settings),
            new WindowTabsPageViewModel(settings),
            new KeybindingsPageViewModel(keybindingStore),
            new ProfilesPageViewModel(profileStore),
            new UpdatesPageViewModel(settings, updateService),
        };
    }

    /// <summary>
    /// Creates the AppSettings-backed pages plus Updates for settings-window tests.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    /// <param name="updateService">The update service.</param>
    /// <returns>The ordered core settings pages.</returns>
    public static IReadOnlyList<SettingsPageViewModel> CreateCorePages(
        AppSettings settings,
        IUpdateService updateService)
    {
        return new SettingsPageViewModel[]
        {
            new AppearancePageViewModel(settings),
            new TerminalPageViewModel(settings),
            new WindowTabsPageViewModel(settings),
            new UpdatesPageViewModel(settings, updateService),
        };
    }
}
