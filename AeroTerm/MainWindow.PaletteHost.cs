// <copyright file="MainWindow.PaletteHost.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm;

using System.Collections.Generic;
using System.Linq;
using AeroTerm.Services;
using Avalonia;

/// <summary>
/// <see cref="IPaletteHost"/> implementation for the main window. Split
/// out into its own partial file so the interface members can cluster
/// without fighting StyleCop's element-ordering rules in the rest of
/// the class.
/// </summary>
public partial class MainWindow : IPaletteHost
{
    /// <inheritdoc />
    IReadOnlyList<string> IPaletteHost.TabTitles =>
        this.tabView.Tabs.Select(t => t.Title).ToList();

    /// <inheritdoc />
    int IPaletteHost.ActiveTabIndex
    {
        get
        {
            var active = this.tabView.ActiveTab;
            if (active is null)
            {
                return 0;
            }

            int idx = 0;
            foreach (var t in this.tabView.Tabs)
            {
                if (ReferenceEquals(t, active))
                {
                    return idx + 1;
                }

                idx++;
            }

            return 0;
        }
    }

    /// <inheritdoc />
    AppSettings IPaletteHost.Settings => this.settings;

    /// <inheritdoc />
    IReadOnlyList<TabGroup> IPaletteHost.TabGroups => App.TabGroupStore.Groups;

    /// <summary>
    /// Opens the Cmd/Ctrl+Shift+P command palette non-modally. The
    /// palette constructs its command list from a snapshot of the
    /// current profiles + color-scheme presets, so each open reflects
    /// live state.
    /// </summary>
    public void OpenCommandPalette()
    {
        IPaletteHost host = this;
        var commands = PaletteCommandSource.Build(
            host,
            App.Profiles.Profiles,
            Models.ColorSchemePresets.All);

        var palette = new Dialogs.CommandPaletteWindow(host, App.PaletteMru, commands);
        palette.ShowForOwner(this);
    }

    /// <inheritdoc />
    void IPaletteHost.NewTab() => this.CreateAndActivateNewTab();

    /// <inheritdoc />
    void IPaletteHost.NewTabFromProfile(Profile profile) => this.CreateAndActivateNewTabFromProfile(profile);

    /// <inheritdoc />
    void IPaletteHost.CloseActiveTab()
    {
        if (this.tabView.ActiveTab is { } active)
        {
            this.tabView.CloseTab(active);
        }
    }

    /// <inheritdoc />
    void IPaletteHost.DuplicateActiveTab() => this.DuplicateActiveTab();

    /// <inheritdoc />
    void IPaletteHost.ActivateNextTab() => this.tabView.ActivateNext();

    /// <inheritdoc />
    void IPaletteHost.ActivatePreviousTab() => this.tabView.ActivatePrev();

    /// <inheritdoc />
    void IPaletteHost.ActivateTabByIndex(int index) => this.tabView.ActivateByIndex(index);

    /// <inheritdoc />
    void IPaletteHost.MoveActiveTabLeft() => this.tabView.MoveActiveTabLeft();

    /// <inheritdoc />
    void IPaletteHost.MoveActiveTabRight() => this.tabView.MoveActiveTabRight();

    /// <inheritdoc />
    void IPaletteHost.OpenSettings() => this.OpenSettings();

    /// <inheritdoc />
    void IPaletteHost.NewWindow()
    {
        if (Application.Current is App app)
        {
            app.CreateNewWindow();
        }
    }

    /// <inheritdoc />
    void IPaletteHost.CloseHostWindow() => this.Close();

    /// <inheritdoc />
    void IPaletteHost.ReloadKeybindings() => App.ReloadKeybindings();

    /// <inheritdoc />
    void IPaletteHost.CreateGroupFromActiveTab() => this.CreateGroupFromActiveTab();

    /// <inheritdoc />
    void IPaletteHost.AssignActiveTabToGroup(string groupId)
    {
        if (this.tabView.ActiveTab is { } active)
        {
            active.GroupId = groupId;
        }
    }

    /// <inheritdoc />
    void IPaletteHost.UngroupActiveTab() => this.UngroupActiveTab();

    /// <inheritdoc />
    void IPaletteHost.SplitActivePaneHorizontal() => this.SplitActivePane(AeroTerm.Controls.Panes.PaneOrientation.Horizontal);

    /// <inheritdoc />
    void IPaletteHost.SplitActivePaneVertical() => this.SplitActivePane(AeroTerm.Controls.Panes.PaneOrientation.Vertical);

    /// <inheritdoc />
    void IPaletteHost.FocusPaneLeft() => this.FocusActivePane(AeroTerm.Controls.Panes.PaneDirection.Left);

    /// <inheritdoc />
    void IPaletteHost.FocusPaneRight() => this.FocusActivePane(AeroTerm.Controls.Panes.PaneDirection.Right);

    /// <inheritdoc />
    void IPaletteHost.FocusPaneUp() => this.FocusActivePane(AeroTerm.Controls.Panes.PaneDirection.Up);

    /// <inheritdoc />
    void IPaletteHost.FocusPaneDown() => this.FocusActivePane(AeroTerm.Controls.Panes.PaneDirection.Down);

    /// <inheritdoc />
    void IPaletteHost.CloseActivePane() => this.CloseActivePane();

    /// <inheritdoc />
    void IPaletteHost.ToggleTabBarOrientation() => this.ToggleTabBarOrientation();

    /// <inheritdoc />
    void IPaletteHost.JumpToPreviousCommand()
    {
        this.tabView.ActiveTab?.Terminal?.JumpToPreviousCommand();
    }

    /// <inheritdoc />
    void IPaletteHost.JumpToNextCommand()
    {
        this.tabView.ActiveTab?.Terminal?.JumpToNextCommand();
    }
}
