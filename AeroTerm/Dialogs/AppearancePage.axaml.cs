// <copyright file="AppearancePage.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Dialogs;

using AeroTerm.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

/// <summary>
/// Appearance settings page.
/// </summary>
public partial class AppearancePage : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppearancePage"/> class.
    /// </summary>
    public AppearancePage()
    {
        this.InitializeComponent();
    }

    private AppearancePageViewModel? ViewModel => this.DataContext as AppearancePageViewModel;

    private async void FontAdd_Click(object? sender, RoutedEventArgs e)
    {
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null || this.ViewModel is null)
        {
            return;
        }

        var picker = new FontPickerWindow();
        await picker.ShowDialog(owner);

        if (!string.IsNullOrWhiteSpace(picker.SelectedFontName))
        {
            this.ViewModel.AddFont(picker.SelectedFontName);
        }
    }

    private void FontRemove_Click(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.RemoveFont();
    }

    private void FontMoveUp_Click(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.MoveFontUp();
    }

    private void FontMoveDown_Click(object? sender, RoutedEventArgs e)
    {
        this.ViewModel?.MoveFontDown();
    }
}
