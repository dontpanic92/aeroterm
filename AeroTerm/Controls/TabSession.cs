// <copyright file="TabSession.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroTerm.Controls.Panes;
using AeroTerm.Models;
using AeroTerm.Services;
using Avalonia.Controls;

/// <summary>
/// Owns a tab's pane split tree. The tree starts as a single leaf (one
/// <see cref="TerminalSessionCoordinator"/> + one <see cref="TerminalControl"/>)
/// and can be split horizontally or vertically via
/// <see cref="SplitActivePane"/>. Every leaf keeps its visual host
/// attached to the tab's <see cref="PaneTreeView"/> for the tab's
/// entire lifetime so PTY readers and renderers keep running for
/// background tabs.
/// </summary>
public sealed class TabSession : INotifyPropertyChanged, IDisposable
{
    private readonly PaneTree tree;
    private readonly PaneTreeView view;
    private readonly AppSettings? settings;
    private readonly Dictionary<ITabSessionContent, PaneHandlers> perPane = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ITabSessionContent> startedContents = new(ReferenceEqualityComparer.Instance);
    private string title;
    private string? groupId;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabSession"/> class
    /// from application settings — creates a fresh
    /// <see cref="TerminalSessionCoordinator"/> using those settings.
    /// </summary>
    /// <param name="settings">Application settings that drive the shell's
    /// font / color scheme / scrollback configuration.</param>
    public TabSession(AppSettings settings)
        : this(CoordinatorTabContent.FromCoordinator(new TerminalSessionCoordinator(settings), settings), settings)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TabSession"/> class
    /// configured from a <see cref="Profile"/>. The profile's launch
    /// fields are merged with <paramref name="fallback"/> (profile wins
    /// where set). Profile appearance overrides are applied to the
    /// resulting <see cref="TerminalControl"/> once it is created.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    /// <param name="profile">The profile driving launch + appearance.</param>
    /// <param name="fallback">Baseline launch spec used to fill gaps.
    /// When <c>null</c>, the coordinator's environment-derived defaults
    /// are used (shell from <c>SHELL</c>, cwd from user home, etc.).</param>
    internal TabSession(AppSettings settings, Profile profile, LaunchSpec? fallback)
        : this(BuildProfileContent(settings, profile, fallback), settings)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TabSession"/> class
    /// from a pre-built content adapter. Primary use case is tests
    /// substituting a fake coordinator seam.
    /// </summary>
    /// <param name="content">Adapter around the underlying terminal
    /// session. Ownership transfers — <see cref="Dispose"/> disposes it.</param>
    internal TabSession(ITabSessionContent content)
        : this(content, settings: null)
    {
    }

    private TabSession(ITabSessionContent content, AppSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(content);
        this.settings = settings;
        this.tree = new PaneTree(content);
        this.view = new PaneTreeView(this.tree, settings);
        this.title = string.IsNullOrEmpty(content.Title) ? "AeroTerm" : content.Title;

        this.WirePaneContent(content);

        this.tree.ActiveLeafChanged += this.OnActiveLeafChanged;
        this.tree.LeafAdded += this.OnLeafAdded;
        this.tree.LeafRemoving += this.OnLeafRemoving;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised when <see cref="Title"/> changes.
    /// </summary>
    public event Action<string>? TitleChanged;

    /// <summary>
    /// Raised when the shell process of a single-pane tab exits
    /// cleanly (or when the last pane of a multi-pane tab just exited
    /// via pane close). Subscribers typically close the tab.
    /// </summary>
    public event Action? ProcessExitedNormally;

    /// <summary>
    /// Raised after the pane layout changes — a split produced a new
    /// leaf or a close removed one. Window chrome that depends on
    /// single-vs-multi pane state can refresh in response.
    /// </summary>
    public event Action? PaneLayoutChanged;

    /// <summary>
    /// Raised when a new pane has just been added to this session's
    /// tree. The host typically wires per-coordinator plumbing (bell /
    /// bg-color). Internal because <see cref="ITabSessionContent"/>
    /// is internal.
    /// </summary>
    internal event Action<ITabSessionContent>? PaneAdded;

    /// <summary>
    /// Raised immediately before a pane is removed and disposed. The
    /// host unwires per-coordinator plumbing in response.
    /// </summary>
    internal event Action<ITabSessionContent>? PaneRemoving;

    /// <summary>
    /// Gets the current tab title. The title tracks the active pane
    /// — background panes never overwrite it. Change notification
    /// fires on both <see cref="PropertyChanged"/> and
    /// <see cref="TitleChanged"/>.
    /// </summary>
    public string Title
    {
        get => this.title;
        private set
        {
            if (this.title == value)
            {
                return;
            }

            this.title = value;
            this.OnPropertyChanged();
            this.TitleChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Gets the visual <see cref="Control"/> representing this tab's
    /// content (a <see cref="PaneTreeView"/>). The same instance is
    /// used for the tab's entire lifetime.
    /// </summary>
    public Control Control => this.view;

    /// <summary>
    /// Gets or sets the id of the <see cref="TabGroup"/> this tab is
    /// assigned to, or <c>null</c> when the tab is not grouped.
    /// </summary>
    public string? GroupId
    {
        get => this.groupId;
        set
        {
            var normalized = string.IsNullOrEmpty(value) ? null : value;
            if (this.groupId == normalized)
            {
                return;
            }

            this.groupId = normalized;
            this.OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the active pane's <see cref="TerminalControl"/>, once the
    /// pane has been started. May be <c>null</c> before <see cref="Start"/>
    /// or when the pane uses a test seam.
    /// </summary>
    public TerminalControl? Terminal => this.tree.ActiveLeaf.Content.Terminal;

    /// <summary>
    /// Gets a value indicating whether this session has been disposed.
    /// </summary>
    public bool IsDisposed => this.disposed;

    /// <summary>
    /// Gets the active pane's coordinator, or <c>null</c> when the
    /// session uses a test seam.
    /// </summary>
    internal TerminalSessionCoordinator? Coordinator => this.tree.ActiveLeaf.Content.Coordinator;

    /// <summary>
    /// Gets the number of panes currently in the split tree.
    /// </summary>
    internal int PaneCount
    {
        get
        {
            int n = 0;
            foreach (var unused in this.tree.EnumerateLeaves())
            {
                _ = unused;
                n++;
            }

            return n;
        }
    }

    /// <summary>
    /// Gets the underlying pane tree. Exposed for tests and for hosts
    /// that need to enumerate all panes (e.g. propagating
    /// <see cref="TerminalControl.BackgroundAlpha"/> to every pane).
    /// </summary>
    internal PaneTree PaneTree => this.tree;

    /// <summary>
    /// Gets an enumeration of every pane's content in the tab's split tree.
    /// </summary>
    internal IEnumerable<ITabSessionContent> AllContents
    {
        get
        {
            foreach (var leaf in this.tree.EnumerateLeaves())
            {
                yield return leaf.Content;
            }
        }
    }

    /// <summary>
    /// Starts every pane that has not yet been started. Must be called
    /// after the session's <see cref="Control"/> has been added to a
    /// visible, laid-out container so each PTY is allocated at correct
    /// dimensions.
    /// </summary>
    public void Start()
    {
        foreach (var leaf in this.tree.EnumerateLeaves())
        {
            if (this.startedContents.Add(leaf.Content))
            {
                leaf.Content.Start();
            }
        }
    }

    /// <summary>
    /// Moves keyboard focus to the active pane's terminal so typing
    /// goes to the right PTY.
    /// </summary>
    public void FocusInput() => this.tree.ActiveLeaf.Content.FocusInput();

    /// <summary>
    /// Creates a new <see cref="TabSession"/> that mirrors the active
    /// pane's launch configuration (shell, args, env, cwd). The
    /// returned session is not yet started — callers must add it to a
    /// <see cref="TabView"/>, force a layout pass, then invoke
    /// <see cref="Start"/>.
    /// </summary>
    /// <returns>The newly-constructed sibling session.</returns>
    public TabSession Duplicate()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(TabSession));
        }

        var activeContent = this.tree.ActiveLeaf.Content;
        var dupContent = activeContent.Duplicate();
        return new TabSession(dupContent, this.settings);
    }

    /// <summary>
    /// Splits the active pane into two. The new pane is created by
    /// duplicating the active pane's content (same shell / cwd /
    /// environment) and becomes the active pane. The caller is
    /// responsible for forcing a layout pass and then calling
    /// <see cref="Start"/> + <see cref="FocusInput"/> to launch the
    /// new shell.
    /// </summary>
    /// <param name="orientation">Divider orientation for the new split.</param>
    public void SplitActivePane(PaneOrientation orientation)
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(TabSession));
        }

        var activeContent = this.tree.ActiveLeaf.Content;
        var newContent = activeContent.Duplicate();
        this.tree.SplitActive(orientation, newContent);
    }

    /// <summary>
    /// Closes the active pane. Returns <see langword="true"/> when at
    /// least one pane survives; the tab stays open and focus moves to
    /// the nearest sibling. Returns <see langword="false"/> when the
    /// closed pane was the last one — the content is already disposed
    /// and the caller should remove the tab from its
    /// <see cref="TabView"/>.
    /// </summary>
    /// <returns><see langword="true"/> if a pane remains; otherwise
    /// <see langword="false"/>.</returns>
    public bool CloseActivePane()
    {
        if (this.disposed)
        {
            return false;
        }

        return this.tree.CloseActive();
    }

    /// <summary>
    /// Moves pane focus in the given direction.
    /// </summary>
    /// <param name="direction">The direction to move focus.</param>
    /// <returns><see langword="true"/> if focus actually moved.</returns>
    public bool FocusPaneDirection(PaneDirection direction)
    {
        if (this.disposed)
        {
            return false;
        }

        bool moved = this.tree.FocusDirection(direction);
        if (moved)
        {
            this.FocusInput();
        }

        return moved;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        foreach (var leaf in this.tree.EnumerateLeaves())
        {
            this.UnwirePaneContent(leaf.Content);
        }

        this.tree.ActiveLeafChanged -= this.OnActiveLeafChanged;
        this.tree.LeafAdded -= this.OnLeafAdded;
        this.tree.LeafRemoving -= this.OnLeafRemoving;
        this.tree.Dispose();
    }

    private static ITabSessionContent BuildProfileContent(AppSettings settings, Profile profile, LaunchSpec? fallback)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profile);

        bool hasLaunchOverrides = profile.Command is not null
            || profile.Args is not null
            || profile.WorkingDirectory is not null
            || (profile.EnvironmentOverrides is not null && profile.EnvironmentOverrides.Count > 0);

        if (fallback is null && !hasLaunchOverrides)
        {
            var plainCoord = new TerminalSessionCoordinator(settings);
            return CoordinatorTabContent.FromCoordinatorWithProfile(plainCoord, settings, profile);
        }

        var baseline = fallback ?? ProfileStore.BuildEnvironmentFallback();
        var merged = ProfileStore.BuildLaunchSpec(profile, baseline);

        var coordinator = new TerminalSessionCoordinator(settings, merged);
        return CoordinatorTabContent.FromCoordinatorWithProfile(coordinator, settings, profile);
    }

    private void WirePaneContent(ITabSessionContent content)
    {
        Action<string> titleHandler = t => this.OnPaneTitleChanged(content, t);
        Action exitHandler = () => this.OnPaneExited(content);
        content.TitleChanged += titleHandler;
        content.ProcessExitedNormally += exitHandler;
        this.perPane[content] = new PaneHandlers(titleHandler, exitHandler);
    }

    private void UnwirePaneContent(ITabSessionContent content)
    {
        if (this.perPane.Remove(content, out var h))
        {
            content.TitleChanged -= h.TitleHandler;
            content.ProcessExitedNormally -= h.ExitHandler;
        }
    }

    private void OnLeafAdded(PaneLeaf leaf)
    {
        this.WirePaneContent(leaf.Content);
        this.PaneAdded?.Invoke(leaf.Content);
        this.PaneLayoutChanged?.Invoke();
    }

    private void OnLeafRemoving(PaneLeaf leaf)
    {
        this.PaneRemoving?.Invoke(leaf.Content);
        this.UnwirePaneContent(leaf.Content);
        this.PaneLayoutChanged?.Invoke();
    }

    private void OnActiveLeafChanged(PaneLeaf leaf)
    {
        this.Title = string.IsNullOrEmpty(leaf.Content.Title) ? "AeroTerm" : leaf.Content.Title;
    }

    private void OnPaneTitleChanged(ITabSessionContent source, string newTitle)
    {
        // Background panes don't overwrite the tab label.
        if (!ReferenceEquals(source, this.tree.ActiveLeaf.Content))
        {
            return;
        }

        this.Title = string.IsNullOrEmpty(newTitle) ? "AeroTerm" : newTitle;
    }

    private void OnPaneExited(ITabSessionContent source)
    {
        // Single-pane tab: propagate to the host so it closes the tab.
        if (this.tree.IsSingleLeaf)
        {
            this.ProcessExitedNormally?.Invoke();
            return;
        }

        // Multi-pane: close just the pane that exited. If the source
        // isn't the active pane, temporarily activate it so CloseActive
        // removes the right leaf.
        if (!ReferenceEquals(source, this.tree.ActiveLeaf.Content))
        {
            foreach (var leaf in this.tree.EnumerateLeaves())
            {
                if (ReferenceEquals(leaf.Content, source))
                {
                    this.tree.SetActive(leaf);
                    break;
                }
            }
        }

        this.tree.CloseActive();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private readonly record struct PaneHandlers(Action<string> TitleHandler, Action ExitHandler);
}
