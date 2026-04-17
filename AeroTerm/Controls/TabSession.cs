// <copyright file="TabSession.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

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
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabSession"/> class
    /// from application settings — creates a fresh
    /// <see cref="TerminalSessionCoordinator"/> using those settings.
    /// </summary>
    /// <param name="settings">Application settings that drive the shell's
    /// font / color scheme / scrollback configuration.</param>
    public TabSession(AppSettings settings)
        : this(new CoordinatorTabContent(new TerminalSessionCoordinator(settings)))
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
