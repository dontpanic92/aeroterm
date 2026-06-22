// <copyright file="GitDiffPane.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

/// <summary>
/// A view-only Git pane that lists the repository's changes on the left and
/// renders the selected change as a side-by-side (old | new) diff on the right.
/// </summary>
internal sealed class GitDiffPane : UserControl
{
    private static readonly IBrush RemovedBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xE5, 0x39, 0x35));
    private static readonly IBrush AddedBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x43, 0xA0, 0x47));
    private static readonly IBrush GutterBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80));
    private static readonly FontFamily MonoFont = FontFamily.Parse("monospace");

    private readonly GitService gitService = new();
    private readonly Func<string?> workingDirectoryProvider;
    private readonly TextBlock cwdText;
    private readonly TextBlock statusText;
    private readonly ListBox changesList;
    private readonly TextBlock diffHeader;
    private readonly StackPanel oldColumn;
    private readonly StackPanel newColumn;
    private readonly Panel diffPlaceholder;
    private readonly TextBlock diffPlaceholderText;
    private readonly Grid diffGrid;

    private GitRepositoryStatus? currentStatus;
    private int refreshToken;

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

        this.changesList = new ListBox();
        this.changesList.SelectionChanged += async (_, _) => await this.UpdateDiffAsync().ConfigureAwait(true);

        var leftPanel = new Grid
        {
            Margin = new Thickness(10),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star),
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
        Grid.SetRow(leftHeader, 0);
        Grid.SetRow(this.changesList, 2);
        leftPanel.Children.Add(leftHeader);
        leftPanel.Children.Add(this.changesList);

        this.diffHeader = new TextBlock
        {
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(10, 10, 10, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        this.oldColumn = new StackPanel();
        this.newColumn = new StackPanel();
        this.diffGrid = this.BuildDiffGrid();
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
            Children = { this.diffGrid, this.diffPlaceholder },
        };

        var rightPanel = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star),
            },
        };
        Grid.SetRow(this.diffHeader, 0);
        Grid.SetRow(diffBody, 1);
        rightPanel.Children.Add(this.diffHeader);
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
        this.changesList.ItemsSource = BuildChangeItems(status);
        this.ShowDiffPlaceholder("Select a change to view its diff.");

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

        if (this.changesList.ItemsSource is IReadOnlyList<GitChangeItem> { Count: 0 })
        {
            this.statusText.Text += "\nWorking tree clean.";
        }
    }

    private static IReadOnlyList<GitChangeItem> BuildChangeItems(GitRepositoryStatus status)
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

    private static Control BuildLine(int? lineNumber, string? text, IBrush background)
    {
        var gutter = new TextBlock
        {
            Text = lineNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            Width = 44,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0),
            FontFamily = MonoFont,
            FontSize = 12,
            Foreground = GutterBrush,
        };
        var content = new TextBlock
        {
            Text = text ?? string.Empty,
            FontFamily = MonoFont,
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
        };
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { gutter, content },
        };
        return new Border
        {
            Background = background,
            Padding = new Thickness(4, 0, 4, 0),
            Child = line,
        };
    }

    private async Task UpdateDiffAsync()
    {
        if (this.changesList.SelectedItem is not GitChangeItem item)
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

        if (item.Status.Bucket == GitStatusBucket.Untracked)
        {
            this.ShowDiffPlaceholder("Untracked file. Stage it to inspect the staged diff.");
            return;
        }

        var token = this.refreshToken;
        var diff = await this.gitService.GetDiffAsync(root, item.Status).ConfigureAwait(true);
        if (token != this.refreshToken || !ReferenceEquals(this.changesList.SelectedItem, item))
        {
            return;
        }

        if (!diff.Succeeded)
        {
            this.ShowDiffPlaceholder(string.IsNullOrWhiteSpace(diff.ErrorMessage)
                ? "Unable to load diff."
                : diff.ErrorMessage);
            return;
        }

        var files = GitDiffParser.Parse(diff.Output);
        var rows = files.SelectMany(f => f.Rows).ToArray();
        if (files.Any(f => f.IsBinary))
        {
            this.ShowDiffPlaceholder("Binary file. No textual diff to display.");
            return;
        }

        if (rows.Length == 0)
        {
            this.ShowDiffPlaceholder("No changes to display.");
            return;
        }

        this.RenderRows(rows);
    }

    private void RenderRows(IReadOnlyList<GitDiffRow> rows)
    {
        this.oldColumn.Children.Clear();
        this.newColumn.Children.Clear();

        foreach (var row in rows)
        {
            this.oldColumn.Children.Add(BuildLine(
                row.OldLineNumber,
                row.OldText,
                row.Kind == GitDiffRowKind.Removed ? RemovedBrush : Brushes.Transparent));
            this.newColumn.Children.Add(BuildLine(
                row.NewLineNumber,
                row.NewText,
                row.Kind == GitDiffRowKind.Added ? AddedBrush : Brushes.Transparent));
        }

        this.diffPlaceholder.IsVisible = false;
        this.diffGrid.IsVisible = true;
    }

    private void ShowDiffPlaceholder(string message)
    {
        this.oldColumn.Children.Clear();
        this.newColumn.Children.Clear();
        this.diffPlaceholderText.Text = message;
        this.diffPlaceholder.IsVisible = true;
        this.diffGrid.IsVisible = false;
    }

    private Grid BuildDiffGrid()
    {
        var oldScroll = new ScrollViewer
        {
            Content = this.oldColumn,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var newScroll = new ScrollViewer
        {
            Content = this.newColumn,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var columns = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(1, GridUnitType.Star),
            },
        };
        var divider = new Border
        {
            Width = 1,
            Background = GutterBrush,
        };
        Grid.SetColumn(oldScroll, 0);
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(newScroll, 2);
        columns.Children.Add(oldScroll);
        columns.Children.Add(divider);
        columns.Children.Add(newScroll);

        var grid = new Grid();
        grid.Children.Add(new ScrollViewer
        {
            Content = columns,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        return grid;
    }
}
