// <copyright file="SettingsViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// View model for the settings window. Manages the page list, the
/// currently-selected page, and the search query that filters both the
/// sidebar and individual page rows.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<SettingsPageViewModel> filteredPages;
    private SettingsPageViewModel selectedPage;
    private string searchQuery = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="pages">The list of settings pages to display.</param>
    public SettingsViewModel(IReadOnlyList<SettingsPageViewModel> pages)
    {
        this.Pages = pages;
        this.filteredPages = new ObservableCollection<SettingsPageViewModel>(pages);
        this.FilteredPages = new ReadOnlyObservableCollection<SettingsPageViewModel>(this.filteredPages);
        this.selectedPage = pages[0];
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the full list of available settings pages, unfiltered.
    /// </summary>
    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    /// <summary>
    /// Gets the list of pages that match the current <see cref="SearchQuery"/>.
    /// A page matches when its display name OR at least one of its
    /// <see cref="SettingsPageViewModel.SearchableLabels"/> entries contains the
    /// query (case-insensitive). When the query is empty, every page is returned.
    /// </summary>
    public ReadOnlyObservableCollection<SettingsPageViewModel> FilteredPages { get; }

    /// <summary>
    /// Gets or sets the currently selected settings page.
    /// </summary>
    public SettingsPageViewModel SelectedPage
    {
        get => this.selectedPage;
        set
        {
            if (this.selectedPage != value && value is not null)
            {
                this.selectedPage = value;
                this.OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the current search query. Setting a new value recomputes
    /// <see cref="FilteredPages"/> and keeps <see cref="SelectedPage"/> valid
    /// (falling back to the first visible page when the previously-selected
    /// page is filtered out).
    /// </summary>
    public string SearchQuery
    {
        get => this.searchQuery;
        set
        {
            var newValue = value ?? string.Empty;
            if (this.searchQuery != newValue)
            {
                this.searchQuery = newValue;
                this.OnPropertyChanged();
                this.RecomputeFilteredPages();
            }
        }
    }

    /// <summary>
    /// Determines whether the given page should be visible for the query.
    /// A page matches when its display name OR any of its searchable labels
    /// contains the (trimmed, case-insensitive) query. An empty query matches
    /// every page.
    /// </summary>
    /// <param name="page">The page to test.</param>
    /// <param name="query">The current search query.</param>
    /// <returns><see langword="true"/> when the page should remain visible.</returns>
    public static bool PageMatches(SettingsPageViewModel page, string? query)
    {
        ArgumentNullException.ThrowIfNull(page);
        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        if (page.DisplayName.Contains(trimmed, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var label in page.SearchableLabels)
        {
            if (!string.IsNullOrWhiteSpace(label)
                && label.Contains(trimmed, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void RecomputeFilteredPages()
    {
        var matches = this.Pages.Where(p => PageMatches(p, this.searchQuery)).ToList();

        // Reset the observable collection in place so the bound ListBox updates.
        this.filteredPages.Clear();
        foreach (var page in matches)
        {
            this.filteredPages.Add(page);
        }

        // Keep a valid selection — if the current page was filtered out,
        // fall back to the first visible page (or leave it alone when no
        // pages match).
        if (!matches.Contains(this.selectedPage) && matches.Count > 0)
        {
            this.SelectedPage = matches[0];
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
