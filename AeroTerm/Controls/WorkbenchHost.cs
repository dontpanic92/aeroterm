// <copyright file="WorkbenchHost.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// Hosts the terminal tab view with an optional experimental Workbench
/// sidebar. When disabled, the sidebar collapses and the terminal fills
/// the available space.
/// </summary>
internal sealed class WorkbenchHost : UserControl
{
    private const double SidebarWidth = 240;
    private const int ExplorerSection = 0;
    private const int EditorSection = 1;
    private const int GitSection = 2;

    private readonly FileExplorerService fileExplorerService = new();
    private readonly TextEditorService textEditorService = new();
    private readonly GitService gitService = new();
    private readonly ColumnDefinition sidebarColumn;
    private readonly Border sidebar;
    private readonly TextBlock rootText;
    private readonly TextBlock viewTitle;
    private readonly ContentControl viewContent;
    private readonly Control explorerView;
    private readonly Control editorView;
    private readonly Control gitView;
    private readonly CheckBox showHiddenCheckBox;
    private readonly TreeView explorerTree;
    private readonly TextBlock explorerStatusText;
    private readonly TextBlock editorPathText;
    private readonly TextBlock editorStatusText;
    private readonly TextBox editorText;
    private readonly Button saveEditorButton;
    private readonly Button reloadEditorButton;
    private readonly Button closeEditorButton;
    private readonly TextBlock gitStatusText;
    private readonly ListBox stagedList;
    private readonly ListBox unstagedList;
    private readonly ListBox untrackedList;
    private readonly TextBox diffText;
    private readonly TextBox commitMessageText;
    private readonly Button stageButton;
    private readonly Button unstageButton;
    private readonly Button commitButton;
    private readonly Button fetchButton;
    private readonly Button pullButton;
    private readonly Button pushButton;
    private readonly DispatcherTimer watcherRefreshTimer;

    private int activeSection = ExplorerSection;
    private string? workspaceRoot;
    private FileSystemWatcher? rootWatcher;
    private TextEditorDocument? currentDocument;
    private GitRepositoryStatus? currentGitStatus;
    private bool suppressEditorDirtyTracking;
    private bool isEditorDirty;
    private bool suppressGitSelectionSync;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkbenchHost"/> class.
    /// </summary>
    /// <param name="terminalContent">The terminal tab content to host.</param>
    public WorkbenchHost(Control terminalContent)
    {
        ArgumentNullException.ThrowIfNull(terminalContent);

        var root = new Grid();
        this.sidebarColumn = new ColumnDefinition(0, GridUnitType.Pixel);
        root.ColumnDefinitions.Add(this.sidebarColumn);
        root.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        this.rootText = new TextBlock
        {
            Text = "No terminal cwd yet",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
        };

        this.viewTitle = new TextBlock { FontWeight = FontWeight.Bold };
        this.viewContent = new ContentControl();
        this.explorerStatusText = this.CreateMutedTextBlock();
        this.explorerTree = new TreeView { MinHeight = 260 };
        this.showHiddenCheckBox = new CheckBox { Content = "Show hidden files" };
        this.showHiddenCheckBox.IsCheckedChanged += (_, _) => this.RefreshExplorerRoot();
        this.editorPathText = this.CreateMutedTextBlock();
        this.editorStatusText = this.CreateMutedTextBlock();
        this.editorText = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            IsReadOnly = true,
            MinHeight = 260,
            FontFamily = FontFamily.Parse("monospace"),
        };
        this.editorText.TextChanged += (_, _) => this.MarkEditorDirty();
        this.saveEditorButton = new Button { Content = "Save", IsEnabled = false };
        this.saveEditorButton.Click += (_, _) => this.SaveEditorDocument();
        this.reloadEditorButton = new Button { Content = "Reload", IsEnabled = false };
        this.reloadEditorButton.Click += (_, _) => this.ReloadEditorDocument();
        this.closeEditorButton = new Button { Content = "Close", IsEnabled = false };
        this.closeEditorButton.Click += (_, _) => this.CloseEditorDocument();
        this.gitStatusText = this.CreateMutedTextBlock();
        this.stagedList = this.CreateGitList();
        this.unstagedList = this.CreateGitList();
        this.untrackedList = this.CreateGitList();
        this.diffText = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            MinHeight = 160,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontFamily.Parse("monospace"),
        };
        this.commitMessageText = new TextBox { PlaceholderText = "Commit message" };
        this.stageButton = new Button { Content = "Stage", IsEnabled = false };
        this.stageButton.Click += async (_, _) => await this.StageSelectedAsync().ConfigureAwait(true);
        this.unstageButton = new Button { Content = "Unstage", IsEnabled = false };
        this.unstageButton.Click += async (_, _) => await this.UnstageSelectedAsync().ConfigureAwait(true);
        this.commitButton = new Button { Content = "Commit", IsEnabled = false };
        this.commitButton.Click += async (_, _) => await this.CommitAsync().ConfigureAwait(true);
        this.fetchButton = new Button { Content = "Fetch", IsEnabled = false };
        this.fetchButton.Click += async (_, _) => await this.RunGitAndRefreshAsync(this.gitService.FetchAsync).ConfigureAwait(true);
        this.pullButton = new Button { Content = "Pull", IsEnabled = false };
        this.pullButton.Click += async (_, _) => await this.RunGitAndRefreshAsync(this.gitService.PullAsync).ConfigureAwait(true);
        this.pushButton = new Button { Content = "Push", IsEnabled = false };
        this.pushButton.Click += async (_, _) => await this.RunGitAndRefreshAsync(this.gitService.PushAsync).ConfigureAwait(true);
        this.watcherRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        this.watcherRefreshTimer.Tick += (_, _) =>
        {
            this.watcherRefreshTimer.Stop();
            this.RefreshExplorerRoot();
        };
        this.explorerView = this.BuildExplorerView();
        this.editorView = this.BuildEditorView();
        this.gitView = this.BuildGitView();

        var buttons = new StackPanel { Spacing = 6 };
        buttons.Children.Add(this.BuildSectionButton("Explorer", ExplorerSection));
        buttons.Children.Add(this.BuildSectionButton("Editor", EditorSection));
        buttons.Children.Add(this.BuildSectionButton("Git", GitSection));

        var sidebarContent = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star),
            },
        };
        var sidebarHeader = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Workbench", FontWeight = FontWeight.Bold },
                this.rootText,
                buttons,
                this.viewTitle,
            },
        };
        Grid.SetRow(sidebarHeader, 0);
        Grid.SetRow(this.viewContent, 1);
        sidebarContent.Children.Add(sidebarHeader);
        sidebarContent.Children.Add(this.viewContent);

        this.sidebar = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
            Child = sidebarContent,
            IsVisible = false,
        };

        Grid.SetColumn(this.sidebar, 0);
        Grid.SetColumn(terminalContent, 1);
        root.Children.Add(this.sidebar);
        root.Children.Add(terminalContent);

        this.Content = root;
        this.ShowSection(ExplorerSection);
    }

    /// <summary>
    /// Gets a value indicating whether the Workbench sidebar is visible.
    /// </summary>
    public bool IsWorkbenchVisible => this.sidebar.IsVisible;

    /// <summary>
    /// Enables or disables the Workbench sidebar.
    /// </summary>
    /// <param name="enabled">Whether the Workbench experiment is enabled.</param>
    public void SetWorkbenchEnabled(bool enabled)
    {
        this.sidebar.IsVisible = enabled;
        this.sidebarColumn.Width = enabled
            ? new GridLength(SidebarWidth, GridUnitType.Pixel)
            : new GridLength(0, GridUnitType.Pixel);

        if (enabled)
        {
            this.ShowSection(this.activeSection);
        }
    }

    /// <summary>
    /// Updates the workspace root text shown in the sidebar.
    /// </summary>
    /// <param name="rootPath">The active terminal cwd, or <c>null</c> if unknown.</param>
    public void UpdateWorkspaceRoot(string? rootPath)
    {
        this.workspaceRoot = string.IsNullOrWhiteSpace(rootPath) ? null : rootPath;
        this.rootText.Text = string.IsNullOrWhiteSpace(rootPath)
            ? "No terminal cwd yet"
            : rootPath;
        this.ConfigureRootWatcher(this.workspaceRoot);

        if (this.activeSection == ExplorerSection)
        {
            this.RefreshExplorerRoot();
        }
        else if (this.activeSection == GitSection)
        {
            _ = this.RefreshGitAsync();
        }
    }

    /// <summary>
    /// Selects the Explorer view.
    /// </summary>
    public void ShowExplorer() => this.ShowSection(ExplorerSection);

    /// <summary>
    /// Selects the Editor view.
    /// </summary>
    public void ShowEditor() => this.ShowSection(EditorSection);

    /// <summary>
    /// Selects the Git view.
    /// </summary>
    public void ShowGit() => this.ShowSection(GitSection);

    /// <inheritdoc/>
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        this.DisposeRootWatcher();
    }

    private Button BuildSectionButton(string label, int section)
    {
        var button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => this.ShowSection(section);
        return button;
    }

    private void ShowSection(int section)
    {
        this.activeSection = section;
        switch (this.activeSection)
        {
            case EditorSection:
                this.viewTitle.Text = "Editor";
                this.SetViewContent(this.editorView);
                break;
            case GitSection:
                this.viewTitle.Text = "Git";
                this.SetViewContent(this.gitView);
                _ = this.RefreshGitAsync();
                break;
            default:
                this.viewTitle.Text = "Explorer";
                this.SetViewContent(this.explorerView);
                this.RefreshExplorerRoot();
                break;
        }
    }

    private void SetViewContent(Control view)
    {
        if (!ReferenceEquals(this.viewContent.Content, view))
        {
            this.viewContent.Content = view;
        }
    }

    private Control BuildExplorerView()
    {
        var refreshButton = new Button { Content = "Refresh" };
        refreshButton.Click += (_, _) => this.RefreshExplorerRoot();

        var openButton = new Button { Content = "Open" };
        openButton.Click += (_, _) => this.OpenSelectedExplorerEntry();

        var copyPathButton = new Button { Content = "Copy path" };
        copyPathButton.Click += async (_, _) => await this.CopySelectedExplorerPathAsync().ConfigureAwait(true);

        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new WrapPanel
                    {
                        Children =
                        {
                            refreshButton,
                            openButton,
                            copyPathButton,
                        },
                    },
                    this.showHiddenCheckBox,
                    this.explorerStatusText,
                    this.explorerTree,
                },
            },
        };
    }

    private Control BuildEditorView()
    {
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    this.editorPathText,
                    this.editorStatusText,
                    this.editorText,
                    new WrapPanel
                    {
                        Children =
                        {
                            this.saveEditorButton,
                            this.reloadEditorButton,
                            this.closeEditorButton,
                        },
                    },
                },
            },
        };
    }

    private Control BuildGitView()
    {
        var refreshButton = new Button { Content = "Refresh" };
        refreshButton.Click += async (_, _) => await this.RefreshGitAsync().ConfigureAwait(true);

        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    this.gitStatusText,
                    new WrapPanel
                    {
                        Children =
                        {
                            refreshButton,
                            this.fetchButton,
                            this.pullButton,
                            this.pushButton,
                        },
                    },
                    new TextBlock { Text = "Staged", FontWeight = FontWeight.Bold },
                    this.stagedList,
                    new TextBlock { Text = "Changes", FontWeight = FontWeight.Bold },
                    this.unstagedList,
                    new TextBlock { Text = "Untracked", FontWeight = FontWeight.Bold },
                    this.untrackedList,
                    new WrapPanel
                    {
                        Children =
                        {
                            this.stageButton,
                            this.unstageButton,
                        },
                    },
                    new TextBlock { Text = "Diff", FontWeight = FontWeight.Bold },
                    this.diffText,
                    this.commitMessageText,
                    this.commitButton,
                },
            },
        };
    }

    private TextBlock CreateMutedTextBlock()
    {
        return new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brushes.Gray,
        };
    }

    private ListBox CreateGitList()
    {
        var list = new ListBox { MinHeight = 72 };
        list.SelectionChanged += async (_, _) =>
        {
            this.OnGitSelectionChanged(list);
            await this.UpdateDiffForSelectionAsync().ConfigureAwait(true);
        };
        return list;
    }

    private void RefreshExplorerRoot()
    {
        var listing = this.fileExplorerService.EnumerateDirectory(
            this.workspaceRoot,
            this.showHiddenCheckBox.IsChecked == true);

        this.explorerStatusText.Text = listing.ErrorMessage ??
            $"{listing.Entries.Count} item(s) in {listing.DirectoryPath}";
        this.explorerTree.ItemsSource = listing.Entries.Select(this.BuildExplorerItem).ToArray();
    }

    private TreeViewItem BuildExplorerItem(FileExplorerEntry entry)
    {
        var item = new TreeViewItem
        {
            Header = entry.IsDirectory ? $"{entry.Name}/" : entry.Name,
            Tag = entry,
        };

        item.DoubleTapped += (_, _) => this.OpenExplorerEntry(entry, item);
        if (entry.IsDirectory)
        {
            item.ItemsSource = new[] { new TextBlock { Text = "Loading..." } };
            item.Expanded += (_, _) => this.LoadExplorerChildren(item, entry);
        }

        return item;
    }

    private void LoadExplorerChildren(TreeViewItem item, FileExplorerEntry entry)
    {
        if (item.ItemsSource is TreeViewItem[])
        {
            return;
        }

        var listing = this.fileExplorerService.EnumerateDirectory(
            entry.FullPath,
            this.showHiddenCheckBox.IsChecked == true);
        if (listing.ErrorMessage is not null)
        {
            item.ItemsSource = new[] { new TextBlock { Text = listing.ErrorMessage } };
            return;
        }

        item.ItemsSource = listing.Entries.Select(this.BuildExplorerItem).ToArray();
    }

    private void OpenSelectedExplorerEntry()
    {
        if (this.explorerTree.SelectedItem is TreeViewItem { Tag: FileExplorerEntry entry } item)
        {
            this.OpenExplorerEntry(entry, item);
        }
    }

    private void OpenExplorerEntry(FileExplorerEntry entry, TreeViewItem item)
    {
        if (entry.IsDirectory)
        {
            item.IsExpanded = true;
            this.LoadExplorerChildren(item, entry);
            return;
        }

        this.OpenEditorFile(entry.FullPath);
    }

    private async Task CopySelectedExplorerPathAsync()
    {
        if (this.explorerTree.SelectedItem is not TreeViewItem { Tag: FileExplorerEntry entry })
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            this.explorerStatusText.Text = "Clipboard is not available.";
            return;
        }

        await clipboard.SetTextAsync(entry.FullPath).ConfigureAwait(true);
        this.explorerStatusText.Text = $"Copied {entry.FullPath}";
    }

    private void OpenEditorFile(string path)
    {
        var result = this.textEditorService.Open(path);
        this.ShowEditor();
        if (result.Document is null)
        {
            this.editorStatusText.Text = result.ErrorMessage ?? "Unable to open file.";
            return;
        }

        this.currentDocument = result.Document;
        this.suppressEditorDirtyTracking = true;
        this.editorText.Text = result.Document.Text;
        this.suppressEditorDirtyTracking = false;
        this.editorText.IsReadOnly = false;
        this.editorPathText.Text = result.Document.Path;
        this.isEditorDirty = false;
        this.UpdateEditorControls("Opened.");
    }

    private void MarkEditorDirty()
    {
        if (this.suppressEditorDirtyTracking || this.currentDocument is null)
        {
            return;
        }

        this.isEditorDirty = true;
        this.UpdateEditorControls("Unsaved changes.");
    }

    private void SaveEditorDocument()
    {
        if (this.currentDocument is null)
        {
            return;
        }

        var result = this.textEditorService.Save(this.currentDocument, this.editorText.Text ?? string.Empty);
        if (result.Document is null)
        {
            this.editorStatusText.Text = result.ErrorMessage ?? "Unable to save file.";
            return;
        }

        this.currentDocument = result.Document;
        this.isEditorDirty = false;
        this.UpdateEditorControls("Saved.");
    }

    private void ReloadEditorDocument()
    {
        if (this.currentDocument is null)
        {
            return;
        }

        this.OpenEditorFile(this.currentDocument.Path);
    }

    private void CloseEditorDocument()
    {
        this.currentDocument = null;
        this.isEditorDirty = false;
        this.suppressEditorDirtyTracking = true;
        this.editorText.Text = string.Empty;
        this.suppressEditorDirtyTracking = false;
        this.editorText.IsReadOnly = true;
        this.editorPathText.Text = "No file open.";
        this.UpdateEditorControls("Open a file from Explorer.");
    }

    private void UpdateEditorControls(string status)
    {
        this.saveEditorButton.IsEnabled = this.currentDocument is not null && this.isEditorDirty;
        this.reloadEditorButton.IsEnabled = this.currentDocument is not null;
        this.closeEditorButton.IsEnabled = this.currentDocument is not null;
        this.editorStatusText.Text = status;
    }

    private async Task RefreshGitAsync()
    {
        var root = this.workspaceRoot;
        this.gitStatusText.Text = "Loading Git status...";
        var status = await this.gitService.GetStatusAsync(root).ConfigureAwait(true);
        if (root != this.workspaceRoot)
        {
            return;
        }

        this.currentGitStatus = status;
        var hasRepository = status.IsRepository;
        this.stageButton.IsEnabled = hasRepository;
        this.unstageButton.IsEnabled = hasRepository;
        this.commitButton.IsEnabled = hasRepository;
        this.fetchButton.IsEnabled = hasRepository;
        this.pullButton.IsEnabled = hasRepository;
        this.pushButton.IsEnabled = hasRepository;
        this.stagedList.ItemsSource = status.Staged;
        this.unstagedList.ItemsSource = status.Unstaged;
        this.untrackedList.ItemsSource = status.Untracked;
        this.diffText.Text = string.Empty;

        if (!hasRepository)
        {
            this.gitStatusText.Text = status.ErrorMessage ?? "Not a Git repository.";
            return;
        }

        var upstream = string.IsNullOrWhiteSpace(status.Upstream) ? string.Empty : $" -> {status.Upstream}";
        var sync = status.Ahead == 0 && status.Behind == 0
            ? string.Empty
            : $" (+{status.Ahead}/-{status.Behind})";
        this.gitStatusText.Text =
            $"{status.Branch ?? "(detached)"}{upstream}{sync}\n{status.RepositoryRoot}";
    }

    private async Task UpdateDiffForSelectionAsync()
    {
        if (this.currentGitStatus?.RepositoryRoot is not { } root)
        {
            return;
        }

        var status = this.GetSelectedGitStatus();
        if (status is null)
        {
            return;
        }

        if (status.Bucket == GitStatusBucket.Untracked)
        {
            this.diffText.Text = "Untracked file. Stage it to inspect the staged diff.";
            return;
        }

        var diff = await this.gitService.GetDiffAsync(root, status).ConfigureAwait(true);
        this.diffText.Text = diff.Succeeded ? diff.Output : diff.ErrorMessage;
    }

    private GitFileStatus? GetSelectedGitStatus()
    {
        return this.stagedList.SelectedItem as GitFileStatus ??
            this.unstagedList.SelectedItem as GitFileStatus ??
            this.untrackedList.SelectedItem as GitFileStatus;
    }

    private void OnGitSelectionChanged(ListBox selectedList)
    {
        if (this.suppressGitSelectionSync)
        {
            return;
        }

        this.suppressGitSelectionSync = true;
        if (!ReferenceEquals(selectedList, this.stagedList))
        {
            this.stagedList.SelectedItem = null;
        }

        if (!ReferenceEquals(selectedList, this.unstagedList))
        {
            this.unstagedList.SelectedItem = null;
        }

        if (!ReferenceEquals(selectedList, this.untrackedList))
        {
            this.untrackedList.SelectedItem = null;
        }

        this.suppressGitSelectionSync = false;
    }

    private async Task StageSelectedAsync()
    {
        if (this.currentGitStatus?.RepositoryRoot is not { } root)
        {
            return;
        }

        var status = this.unstagedList.SelectedItem as GitFileStatus ??
            this.untrackedList.SelectedItem as GitFileStatus;
        if (status is null)
        {
            this.gitStatusText.Text = "Select an unstaged or untracked path to stage.";
            return;
        }

        await this.RunGitPathActionAndRefreshAsync(root, status.Path, this.gitService.StageAsync).ConfigureAwait(true);
    }

    private async Task UnstageSelectedAsync()
    {
        if (this.currentGitStatus?.RepositoryRoot is not { } root ||
            this.stagedList.SelectedItem is not GitFileStatus status)
        {
            this.gitStatusText.Text = "Select a staged path to unstage.";
            return;
        }

        await this.RunGitPathActionAndRefreshAsync(root, status.Path, this.gitService.UnstageAsync).ConfigureAwait(true);
    }

    private async Task CommitAsync()
    {
        if (this.currentGitStatus?.RepositoryRoot is not { } root)
        {
            return;
        }

        var message = this.commitMessageText.Text;
        if (string.IsNullOrWhiteSpace(message))
        {
            this.gitStatusText.Text = "Enter a commit message first.";
            return;
        }

        var result = await this.gitService.CommitAsync(root, message).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            this.gitStatusText.Text = result.ErrorMessage;
            return;
        }

        this.commitMessageText.Text = string.Empty;
        await this.RefreshGitAsync().ConfigureAwait(true);
    }

    private async Task RunGitAndRefreshAsync(Func<string, Task<GitCommandResult>> action)
    {
        if (this.currentGitStatus?.RepositoryRoot is not { } root)
        {
            return;
        }

        var result = await action(root).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            this.gitStatusText.Text = result.ErrorMessage;
            return;
        }

        await this.RefreshGitAsync().ConfigureAwait(true);
    }

    private async Task RunGitPathActionAndRefreshAsync(
        string root,
        string path,
        Func<string, string, Task<GitCommandResult>> action)
    {
        var result = await action(root, path).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            this.gitStatusText.Text = result.ErrorMessage;
            return;
        }

        await this.RefreshGitAsync().ConfigureAwait(true);
    }

    private void ConfigureRootWatcher(string? rootPath)
    {
        this.DisposeRootWatcher();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        try
        {
            this.rootWatcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            this.rootWatcher.Created += this.OnRootDirectoryChanged;
            this.rootWatcher.Deleted += this.OnRootDirectoryChanged;
            this.rootWatcher.Renamed += this.OnRootDirectoryChanged;
            this.rootWatcher.Changed += this.OnRootDirectoryChanged;
            this.rootWatcher.Error += (_, args) =>
                Dispatcher.UIThread.Post(() => this.explorerStatusText.Text = args.GetException().Message);
        }
        catch (ArgumentException ex)
        {
            this.explorerStatusText.Text = ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            this.explorerStatusText.Text = ex.Message;
        }
        catch (IOException ex)
        {
            this.explorerStatusText.Text = ex.Message;
        }
    }

    private void OnRootDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.activeSection != ExplorerSection)
            {
                return;
            }

            this.watcherRefreshTimer.Stop();
            this.watcherRefreshTimer.Start();
        });
    }

    private void DisposeRootWatcher()
    {
        this.watcherRefreshTimer.Stop();
        if (this.rootWatcher is null)
        {
            return;
        }

        this.rootWatcher.EnableRaisingEvents = false;
        this.rootWatcher.Dispose();
        this.rootWatcher = null;
    }
}
