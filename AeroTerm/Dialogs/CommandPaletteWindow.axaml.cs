// <copyright file="CommandPaletteWindow.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Dialogs;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AeroTerm.Models;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

/// <summary>
/// Borderless translucent Cmd/Ctrl+Shift+P command palette.
/// </summary>
/// <remarks>
/// <para>Anchors itself 25% from the top of the owner window and
/// auto-resizes its result list (up to a 480px cap). Closes on
/// <see cref="InputElement.LostFocus"/>, <see cref="Key.Escape"/>, and
/// successful command execution.</para>
/// <para>Filtering uses <see cref="FuzzyMatcher"/>; a short
/// <see cref="DispatcherTimer"/> debounce keeps typing cheap even when
/// the command list is large. Execution and MRU persistence are
/// handled by the owner — the palette itself is pure UI.</para>
/// </remarks>
internal partial class CommandPaletteWindow : Window
{
    private const int MaxResults = 50;
    private const double MaxResultsHeight = 480;

    private readonly IPaletteHost host;
    private readonly PaletteMruStore mru;
    private readonly IReadOnlyList<PaletteCommand> allCommands;
    private readonly ObservableCollection<PaletteRowViewModel> rows = new();
    private readonly DispatcherTimer debounce;

    private TextBox? queryBox;
    private ListBox? results;
    private bool executing;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandPaletteWindow"/> class.
    /// </summary>
    public CommandPaletteWindow()
        : this(
            host: new NullPaletteHost(),
            mru: new PaletteMruStore(),
            commands: Array.Empty<PaletteCommand>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandPaletteWindow"/> class.
    /// </summary>
    /// <param name="host">The palette host (usually <c>MainWindow</c>).</param>
    /// <param name="mru">The MRU store for ordering and persistence.</param>
    /// <param name="commands">The snapshot of commands to show.</param>
    public CommandPaletteWindow(IPaletteHost host, PaletteMruStore mru, IReadOnlyList<PaletteCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mru);
        ArgumentNullException.ThrowIfNull(commands);
        this.host = host;
        this.mru = mru;
        this.allCommands = commands;

        this.InitializeComponent();

        this.queryBox = this.FindControl<TextBox>("QueryTextBox");
        this.results = this.FindControl<ListBox>("Results");

        if (this.queryBox is not null)
        {
            this.queryBox.TextChanged += this.OnQueryTextChanged;
            this.queryBox.KeyDown += this.OnQueryKeyDown;
        }

        if (this.results is not null)
        {
            this.results.ItemsSource = this.rows;
            this.results.DoubleTapped += this.OnResultsDoubleTapped;
            this.results.ContainerPrepared += this.OnResultContainerPrepared;
        }

        this.debounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30),
        };
        this.debounce.Tick += this.OnDebounceTick;

        this.Deactivated += (_, _) =>
        {
            if (!this.executing)
            {
                this.Close();
            }
        };

        this.Opened += (_, _) =>
        {
            this.RebuildRows(string.Empty);
            this.queryBox?.Focus();
        };
    }

    /// <summary>
    /// Positions the palette near the top-centre of <paramref name="owner"/>
    /// and shows it non-modally. Caller remains responsible for the
    /// owner's focus after the palette closes.
    /// </summary>
    /// <param name="owner">The owning window.</param>
    public void ShowForOwner(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var ownerBounds = owner.Bounds;
        var ownerPos = owner.Position;

        double width = this.Width;
        double leftLocal = (ownerBounds.Width - width) / 2.0;
        double top = ownerBounds.Height * 0.25;

        this.WindowStartupLocation = WindowStartupLocation.Manual;
        this.Position = new PixelPoint(
            ownerPos.X + (int)Math.Max(0, leftLocal),
            ownerPos.Y + (int)Math.Max(0, top));

        this.Show(owner);
        this.Activate();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnQueryTextChanged(object? sender, TextChangedEventArgs e)
    {
        this.debounce.Stop();
        this.debounce.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        this.debounce.Stop();
        this.RebuildRows(this.queryBox?.Text ?? string.Empty);
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            this.MoveSelection(+1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            this.MoveSelection(-1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            _ = this.ExecuteSelectedAsync();
            e.Handled = true;
        }
    }

    private void OnResultsDoubleTapped(object? sender, RoutedEventArgs e)
    {
        _ = this.ExecuteSelectedAsync();
    }

    private void OnResultContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ListBoxItem item
            && item.DataContext is PaletteRowViewModel vm)
        {
            // The template is not necessarily fully applied yet — defer
            // to the dispatcher so the visual tree has settled.
            Dispatcher.UIThread.Post(() => this.ApplyTitleInlines(item, vm));
        }
    }

    private void ApplyTitleInlines(ListBoxItem item, PaletteRowViewModel vm)
    {
        TextBlock? titleBlock = null;
        foreach (var tb in item.GetVisualDescendants().OfType<TextBlock>())
        {
            if (tb.Name == "TitleBlock")
            {
                titleBlock = tb;
                break;
            }
        }

        if (titleBlock is null)
        {
            return;
        }

        // Only mutate if the container still matches this palette's row
        // set (defensive against rapid debounce rebuilds).
        if (!this.rows.Contains(vm))
        {
            return;
        }

        titleBlock.Inlines?.Clear();
        foreach (var run in vm.CreateTitleInlines())
        {
            titleBlock.Inlines?.Add(run);
        }
    }

    private void MoveSelection(int delta)
    {
        if (this.results is null || this.rows.Count == 0)
        {
            return;
        }

        int current = this.results.SelectedIndex;
        int next = current + delta;
        if (next < 0)
        {
            next = 0;
        }

        if (next >= this.rows.Count)
        {
            next = this.rows.Count - 1;
        }

        this.results.SelectedIndex = next;
        this.results.ScrollIntoView(this.rows[next]);
    }

    private async Task ExecuteSelectedAsync()
    {
        if (this.results is null || this.rows.Count == 0)
        {
            return;
        }

        int idx = this.results.SelectedIndex;
        if (idx < 0)
        {
            idx = 0;
        }

        var row = this.rows[idx];
        this.executing = true;
        this.Hide();
        try
        {
            await row.Command.Execute().ConfigureAwait(true);
            this.mru.Record(row.Command.Id);
        }
        finally
        {
            this.Close();
        }
    }

    private void RebuildRows(string query)
    {
        var matches = new List<(PaletteCommand Cmd, FuzzyMatcher.Match Match)>();
        foreach (var cmd in this.allCommands)
        {
            var m = FuzzyMatcher.Score(query, cmd.Title);
            if (m is null)
            {
                continue;
            }

            matches.Add((cmd, m.Value));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            // Empty query: MRU first (in recorded order), then declaration order for the rest.
            matches.Sort((a, b) =>
            {
                int ra = this.mru.RankOf(a.Cmd.Id);
                int rb = this.mru.RankOf(b.Cmd.Id);
                return ra.CompareTo(rb);
            });
        }
        else
        {
            matches.Sort((a, b) =>
            {
                int byScore = a.Match.Score.CompareTo(b.Match.Score);
                if (byScore != 0)
                {
                    return byScore;
                }

                return this.mru.RankOf(a.Cmd.Id).CompareTo(this.mru.RankOf(b.Cmd.Id));
            });
        }

        this.rows.Clear();
        int count = Math.Min(matches.Count, MaxResults);
        for (int i = 0; i < count; i++)
        {
            var (cmd, match) = matches[i];
            this.rows.Add(new PaletteRowViewModel(cmd, match.Positions));
        }

        if (this.results is not null && this.rows.Count > 0)
        {
            this.results.SelectedIndex = 0;
        }

        // Grow to fit content up to MaxResultsHeight.
        this.MaxHeight = MaxResultsHeight;
    }

    private sealed class NullPaletteHost : IPaletteHost
    {
        public IReadOnlyList<string> TabTitles => Array.Empty<string>();

        public int ActiveTabIndex => 0;

        public AppSettings Settings => AppSettings.Default;

        public IReadOnlyList<TabGroup> TabGroups => Array.Empty<TabGroup>();

        public void NewTab()
        {
        }

        public void NewTabFromProfile(Profile profile)
        {
        }

        public void CloseActiveTab()
        {
        }

        public void DuplicateActiveTab()
        {
        }

        public void ActivateNextTab()
        {
        }

        public void ActivatePreviousTab()
        {
        }

        public void ActivateTabByIndex(int index)
        {
        }

        public void MoveActiveTabLeft()
        {
        }

        public void MoveActiveTabRight()
        {
        }

        public void OpenSettings()
        {
        }

        public void NewWindow()
        {
        }

        public void CloseHostWindow()
        {
        }

        public void ReloadKeybindings()
        {
        }

        public void CreateGroupFromActiveTab()
        {
        }

        public void AssignActiveTabToGroup(string groupId)
        {
        }

        public void UngroupActiveTab()
        {
        }
    }
}
