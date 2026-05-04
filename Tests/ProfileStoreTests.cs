// <copyright file="ProfileStoreTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Unit tests for <see cref="ProfileStore"/> file I/O and fault tolerance.
/// </summary>
[TestFixture]
public class ProfileStoreTests
{
    private string tempDir = string.Empty;

    /// <summary>Create a per-test temp directory under the test work dir.</summary>
    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "profiles-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);
    }

    /// <summary>Clean up the temp directory after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, recursive: true);
        }
    }

    /// <summary>Load synthesizes a default profile when profiles.json is absent and no shells were discovered.</summary>
    [Test]
    public void Load_MissingFile_NoDiscovery_ReturnsSynthesizedDefault()
    {
        var store = new ProfileStore(this.tempDir, () => System.Array.Empty<DiscoveredShell>());
        var data = store.Load();

        Assert.That(data.Profiles, Has.Count.EqualTo(1));
        Assert.That(data.Profiles[0].Name, Is.EqualTo("Default"));
        Assert.That(data.DefaultProfileId, Is.EqualTo(data.Profiles[0].Id));
        Assert.That(data.DefaultProfile, Is.SameAs(data.Profiles[0]));
    }

    /// <summary>Load seeds profiles.json from discovery on first run and persists immediately.</summary>
    [Test]
    public void Load_MissingFile_WithDiscovery_SeedsAndPersists()
    {
        var discovered = new List<DiscoveredShell>
        {
            new("Bash", "/bin/bash", System.Array.Empty<string>(), null),
            new("Zsh", "/bin/zsh", System.Array.Empty<string>(), null),
        };
        var store = new ProfileStore(this.tempDir, () => discovered);

        var data = store.Load();

        Assert.That(data.Profiles, Has.Count.EqualTo(2));
        Assert.That(data.Profiles.Select(p => p.Command), Is.EquivalentTo(new[] { "/bin/bash", "/bin/zsh" }));
        Assert.That(File.Exists(store.FilePath), Is.True, "Seeded profiles should be persisted to disk.");

        // Subsequent loads (with discovery returning nothing) should still
        // yield the two persisted profiles.
        var store2 = new ProfileStore(this.tempDir, () => System.Array.Empty<DiscoveredShell>());
        var data2 = store2.Load();
        Assert.That(data2.Profiles, Has.Count.EqualTo(2));
    }

    /// <summary>Malformed JSON does not throw and yields a synthesized default.</summary>
    [Test]
    public void Load_MalformedJson_ReturnsSynthesizedDefault()
    {
        var store = new ProfileStore(this.tempDir);
        File.WriteAllText(store.FilePath, "{ not valid json ...");

        var data = store.Load();
        Assert.That(data.Profiles, Has.Count.EqualTo(1));
        Assert.That(data.Profiles[0].Name, Is.EqualTo("Default"));
    }

    /// <summary>Round-trip preserves every field on the slim profile model.</summary>
    [Test]
    public void SaveThenLoad_PreservesAllFields()
    {
        var store = new ProfileStore(this.tempDir);

        var p = new Profile
        {
            Name = "Python REPL",
            Command = "/usr/bin/python3",
            Args = new[] { "-i" },
            WorkingDirectory = "/srv/py",
        };

        // A profile with all null overrides verifies the null-vs-empty distinction.
        var minimal = new Profile { Name = "Minimal" };

        Assert.That(store.Save(new[] { p, minimal }, defaultProfileId: p.Id), Is.True);

        var data = store.Load();
        Assert.That(data.Profiles, Has.Count.EqualTo(2));
        Assert.That(data.DefaultProfileId, Is.EqualTo(p.Id));

        var round = data.Profiles[0];
        Assert.That(round.Id, Is.EqualTo(p.Id));
        Assert.That(round.Name, Is.EqualTo("Python REPL"));
        Assert.That(round.Command, Is.EqualTo("/usr/bin/python3"));
        Assert.That(round.Args, Is.EquivalentTo(new[] { "-i" }));
        Assert.That(round.WorkingDirectory, Is.EqualTo("/srv/py"));

        var roundMinimal = data.Profiles[1];
        Assert.That(roundMinimal.Command, Is.Null);
        Assert.That(roundMinimal.Args, Is.Null);
        Assert.That(roundMinimal.WorkingDirectory, Is.Null);
    }

    /// <summary>The persisted default id survives a round-trip and is honoured by Load.</summary>
    [Test]
    public void SaveThenLoad_PreservesDefaultProfileId()
    {
        var store = new ProfileStore(this.tempDir);
        var a = new Profile { Name = "A" };
        var b = new Profile { Name = "B" };
        var c = new Profile { Name = "C" };
        Assert.That(store.Save(new[] { a, b, c }, defaultProfileId: b.Id), Is.True);

        var data = store.Load();
        Assert.That(data.DefaultProfileId, Is.EqualTo(b.Id));
        Assert.That(data.DefaultProfile?.Name, Is.EqualTo("B"));
    }

    /// <summary>An unknown default id is reconciled to the first profile in the list.</summary>
    [Test]
    public void Load_UnknownDefaultId_FallsBackToFirstProfile()
    {
        var store = new ProfileStore(this.tempDir);
        var a = new Profile { Name = "A" };
        var b = new Profile { Name = "B" };
        Assert.That(store.Save(new[] { a, b }, defaultProfileId: "not-a-real-id"), Is.True);

        var data = store.Load();
        Assert.That(data.DefaultProfileId, Is.EqualTo(a.Id));
    }

    /// <summary>Save raises <see cref="ProfileStore.ProfilesChanged"/> on success.</summary>
    [Test]
    public void Save_Success_RaisesProfilesChanged()
    {
        var store = new ProfileStore(this.tempDir);
        int count = 0;
        store.ProfilesChanged += () => count++;

        Assert.That(store.Save(new[] { new Profile { Name = "X" } }, null), Is.True);
        Assert.That(count, Is.EqualTo(1));
    }

    /// <summary>A non-writable target directory logs and returns false rather than crashing.</summary>
    [Test]
    public void Save_UnwritableDirectory_DoesNotThrow()
    {
        // Use a path whose *parent* is a regular file — Directory.CreateDirectory
        // on such a path throws IOException, exercising the swallow-and-log branch.
        var regularFile = Path.Combine(this.tempDir, "blocking.txt");
        File.WriteAllText(regularFile, "blocker");
        var bogus = Path.Combine(regularFile, "subdir");
        var store = new ProfileStore(bogus);

        bool result = true;
        Assert.DoesNotThrow(() =>
        {
            result = store.Save(new[] { new Profile { Name = "X" } }, null);
        });
        Assert.That(result, Is.False);
    }

    /// <summary>Empty profile list on disk triggers shell discovery (or synthesized default if discovery is empty).</summary>
    [Test]
    public void Load_EmptyProfileList_SeedsFromDiscovery()
    {
        var store = new ProfileStore(this.tempDir, () => System.Array.Empty<DiscoveredShell>());
        const string Json = """
            { "version": 1, "defaultProfileId": null, "profiles": [] }
            """;
        File.WriteAllText(store.FilePath, Json);

        var data = store.Load();
        Assert.That(data.Profiles, Has.Count.EqualTo(1));
        Assert.That(data.Profiles[0].Name, Is.EqualTo("Default"));
    }

    /// <summary>An empty list on disk is healed by discovery results when available.</summary>
    [Test]
    public void Load_EmptyProfileList_HealedByDiscovery()
    {
        var discovered = new List<DiscoveredShell>
        {
            new("Bash", "/bin/bash", System.Array.Empty<string>(), null),
        };
        var store = new ProfileStore(this.tempDir, () => discovered);
        const string Json = """
            { "version": 1, "defaultProfileId": null, "profiles": [] }
            """;
        File.WriteAllText(store.FilePath, Json);

        var data = store.Load();
        Assert.That(data.Profiles.Select(p => p.Command), Is.EquivalentTo(new[] { "/bin/bash" }));
    }
}
