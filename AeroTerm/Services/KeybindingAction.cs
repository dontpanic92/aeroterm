// <copyright file="KeybindingAction.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Enumerates every app-level action that can be bound to a keyboard chord.
/// Values are stable identifiers serialized into <c>keybindings.json</c>;
/// do not rename or reuse existing members.
/// </summary>
public enum KeybindingAction
{
    /// <summary>Open a new tab in the current window.</summary>
    NewTab,

    /// <summary>Close the active tab.</summary>
    CloseTab,

    /// <summary>Activate the next tab (wraps).</summary>
    NextTab,

    /// <summary>Activate the previous tab (wraps).</summary>
    PreviousTab,

    /// <summary>Activate tab 1.</summary>
    JumpToTab1,

    /// <summary>Activate tab 2.</summary>
    JumpToTab2,

    /// <summary>Activate tab 3.</summary>
    JumpToTab3,

    /// <summary>Activate tab 4.</summary>
    JumpToTab4,

    /// <summary>Activate tab 5.</summary>
    JumpToTab5,

    /// <summary>Activate tab 6.</summary>
    JumpToTab6,

    /// <summary>Activate tab 7.</summary>
    JumpToTab7,

    /// <summary>Activate tab 8.</summary>
    JumpToTab8,

    /// <summary>Activate tab 9.</summary>
    JumpToTab9,

    /// <summary>Duplicate the active tab.</summary>
    DuplicateTab,

    /// <summary>Open the Settings window.</summary>
    OpenSettings,

    /// <summary>Copy the current selection to the clipboard.</summary>
    Copy,

    /// <summary>Paste clipboard text into the terminal.</summary>
    Paste,

    /// <summary>Open the scrollback search overlay.</summary>
    FindInScrollback,

    /// <summary>Open a new application window.</summary>
    NewWindow,

    /// <summary>Close the active window.</summary>
    CloseWindow,

    /// <summary>Reserved. Not wired yet.</summary>
    ToggleTransparency,

    /// <summary>Reserved. Not wired yet.</summary>
    OpenCommandPalette,

    /// <summary>Move the active tab one slot to the left.</summary>
    MoveTabLeft,

    /// <summary>Move the active tab one slot to the right.</summary>
    MoveTabRight,

    /// <summary>Create a new tab group from the active tab.</summary>
    GroupNewFromActive,

    /// <summary>Remove the active tab from its current group.</summary>
    UngroupActive,

    /// <summary>Split the active pane with a horizontal divider (stacks the two panes vertically).</summary>
    SplitPaneHorizontal,

    /// <summary>Split the active pane with a vertical divider (places the two panes side-by-side).</summary>
    SplitPaneVertical,

    /// <summary>Move pane focus one pane to the left.</summary>
    FocusPaneLeft,

    /// <summary>Move pane focus one pane to the right.</summary>
    FocusPaneRight,

    /// <summary>Move pane focus one pane up.</summary>
    FocusPaneUp,

    /// <summary>Move pane focus one pane down.</summary>
    FocusPaneDown,

    /// <summary>Close the active pane (falls back to closing the tab when it is the last pane).</summary>
    ClosePane,

    /// <summary>Toggle the tab-strip orientation between horizontal and vertical.</summary>
    ToggleTabBarOrientation,
}
