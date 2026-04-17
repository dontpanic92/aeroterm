// <copyright file="TabSession.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroTerm.Models;
using AeroTerm.Services;
using Avalonia.Controls;

/// <summary>
/// Owns a single terminal session within a <see cref="TabView"/>: one
/// <see cref="TerminalSessionCoordinator"/>, one <see cref="TerminalControl"/>,
/// and the tab's current title. The session's visual <see cref="Control"/>
/// when the tab is not active) so the PTY reader thread and the renderer
/// keep functioning for background tabs.
/// </summary>
public sealed class TabSession : INotifyPropertyChanged, IDisposable
{
    private readonly ITabSessionContent content;
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
        : this(CoordinatorTabContent.FromCoordinator(new TerminalSessionCoordinator(settings), settings))
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
        : this(BuildProfileContent(settings, profile, fallback))
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
    {
        this.content = content ?? throw new ArgumentNullException(nameof(content));
        this.title = string.IsNullOrEmpty(content.Title) ? "AeroTerm" : content.Title;
        this.content.TitleChanged += this.OnContentTitleChanged;
        this.content.ProcessExitedNormally += this.OnContentProcessExited;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised when <see cref="Title"/> changes.
    /// </summary>
    public event Action<string>? TitleChanged;

    /// <summary>
    /// Raised when the underlying shell process exits cleanly. Subscribers
    /// typically close the tab.
    /// </summary>
    public event Action? ProcessExitedNormally;

    /// <summary>
    /// Gets the current tab title (reported by the shell via OSC 0/2 or a
    /// sensible default). Change notification fires on both
    /// <see cref="PropertyChanged"/> and <see cref="TitleChanged"/>.
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
    /// content. The same instance is used for the tab's entire lifetime.
    /// </summary>
    public Control Control => this.content.Host;

    /// <summary>
    /// Gets or sets the id of the <see cref="TabGroup"/> this tab is
    /// assigned to, or <c>null</c> when the tab is not grouped. The
    /// id refers to an entry in the application's
    /// <see cref="TabGroupStore"/>; callers are responsible for
    /// keeping it in sync. Setting the property fires
    /// <see cref="PropertyChanged"/> so the tab strip can repaint the
    /// group pill.
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
    /// Gets the underlying <see cref="TerminalControl"/>, once the session
    /// has been started. May be <c>null</c> before <see cref="Start"/> or
    /// when the session uses a test seam.
    /// </summary>
    public TerminalControl? Terminal => this.content.Terminal;

    /// <summary>
    /// Gets a value indicating whether this session has been disposed.
    /// </summary>
    public bool IsDisposed => this.disposed;

    /// <summary>
    /// Gets the underlying coordinator, or <c>null</c> when the session
    /// was constructed with a fake test seam.
    /// </summary>
    internal TerminalSessionCoordinator? Coordinator => this.content.Coordinator;

    /// <summary>
    /// Starts the underlying shell. Must be called after the session's
    /// <see cref="Control"/> has been added to a visible, laid-out
    /// container so the PTY is allocated at correct dimensions.
    /// </summary>
    public void Start() => this.content.Start();

    /// <summary>
    /// Moves keyboard focus to the terminal so typing goes to the right PTY.
    /// </summary>
    public void FocusInput() => this.content.FocusInput();

    /// <summary>
    /// Creates a new <see cref="TabSession"/> that mirrors this one's launch
    /// configuration (shell, args, env, cwd). The returned session is not
    /// yet started — callers must add it to a <see cref="TabView"/>, force a
    /// layout pass, then invoke <see cref="Start"/>.
    /// </summary>
    /// <returns>The newly-constructed sibling session.</returns>
    public TabSession Duplicate()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(TabSession));
        }

        var dupContent = this.content.Duplicate();
        return new TabSession(dupContent);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.content.TitleChanged -= this.OnContentTitleChanged;
        this.content.ProcessExitedNormally -= this.OnContentProcessExited;
        this.content.Dispose();
    }

    private static ITabSessionContent BuildProfileContent(AppSettings settings, Profile profile, LaunchSpec? fallback)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profile);

        // When the profile has no launch overrides and the caller passed no
        // fallback, let the coordinator compute its own environment-derived
        // defaults — that preserves exact pre-profile behaviour.
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

    private void OnContentTitleChanged(string newTitle)
    {
        this.Title = string.IsNullOrEmpty(newTitle) ? "AeroTerm" : newTitle;
    }

    private void OnContentProcessExited()
    {
        this.ProcessExitedNormally?.Invoke();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
