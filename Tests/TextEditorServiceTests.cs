// <copyright file="TextEditorServiceTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.IO;
using System.Text;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests for the Workbench lightweight text editor service.
/// </summary>
[TestFixture]
public sealed class TextEditorServiceTests
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
            "editor-test-" + Guid.NewGuid().ToString("N"));
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
    /// UTF-8 files can be opened and saved while preserving document metadata.
    /// </summary>
    [Test]
    public void OpenAndSaveUtf8File_UpdatesDocumentMetadata()
    {
        var path = Path.Combine(this.tempDir, "notes.txt");
        File.WriteAllText(path, "hello", new UTF8Encoding(false));

        var service = new TextEditorService();
        var opened = service.Open(path);
        Assert.That(opened.Document, Is.Not.Null);
        Assert.That(opened.Document!.Text, Is.EqualTo("hello"));

        var saved = service.Save(opened.Document, "hello world");

        Assert.That(saved.Document, Is.Not.Null);
        Assert.That(saved.Document!.Text, Is.EqualTo("hello world"));
        Assert.That(File.ReadAllText(path), Is.EqualTo("hello world"));
    }

    /// <summary>
    /// Files modified after opening are not overwritten silently.
    /// </summary>
    [Test]
    public void Save_WhenFileChangedExternallyReturnsError()
    {
        var path = Path.Combine(this.tempDir, "notes.txt");
        File.WriteAllText(path, "hello", new UTF8Encoding(false));

        var service = new TextEditorService();
        var opened = service.Open(path);
        Assert.That(opened.Document, Is.Not.Null);

        File.WriteAllText(path, "external", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, opened.Document!.LastWriteTimeUtc.AddSeconds(2));

        var saved = service.Save(opened.Document, "mine");

        Assert.That(saved.Document, Is.Null);
        Assert.That(saved.ErrorMessage, Does.Contain("changed on disk"));
    }

    /// <summary>
    /// Binary files are rejected before the editor attempts to decode them as text.
    /// </summary>
    [Test]
    public void Open_BinaryFileReturnsError()
    {
        var path = Path.Combine(this.tempDir, "data.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 0, 3 });

        var service = new TextEditorService();
        var opened = service.Open(path);

        Assert.That(opened.Document, Is.Null);
        Assert.That(opened.ErrorMessage, Does.Contain("Binary"));
    }

    /// <summary>
    /// Large files are rejected by the lightweight editor guard.
    /// </summary>
    [Test]
    public void Open_LargeFileReturnsError()
    {
        var path = Path.Combine(this.tempDir, "large.txt");
        File.WriteAllBytes(path, new byte[TextEditorService.MaxEditableBytes + 1]);

        var service = new TextEditorService();
        var opened = service.Open(path);

        Assert.That(opened.Document, Is.Null);
        Assert.That(opened.ErrorMessage, Does.Contain("larger"));
    }
}
