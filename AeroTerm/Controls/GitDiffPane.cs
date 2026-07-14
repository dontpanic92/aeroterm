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
using AeroTerm.Utilities;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
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
    private readonly TextBlock oldHeader;
    private readonly TextBlock newHeader;
    private readonly TextEditor oldEditor;
    private readonly TextEditor newEditor;
    private readonly GitDiffLineNumberMargin oldLineNumberMargin;
    private readonly GitDiffLineNumberMargin newLineNumberMargin;
    private readonly FullLineHighlightRenderer oldHighlightRenderer;
    private readonly FullLineHighlightRenderer newHighlightRenderer;
    private readonly Panel diffPlaceholder;
    private readonly TextBlock diffPlaceholderText;
    private readonly Grid comparisonGrid;
    private readonly DispatcherTimer watcherRefreshTimer;
    private readonly HashSet<string> pendingWatcherPaths = new(GetPathComparer());

    private GitRepositoryStatus? currentStatus;
    private FileSystemWatcher? repositoryWatcher;
    private IReadOnlyList<GitChangeTreeNode> currentChangeTree = Array.Empty<GitChangeTreeNode>();
    private (string RepositoryRoot, GitStatusBucket Bucket, string Path)? currentComparisonKey;
    private string? currentChangeFingerprint;
    private string? currentSummaryFingerprint;
    private Task refreshLoopTask = Task.CompletedTask;
    private int diffRequestToken;
    private bool suppressEditorScrollSync;
    private bool suppressChangeSelection;
    private bool gitActionRunning;
    private bool refreshRequested;
    private bool manualRefreshRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitDiffPane"/> class.
    /// </summary>
    /// <param name="workingDirectoryProvider">Provides the current terminal working directory.</param>
    /// <param name="settings">Optional application settings whose terminal typography should be used.</param>
    public GitDiffPane(Func<string?> workingDirectoryProvider, AppSettings? settings = null)
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

        this.changesList = new TreeView
        {
            DataTemplates =
            {
                new FuncTreeDataTemplate<GitChangeTreeNode>(
                    (node, _) => this.BuildChangeTreeHeader(node),
                    node => node.Children),
            },
        };
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
        this.oldHeader = this.BuildSideHeader();
        this.newHeader = this.BuildSideHeader();
        this.oldHighlightRenderer = new FullLineHighlightRenderer(this.oldHeader);
        this.newHighlightRenderer = new FullLineHighlightRenderer(this.newHeader);
        this.oldLineNumberMargin = this.BuildLineNumberMargin();
        this.newLineNumberMargin = this.BuildLineNumberMargin();
        this.oldEditor = this.BuildEditor(this.oldHighlightRenderer, this.oldLineNumberMargin);
        this.newEditor = this.BuildEditor(this.newHighlightRenderer, this.newLineNumberMargin);
        this.oldEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => this.SyncScrollOffset(
            this.oldEditor,
            this.newEditor);
        this.newEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => this.SyncScrollOffset(
            this.newEditor,
            this.oldEditor);
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
        };
        diffToolbar.Children.Add(this.diffHeader);
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
                await this.RequestRefreshAsync(manual: false).ConfigureAwait(true);
            }
        };
        this.ShowDiffPlaceholder("Select a change to view its diff.");
        if (settings is not null)
        {
            this.ApplyTypography(settings);
        }
    }

    /// <summary>
    /// Reloads the repository status and refreshes the changes list.
    /// </summary>
    /// <returns>A task that completes when the refresh finishes.</returns>
    public Task RefreshAsync()
    {
        return this.RequestRefreshAsync(manual: true);
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

    /// <summary>
    /// Requests a background refresh for filesystem changes.
    /// </summary>
    /// <param name="paths">Changed absolute paths reported by the repository watcher.</param>
    /// <returns>A task that completes when queued refresh work finishes.</returns>
    internal Task RefreshForFileSystemChangesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            this.pendingWatcherPaths.Add(Path.GetFullPath(path));
        }

        return this.RequestRefreshAsync(manual: false);
    }

    /// <summary>
    /// Applies the terminal font family chain and size to the diff editors.
    /// </summary>
    /// <param name="settings">The application settings to resolve.</param>
    internal void ApplyTypography(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fonts = FontPriorityList.Resolve(
            settings.FontFamily,
            settings.FallbackFonts);
        var fontFamily = fonts.Count == 0
            ? MonoFont
            : new FontFamily(string.Join(",", fonts));

        this.oldEditor.FontFamily = fontFamily;
        this.newEditor.FontFamily = fontFamily;
        this.oldLineNumberMargin.SetValue(TextBlock.FontFamilyProperty, fontFamily);
        this.newLineNumberMargin.SetValue(TextBlock.FontFamilyProperty, fontFamily);
        if (settings.FontSize > 0)
        {
            this.oldEditor.FontSize = settings.FontSize;
            this.newEditor.FontSize = settings.FontSize;
            this.oldLineNumberMargin.SetValue(TextBlock.FontSizeProperty, settings.FontSize);
            this.newLineNumberMargin.SetValue(TextBlock.FontSizeProperty, settings.FontSize);
        }
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

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static string BuildChangeFingerprint(GitRepositoryStatus status)
    {
        var parts = new List<string>();
        AddFingerprintPart(parts, status.RepositoryRoot);
        AddFingerprintEntries(parts, status.Staged);
        AddFingerprintEntries(parts, status.Unstaged);
        AddFingerprintEntries(parts, status.Untracked);
        return string.Concat(parts);
    }

    private static string BuildSummaryFingerprint(GitRepositoryStatus status)
    {
        var parts = new List<string>();
        AddFingerprintPart(parts, status.RepositoryRoot);
        AddFingerprintPart(parts, status.Branch);
        AddFingerprintPart(parts, status.Upstream);
        AddFingerprintPart(parts, status.Ahead.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddFingerprintPart(parts, status.Behind.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddFingerprintPart(parts, status.ErrorMessage);
        return string.Concat(parts);
    }

    private static void AddFingerprintEntries(
        ICollection<string> parts,
        IReadOnlyList<GitFileStatus> entries)
    {
        AddFingerprintPart(
            parts,
            entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var entry in entries)
        {
            AddFingerprintPart(parts, entry.Path);
            AddFingerprintPart(parts, entry.OriginalPath);
            AddFingerprintPart(parts, entry.IndexStatus.ToString());
            AddFingerprintPart(parts, entry.WorkTreeStatus.ToString());
            AddFingerprintPart(
                parts,
                ((int)entry.Bucket).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void AddFingerprintPart(ICollection<string> parts, string? value)
    {
        value ??= string.Empty;
        parts.Add(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        parts.Add(":");
        parts.Add(value);
        parts.Add(";");
    }

    private static bool IsConflict(GitFileStatus status)
    {
        return status.IndexStatus == 'U' || status.WorkTreeStatus == 'U';
    }

    private static string GetChangeGroupKey(GitChangeTreeNode node)
    {
        var countSeparator = node.Title.LastIndexOf(" (", StringComparison.Ordinal);
        return countSeparator < 0 ? node.Title : node.Title[..countSeparator];
    }

    private static string NormalizeRepositoryIdentity(string repositoryRoot)
    {
        var fullPath = Path.GetFullPath(repositoryRoot);
        return OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
    }

    private static EditorViewState CaptureEditorViewState(TextEditor editor)
    {
        var scroll = (IScrollable)editor.TextArea.TextView;
        return new EditorViewState(
            editor.CaretOffset,
            editor.SelectionStart,
            editor.SelectionLength,
            scroll.Offset);
    }

    private static void RestoreEditorViewState(TextEditor editor, EditorViewState state)
    {
        var textLength = editor.Document?.TextLength ?? 0;
        var selectionStart = Math.Clamp(state.SelectionStart, 0, textLength);
        var selectionLength = Math.Clamp(state.SelectionLength, 0, textLength - selectionStart);
        editor.Select(selectionStart, selectionLength);
        editor.CaretOffset = Math.Clamp(state.CaretOffset, 0, textLength);

        var scroll = (IScrollable)editor.TextArea.TextView;
        var maximumX = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
        var maximumY = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(
            Math.Clamp(state.ScrollOffset.X, 0, maximumX),
            Math.Clamp(state.ScrollOffset.Y, 0, maximumY));
    }

    private static IBrush ResolveHighlightBrush(
        Control resourceHost,
        GitDiffHighlightKind kind)
    {
        var (resourceKey, fallback) = kind switch
        {
            GitDiffHighlightKind.Added => ("GitDiffAddedBackgroundBrush", AddedBrush),
            GitDiffHighlightKind.Removed => ("GitDiffRemovedBackgroundBrush", RemovedBrush),
            GitDiffHighlightKind.Modified => ("GitDiffModifiedBackgroundBrush", ModifiedBrush),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return resourceHost.TryGetResource(
            resourceKey,
            resourceHost.ActualThemeVariant,
            out var value) && value is IBrush brush
                ? brush
                : fallback;
    }

    private Task RequestRefreshAsync(bool manual)
    {
        this.refreshRequested = true;
        this.manualRefreshRequested |= manual;
        if (this.refreshLoopTask.IsCompleted)
        {
            this.refreshLoopTask = this.RunRefreshLoopAsync();
        }

        return this.refreshLoopTask;
    }

    private async Task RunRefreshLoopAsync()
    {
        while (this.refreshRequested)
        {
            var manual = this.manualRefreshRequested;
            var changedPaths = this.pendingWatcherPaths.ToArray();
            this.refreshRequested = false;
            this.manualRefreshRequested = false;
            this.pendingWatcherPaths.Clear();
            await this.RefreshCoreAsync(manual, changedPaths).ConfigureAwait(true);
        }
    }

    private async Task RefreshCoreAsync(bool manual, IReadOnlyCollection<string> changedPaths)
    {
        var root = this.workingDirectoryProvider();
        this.cwdText.Text = string.IsNullOrEmpty(root) ? "(no working directory reported yet)" : root;
        if (manual)
        {
            this.statusText.Text = "Loading Git status...";
        }

        var status = await this.gitService.GetStatusAsync(root).ConfigureAwait(true);
        this.ConfigureRepositoryWatcher(status.RepositoryRoot);
        var changeFingerprint = BuildChangeFingerprint(status);
        var summaryFingerprint = BuildSummaryFingerprint(status);
        var changeTreeChanged = !string.Equals(
            this.currentChangeFingerprint,
            changeFingerprint,
            StringComparison.Ordinal);
        var summaryChanged = !string.Equals(
            this.currentSummaryFingerprint,
            summaryFingerprint,
            StringComparison.Ordinal);
        var selectedItem = this.GetSelectedChangeItem();
        (GitStatusBucket Bucket, string Path)? selectedKey = selectedItem is null
            ? null
            : (selectedItem.Status.Bucket, selectedItem.Status.Path);
        this.currentStatus = status;
        this.currentChangeFingerprint = changeFingerprint;
        this.currentSummaryFingerprint = summaryFingerprint;
        var items = BuildChangeItems(status);
        if (changeTreeChanged)
        {
            this.CaptureChangeGroupExpansion();
            var expansion = this.currentChangeTree.ToDictionary(
                GetChangeGroupKey,
                node => node.IsExpanded,
                StringComparer.Ordinal);
            this.currentChangeTree = this.BuildChangeTree(items);
            foreach (var group in this.currentChangeTree)
            {
                if (expansion.TryGetValue(GetChangeGroupKey(group), out var isExpanded))
                {
                    group.IsExpanded = isExpanded;
                }
            }

            this.changesList.ItemsSource = this.currentChangeTree;
            this.ApplyChangeGroupExpansion();
            Dispatcher.UIThread.Post(this.ApplyChangeGroupExpansion);
            var itemToSelect = selectedKey is { } key
                ? this.FindChangeNode(key.Bucket, key.Path)
                : null;
            itemToSelect ??= this.currentChangeTree
                .SelectMany(group => group.Children)
                .FirstOrDefault();
            this.suppressChangeSelection = true;
            this.changesList.SelectedItem = itemToSelect;
            this.suppressChangeSelection = false;
        }

        var currentItem = this.GetSelectedChangeItem();
        if (currentItem is not null &&
            (manual ||
             changeTreeChanged ||
             this.IsSelectedPathAffected(currentItem.Status, changedPaths) ||
             this.IsGitMetadataAffected(changedPaths)))
        {
            await this.UpdateDiffAsync(showLoading: manual || !this.comparisonGrid.IsVisible).ConfigureAwait(true);
        }
        else if (currentItem is null && (manual || changeTreeChanged))
        {
            this.diffRequestToken++;
            this.ShowDiffPlaceholder("Select a change to view its diff.");
        }

        this.UpdateActionStates();
        if (manual || summaryChanged || changeTreeChanged)
        {
            this.UpdateStatusText(status, items.Count);
        }
    }

    private void CaptureChangeGroupExpansion()
    {
        for (var index = 0; index < this.currentChangeTree.Count; index++)
        {
            if (this.changesList.ContainerFromIndex(index) is TreeViewItem container)
            {
                this.currentChangeTree[index].IsExpanded = container.IsExpanded;
            }
        }
    }

    private void ApplyChangeGroupExpansion()
    {
        for (var index = 0; index < this.currentChangeTree.Count; index++)
        {
            if (this.changesList.ContainerFromIndex(index) is TreeViewItem container)
            {
                container.IsExpanded = this.currentChangeTree[index].IsExpanded;
            }
        }
    }

    private void UpdateStatusText(GitRepositoryStatus status, int itemCount)
    {
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

        if (itemCount == 0)
        {
            this.statusText.Text += "\nWorking tree clean.";
        }
    }

    private IReadOnlyList<GitChangeTreeNode> BuildChangeTree(IReadOnlyList<GitChangeItem> items)
    {
        var duplicateNames = items
            .GroupBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = new List<GitChangeTreeNode>();
        this.AddChangeGroup(
            roots,
            "Merge Changes",
            items.Where(item => IsConflict(item.Status)),
            duplicateNames);
        this.AddChangeGroup(
            roots,
            "Staged Changes",
            items.Where(item => item.Status.Bucket == GitStatusBucket.Staged && !IsConflict(item.Status)),
            duplicateNames);
        this.AddChangeGroup(
            roots,
            "Changes",
            items.Where(item => item.Status.Bucket != GitStatusBucket.Staged && !IsConflict(item.Status)),
            duplicateNames);
        return roots;
    }

    private void AddChangeGroup(
        ICollection<GitChangeTreeNode> roots,
        string title,
        IEnumerable<GitChangeItem> groupItems,
        IReadOnlySet<string> duplicateNames)
    {
        var items = groupItems.ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var children = new List<GitChangeTreeNode>(items.Length);
        foreach (var item in items)
        {
            children.Add(new GitChangeTreeNode(
                string.Empty,
                item,
                Array.Empty<GitChangeTreeNode>(),
                duplicateNames.Contains(item.FileName)));
        }

        roots.Add(new GitChangeTreeNode($"{title} ({items.Length})", null, children));
    }

    private Control BuildChangeTreeHeader(GitChangeTreeNode node)
    {
        if (node.Item is not { } item)
        {
            return new TextBlock
            {
                Text = node.Title,
                FontWeight = FontWeight.Bold,
            };
        }

        var namePanel = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                new TextBlock
                {
                    Text = item.FileName,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };
        if (node.ShowParentPath && item.ParentPath.Length > 0)
        {
            var parentPath = new TextBlock
            {
                Text = item.ParentPath,
                FontSize = 10,
                Foreground = Brushes.Gray,
                Margin = new Thickness(6, 0, 0, 0),
                MaxWidth = 100,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(parentPath, 1);
            namePanel.Children.Add(parentPath);
        }

        var header = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
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
        ToolTip.SetTip(header, item.Status.Path);
        AutomationProperties.SetName(header, item.AccessibleName);
        return header;
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

    private GitDiffLineNumberMargin BuildLineNumberMargin()
    {
        var margin = new GitDiffLineNumberMargin(this.ResolveHighlightBrush);
        margin.SetValue(TextBlock.FontFamilyProperty, MonoFont);
        margin.SetValue(TextBlock.FontSizeProperty, 12);
        margin.Bind(
            TextBlock.ForegroundProperty,
            this.GetResourceObservable("TextSecondaryBrush"));
        return margin;
    }

    private TextEditor BuildEditor(
        FullLineHighlightRenderer highlightRenderer,
        GitDiffLineNumberMargin lineNumberMargin)
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
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        editor.Bind(ForegroundProperty, this.GetResourceObservable("TextPrimaryBrush"));
        editor.TextArea.Background = Brushes.Transparent;
        editor.TextArea.LeftMargins.Add(lineNumberMargin);
        editor.TextArea.TextView.BackgroundRenderers.Add(highlightRenderer);
        return editor;
    }

    private async Task UpdateDiffAsync(bool showLoading = true)
    {
        var token = ++this.diffRequestToken;
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

        var comparisonKey = (
            NormalizeRepositoryIdentity(root),
            item.Status.Bucket,
            item.Status.Path);
        var preserveEditorState = this.currentComparisonKey == comparisonKey;
        if (showLoading && !this.comparisonGrid.IsVisible)
        {
            this.ShowDiffPlaceholder("Loading full-file diff...");
        }

        var comparison = await this.gitService.GetFileComparisonAsync(root, item.Status).ConfigureAwait(true);
        if (token != this.diffRequestToken)
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

        await Dispatcher.UIThread.InvokeAsync(() =>
            this.RenderComparison(comparison, comparisonKey, preserveEditorState));
    }

    private void RenderComparison(
        GitFileComparison comparison,
        (string RepositoryRoot, GitStatusBucket Bucket, string Path) comparisonKey,
        bool preserveEditorState)
    {
        var oldSide = comparison.OldSide!;
        var newSide = comparison.NewSide!;
        var highlighting = GitSyntaxHighlightingResolver.Resolve(comparison.Path);
        this.oldEditor.SyntaxHighlighting = highlighting;
        this.newEditor.SyntaxHighlighting = highlighting;
        this.oldHeader.Text = $"Old: {oldSide.SourceLabel}";
        this.newHeader.Text = $"New: {newSide.SourceLabel}";
        this.SetEditorContent(
            this.oldEditor,
            this.oldHighlightRenderer,
            this.oldLineNumberMargin,
            oldSide,
            preserveEditorState);
        this.SetEditorContent(
            this.newEditor,
            this.newHighlightRenderer,
            this.newLineNumberMargin,
            newSide,
            preserveEditorState);
        this.currentComparisonKey = comparisonKey;
        this.diffPlaceholder.IsVisible = false;
        this.comparisonGrid.IsVisible = true;
    }

    private void SetEditorContent(
        TextEditor editor,
        FullLineHighlightRenderer highlightRenderer,
        GitDiffLineNumberMargin lineNumberMargin,
        GitFileSideContent side,
        bool preserveEditorState)
    {
        EditorViewState? viewState = preserveEditorState
            ? CaptureEditorViewState(editor)
            : null;
        if (!string.Equals(editor.Text, side.Text, StringComparison.Ordinal))
        {
            editor.Text = side.Text;
        }

        highlightRenderer.SetHighlights(side.Highlights);
        lineNumberMargin.SetContent(side.LineNumbers, side.Highlights);
        editor.TextArea.TextView.Redraw();
        if (viewState is { } state)
        {
            RestoreEditorViewState(editor, state);
        }
        else
        {
            editor.Select(0, 0);
            editor.CaretOffset = 0;
            ((IScrollable)editor.TextArea.TextView).Offset = default;
        }
    }

    private void SyncScrollOffset(TextEditor source, TextEditor target)
    {
        if (this.suppressEditorScrollSync)
        {
            return;
        }

        this.suppressEditorScrollSync = true;
        try
        {
            var sourceScroll = (IScrollable)source.TextArea.TextView;
            var targetScroll = (IScrollable)target.TextArea.TextView;
            var maximumOffset = Math.Max(0, targetScroll.Extent.Height - targetScroll.Viewport.Height);
            var targetOffset = Math.Clamp(sourceScroll.Offset.Y, 0, maximumOffset);
            targetScroll.Offset = new Vector(targetScroll.Offset.X, targetOffset);
        }
        finally
        {
            this.suppressEditorScrollSync = false;
        }
    }

    private void ShowDiffPlaceholder(string message)
    {
        this.ClearEditors();
        this.currentComparisonKey = null;
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
            this.oldHighlightRenderer,
            this.oldLineNumberMargin,
            this.EmptySide(),
            preserveEditorState: false);
        this.SetEditorContent(
            this.newEditor,
            this.newHighlightRenderer,
            this.newLineNumberMargin,
            this.EmptySide(),
            preserveEditorState: false);
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
        Grid.SetRow(this.oldEditor, 1);
        Grid.SetColumn(this.oldEditor, 0);
        Grid.SetRow(this.newEditor, 1);
        Grid.SetColumn(this.newEditor, 2);
        grid.Children.Add(this.oldHeader);
        grid.Children.Add(divider);
        grid.Children.Add(this.newHeader);
        grid.Children.Add(this.oldEditor);
        grid.Children.Add(this.newEditor);
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
        return (this.changesList.SelectedItem as GitChangeTreeNode)?.Item;
    }

    private GitChangeTreeNode? FindChangeNode(GitStatusBucket bucket, string path)
    {
        return this.currentChangeTree
            .SelectMany(group => group.Children)
            .FirstOrDefault(node =>
                node.Item?.Status.Bucket == bucket &&
                string.Equals(node.Item.Status.Path, path, StringComparison.Ordinal));
    }

    private bool IsSelectedPathAffected(
        GitFileStatus status,
        IReadOnlyCollection<string> changedPaths)
    {
        if (changedPaths.Count == 0 || this.currentStatus?.RepositoryRoot is not { } root)
        {
            return false;
        }

        var comparer = GetPathComparer();
        var selectedPaths = status.OriginalPath is null
            ? new[] { status.Path }
            : new[] { status.Path, status.OriginalPath };
        foreach (var selectedPath in selectedPaths)
        {
            var absolutePath = Path.GetFullPath(Path.Combine(root, selectedPath));
            if (changedPaths.Any(path => comparer.Equals(path, absolutePath)))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsGitMetadataAffected(IReadOnlyCollection<string> changedPaths)
    {
        if (changedPaths.Count == 0 || this.currentStatus?.RepositoryRoot is not { } root)
        {
            return false;
        }

        var comparer = GetPathComparer();
        var gitPath = Path.GetFullPath(Path.Combine(root, ".git"));
        foreach (var path in changedPaths)
        {
            if (comparer.Equals(path, gitPath))
            {
                return true;
            }

            var relativePath = Path.GetRelativePath(gitPath, path);
            if (relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                string.Equals(relativePath, "..", StringComparison.Ordinal))
            {
                continue;
            }

            var normalizedPath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            if (normalizedPath is "HEAD" or "index" or "index.lock" or "packed-refs" or
                "MERGE_HEAD" or "REBASE_HEAD" or "CHERRY_PICK_HEAD" or "BISECT_HEAD" ||
                normalizedPath.StartsWith("refs/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private IBrush ResolveHighlightBrush(GitDiffHighlightKind kind)
    {
        return ResolveHighlightBrush(this, kind);
    }

    private void ConfigureRepositoryWatcher(string? repositoryRoot)
    {
        if (this.Parent is null)
        {
            return;
        }

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(
            this.repositoryWatcher?.Path,
            repositoryRoot,
            pathComparison))
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
            if (!this.IsVisible)
            {
                return;
            }

            this.pendingWatcherPaths.Add(Path.GetFullPath(e.FullPath));
            this.watcherRefreshTimer.Stop();
            this.watcherRefreshTimer.Start();
        });
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

    private readonly record struct EditorViewState(
        int CaretOffset,
        int SelectionStart,
        int SelectionLength,
        Vector ScrollOffset);

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
                    return ResolveHighlightBrush(this.resourceHost, range.Kind);
                }
            }

            return null;
        }
    }
}
