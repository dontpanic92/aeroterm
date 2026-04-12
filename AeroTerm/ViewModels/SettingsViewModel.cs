// <copyright file="SettingsViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// View model for the settings window. Manages the page list and selected page.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private SettingsPageViewModel selectedPage;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="pages">The list of settings pages to display.</param>
    public SettingsViewModel(IReadOnlyList<SettingsPageViewModel> pages)
    {
        this.Pages = pages;
        this.selectedPage = pages[0];
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the list of available settings pages.
    /// </summary>
    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    /// <summary>
    /// Gets or sets the currently selected settings page.
    /// </summary>
    public SettingsPageViewModel SelectedPage
    {
        get => this.selectedPage;
        set
        {
            if (this.selectedPage != value)
            {
                this.selectedPage = value;
                this.OnPropertyChanged();
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
