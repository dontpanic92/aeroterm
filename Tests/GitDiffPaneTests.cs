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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
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
    /// Refreshing a repository with changes selects the first change and renders aligned read-only editors.
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
            .OfType<TextEditor>()
            .ToArray());
        var editorTexts = await Dispatcher.UIThread.InvokeAsync(() => editors.Select(editor => editor.Text).ToArray());
        var editorsVisible = await Dispatcher.UIThread.InvokeAsync(() => editors.All(editor => editor.IsVisible));
        var editorsReadOnly = await Dispatcher.UIThread.InvokeAsync(() => editors.All(editor => editor.IsReadOnly));
        var lineNumberMarginCount = await Dispatcher.UIThread.InvokeAsync(() => editors
            .SelectMany(editor => editor.TextArea.LeftMargins)
            .OfType<GitDiffLineNumberMargin>()
            .Count());
        var buttons = await Dispatcher.UIThread.InvokeAsync(() => pane.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => button.Content is string)
            .ToDictionary(button => (string)button.Content!, button => button.IsEnabled));
        var splitterCount = await Dispatcher.UIThread.InvokeAsync(() => pane.GetLogicalDescendants()
            .OfType<GridSplitter>()
            .Count());
        Assert.That(editors, Has.Length.EqualTo(2));
        Assert.That(editorsVisible, Is.True);
        Assert.That(editorTexts, Does.Contain("initial"));
        Assert.That(editorTexts, Does.Contain("changed"));
        Assert.That(editorsReadOnly, Is.True);
        Assert.That(lineNumberMarginCount, Is.EqualTo(2));
        Assert.That(buttons["Stage"], Is.True);
        Assert.That(buttons["Unstage"], Is.False);
        Assert.That(buttons["Fetch"], Is.True);
        Assert.That(buttons["Pull"], Is.False);
        Assert.That(buttons, Does.Not.ContainKey("Previous"));
        Assert.That(buttons, Does.Not.ContainKey("Next"));
        Assert.That(splitterCount, Is.EqualTo(2));
    }

    /// <summary>
    /// The changes tree renders data-backed file leaves instead of using control containers as data.
    /// </summary>
    /// <returns>A task that completes when the tree is rendered.</returns>
    [AvaloniaTest]
    public async Task RefreshAsync_WithChangedFile_RendersFileLeaf()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "changed", new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));
        var window = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var result = new Window
            {
                Width = 900,
                Height = 600,
                Content = pane,
            };
            result.Show();
            return result;
        });

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
            await Task.Delay(100).ConfigureAwait(true);

            var result = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var tree = pane.GetLogicalDescendants().OfType<TreeView>().Single();
                var roots = tree.ItemsSource!.Cast<GitChangeTreeNode>().ToArray();
                var renderedFileNames = pane.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(textBlock => textBlock.Text)
                    .ToArray();
                var rootContainer = tree.ContainerFromIndex(0) as TreeViewItem;
                var hasTreeItemTemplate = rootContainer?.GetVisualDescendants()
                    .OfType<Border>()
                    .Any(border => border.Name == "PART_LayoutRoot") == true;
                var hasTrackedFile = roots
                    .SelectMany(root => root.Children)
                    .Any(node => node.Item?.Status.Path == "tracked.txt");
                return (
                    HasTrackedFile: hasTrackedFile,
                    HasTreeItemTemplate: hasTreeItemTemplate,
                    HorizontalScrollBarVisibility: tree.GetValue(
                        ScrollViewer.HorizontalScrollBarVisibilityProperty),
                    RenderedFileNames: renderedFileNames,
                    SelectedItem: tree.SelectedItem as GitChangeTreeNode);
            });

            Assert.That(result.HasTrackedFile, Is.True);
            Assert.That(result.HasTreeItemTemplate, Is.True);
            Assert.That(
                result.HorizontalScrollBarVisibility,
                Is.EqualTo(ScrollBarVisibility.Disabled));
            Assert.That(result.RenderedFileNames, Does.Contain("tracked.txt"));
            Assert.That(result.SelectedItem, Is.Not.Null);
            Assert.That(result.SelectedItem!.Item, Is.Not.Null);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(window.Close);
        }
    }

    /// <summary>
    /// Long filenames are constrained to the change list width and use ellipsis trimming.
    /// </summary>
    /// <returns>A task that completes when the long filename is rendered.</returns>
    [AvaloniaTest]
    public async Task RefreshAsync_WithLongFilename_TrimsWithoutHorizontalScroll()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        var fileName = new string('a', 120) + ".txt";
        File.WriteAllText(Path.Combine(this.tempDir, fileName), "new", new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));
        var window = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var result = new Window
            {
                Width = 700,
                Height = 500,
                Content = pane,
            };
            result.Show();
            return result;
        });

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
            await Task.Delay(100).ConfigureAwait(true);
            var layout = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var tree = pane.GetLogicalDescendants().OfType<TreeView>().Single();
                var fileNameBlock = tree.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(textBlock => textBlock.Text == fileName);
                return (
                    FileNameWidth: fileNameBlock.Bounds.Width,
                    TreeWidth: tree.Bounds.Width,
                    fileNameBlock.TextTrimming,
                    HorizontalScrollBarVisibility: tree.GetValue(
                        ScrollViewer.HorizontalScrollBarVisibilityProperty));
            });

            Assert.That(layout.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
            Assert.That(layout.FileNameWidth, Is.LessThan(layout.TreeWidth));
            Assert.That(
                layout.HorizontalScrollBarVisibility,
                Is.EqualTo(ScrollBarVisibility.Disabled));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(window.Close);
        }
    }

    /// <summary>
    /// Background filesystem churn that does not change Git status leaves the tree and diff intact.
    /// </summary>
    /// <returns>A task that completes after the background refresh.</returns>
    [AvaloniaTest]
    public async Task BackgroundRefresh_UnchangedStatus_DoesNotRebuildTree()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "changed", new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));

        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
        var before = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var tree = pane.GetLogicalDescendants().OfType<TreeView>().Single();
            return (
                Tree: tree,
                ItemsSource: tree.ItemsSource,
                Selection: tree.SelectedItem,
                Texts: pane.GetLogicalDescendants()
                    .OfType<TextEditor>()
                    .Select(editor => editor.Text)
                    .ToArray());
        });

        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshForFileSystemChangesAsync(
            new[] { Path.Combine(this.tempDir, "unrelated-event.tmp") }).ConfigureAwait(true));
        var after = await Dispatcher.UIThread.InvokeAsync(() => (
            ItemsSource: before.Tree.ItemsSource,
            Selection: before.Tree.SelectedItem,
            Texts: pane.GetLogicalDescendants()
                .OfType<TextEditor>()
                .Select(editor => editor.Text)
                .ToArray()));

        Assert.That(after.ItemsSource, Is.SameAs(before.ItemsSource));
        Assert.That(after.Selection, Is.SameAs(before.Selection));
        Assert.That(after.Texts, Is.EqualTo(before.Texts));
    }

    /// <summary>
    /// Repository summary changes update status without rebuilding an unchanged file tree.
    /// </summary>
    /// <returns>A task that completes after the background refresh.</returns>
    [AvaloniaTest]
    public async Task BackgroundRefresh_BranchChange_DoesNotRebuildTree()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "changed", new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));

        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
        var tree = await Dispatcher.UIThread.InvokeAsync(() =>
            pane.GetLogicalDescendants().OfType<TreeView>().Single());
        var originalItemsSource = await Dispatcher.UIThread.InvokeAsync(() => tree.ItemsSource);
        var renameResult = await service.RunGitAsync(
            this.tempDir,
            "branch",
            "-m",
            "renamed").ConfigureAwait(false);
        Assert.That(renameResult.Succeeded, Is.True, renameResult.ErrorMessage);

        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshForFileSystemChangesAsync(
            new[] { Path.Combine(this.tempDir, ".git", "HEAD") }).ConfigureAwait(true));
        var currentItemsSource = await Dispatcher.UIThread.InvokeAsync(() => tree.ItemsSource);

        Assert.That(currentItemsSource, Is.SameAs(originalItemsSource));
    }

    /// <summary>
    /// A selected file is reloaded when its content changes without changing its Git status code.
    /// </summary>
    /// <returns>A task that completes after the selected comparison reloads.</returns>
    [AvaloniaTest]
    public async Task BackgroundRefresh_SelectedModifiedFile_ReloadsDiff()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        var path = Path.Combine(this.tempDir, "tracked.txt");
        File.WriteAllText(path, "changed once", new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));

        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
        File.WriteAllText(path, "changed twice", new UTF8Encoding(false));
        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshForFileSystemChangesAsync(
            new[] { path }).ConfigureAwait(true));
        var editorTexts = await Dispatcher.UIThread.InvokeAsync(() => pane.GetLogicalDescendants()
            .OfType<TextEditor>()
            .Select(editor => editor.Text)
            .ToArray());

        Assert.That(editorTexts, Does.Contain("changed twice"));
    }

    /// <summary>
    /// Refreshing the same comparison preserves its caret and selection.
    /// </summary>
    /// <returns>A task that completes after the selected comparison reloads.</returns>
    [AvaloniaTest]
    public async Task BackgroundRefresh_SelectedFile_PreservesCaretAndSelection()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        var path = Path.Combine(this.tempDir, "tracked.txt");
        File.WriteAllText(path, "before refresh content", new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));

        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var editor = pane.GetLogicalDescendants()
                .OfType<TextEditor>()
                .Single(candidate => candidate.Text == "before refresh content");
            editor.Select(3, 4);
            editor.CaretOffset = 6;
        });

        File.WriteAllText(path, "after refresh content remains long", new UTF8Encoding(false));
        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshForFileSystemChangesAsync(
            new[] { path }).ConfigureAwait(true));
        var state = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var editor = pane.GetLogicalDescendants()
                .OfType<TextEditor>()
                .Single(candidate => candidate.Text == "after refresh content remains long");
            return (
                editor.CaretOffset,
                editor.SelectionStart,
                editor.SelectionLength);
        });

        Assert.That(state.CaretOffset, Is.EqualTo(6));
        Assert.That(state.SelectionStart, Is.EqualTo(3));
        Assert.That(state.SelectionLength, Is.EqualTo(4));
    }

    /// <summary>
    /// Markdown files are displayed as plain source text without rich heading styling.
    /// </summary>
    /// <returns>A task that completes after the Markdown comparison renders.</returns>
    [AvaloniaTest]
    public async Task RefreshAsync_WithMarkdownFile_DoesNotApplyMarkdownHighlighting()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(
            Path.Combine(this.tempDir, "README.md"),
            "# Heading\n\nBody",
            new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));

        await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
        var highlighting = await Dispatcher.UIThread.InvokeAsync(() => pane.GetLogicalDescendants()
            .OfType<TextEditor>()
            .Select(editor => editor.SyntaxHighlighting)
            .ToArray());

        Assert.That(highlighting, Is.All.Null);
    }

    /// <summary>
    /// Read-only editors retain visible selection and selectable text behavior.
    /// </summary>
    /// <returns>A task that completes after the editor is rendered.</returns>
    [AvaloniaTest]
    public async Task Editors_AreReadOnlySelectableAndThemeSelection()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        File.WriteAllText(Path.Combine(this.tempDir, "tracked.txt"), "changed", new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));
        var window = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var result = new Window { Content = pane };
            result.Show();
            return result;
        });

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
            var result = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var editor = pane.GetLogicalDescendants()
                    .OfType<TextEditor>()
                    .Single(candidate => candidate.Text == "changed");
                editor.Select(0, 3);
                return (
                    EditorReadOnly: editor.IsReadOnly,
                    TextAreaReadOnly: editor.TextArea.IsReadOnly,
                    SelectionBrush: editor.TextArea.SelectionBrush,
                    editor.SelectedText);
            });

            Assert.That(result.EditorReadOnly, Is.True);
            Assert.That(result.TextAreaReadOnly, Is.True);
            Assert.That(result.SelectionBrush, Is.Not.Null);
            Assert.That(result.SelectedText, Is.EqualTo("cha"));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(window.Close);
        }
    }

    /// <summary>
    /// Side-by-side editors synchronize vertical scrolling while retaining independent horizontal offsets.
    /// </summary>
    /// <returns>A task that completes after scroll offsets settle.</returns>
    [AvaloniaTest]
    public async Task Editors_SynchronizeVerticalScrollOnly()
    {
        var service = new GitService();
        await this.InitializeRepositoryAsync(service).ConfigureAwait(false);
        var lines = Enumerable.Range(1, 200)
            .Select(index => $"{index:D3} {new string('x', 180)}");
        File.WriteAllText(
            Path.Combine(this.tempDir, "tracked.txt"),
            string.Join('\n', lines),
            new UTF8Encoding(false));
        var pane = await Dispatcher.UIThread.InvokeAsync(() => new GitDiffPane(() => this.tempDir));
        var window = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var result = new Window
            {
                Width = 900,
                Height = 500,
                Content = pane,
            };
            result.Show();
            return result;
        });

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () => await pane.RefreshAsync().ConfigureAwait(true));
            await Task.Delay(100).ConfigureAwait(true);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var editors = pane.GetLogicalDescendants().OfType<TextEditor>().ToArray();
                var sourceScroll = (IScrollable)editors[0].TextArea.TextView;
                var targetScroll = (IScrollable)editors[1].TextArea.TextView;
                targetScroll.Offset = new Vector(40, targetScroll.Offset.Y);
                sourceScroll.Offset = new Vector(sourceScroll.Offset.X, 160);
            });
            await Task.Delay(50).ConfigureAwait(true);
            var offsets = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var editors = pane.GetLogicalDescendants().OfType<TextEditor>().ToArray();
                var sourceScroll = (IScrollable)editors[0].TextArea.TextView;
                var targetScroll = (IScrollable)editors[1].TextArea.TextView;
                return (
                    SourceVertical: sourceScroll.Offset.Y,
                    TargetVertical: targetScroll.Offset.Y,
                    TargetHorizontal: targetScroll.Offset.X);
            });

            Assert.That(offsets.TargetVertical, Is.EqualTo(offsets.SourceVertical).Within(0.5));
            Assert.That(offsets.TargetHorizontal, Is.EqualTo(40).Within(0.5));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(window.Close);
        }
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
            .OfType<TextEditor>()
            .Select(editor => editor.Text)
            .ToArray());

        Assert.That(contentTexts, Is.EquivalentTo(new[] { "initial\n", "initial\ninserted" }));
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
