// <copyright file="GitDiffPane.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
    private static readonly IBrush GutterBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80));
    private static readonly FontFamily MonoFont = FontFamily.Parse("monospace");

    private readonly GitService gitService = new();
    private readonly Func<string?> workingDirectoryProvider;
    private readonly TextBlock cwdText;
    private readonly TextBlock statusText;
    private readonly ListBox changesList;
    private readonly TextBlock diffHeader;
    private readonly TextBlock oldHeader;
    private readonly TextBlock newHeader;
    private readonly TextEditor oldEditor;
    private readonly TextEditor newEditor;
    private readonly HighlightColorizer oldColorizer = new();
    private readonly HighlightColorizer newColorizer = new();
    private readonly Panel diffPlaceholder;
    private readonly TextBlock diffPlaceholderText;
    private readonly Grid comparisonGrid;

    private GitRepositoryStatus? currentStatus;
    private int refreshToken;
    private bool suppressEditorScrollSync;

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
        this.oldHeader = this.BuildSideHeader();
        this.newHeader = this.BuildSideHeader();
        this.oldEditor = this.BuildEditor(this.oldColorizer);
        this.newEditor = this.BuildEditor(this.newColorizer);
        this.oldEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => this.SyncScrollOffset(this.oldEditor, this.newEditor);
        this.newEditor.TextArea.TextView.ScrollOffsetChanged += (_, _) => this.SyncScrollOffset(this.newEditor, this.oldEditor);
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
        var items = BuildChangeItems(status);
        this.changesList.ItemsSource = items;
        if (items.Count > 0)
        {
            this.changesList.SelectedIndex = 0;
            await this.UpdateDiffAsync().ConfigureAwait(true);
        }
        else
        {
            this.ShowDiffPlaceholder("Select a change to view its diff.");
        }

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

    private TextEditor BuildEditor(HighlightColorizer colorizer)
    {
        var editor = new TextEditor
        {
            Document = new TextDocument(string.Empty),
            IsReadOnly = true,
            ShowLineNumbers = true,
            WordWrap = false,
            FontFamily = MonoFont,
            FontSize = 12,
            Background = Brushes.Transparent,
            Foreground = Brushes.Black,
            LineNumbersForeground = GutterBrush,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        editor.TextArea.Background = Brushes.Transparent;
        editor.TextArea.Foreground = Brushes.Black;
        editor.TextArea.Caret.CaretBrush = Brushes.Black;
        editor.TextArea.TextView.LineTransformers.Add(colorizer);
        return editor;
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

        var token = this.refreshToken;
        this.ShowDiffPlaceholder("Loading full-file diff...");
        var comparison = await this.gitService.GetFileComparisonAsync(root, item.Status).ConfigureAwait(true);
        if (token != this.refreshToken || !ReferenceEquals(this.changesList.SelectedItem, item))
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
        this.oldHeader.Text = $"Old: {oldSide.SourceLabel}";
        this.newHeader.Text = $"New: {newSide.SourceLabel}";
        this.SetEditorContent(this.oldEditor, this.oldColorizer, oldSide);
        this.SetEditorContent(this.newEditor, this.newColorizer, newSide);
        this.diffPlaceholder.IsVisible = false;
        this.comparisonGrid.IsVisible = true;
    }

    private void SetEditorContent(TextEditor editor, HighlightColorizer colorizer, GitFileSideContent side)
    {
        editor.Text = side.Text;
        colorizer.SetHighlights(side.Highlights);
        editor.TextArea.TextView.Redraw();
    }

    private void SyncScrollOffset(TextEditor source, TextEditor target)
    {
        if (this.suppressEditorScrollSync)
        {
            return;
        }

        this.suppressEditorScrollSync = true;
        var offset = source.TextArea.TextView.ScrollOffset;
        target.ScrollToHorizontalOffset(offset.X);
        target.ScrollToVerticalOffset(offset.Y);
        this.suppressEditorScrollSync = false;
    }

    private void ShowDiffPlaceholder(string message)
    {
        this.ClearEditors();
        this.diffPlaceholderText.Text = message;
        this.diffPlaceholder.IsVisible = true;
        this.comparisonGrid.IsVisible = false;
    }

    private void ClearEditors()
    {
        this.oldHeader.Text = string.Empty;
        this.newHeader.Text = string.Empty;
        this.SetEditorContent(this.oldEditor, this.oldColorizer, this.EmptySide());
        this.SetEditorContent(this.newEditor, this.newColorizer, this.EmptySide());
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

        var divider = new Border
        {
            Width = 1,
            Background = GutterBrush,
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
        return new GitFileSideContent(string.Empty, string.Empty, Array.Empty<GitDiffHighlightRange>());
    }

    private sealed class HighlightColorizer : DocumentColorizingTransformer
    {
        private IReadOnlyList<GitDiffHighlightRange> highlights = Array.Empty<GitDiffHighlightRange>();

        public void SetHighlights(IReadOnlyList<GitDiffHighlightRange> ranges)
        {
            this.highlights = ranges;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var brush = this.GetBrush(line.LineNumber);
            if (brush is null)
            {
                return;
            }

            this.ChangeLinePart(
                line.Offset,
                line.EndOffset,
                element => element.TextRunProperties.SetBackgroundBrush(brush));
        }

        private IBrush? GetBrush(int lineNumber)
        {
            foreach (var range in this.highlights)
            {
                if (lineNumber >= range.StartLine && lineNumber < range.StartLine + range.LineCount)
                {
                    return range.Kind switch
                    {
                        GitDiffHighlightKind.Added => AddedBrush,
                        GitDiffHighlightKind.Removed => RemovedBrush,
                        GitDiffHighlightKind.Modified => ModifiedBrush,
                        _ => null,
                    };
                }
            }

            return null;
        }
    }
}
