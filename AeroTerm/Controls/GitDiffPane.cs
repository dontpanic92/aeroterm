// <copyright file="GitDiffPane.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AeroTerm.Services;
using AeroTerm.Theme.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;

/// <summary>
/// A view-only Git pane that lists repository changes and renders the selected
/// change as full-file old/new editors with highlighted changed lines.
/// </summary>
internal sealed class GitDiffPane : UserControl
{
    private static readonly IBrush RemovedBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xE5, 0x39, 0x35));
    private static readonly IBrush AddedBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x43, 0xA0, 0x47));
    private static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xB3, 0x00));
    private static readonly FontFamily MonoFont = FontFamily.Parse("monospace");

    private readonly GitService gitService = new();
    private readonly Func<string?> workingDirectoryProvider;
    private readonly TextBlock cwdText;
    private readonly TextBlock statusText;
    private readonly TreeView changesList;
    private readonly Button stageButton;
    private readonly Button unstageButton;
    private readonly Button discardButton;
    private readonly Button stageAllButton;
    private readonly Button unstageAllButton;
    private readonly Button fetchButton;
    private readonly Button pullButton;
    private readonly Button pushButton;
    private readonly Button syncButton;
    private readonly TextBox commitMessageText;
    private readonly Button commitButton;
    private readonly TextBlock diffHeader;
    private readonly Button previousChangeButton;
    private readonly Button nextChangeButton;
    private readonly TextBlock oldHeader;
    private readonly TextBlock newHeader;
    private readonly TextEditor oldLineNumberEditor;
    private readonly TextEditor newLineNumberEditor;
    private readonly TextEditor oldEditor;
    private readonly TextEditor newEditor;
    private readonly FullLineHighlightRenderer oldHighlightRenderer;
    private readonly FullLineHighlightRenderer newHighlightRenderer;
    private readonly FullLineHighlightRenderer oldLineNumberHighlightRenderer;
    private readonly FullLineHighlightRenderer newLineNumberHighlightRenderer;
    private readonly Panel diffPlaceholder;
    private readonly TextBlock diffPlaceholderText;
    private readonly Grid comparisonGrid;
    private readonly DispatcherTimer watcherRefreshTimer;

    private GitRepositoryStatus? currentStatus;
    private FileSystemWatcher? repositoryWatcher;
    private IReadOnlyList<int> currentChangeLines = Array.Empty<int>();
    private int refreshToken;
    private int currentChangeIndex = -1;
    private bool suppressEditorScrollSync;
    private bool suppressChangeSelection;
    private bool gitActionRunning;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitDiffPane"/> class.
    /// </summary>
    /// <param name="workingDirectoryProvider">Provides the current terminal working directory.</param>
    public GitDiffPane(Func<string?> workingDirectoryProvider)
    {
        this.workingDirectoryProvider = workingDirectoryProvider
            ?? throw new ArgumentNullException(nameof(workingDirectoryProvider));

        this.cwdText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            FontFamily = MonoFont,
        };

        this.statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
        };

        var refreshButton = new Button { Content = "Refresh" };
        refreshButton.Click += async (_, _) => await this.RefreshAsync().ConfigureAwait(true);

        this.changesList = new TreeView();
        this.changesList.SelectionChanged += async (_, _) =>
        {
            if (!this.suppressChangeSelection)
            {
                await this.UpdateDiffAsync().ConfigureAwait(true);
            }

            this.UpdateActionStates();
        };

        this.stageButton = new Button { Content = "Stage", IsEnabled = false };
        this.stageButton.Click += async (_, _) => await this.StageSelectedAsync().ConfigureAwait(true);
        this.unstageButton = new Button { Content = "Unstage", IsEnabled = false };
        this.unstageButton.Click += async (_, _) => await this.UnstageSelectedAsync().ConfigureAwait(true);
        this.discardButton = new Button { Content = "Discard", IsEnabled = false };
        this.discardButton.Click += async (_, _) => await this.DiscardSelectedAsync().ConfigureAwait(true);
        this.stageAllButton = new Button { Content = "Stage All", IsEnabled = false };
        this.stageAllButton.Click += async (_, _) => await this.StageAllAsync().ConfigureAwait(true);
        this.unstageAllButton = new Button { Content = "Unstage All", IsEnabled = false };
        this.unstageAllButton.Click += async (_, _) => await this.UnstageAllAsync().ConfigureAwait(true);
        this.fetchButton = new Button { Content = "Fetch", IsEnabled = false };
        this.fetchButton.Click += async (_, _) => await this.FetchAsync().ConfigureAwait(true);
        this.pullButton = new Button { Content = "Pull", IsEnabled = false };
        this.pullButton.Click += async (_, _) => await this.PullAsync().ConfigureAwait(true);
        this.pushButton = new Button { Content = "Push", IsEnabled = false };
        this.pushButton.Click += async (_, _) => await this.PushAsync().ConfigureAwait(true);
        this.syncButton = new Button { Content = "Sync", IsEnabled = false };
        this.syncButton.Click += async (_, _) => await this.SyncAsync().ConfigureAwait(true);
        this.commitMessageText = new TextBox
        {
            PlaceholderText = "Commit message",
            AcceptsReturn = true,
            MinHeight = 54,
        };
        this.commitMessageText.TextChanged += (_, _) => this.UpdateActionStates();
        this.commitButton = new Button
        {
            Content = "Commit",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = false,
        };
        this.commitButton.Click += async (_, _) => await this.CommitAsync().ConfigureAwait(true);

        var leftPanel = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };
        var leftHeader = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Changes", FontWeight = FontWeight.Bold },
                new TextBlock { Text = "Working directory", FontWeight = FontWeight.Bold, FontSize = 11 },
                this.cwdText,
                this.statusText,
                refreshButton,
            },
        };
        var fileActions = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 8),
            Spacing = 4,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { this.stageButton, this.unstageButton, this.discardButton },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { this.stageAllButton, this.unstageAllButton },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { this.fetchButton, this.pullButton, this.pushButton, this.syncButton },
                },
            },
        };
        var commitPanel = new StackPanel
        {
            Spacing = 4,
            Children = { this.commitMessageText, this.commitButton },
        };
        Grid.SetRow(leftHeader, 0);
        Grid.SetRow(this.changesList, 1);
        Grid.SetRow(fileActions, 2);
        Grid.SetRow(commitPanel, 3);
        leftPanel.Children.Add(leftHeader);
        leftPanel.Children.Add(this.changesList);
        leftPanel.Children.Add(fileActions);
        leftPanel.Children.Add(commitPanel);

        this.diffHeader = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(10, 10, 10, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        this.previousChangeButton = new Button
        {
            Content = "Previous",
            IsEnabled = false,
        };
        this.previousChangeButton.Click += (_, _) => this.NavigateChange(-1);
        this.nextChangeButton = new Button
        {
            Content = "Next",
            IsEnabled = false,
        };
        this.nextChangeButton.Click += (_, _) => this.NavigateChange(1);
        this.oldHeader = this.BuildSideHeader();
        this.newHeader = this.BuildSideHeader();
        this.oldHighlightRenderer = new FullLineHighlightRenderer(this.oldHeader);
        this.newHighlightRenderer = new FullLineHighlightRenderer(this.newHeader);
        this.oldLineNumberHighlightRenderer = new FullLineHighlightRenderer(this.oldHeader);
        this.newLineNumberHighlightRenderer = new FullLineHighlightRenderer(this.newHeader);
        this.oldLineNumberEditor = this.BuildEditor(this.oldLineNumberHighlightRenderer, isLineNumberGutter: true);
        this.newLineNumberEditor = this.BuildEditor(this.newLineNumberHighlightRenderer, isLineNumberGutter: true);
        this.oldEditor = this.BuildEditor(this.oldHighlightRenderer, isLineNumberGutter: false);
        this.newEditor = this.BuildEditor(this.newHighlightRenderer, isLineNumberGutter: false);
        this.oldEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => this.SyncScrollOffset(
            this.oldEditor,
            this.newEditor,
            this.oldLineNumberEditor,
            this.newLineNumberEditor);
        this.newEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => this.SyncScrollOffset(
            this.newEditor,
            this.oldEditor,
            this.newLineNumberEditor,
            this.oldLineNumberEditor);
        this.comparisonGrid = this.BuildComparisonGrid();
        this.diffPlaceholderText = new TextBlock
        {
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20),
            Text = "Select a change to view its diff.",
        };
        this.diffPlaceholder = new Panel { Children = { this.diffPlaceholderText } };

        var diffBody = new Panel
        {
            Children = { this.comparisonGrid, this.diffPlaceholder },
        };

        var rightPanel = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star),
            },
        };
        var diffToolbar = new Grid
        {
            Margin = new Thickness(0, 0, 8, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        var navigationButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { this.previousChangeButton, this.nextChangeButton },
        };
        Grid.SetColumn(this.diffHeader, 0);
        Grid.SetColumn(navigationButtons, 1);
        diffToolbar.Children.Add(this.diffHeader);
        diffToolbar.Children.Add(navigationButtons);
        Grid.SetRow(diffToolbar, 0);
        Grid.SetRow(diffBody, 1);
        rightPanel.Children.Add(diffToolbar);
        rightPanel.Children.Add(diffBody);

        var splitter = new GridSplitter { Width = 4, ResizeDirection = GridResizeDirection.Columns };

        var root = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(280, GridUnitType.Pixel) { MinWidth = 160 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(1, GridUnitType.Star),
            },
        };
        Grid.SetColumn(leftPanel, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(rightPanel, 2);
        root.Children.Add(leftPanel);
        root.Children.Add(splitter);
        root.Children.Add(rightPanel);

        this.Bind(BackgroundProperty, this.GetResourceObservable("SurfaceBackgroundBrush"));
        this.Content = root;
        this.watcherRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        this.watcherRefreshTimer.Tick += async (_, _) =>
        {
            this.watcherRefreshTimer.Stop();
            if (this.IsVisible)
            {
                await this.RefreshAsync().ConfigureAwait(true);
            }
        };
        this.KeyDown += this.OnKeyDown;
        this.ShowDiffPlaceholder("Select a change to view its diff.");
    }

    /// <summary>
    /// Reloads the repository status and refreshes the changes list.
    /// </summary>
    /// <returns>A task that completes when the refresh finishes.</returns>
    public async Task RefreshAsync()
    {
        var token = ++this.refreshToken;
        var root = this.workingDirectoryProvider();
        this.cwdText.Text = string.IsNullOrEmpty(root) ? "(no working directory reported yet)" : root;
        this.statusText.Text = "Loading Git status...";

        var status = await this.gitService.GetStatusAsync(root).ConfigureAwait(true);
        if (token != this.refreshToken)
        {
            return;
        }

        this.currentStatus = status;
        this.ConfigureRepositoryWatcher(status.RepositoryRoot);
        var items = BuildChangeItems(status);
        var selectedItem = this.GetSelectedChangeItem();
        (GitStatusBucket Bucket, string Path)? selectedKey = selectedItem is null
            ? null
            : (selectedItem.Status.Bucket, selectedItem.Status.Path);
        var tree = this.BuildChangeTree(items);
        this.changesList.ItemsSource = tree.Roots;
        var itemToSelect = selectedKey is { } key
            ? tree.Items.FirstOrDefault(entry =>
                entry.Item.Status.Bucket == key.Bucket &&
                string.Equals(entry.Item.Status.Path, key.Path, StringComparison.Ordinal)).Container
            : null;
        itemToSelect ??= tree.Items.FirstOrDefault().Container;
        if (itemToSelect is not null)
        {
            this.suppressChangeSelection = true;
            this.changesList.SelectedItem = itemToSelect;
            this.suppressChangeSelection = false;
            await this.UpdateDiffAsync().ConfigureAwait(true);
        }
        else
        {
            this.ShowDiffPlaceholder("Select a change to view its diff.");
        }

        this.UpdateActionStates();

        if (!status.IsRepository)
        {
            this.statusText.Text = status.ErrorMessage ?? "Not a Git repository.";
            return;
        }

        var upstream = string.IsNullOrWhiteSpace(status.Upstream) ? string.Empty : $" -> {status.Upstream}";
        var sync = status.Ahead == 0 && status.Behind == 0
            ? string.Empty
            : $" (+{status.Ahead}/-{status.Behind})";
        this.statusText.Text = $"{status.Branch ?? "(detached)"}{upstream}{sync}";

        if (items.Count == 0)
        {
            this.statusText.Text += "\nWorking tree clean.";
        }
    }

    /// <summary>
    /// Builds compact presentation items from repository status buckets.
    /// </summary>
    /// <param name="status">Repository status to present.</param>
    /// <returns>Ordered staged, unstaged, and untracked items.</returns>
    internal static IReadOnlyList<GitChangeItem> BuildChangeItems(GitRepositoryStatus status)
    {
        var items = new List<GitChangeItem>();
        foreach (var entry in status.Staged)
        {
            items.Add(new GitChangeItem(entry, "staged"));
        }

        foreach (var entry in status.Unstaged)
        {
            items.Add(new GitChangeItem(entry, "changed"));
        }

        foreach (var entry in status.Untracked)
        {
            items.Add(new GitChangeItem(entry, "untracked"));
        }

        return items;
    }

    /// <inheritdoc/>
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        this.ConfigureRepositoryWatcher(this.currentStatus?.RepositoryRoot);
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        this.repositoryWatcher?.Dispose();
        this.repositoryWatcher = null;
        this.watcherRefreshTimer.Stop();
    }

    private static bool IsConflict(GitFileStatus status)
    {
        return status.IndexStatus == 'U' || status.WorkTreeStatus == 'U';
    }

    private (IReadOnlyList<TreeViewItem> Roots, IReadOnlyList<(GitChangeItem Item, TreeViewItem Container)> Items)
        BuildChangeTree(IReadOnlyList<GitChangeItem> items)
    {
        var duplicateNames = items
            .GroupBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(GitChangeItem Item, TreeViewItem Container)>();
        var roots = new List<TreeViewItem>();
        this.AddChangeGroup(
            roots,
            entries,
            "Merge Changes",
            items.Where(item => IsConflict(item.Status)),
            duplicateNames);
        this.AddChangeGroup(
            roots,
            entries,
            "Staged Changes",
            items.Where(item => item.Status.Bucket == GitStatusBucket.Staged && !IsConflict(item.Status)),
            duplicateNames);
        this.AddChangeGroup(
            roots,
            entries,
            "Changes",
            items.Where(item => item.Status.Bucket != GitStatusBucket.Staged && !IsConflict(item.Status)),
            duplicateNames);
        return (roots, entries);
    }

    private void AddChangeGroup(
        ICollection<TreeViewItem> roots,
        ICollection<(GitChangeItem Item, TreeViewItem Container)> entries,
        string title,
        IEnumerable<GitChangeItem> groupItems,
        IReadOnlySet<string> duplicateNames)
    {
        var items = groupItems.ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var children = new List<TreeViewItem>(items.Length);
        foreach (var item in items)
        {
            var container = this.BuildChangeTreeItem(item, duplicateNames.Contains(item.FileName));
            children.Add(container);
            entries.Add((item, container));
        }

        roots.Add(new TreeViewItem
        {
            Header = $"{title} ({items.Length})",
            IsExpanded = true,
            ItemsSource = children,
        });
    }

    private TreeViewItem BuildChangeTreeItem(GitChangeItem item, bool showParentPath)
    {
        var namePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = item.FileName,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        };
        if (showParentPath && item.ParentPath.Length > 0)
        {
            namePanel.Children.Add(new TextBlock
            {
                Text = item.ParentPath,
                FontSize = 10,
                Foreground = Brushes.Gray,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                namePanel,
                new TextBlock
                {
                    Text = item.StatusBadge,
                    Margin = new Thickness(8, 0, 2, 0),
                    FontWeight = FontWeight.Bold,
                },
            },
        };
        Grid.SetColumn(header.Children[1], 1);
        var container = new TreeViewItem
        {
            Header = header,
            Tag = item,
        };
        ToolTip.SetTip(container, item.Status.Path);
        AutomationProperties.SetName(container, item.AccessibleName);
        return container;
    }

    private TextBlock BuildSideHeader()
    {
        return new TextBlock
        {
            FontFamily = MonoFont,
            FontSize = 11,
            Foreground = Brushes.Gray,
            Margin = new Thickness(6, 0, 6, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
    }

    private TextEditor BuildEditor(FullLineHighlightRenderer highlightRenderer, bool isLineNumberGutter)
    {
        var editor = new TextEditor
        {
            Document = new TextDocument(string.Empty),
            IsReadOnly = true,
            ShowLineNumbers = false,
            WordWrap = false,
            FontFamily = MonoFont,
            FontSize = 12,
            Background = Brushes.Transparent,
            HorizontalScrollBarVisibility = isLineNumberGutter
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = isLineNumberGutter
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        if (isLineNumberGutter)
        {
            editor.Width = 52;
            editor.IsHitTestVisible = false;
            editor.Padding = new Thickness(4, 0, 6, 0);
            editor.Bind(ForegroundProperty, this.GetResourceObservable("TextSecondaryBrush"));
        }
        else
        {
            editor.Bind(ForegroundProperty, this.GetResourceObservable("TextPrimaryBrush"));
        }

        editor.TextArea.Background = Brushes.Transparent;
        editor.TextArea.TextView.BackgroundRenderers.Add(highlightRenderer);
        return editor;
    }

    private async Task UpdateDiffAsync()
    {
        if (this.GetSelectedChangeItem() is not { } item)
        {
            this.ShowDiffPlaceholder("Select a change to view its diff.");
            return;
        }

        this.diffHeader.Text = item.Status.Path;

        if (this.currentStatus?.RepositoryRoot is not { } root)
        {
            this.ShowDiffPlaceholder("No repository available.");
            return;
        }

        var token = this.refreshToken;
        this.ShowDiffPlaceholder("Loading full-file diff...");
        var comparison = await this.gitService.GetFileComparisonAsync(root, item.Status).ConfigureAwait(true);
        if (token != this.refreshToken || !ReferenceEquals(this.GetSelectedChangeItem(), item))
        {
            return;
        }

        if (comparison.IsBinary)
        {
            this.ShowDiffPlaceholder("Binary file. No textual diff to display.");
            return;
        }

        if (!comparison.Succeeded)
        {
            this.ShowDiffPlaceholder(string.IsNullOrWhiteSpace(comparison.ErrorMessage)
                ? "Unable to load diff."
                : comparison.ErrorMessage);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => this.RenderComparison(comparison));
    }

    private void RenderComparison(GitFileComparison comparison)
    {
        var oldSide = comparison.OldSide!;
        var newSide = comparison.NewSide!;
        var highlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(comparison.Path));
        this.oldEditor.SyntaxHighlighting = highlighting;
        this.newEditor.SyntaxHighlighting = highlighting;
        this.oldHeader.Text = $"Old: {oldSide.SourceLabel}";
        this.newHeader.Text = $"New: {newSide.SourceLabel}";
        this.SetEditorContent(
            this.oldEditor,
            this.oldLineNumberEditor,
            this.oldHighlightRenderer,
            this.oldLineNumberHighlightRenderer,
            oldSide);
        this.SetEditorContent(
            this.newEditor,
            this.newLineNumberEditor,
            this.newHighlightRenderer,
            this.newLineNumberHighlightRenderer,
            newSide);
        this.currentChangeLines = oldSide.Highlights
            .Select(range => range.StartLine)
            .Concat(newSide.Highlights.Select(range => range.StartLine))
            .Distinct()
            .OrderBy(line => line)
            .ToArray();
        this.currentChangeIndex = -1;
        this.previousChangeButton.IsEnabled = this.currentChangeLines.Count > 0;
        this.nextChangeButton.IsEnabled = this.currentChangeLines.Count > 0;
        this.diffPlaceholder.IsVisible = false;
        this.comparisonGrid.IsVisible = true;
    }

    private void SetEditorContent(
        TextEditor editor,
        TextEditor lineNumberEditor,
        FullLineHighlightRenderer highlightRenderer,
        FullLineHighlightRenderer lineNumberHighlightRenderer,
        GitFileSideContent side)
    {
        editor.Text = side.Text;
        lineNumberEditor.Text = string.Join(
            "\n",
            side.LineNumbers.Select(lineNumber => lineNumber?.ToString() ?? string.Empty));
        highlightRenderer.SetHighlights(side.Highlights);
        lineNumberHighlightRenderer.SetHighlights(side.Highlights);
        editor.TextArea.TextView.Redraw();
        lineNumberEditor.TextArea.TextView.Redraw();
    }

    private void SyncScrollOffset(
        TextEditor source,
        TextEditor target,
        TextEditor sourceLineNumbers,
        TextEditor targetLineNumbers)
    {
        if (this.suppressEditorScrollSync)
        {
            return;
        }

        this.suppressEditorScrollSync = true;
        var offset = source.TextArea.TextView.ScrollOffset;
        target.ScrollToHorizontalOffset(offset.X);
        target.ScrollToVerticalOffset(offset.Y);
        sourceLineNumbers.ScrollToVerticalOffset(offset.Y);
        targetLineNumbers.ScrollToVerticalOffset(offset.Y);
        this.suppressEditorScrollSync = false;
    }

    private void ShowDiffPlaceholder(string message)
    {
        this.ClearEditors();
        this.currentChangeLines = Array.Empty<int>();
        this.currentChangeIndex = -1;
        this.previousChangeButton.IsEnabled = false;
        this.nextChangeButton.IsEnabled = false;
        this.diffPlaceholderText.Text = message;
        this.diffPlaceholder.IsVisible = true;
        this.comparisonGrid.IsVisible = false;
    }

    private void ClearEditors()
    {
        this.oldHeader.Text = string.Empty;
        this.newHeader.Text = string.Empty;
        this.SetEditorContent(
            this.oldEditor,
            this.oldLineNumberEditor,
            this.oldHighlightRenderer,
            this.oldLineNumberHighlightRenderer,
            this.EmptySide());
        this.SetEditorContent(
            this.newEditor,
            this.newLineNumberEditor,
            this.newHighlightRenderer,
            this.newLineNumberHighlightRenderer,
            this.EmptySide());
    }

    private Grid BuildComparisonGrid()
    {
        var grid = new Grid
        {
            Margin = new Thickness(10, 0, 10, 10),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star),
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(1, GridUnitType.Star),
            },
        };

        var divider = new GridSplitter
        {
            Width = 4,
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetRowSpan(divider, 2);
        Grid.SetColumn(this.oldHeader, 0);
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(this.newHeader, 2);
        var oldSide = this.BuildEditorSide(this.oldLineNumberEditor, this.oldEditor);
        var newSide = this.BuildEditorSide(this.newLineNumberEditor, this.newEditor);
        Grid.SetRow(oldSide, 1);
        Grid.SetColumn(oldSide, 0);
        Grid.SetRow(newSide, 1);
        Grid.SetColumn(newSide, 2);
        grid.Children.Add(this.oldHeader);
        grid.Children.Add(divider);
        grid.Children.Add(this.newHeader);
        grid.Children.Add(oldSide);
        grid.Children.Add(newSide);
        return grid;
    }

    private Grid BuildEditorSide(TextEditor lineNumbers, TextEditor editor)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(1, GridUnitType.Star),
            },
        };
        Grid.SetColumn(lineNumbers, 0);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(lineNumbers);
        grid.Children.Add(editor);
        return grid;
    }

    private GitFileSideContent EmptySide()
    {
        return new GitFileSideContent(
            string.Empty,
            string.Empty,
            Array.Empty<GitDiffHighlightRange>(),
            Array.Empty<int?>());
    }

    private GitChangeItem? GetSelectedChangeItem()
    {
        return (this.changesList.SelectedItem as TreeViewItem)?.Tag as GitChangeItem;
    }

    private void NavigateChange(int direction)
    {
        if (this.currentChangeLines.Count == 0)
        {
            return;
        }

        this.currentChangeIndex = this.currentChangeIndex < 0
            ? direction > 0 ? 0 : this.currentChangeLines.Count - 1
            : (this.currentChangeIndex + direction + this.currentChangeLines.Count) % this.currentChangeLines.Count;
        var line = this.currentChangeLines[this.currentChangeIndex];
        this.oldEditor.ScrollToLine(line);
        this.newEditor.ScrollToLine(line);
    }

    private void ConfigureRepositoryWatcher(string? repositoryRoot)
    {
        if (this.Parent is null)
        {
            return;
        }

        if (string.Equals(
            this.repositoryWatcher?.Path,
            repositoryRoot,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        this.repositoryWatcher?.Dispose();
        this.repositoryWatcher = null;
        if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
        {
            return;
        }

        try
        {
            this.repositoryWatcher = new FileSystemWatcher(repositoryRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            this.repositoryWatcher.Changed += this.OnRepositoryChanged;
            this.repositoryWatcher.Created += this.OnRepositoryChanged;
            this.repositoryWatcher.Deleted += this.OnRepositoryChanged;
            this.repositoryWatcher.Renamed += this.OnRepositoryChanged;
        }
        catch (IOException)
        {
            this.repositoryWatcher?.Dispose();
            this.repositoryWatcher = null;
        }
        catch (UnauthorizedAccessException)
        {
            this.repositoryWatcher?.Dispose();
            this.repositoryWatcher = null;
        }
    }

    private void OnRepositoryChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.watcherRefreshTimer.Stop();
            this.watcherRefreshTimer.Start();
        });
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F7)
        {
            return;
        }

        this.NavigateChange(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
        e.Handled = true;
    }

    private async Task StageSelectedAsync()
    {
        var item = this.GetSelectedChangeItem();
        if (this.currentStatus?.RepositoryRoot is not { } root ||
            item is null ||
            item.Status.Bucket == GitStatusBucket.Staged)
        {
            return;
        }

        await this.RunGitActionAsync(
            () => this.gitService.StageAsync(root, item.Status.Path),
            "Unable to stage the selected file.").ConfigureAwait(true);
    }

    private async Task UnstageSelectedAsync()
    {
        var item = this.GetSelectedChangeItem();
        if (this.currentStatus?.RepositoryRoot is not { } root ||
            item?.Status.Bucket != GitStatusBucket.Staged)
        {
            return;
        }

        await this.RunGitActionAsync(
            () => this.gitService.UnstageAsync(root, item.Status),
            "Unable to unstage the selected file.").ConfigureAwait(true);
    }

    private async Task StageAllAsync()
    {
        if (this.currentStatus?.RepositoryRoot is not { } root)
        {
            return;
        }

        await this.RunGitActionAsync(
            () => this.gitService.StageAllAsync(root),
            "Unable to stage all changes.").ConfigureAwait(true);
    }

    private async Task UnstageAllAsync()
    {
        if (this.currentStatus?.RepositoryRoot is not { } root)
        {
            return;
        }

        await this.RunGitActionAsync(
            () => this.gitService.UnstageAllAsync(root),
            "Unable to unstage all changes.").ConfigureAwait(true);
    }

    private async Task DiscardSelectedAsync()
    {
        var item = this.GetSelectedChangeItem();
        if (this.currentStatus?.RepositoryRoot is not { } root ||
            item is null ||
            item.Status.Bucket == GitStatusBucket.Staged ||
            IsConflict(item.Status) ||
            TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var result = await NativeMessageBox.ShowYesNoAsync(
            owner,
            "Discard changes?",
            $"Discard all uncommitted changes in {item.Status.Path}? This cannot be undone by AeroTerm.",
            "Discard",
            "Cancel").ConfigureAwait(true);
        if (result != NativeMessageBoxResult.Yes)
        {
            return;
        }

        await this.RunGitActionAsync(
            () => item.Status.Bucket == GitStatusBucket.Untracked
                ? this.gitService.DeleteUntrackedAsync(root, item.Status.Path)
                : this.gitService.DiscardAsync(root, item.Status.Path),
            "Unable to discard the selected file.").ConfigureAwait(true);
    }

    private async Task CommitAsync()
    {
        var message = this.commitMessageText.Text?.Trim();
        if (this.currentStatus?.RepositoryRoot is not { } root || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var succeeded = await this.RunGitActionAsync(
            () => this.gitService.CommitAsync(root, message),
            "Unable to commit staged changes.").ConfigureAwait(true);
        if (succeeded)
        {
            this.commitMessageText.Text = string.Empty;
        }
    }

    private async Task FetchAsync()
    {
        if (this.currentStatus?.RepositoryRoot is not { } root)
        {
            return;
        }

        await this.RunGitActionAsync(
            () => this.gitService.FetchAsync(root),
            "Unable to fetch remote updates.").ConfigureAwait(true);
    }

    private async Task PullAsync()
    {
        if (this.currentStatus?.RepositoryRoot is not { } root)
        {
            return;
        }

        await this.RunGitActionAsync(
            () => this.gitService.PullAsync(root),
            "Unable to pull remote updates.").ConfigureAwait(true);
    }

    private async Task PushAsync()
    {
        if (this.currentStatus?.RepositoryRoot is not { } root)
        {
            return;
        }

        await this.RunGitActionAsync(
            () => this.gitService.PushAsync(root),
            "Unable to push local commits.").ConfigureAwait(true);
    }

    private async Task SyncAsync()
    {
        if (this.currentStatus?.RepositoryRoot is not { } root)
        {
            return;
        }

        await this.RunGitActionAsync(
            () => this.gitService.SyncAsync(root),
            "Unable to synchronize with the upstream branch.").ConfigureAwait(true);
    }

    private async Task<bool> RunGitActionAsync(
        Func<Task<GitCommandResult>> action,
        string fallbackError)
    {
        if (this.gitActionRunning)
        {
            return false;
        }

        this.gitActionRunning = true;
        this.statusText.Text = "Running Git command...";
        this.UpdateActionStates();
        var result = await action().ConfigureAwait(true);
        this.gitActionRunning = false;
        if (!result.Succeeded)
        {
            this.statusText.Text = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? fallbackError
                : result.ErrorMessage;
            this.UpdateActionStates();
            return false;
        }

        await this.RefreshAsync().ConfigureAwait(true);
        return true;
    }

    private void UpdateActionStates()
    {
        var status = this.currentStatus;
        var item = this.GetSelectedChangeItem();
        var hasRepository = status?.IsRepository == true && !this.gitActionRunning;
        var conflict = item is not null && IsConflict(item.Status);
        this.stageButton.IsEnabled = hasRepository &&
            item is not null &&
            item.Status.Bucket != GitStatusBucket.Staged &&
            !conflict;
        this.unstageButton.IsEnabled = hasRepository &&
            item?.Status.Bucket == GitStatusBucket.Staged &&
            !conflict;
        this.discardButton.IsEnabled = hasRepository &&
            item is not null &&
            item.Status.Bucket != GitStatusBucket.Staged &&
            !conflict;
        this.stageAllButton.IsEnabled = hasRepository &&
            status!.Unstaged.Concat(status.Untracked).Any(entry => !IsConflict(entry));
        this.unstageAllButton.IsEnabled = hasRepository && status!.Staged.Count > 0;
        this.fetchButton.IsEnabled = hasRepository;
        var hasUpstream = hasRepository && !string.IsNullOrWhiteSpace(status!.Upstream);
        this.pullButton.IsEnabled = hasUpstream;
        this.pushButton.IsEnabled = hasUpstream;
        this.syncButton.IsEnabled = hasUpstream;
        this.commitMessageText.IsEnabled = hasRepository;
        this.commitButton.IsEnabled = hasRepository &&
            status!.Staged.Count > 0 &&
            !string.IsNullOrWhiteSpace(this.commitMessageText.Text);
    }

    private sealed class FullLineHighlightRenderer : IBackgroundRenderer
    {
        private readonly Control resourceHost;
        private IReadOnlyList<GitDiffHighlightRange> highlights = Array.Empty<GitDiffHighlightRange>();

        public FullLineHighlightRenderer(Control resourceHost)
        {
            this.resourceHost = resourceHost;
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void SetHighlights(IReadOnlyList<GitDiffHighlightRange> ranges)
        {
            this.highlights = ranges;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid)
            {
                return;
            }

            foreach (var visualLine in textView.VisualLines)
            {
                var brush = this.GetBrush(visualLine.FirstDocumentLine.LineNumber);
                if (brush is null)
                {
                    continue;
                }

                drawingContext.DrawRectangle(
                    brush,
                    null,
                    new Rect(
                        0,
                        visualLine.VisualTop - textView.ScrollOffset.Y,
                        textView.Bounds.Width,
                        visualLine.Height));
            }
        }

        private IBrush? GetBrush(int lineNumber)
        {
            foreach (var range in this.highlights)
            {
                if (lineNumber >= range.StartLine && lineNumber < range.StartLine + range.LineCount)
                {
                    return range.Kind switch
                    {
                        GitDiffHighlightKind.Added => this.ResolveBrush("GitDiffAddedBackgroundBrush", AddedBrush),
                        GitDiffHighlightKind.Removed => this.ResolveBrush("GitDiffRemovedBackgroundBrush", RemovedBrush),
                        GitDiffHighlightKind.Modified => this.ResolveBrush("GitDiffModifiedBackgroundBrush", ModifiedBrush),
                        _ => null,
                    };
                }
            }

            return null;
        }

        private IBrush ResolveBrush(string resourceKey, IBrush fallback)
        {
            return this.resourceHost.TryGetResource(
                resourceKey,
                this.resourceHost.ActualThemeVariant,
                out var value) && value is IBrush brush
                    ? brush
                    : fallback;
        }
    }
}
