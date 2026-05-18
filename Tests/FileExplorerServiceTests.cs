// <copyright file="FileExplorerServiceTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.IO;
using System.Linq;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests for the Workbench file explorer service.
/// </summary>
[TestFixture]
public sealed class FileExplorerServiceTests
{
    private string tempDir = string.Empty;

    /// <summary>
    /// Creates a temporary directory for each test.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "explorer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Directory listings are sorted for explorer display and hide dot-files by default.
    /// </summary>
    [Test]
    public void EnumerateDirectory_SortsDirectoriesBeforeFilesAndHidesDotFilesByDefault()
    {
        Directory.CreateDirectory(Path.Combine(this.tempDir, "z-directory"));
        File.WriteAllText(Path.Combine(this.tempDir, "a-file.txt"), "hello");
        File.WriteAllText(Path.Combine(this.tempDir, ".hidden"), "secret");

        var service = new FileExplorerService();
        var listing = service.EnumerateDirectory(this.tempDir, showHidden: false);

        Assert.That(listing.ErrorMessage, Is.Null);
        Assert.That(listing.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { "z-directory", "a-file.txt" }));
        Assert.That(listing.Entries[0].Kind, Is.EqualTo(FileExplorerEntryKind.Directory));
        Assert.That(listing.Entries[1].Kind, Is.EqualTo(FileExplorerEntryKind.File));
    }

    /// <summary>
    /// Hidden entries can be included on request.
    /// </summary>
    [Test]
    public void EnumerateDirectory_ShowHiddenIncludesDotFiles()
    {
        File.WriteAllText(Path.Combine(this.tempDir, ".hidden"), "secret");

        var service = new FileExplorerService();
        var listing = service.EnumerateDirectory(this.tempDir, showHidden: true);

        Assert.That(listing.ErrorMessage, Is.Null);
        Assert.That(listing.Entries.Select(entry => entry.Name), Contains.Item(".hidden"));
    }

    /// <summary>
    /// Missing roots are reported as user-visible errors.
    /// </summary>
    [Test]
    public void EnumerateDirectory_MissingDirectoryReturnsError()
    {
        var missing = Path.Combine(this.tempDir, "missing");

        var service = new FileExplorerService();
        var listing = service.EnumerateDirectory(missing, showHidden: false);

        Assert.That(listing.Entries, Is.Empty);
        Assert.That(listing.ErrorMessage, Is.Not.Null);
    }
}
