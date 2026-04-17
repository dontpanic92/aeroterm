// <copyright file="SettingsWindowHeadlessTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.UI;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using AeroTerm.Dialogs;
using AeroTerm.Services;
using AeroTerm.ViewModels;
using Avalonia.Headless.NUnit;
using NUnit.Framework;

/// <summary>
/// Headless UI tests for <see cref="SettingsWindow"/>: verifies that the
/// view-model's search filtering, page selection, and persisted size flow
/// end-to-end through the real Avalonia window.
/// </summary>
[TestFixture]
public class SettingsWindowHeadlessTests
{
    private string? tempSettingsDir;

    /// <summary>
    /// Redirects <see cref="AppSettings"/> persistence to a scratch
    /// directory so assertions on persisted width / height don't trample
    /// the developer's real settings file.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        this.tempSettingsDir = Path.Combine(
            Path.GetTempPath(),
            "AeroTerm.Tests.SettingsWindowHeadless",
            System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempSettingsDir);
        AppSettings.SetStorageDirectoryForTesting(this.tempSettingsDir);
    }

    /// <summary>
    /// Resets the settings override and removes the scratch directory.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        AppSettings.ResetForTesting();
        if (this.tempSettingsDir is not null && Directory.Exists(this.tempSettingsDir))
        {
            try
            {
                Directory.Delete(this.tempSettingsDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; ignore locked files.
            }
        }
    }

    /// <summary>
    /// Typing a query that matches only the Appearance page's searchable
    /// labels filters the sidebar down to that single page.
    /// </summary>
    [AvaloniaTest]
    public void SettingsSearch_MatchingQuery_FiltersSidebar()
    {
        var vm = BuildViewModel(out var settings);
        var window = new SettingsWindow(settings, vm);
        window.Show();

        vm.SearchQuery = "scrollback";

        Assert.That(vm.FilteredPages, Has.Count.EqualTo(1));
        Assert.That(vm.FilteredPages[0].DisplayName, Is.EqualTo("Appearance"));
    }

    /// <summary>
    /// Clearing a previously-set search query restores the full sidebar.
    /// </summary>
    [AvaloniaTest]
    public void SettingsSearch_EmptyQuery_RestoresAllPages()
    {
        var vm = BuildViewModel(out var settings);
        var window = new SettingsWindow(settings, vm);
        window.Show();

        vm.SearchQuery = "scrollback";
        Assume.That(vm.FilteredPages, Has.Count.EqualTo(1));

        vm.SearchQuery = string.Empty;

        Assert.That(vm.FilteredPages, Has.Count.EqualTo(vm.Pages.Count));
    }

    /// <summary>
    /// Swapping <see cref="SettingsViewModel.SelectedPage"/> raises the
    /// expected property change. The bound ContentPresenter renders the
    /// new page's content; here we validate the VM wiring which drives
    /// that presenter.
    /// </summary>
    [AvaloniaTest]
    public void SettingsWindow_SelectingPage_ChangesContent()
    {
        var vm = BuildViewModel(out var settings);
        var window = new SettingsWindow(settings, vm);
        window.Show();

        Assume.That(vm.Pages, Has.Count.GreaterThanOrEqualTo(2));

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var other = vm.Pages.First(p => p != vm.SelectedPage);
        vm.SelectedPage = other;

        Assert.That(vm.SelectedPage, Is.SameAs(other));
        Assert.That(raised, Does.Contain(nameof(SettingsViewModel.SelectedPage)));
    }

    /// <summary>
    /// Resizing the settings window and closing it persists the new size
    /// to <see cref="AppSettings"/>; a freshly opened window restores it.
    /// </summary>
    [AvaloniaTest]
    public void SettingsWindow_Size_IsPersisted()
    {
        var settings = AppSettings.Default;
        var vm1 = new SettingsViewModel(BuildPages(settings));
        var window1 = new SettingsWindow(settings, vm1);
        window1.Show();
        window1.Width = 720;
        window1.Height = 540;
        window1.Close();

        Assert.That(settings.SettingsWindowWidth, Is.EqualTo(720));
        Assert.That(settings.SettingsWindowHeight, Is.EqualTo(540));

        var vm2 = new SettingsViewModel(BuildPages(settings));
        var window2 = new SettingsWindow(settings, vm2);
        window2.Show();

        Assert.That((int)window2.Width, Is.EqualTo(720));
        Assert.That((int)window2.Height, Is.EqualTo(540));
    }

    private static IReadOnlyList<SettingsPageViewModel> BuildPages(AppSettings settings)
    {
        return new SettingsPageViewModel[]
        {
            new AppearancePageViewModel(settings),
            new UpdatesPageViewModel(settings, new UpdateService(settings)),
        };
    }

    private static SettingsViewModel BuildViewModel(out AppSettings settings)
    {
        settings = AppSettings.Default;
        return new SettingsViewModel(BuildPages(settings));
    }
}
