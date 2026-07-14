// <copyright file="GitDiffPaneTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AeroTerm.Controls;
using AeroTerm.Services;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using NUnit.Framework;

/// <summary>
/// Headless tests for the side-by-side Git diff pane.
/// </summary>
[TestFixture]
public sealed class GitDiffPaneTests
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
            "git-pane-test-" + Guid.NewGuid().ToString("N"));
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
            ClearReadOnlyAttributes(this.tempDir);
            Directory.Delete(this.tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Constructing the pane and refreshing outside a repository should not throw.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [AvaloniaTest]
    public async Task RefreshAsync_OutsideRepository_DoesNotThrow()
    {
        var pane = new GitDiffPane(() => Path.GetTempPath());

        await pane.RefreshAsync();

        Assert.That(pane.Content, Is.Not.Null);
    }

    /// <summary>
    /// Refreshing with a null working directory should not throw.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [AvaloniaTest]
    public async Task RefreshAsync_NullWorkingDirectory_DoesNotThrow()
    {
        var pane = new GitDiffPane(() => null);

        Assert.DoesNotThrowAsync(async () => await pane.RefreshAsync());
    }

    /// <summary>
    /// Refreshing a repository with changes selects the first change and renders aligned content and gutter editors.
    /// </summary>
    /// <returns>A task that completes when the pane updates.</returns>
    [AvaloniaTest]
    public async Task RefreshAsync_WithChangedFile_ShowsAvaloniaEditEditors()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "changed", new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));

        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
        await Task.Delay(100).ConfigureAwait(true);

        var editors = await Dispatcher.UIThread.InvokeAsync(() => pane.GetLogicalDescendants()
            .OfType<Control>()
            .Where(control => control.GetType().FullName == "AvaloniaEdit.TextEditor")
            .ToArray());
        var editorTexts = await Dispatcher.UIThread.InvokeAsync(() => editors.Select(GetEditorText).ToArray());
        var editorsVisible = await Dispatcher.UIThread.InvokeAsync(() => editors.All(editor => editor.IsVisible));
        var buttons = await Dispatcher.UIThread.InvokeAsync(() => pane.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => button.Content is string)
            .ToDictionary(button => (string)button.Content!, button => button.IsEnabled));
        var splitterCount = await Dispatcher.UIThread.InvokeAsync(() => pane.GetLogicalDescendants()
            .OfType<GridSplitter>()
            .Count());
        Assert.That(editors, Has.Length.EqualTo(4));
        Assert.That(editorsVisible, Is.True);
        Assert.That(editorTexts, Does.Contain("initial"));
        Assert.That(editorTexts, Does.Contain("changed"));
        Assert.That(buttons["Stage"], Is.True);
        Assert.That(buttons["Unstage"], Is.False);
        Assert.That(buttons["Fetch"], Is.True);
        Assert.That(buttons["Pull"], Is.False);
        Assert.That(buttons["Previous"], Is.True);
        Assert.That(buttons["Next"], Is.True);
        Assert.That(splitterCount, Is.EqualTo(2));
    }

    /// <summary>
    /// The compact changes tree shows filenames while retaining the full path in its item model.
    /// </summary>
    /// <returns>A task that completes when the pane updates.</returns>
    [AvaloniaTest]
    public async Task RefreshAsync_WithNestedChange_ShowsCompactFilename()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(this.tempDir, "folder"));
        File.WriteAllText(Path.Combine(this.tempDir, "folder", "nested.txt"), "new", new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));

        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
        var status = await service.GetStatusAsync(this.tempDir).ConfigureAwait(false);
        var items = GitDiffPane.BuildChangeItems(status);

        Assert.That(items.Select(item => item.ToString()), Does.Contain("nested.txt"));
        Assert.That(items.Single(item => item.FileName == "nested.txt").Status.Path, Is.EqualTo("folder/nested.txt"));
    }

    /// <summary>
    /// Inserted lines produce a blank old-side placeholder so both content documents have equal row counts.
    /// </summary>
    /// <returns>A task that completes when the pane updates.</returns>
    [AvaloniaTest]
    public async Task RefreshAsync_WithInsertedLine_RendersAlignedPlaceholder()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(
            Path.Combine(this.tempDir, "tracked.txt"),
            "initial\ninserted",
            new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));

        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));

        var contentTexts = await Dispatcher.UIThread.InvokeAsync(() => pane.GetLogicalDescendants()
            .OfType<Control>()
            .Where(control => control.GetType().FullName == "AvaloniaEdit.TextEditor" && control.Width != 52)
            .Select(GetEditorText)
            .ToArray());

        Assert.That(contentTexts, Is.EquivalentTo(new[] { "initial\n", "initial\ninserted" }));
    }

    private static string? GetEditorText(Control editor)
    {
        return editor.GetType().GetProperty("Text")?.GetValue(editor) as string;
    }

    private static void ClearReadOnlyAttributes(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }

        var directoryAttributes = File.GetAttributes(directory);
        if ((directoryAttributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(directory, directoryAttributes & ~FileAttributes.ReadOnly);
        }
    }

    private async Task InitializeRepositoryAsync(GitService service)
    {
        var version = await service.RunGitAsync(this.tempDir, "--version").ConfigureAwait(false);
        if (!version.Succeeded)
        {
            Assert.Ignore("git is not available on PATH.");
        }

        var initResult = await service.RunGitAsync(this.tempDir, "init").ConfigureAwait(false);
        Assert.That(initResult.Succeeded, Is.True, initResult.ErrorMessage);
        var nameResult = await service.RunGitAsync(this.tempDir, "config", "user.name", "AeroTerm Tests").ConfigureAwait(false);
        Assert.That(nameResult.Succeeded, Is.True, nameResult.ErrorMessage);
        var emailResult = await service.RunGitAsync(this.tempDir, "config", "user.email", "tests@example.invalid").ConfigureAwait(false);
        Assert.That(emailResult.Succeeded, Is.True, emailResult.ErrorMessage);
        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "initial", new UTF8Encoding(false));
        var addResult = await service.RunGitAsync(this.tempDir, "add", "tracked.txt").ConfigureAwait(false);
        Assert.That(addResult.Succeeded, Is.True, addResult.ErrorMessage);
        var commitResult = await service.RunGitAsync(this.tempDir, "commit", "-m", "initial").ConfigureAwait(false);
        Assert.That(commitResult.Succeeded, Is.True, commitResult.ErrorMessage);
    }
}
