// <copyright file="TabGroupStoreTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.IO;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Unit tests for <see cref="TabGroupStore"/> — persistence round-trip,
/// mutation APIs, auto-cycled colors, and fault tolerance against
/// missing / malformed <c>groups.json</c>.
/// </summary>
[TestFixture]
public class TabGroupStoreTests
{
    private string tempDir = string.Empty;

    /// <summary>Create a per-test temp directory under the test work dir.</summary>
    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "groups-test-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A fresh store on an empty directory reports no groups.</summary>
    [Test]
    public void Groups_MissingFile_EmptyList()
    {
        var store = new TabGroupStore(this.tempDir);
        Assert.That(store.Groups, Is.Empty);
    }

    /// <summary>Malformed JSON does not throw and yields an empty store.</summary>
    [Test]
    public void Load_MalformedJson_EmptyList()
    {
        var store = new TabGroupStore(this.tempDir);
        File.WriteAllText(store.FilePath, "{ not valid json ...");
        Assert.That(store.Groups, Is.Empty);
    }

    /// <summary>CreateGroup assigns a stable id, picks a palette color, and raises the event.</summary>
    [Test]
    public void CreateGroup_AutoColorAndEvent()
    {
        var store = new TabGroupStore(this.tempDir);
        int fired = 0;
        store.GroupsChanged += () => fired++;

        var g1 = store.CreateGroup("Work");
        var g2 = store.CreateGroup("Home");

        Assert.That(g1.Id, Is.Not.Empty);
        Assert.That(g1.Name, Is.EqualTo("Work"));
        Assert.That(g2.Id, Is.Not.EqualTo(g1.Id));
        Assert.That(g1.Color, Is.Not.EqualTo(g2.Color));
        Assert.That(fired, Is.EqualTo(2));
        Assert.That(store.Groups, Has.Count.EqualTo(2));
    }

    /// <summary>Rename preserves id and color (color stability).</summary>
    [Test]
    public void RenameGroup_PreservesIdAndColor()
    {
        var store = new TabGroupStore(this.tempDir);
        var g = store.CreateGroup("Initial");
        var originalColor = g.Color;
        var originalId = g.Id;

        Assert.That(store.RenameGroup(g.Id, "Renamed"), Is.True);

        var same = store.Find(originalId);
        Assert.That(same, Is.Not.Null);
        Assert.That(same!.Name, Is.EqualTo("Renamed"));
        Assert.That(same.Color, Is.EqualTo(originalColor));
    }

    /// <summary>SetGroupColor updates only the color.</summary>
    [Test]
    public void SetGroupColor_UpdatesColorOnly()
    {
        var store = new TabGroupStore(this.tempDir);
        var g = store.CreateGroup("Blue");
        Assert.That(store.SetGroupColor(g.Id, 0x112233), Is.True);

        Assert.That(store.Find(g.Id)!.Color, Is.EqualTo(0x112233));
        Assert.That(store.Find(g.Id)!.Name, Is.EqualTo("Blue"));
    }

    /// <summary>RemoveGroup removes and reports true; unknown id reports false.</summary>
    [Test]
    public void RemoveGroup_KnownAndUnknown()
    {
        var store = new TabGroupStore(this.tempDir);
        var g = store.CreateGroup("Temp");

        Assert.That(store.RemoveGroup(g.Id), Is.True);
        Assert.That(store.Find(g.Id), Is.Null);
        Assert.That(store.RemoveGroup(g.Id), Is.False);
        Assert.That(store.RemoveGroup("unknown-id"), Is.False);
    }

    /// <summary>Find ignores null/empty ids and unknown ids.</summary>
    [Test]
    public void Find_NullAndUnknown_Null()
    {
        var store = new TabGroupStore(this.tempDir);
        store.CreateGroup("X");

        Assert.That(store.Find(null), Is.Null);
        Assert.That(store.Find(string.Empty), Is.Null);
        Assert.That(store.Find("nope"), Is.Null);
    }

    /// <summary>Groups round-trip across store instances sharing the same directory.</summary>
    [Test]
    public void Persistence_RoundTrip()
    {
        var store1 = new TabGroupStore(this.tempDir);
        var a = store1.CreateGroup("Alpha");
        var b = store1.CreateGroup("Beta");
        store1.SetGroupColor(b.Id, 0xDEADBE);

        var store2 = new TabGroupStore(this.tempDir);
        Assert.That(store2.Groups, Has.Count.EqualTo(2));
        Assert.That(store2.Find(a.Id)!.Name, Is.EqualTo("Alpha"));
        Assert.That(store2.Find(b.Id)!.Color, Is.EqualTo(0xDEADBE));
    }

    /// <summary>Reload replaces in-memory state with on-disk state and raises the event.</summary>
    [Test]
    public void Reload_PicksUpExternalChanges()
    {
        var store = new TabGroupStore(this.tempDir);
        store.CreateGroup("Ephemeral");
        Assert.That(store.Groups, Has.Count.EqualTo(1));

        File.WriteAllText(store.FilePath, "{}");

        int fired = 0;
        store.GroupsChanged += () => fired++;
        store.Reload();

        Assert.That(store.Groups, Is.Empty);
        Assert.That(fired, Is.EqualTo(1));
    }
}
