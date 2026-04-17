// <copyright file="ProfilesPage.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Dialogs;

using AeroTerm.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

/// <summary>
/// Code-behind for the Profiles settings page. Hosts a list of
/// <see cref="Services.Profile"/> entries with an inline editor and
/// forwards button clicks to <see cref="ProfilesPageViewModel"/>.
/// </summary>
internal partial class ProfilesPage : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProfilesPage"/> class.
    /// </summary>
    public ProfilesPage()
    {
        this.InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is ProfilesPageViewModel vm)
        {
            vm.AddProfile();
        }
    }

    private void DuplicateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is ProfilesPageViewModel vm)
        {
            vm.DuplicateSelected();
        }
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is ProfilesPageViewModel vm)
        {
            vm.RemoveSelected();
        }
    }

    private void SetDefaultButton_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is ProfilesPageViewModel vm)
        {
            vm.SetSelectedAsDefault();
        }
    }
}
