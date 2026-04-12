// <copyright file="UpdatesPage.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Dialogs;

using AeroTerm.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

/// <summary>
/// Updates settings page.
/// </summary>
public partial class UpdatesPage : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatesPage"/> class.
    /// </summary>
    public UpdatesPage()
    {
        this.InitializeComponent();
    }

    private void ReleaseNotesLink_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (this.DataContext is UpdatesPageViewModel vm)
        {
            vm.ViewReleaseNotes();
        }
    }

    private async void CheckForUpdates_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is UpdatesPageViewModel vm)
        {
            await vm.CheckForUpdateAsync();
        }
    }

    private async void DownloadAndInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is UpdatesPageViewModel vm)
        {
            await vm.DownloadAndInstallAsync();
        }
    }

    private void RestartToUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is UpdatesPageViewModel vm)
        {
            vm.RestartToUpdate();
        }
    }

    private void SkipThisVersion_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is UpdatesPageViewModel vm)
        {
            vm.SkipThisVersion();
        }
    }
}
