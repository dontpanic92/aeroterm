// <copyright file="IPaletteHost.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// Abstraction over the window-level operations the Command Palette
/// needs. Implemented by <c>MainWindow</c> in production and by a
/// lightweight fake in tests so command-source logic can be exercised
/// without a live Avalonia window.
/// </summary>
internal interface IPaletteHost
{
    /// <summary>
    /// Gets the live titles of the window's tabs in tab-strip order.
    /// Used to synthesize "Jump to tab N" commands and pick a reasonable
    /// subtitle ("tab N — {title}") without the palette needing to see
    /// the full <c>TabSession</c>.
    /// </summary>
    IReadOnlyList<string> TabTitles { get; }

    /// <summary>
    /// Gets the one-based index of the active tab, or zero when no tab
    /// is active. Used to label "Close active tab" etc.
    /// </summary>
    int ActiveTabIndex { get; }

    /// <summary>Gets the application settings to mutate for scheme /
    /// transparency commands.</summary>
    AppSettings Settings { get; }

    /// <summary>
    /// Gets the live snapshot of tab-group definitions the active
    /// window knows about. Palette commands use this to offer "Add
    /// active tab to group …" entries.
    /// </summary>
    IReadOnlyList<TabGroup> TabGroups { get; }

    /// <summary>Opens a new tab using the application's default profile.</summary>
    void NewTab();

    /// <summary>Opens a new tab launched from the supplied profile.</summary>
    /// <param name="profile">The profile to launch.</param>
    void NewTabFromProfile(Profile profile);

    /// <summary>Closes the currently-active tab.</summary>
    void CloseActiveTab();

    /// <summary>Duplicates the currently-active tab.</summary>
    void DuplicateActiveTab();

    /// <summary>Activates the next tab (wraps).</summary>
    void ActivateNextTab();

    /// <summary>Activates the previous tab (wraps).</summary>
    void ActivatePreviousTab();

    /// <summary>Activates the tab at the given zero-based index.</summary>
    /// <param name="index">The zero-based tab index.</param>
    void ActivateTabByIndex(int index);

    /// <summary>Opens the settings dialog.</summary>
    void OpenSettings();

    /// <summary>Opens a new top-level window.</summary>
    void NewWindow();

    /// <summary>Closes this window.</summary>
    void CloseHostWindow();

    /// <summary>Moves the active tab one slot to the left.</summary>
    void MoveActiveTabLeft();

    /// <summary>Moves the active tab one slot to the right.</summary>
    void MoveActiveTabRight();

    /// <summary>
    /// Reloads keybindings from disk. Surfaced as a palette command so
    /// users editing <c>keybindings.json</c> externally can pick up
    /// changes without restarting.
    /// </summary>
    void ReloadKeybindings();

    /// <summary>
    /// Creates a fresh group (with a synthesized name) and assigns
    /// the active tab to it. Used by the
    /// <see cref="KeybindingAction.GroupNewFromActive"/> binding and
    /// the equivalent palette entry.
    /// </summary>
    void CreateGroupFromActiveTab();

    /// <summary>
    /// Assigns the active tab to the group with the supplied id.
    /// No-op when the id is unknown or there is no active tab.
    /// </summary>
    /// <param name="groupId">Target group id.</param>
    void AssignActiveTabToGroup(string groupId);

    /// <summary>
    /// Removes the active tab from whichever group it currently
    /// belongs to (if any).
    /// </summary>
    void UngroupActiveTab();
}
