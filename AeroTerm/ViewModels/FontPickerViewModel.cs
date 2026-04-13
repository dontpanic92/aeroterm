// <copyright file="FontPickerViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SkiaSharp;

/// <summary>
/// View model for the font picker dialog.
/// </summary>
internal sealed class FontPickerViewModel : INotifyPropertyChanged
{
    private readonly string[] allFontNames;
    private string filterText = string.Empty;
    private string? selectedFontName;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontPickerViewModel"/> class.
    /// </summary>
    public FontPickerViewModel()
    {
        this.allFontNames = SKFontManager.Default.FontFamilies
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var name in this.allFontNames)
        {
            this.FilteredFonts.Add(name);
        }
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the filtered list of font names.
    /// </summary>
    public ObservableCollection<string> FilteredFonts { get; } = new();

    /// <summary>
    /// Gets or sets the filter text.
    /// </summary>
    public string FilterText
    {
        get => this.filterText;
        set
        {
            if (this.filterText != value)
            {
                this.filterText = value;
                this.OnPropertyChanged();
                this.ApplyFilter();
            }
        }
    }

    /// <summary>
    /// Gets or sets the selected font name.
    /// </summary>
    public string? SelectedFontName
    {
        get => this.selectedFontName;
        set
        {
            if (this.selectedFontName != value)
            {
                this.selectedFontName = value;
                this.OnPropertyChanged();
            }
        }
    }

    private void ApplyFilter()
    {
        this.FilteredFonts.Clear();
        foreach (var name in this.allFontNames)
        {
            if (name.Contains(this.filterText, StringComparison.OrdinalIgnoreCase))
            {
                this.FilteredFonts.Add(name);
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
