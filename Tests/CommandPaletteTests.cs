// <copyright file="CommandPaletteTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AeroTerm.Models;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="PaletteCommandSource"/>, <see cref="PaletteMruStore"/>,
/// and the <see cref="IPaletteHost"/> seam.
/// </summary>
[TestFixture]
public class CommandPaletteTests
{
    private string tempDir = string.Empty;

    /// <summary>Creates a per-test temp directory under the test work dir.</summary>
    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "palette-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);
    }

    /// <summary>Cleans up the temp directory.</summary>
    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, recursive: true);
        }
    }

    /// <summary>Profile commands reflect the live profile list.</summary>
    [Test]
    public void ProfileCommands_ReflectProfileList()
    {
        var host = new FakePaletteHost();
        var profiles = new List<Profile>
        {
            new Profile { Name = "Dev" },
            new Profile { Name = "Ops" },
        };

        var commands = InvokeBuild(host, profiles, ColorSchemePresets.All);
        var profileTitles = commands
            .Where(c => c.Category == "Profile")
            .Select(c => c.Title)
            .ToList();

        Assert.That(profileTitles, Has.Count.EqualTo(2));
        Assert.That(profileTitles, Does.Contain("New tab: Dev"));
        Assert.That(profileTitles, Does.Contain("New tab: Ops"));
    }

    /// <summary>
    /// Tab commands include a "Jump to tab N" row for each open tab.
    /// </summary>
    [Test]
    public void TabCommands_IncludeJumpPerTab()
    {
        var host = new FakePaletteHost
        {
            TabTitlesList = new List<string> { "bash", "vim", "htop" },
        };

        var commands = InvokeBuild(host, new List<Profile>(), ColorSchemePresets.All);
        var jumps = commands
            .Where(c => c.Id.StartsWith("tab.jump.", StringComparison.Ordinal))
            .Select(c => c.Title)
            .ToList();

        Assert.That(jumps, Is.EqualTo(new[] { "Jump to tab 1", "Jump to tab 2", "Jump to tab 3" }));
    }

    /// <summary>
    /// Each palette rebuild reads the latest profile list — the source
    /// is stateless, so a <see cref="ProfileStore.ProfilesChanged"/>
    /// event in the host is reflected on the next open simply by
    /// passing fresh data.
    /// </summary>
    [Test]
    public void ProfileCommands_PickUpChangesOnRebuild()
    {
        var host = new FakePaletteHost();
        var profiles = new List<Profile> { new Profile { Name = "A" } };
        var before = InvokeBuild(host, profiles, ColorSchemePresets.All)
            .Count(c => c.Category == "Profile");

        profiles.Add(new Profile { Name = "B" });
        var after = InvokeBuild(host, profiles, ColorSchemePresets.All)
            .Count(c => c.Category == "Profile");

        Assert.That(before, Is.EqualTo(1));
        Assert.That(after, Is.EqualTo(2));
    }

    /// <summary>Invoking a command moves its id to the head of the MRU.</summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Test]
    public async Task Mru_RecordMovesIdToHead()
    {
        var mru = new PaletteMruStore(this.tempDir);
        Assert.That(mru.RankOf("tab.new"), Is.EqualTo(int.MaxValue));

        mru.Record("tab.next");
        mru.Record("tab.new");

        Assert.That(mru.RankOf("tab.new"), Is.EqualTo(0));
        Assert.That(mru.RankOf("tab.next"), Is.EqualTo(1));

        // Re-recording moves the id to the front again.
        mru.Record("tab.next");
        Assert.That(mru.RankOf("tab.next"), Is.EqualTo(0));
        Assert.That(mru.RankOf("tab.new"), Is.EqualTo(1));
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>MRU persists across store instances via the backing file.</summary>
    [Test]
    public void Mru_PersistenceRoundTrip()
    {
        var first = new PaletteMruStore(this.tempDir);
        first.Record("scheme.activate.Dracula");
        first.Record("tab.new");

        var second = new PaletteMruStore(this.tempDir);
        Assert.That(second.Order, Is.EqualTo(new[] { "tab.new", "scheme.activate.Dracula" }));
    }

    /// <summary>A missing file yields an empty MRU without throwing.</summary>
    [Test]
    public void Mru_MissingFile_ReturnsEmpty()
    {
        var store = new PaletteMruStore(this.tempDir);
        Assert.That(store.Order, Is.Empty);
        Assert.That(File.Exists(store.FilePath), Is.False);
    }

    /// <summary>A malformed file yields an empty MRU without throwing.</summary>
    [Test]
    public void Mru_MalformedFile_ReturnsEmpty()
    {
        string path = Path.Combine(this.tempDir, "palette-mru.json");
        File.WriteAllText(path, "{this is not JSON");

        var store = new PaletteMruStore(this.tempDir);
        Assert.That(store.Order, Is.Empty);
    }

    /// <summary>
    /// The MRU list is bounded at <see cref="PaletteMruStore.MaxEntries"/>
    /// entries; older ids fall off the tail.
    /// </summary>
    [Test]
    public void Mru_BoundedCapacity()
    {
        var store = new PaletteMruStore(this.tempDir);
        for (int i = 0; i < PaletteMruStore.MaxEntries + 5; i++)
        {
            store.Record($"cmd.{i}");
        }

        Assert.That(store.Order.Count, Is.EqualTo(PaletteMruStore.MaxEntries));
        Assert.That(store.Order[0], Is.EqualTo($"cmd.{PaletteMruStore.MaxEntries + 4}"));
    }

    /// <summary>
    /// Toggle-transparency swaps the blur type between its current
    /// value and <c>Transparent</c> on successive invocations.
    /// </summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Test]
    public async Task ToggleTransparency_SwapsBlurType()
    {
        var host = new FakePaletteHost();
        host.FakeSettings.BlurType = WindowEffects.BlurType.Acrylic;

        var commands = InvokeBuild(host, new List<Profile>(), ColorSchemePresets.All);
        var toggle = commands.First(c => c.Id == "window.toggle-transparency");

        await toggle.Execute().ConfigureAwait(false);
        Assert.That(host.FakeSettings.BlurType, Is.EqualTo(WindowEffects.BlurType.Transparent));

        await toggle.Execute().ConfigureAwait(false);
        Assert.That(host.FakeSettings.BlurType, Is.EqualTo(WindowEffects.BlurType.Acrylic));
    }

    /// <summary>Executing a tab command delegates to the host.</summary>
    /// <returns>A task representing asynchronous test completion.</returns>
    [Test]
    public async Task NewTabCommand_DelegatesToHost()
    {
        var host = new FakePaletteHost();
        var commands = InvokeBuild(host, new List<Profile>(), ColorSchemePresets.All);
        var newTab = commands.First(c => c.Id == "tab.new");

        await newTab.Execute().ConfigureAwait(false);
        Assert.That(host.NewTabCalls, Is.EqualTo(1));
    }

    private static IReadOnlyList<PaletteCommand> InvokeBuild(
        IPaletteHost host,
        IReadOnlyList<Profile> profiles,
        IReadOnlyList<ColorScheme> schemes)
    {
        // PaletteCommandSource.Build is internal — Tests has InternalsVisibleTo.
        return PaletteCommandSource.Build(host, profiles, schemes);
    }

    /// <summary>
    /// In-memory <see cref="IPaletteHost"/> for testing the command
    /// source without a live Avalonia window.
    /// </summary>
    private sealed class FakePaletteHost : IPaletteHost
    {
        public AppSettings FakeSettings { get; } = new AppSettings();

        public List<string> TabTitlesList { get; set; } = new List<string>();

        public int NewTabCalls { get; private set; }

        public IReadOnlyList<string> TabTitles => this.TabTitlesList;

        public int ActiveTabIndex { get; set; }

        public AppSettings Settings => this.FakeSettings;

        public IReadOnlyList<TabGroup> TabGroups => Array.Empty<TabGroup>();

        public void NewTab() => this.NewTabCalls++;

        public void NewTabFromProfile(Profile profile)
        {
        }

        public void CloseActiveTab()
        {
        }

        public void DuplicateActiveTab()
        {
        }

        public void ActivateNextTab()
        {
        }

        public void ActivatePreviousTab()
        {
        }

        public void ActivateTabByIndex(int index)
        {
        }

        public void MoveActiveTabLeft()
        {
        }

        public void MoveActiveTabRight()
        {
        }

        public void OpenSettings()
        {
        }

        public void NewWindow()
        {
        }

        public void CloseHostWindow()
        {
        }

        public void ReloadKeybindings()
        {
        }

        public void CreateGroupFromActiveTab()
        {
        }

        public void AssignActiveTabToGroup(string groupId)
        {
        }

        public void UngroupActiveTab()
        {
        }
    }
}
