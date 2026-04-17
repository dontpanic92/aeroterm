// <copyright file="SettingsSearchTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Tests;

using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using AeroTerm.Dialogs;
using AeroTerm.ViewModels;
using NUnit.Framework;

/// <summary>
/// Tests for the settings dialog search filter:
/// <see cref="SettingsSearch.Matches"/>, <see cref="SettingsViewModel.PageMatches"/>,
/// and <see cref="SettingsViewModel.FilteredPages"/> recomputation.
/// </summary>
[TestFixture]
public class SettingsSearchTests
{
    /// <summary>An empty query makes every row visible.</summary>
    [Test]
    public void Matches_EmptyQuery_ReturnsTrueForAnyLabel()
    {
        Assert.That(SettingsSearch.Matches("Font size", string.Empty), Is.True);
        Assert.That(SettingsSearch.Matches("Font size", null), Is.True);
        Assert.That(SettingsSearch.Matches("Font size", "   "), Is.True);
        Assert.That(SettingsSearch.Matches(null, string.Empty), Is.True);
    }

    /// <summary>Rows with a null or whitespace label are always visible.</summary>
    [Test]
    public void Matches_MissingLabel_StaysVisible()
    {
        Assert.That(SettingsSearch.Matches(null, "font"), Is.True);
        Assert.That(SettingsSearch.Matches(string.Empty, "font"), Is.True);
        Assert.That(SettingsSearch.Matches("   ", "font"), Is.True);
    }

    /// <summary>Matching is case-insensitive substring over the trimmed query.</summary>
    [Test]
    public void Matches_CaseInsensitiveSubstring()
    {
        Assert.That(SettingsSearch.Matches("Font Size", "font"), Is.True);
        Assert.That(SettingsSearch.Matches("Font Size", "FONT"), Is.True);
        Assert.That(SettingsSearch.Matches("Font Size", "  size  "), Is.True);
        Assert.That(SettingsSearch.Matches("Font Size", "color"), Is.False);
    }

    /// <summary>A page matches when its display name contains the query.</summary>
    [Test]
    public void PageMatches_DisplayNameMatch()
    {
        var page = new FakePage("Appearance", new[] { "Font", "Color" });
        Assert.That(SettingsViewModel.PageMatches(page, "appear"), Is.True);
    }

    /// <summary>A page matches when at least one searchable label contains the query.</summary>
    [Test]
    public void PageMatches_LabelMatch()
    {
        var page = new FakePage("Appearance", new[] { "Font Size", "Color Scheme" });
        Assert.That(SettingsViewModel.PageMatches(page, "color"), Is.True);
    }

    /// <summary>A page with no matching display name or labels is filtered out.</summary>
    [Test]
    public void PageMatches_NoMatch()
    {
        var page = new FakePage("Appearance", new[] { "Font Size" });
        Assert.That(SettingsViewModel.PageMatches(page, "keybinding"), Is.False);
    }

    /// <summary>Empty or whitespace queries always match.</summary>
    [Test]
    public void PageMatches_EmptyQuery_MatchesEverything()
    {
        var page = new FakePage("Anything", System.Array.Empty<string>());
        Assert.That(SettingsViewModel.PageMatches(page, string.Empty), Is.True);
        Assert.That(SettingsViewModel.PageMatches(page, null), Is.True);
        Assert.That(SettingsViewModel.PageMatches(page, "   "), Is.True);
    }

    /// <summary><see cref="SettingsViewModel.FilteredPages"/> is recomputed when
    /// <see cref="SettingsViewModel.SearchQuery"/> changes.</summary>
    [Test]
    public void FilteredPages_UpdatesWhenSearchQueryChanges()
    {
        var pages = new List<SettingsPageViewModel>
        {
            new FakePage("Appearance", new[] { "Font Size", "Color Scheme" }),
            new FakePage("Updates", new[] { "Channel" }),
        };

        var vm = new SettingsViewModel(pages);
        var collectionChanges = 0;
        ((INotifyCollectionChanged)vm.FilteredPages).CollectionChanged += (_, _) => collectionChanges++;

        Assert.That(vm.FilteredPages, Has.Count.EqualTo(2));

        vm.SearchQuery = "color";
        Assert.That(vm.FilteredPages, Has.Count.EqualTo(1));
        Assert.That(vm.FilteredPages[0].DisplayName, Is.EqualTo("Appearance"));
        Assert.That(collectionChanges, Is.GreaterThan(0));

        vm.SearchQuery = "channel";
        Assert.That(vm.FilteredPages, Has.Count.EqualTo(1));
        Assert.That(vm.FilteredPages[0].DisplayName, Is.EqualTo("Updates"));

        vm.SearchQuery = string.Empty;
        Assert.That(vm.FilteredPages, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// When the current page is filtered out, selection falls back to the first
    /// visible page so the content area does not point at a hidden entry.
    /// </summary>
    [Test]
    public void FilteredPages_ResetsSelectionWhenCurrentPageFilteredOut()
    {
        var pages = new List<SettingsPageViewModel>
        {
            new FakePage("Appearance", new[] { "Font" }),
            new FakePage("Updates", new[] { "Channel" }),
        };
        var vm = new SettingsViewModel(pages) { SelectedPage = pages[1] };

        vm.SearchQuery = "font";

        Assert.That(vm.FilteredPages, Has.Count.EqualTo(1));
        Assert.That(vm.SelectedPage, Is.SameAs(pages[0]));
    }

    /// <summary>Setting the same query twice does not raise redundant change events.</summary>
    [Test]
    public void SearchQuery_IdempotentAssignment()
    {
        var pages = new List<SettingsPageViewModel> { new FakePage("One", System.Array.Empty<string>()) };
        var vm = new SettingsViewModel(pages);
        var propertyChanges = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.SearchQuery))
            {
                propertyChanges++;
            }
        };

        vm.SearchQuery = "abc";
        vm.SearchQuery = "abc";

        Assert.That(propertyChanges, Is.EqualTo(1));
    }

    private sealed class FakePage : SettingsPageViewModel
    {
        public FakePage(string displayName, IReadOnlyList<string> labels)
        {
            this.DisplayName = displayName;
            this.SearchableLabels = labels;
        }

        public override string DisplayName { get; }

        public override IReadOnlyList<string> SearchableLabels { get; }
    }
}
