// <copyright file="ProfileStoreTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>Load synthesizes a default profile when profiles.json is absent.</summary>
    [Test]
    public void Load_MissingFile_ReturnsSynthesizedDefault()
    {
        var store = new ProfileStore(this.tempDir);
        var data = store.Load();

        Assert.That(data.Profiles, Has.Count.EqualTo(1));
        Assert.That(data.Profiles[0].Name, Is.EqualTo("Default"));
        Assert.That(data.DefaultProfileId, Is.EqualTo(data.Profiles[0].Id));
        Assert.That(data.DefaultProfile, Is.SameAs(data.Profiles[0]));
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

    /// <summary>Round-trip preserves every field, including env dictionary and null distinction.</summary>
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
            EnvironmentOverrides = new Dictionary<string, string>
            {
                ["PYTHONUNBUFFERED"] = "1",
                ["LANG"] = "en_US.UTF-8",
            },
            ColorSchemeName = "Solarized Dark",
            FontFamilies = new[] { "Fira Code", "Menlo" },
            FontSize = 13.5,
            WindowEffect = "Acrylic",
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
        Assert.That(round.EnvironmentOverrides, Is.Not.Null);
        Assert.That(round.EnvironmentOverrides!["PYTHONUNBUFFERED"], Is.EqualTo("1"));
        Assert.That(round.EnvironmentOverrides["LANG"], Is.EqualTo("en_US.UTF-8"));
        Assert.That(round.ColorSchemeName, Is.EqualTo("Solarized Dark"));
        Assert.That(round.FontFamilies, Is.EquivalentTo(new[] { "Fira Code", "Menlo" }));
        Assert.That(round.FontSize, Is.EqualTo(13.5));
        Assert.That(round.WindowEffect, Is.EqualTo("Acrylic"));

        var roundMinimal = data.Profiles[1];
        Assert.That(roundMinimal.Command, Is.Null);
        Assert.That(roundMinimal.Args, Is.Null);
        Assert.That(roundMinimal.WorkingDirectory, Is.Null);
        Assert.That(roundMinimal.EnvironmentOverrides, Is.Null);
        Assert.That(roundMinimal.ColorSchemeName, Is.Null);
        Assert.That(roundMinimal.FontFamilies, Is.Null);
        Assert.That(roundMinimal.FontSize, Is.Null);
        Assert.That(roundMinimal.WindowEffect, Is.Null);
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

    /// <summary>Empty profile list on disk still yields a synthesized default.</summary>
    [Test]
    public void Load_EmptyProfileList_SynthesizesDefault()
    {
        var store = new ProfileStore(this.tempDir);
        const string Json = """
            { "version": 1, "defaultProfileId": null, "profiles": [] }
            """;
        File.WriteAllText(store.FilePath, Json);

        var data = store.Load();
        Assert.That(data.Profiles, Has.Count.EqualTo(1));
        Assert.That(data.Profiles[0].Name, Is.EqualTo("Default"));
    }
}
