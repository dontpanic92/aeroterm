// <copyright file="CoordinatorTabContent.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using AeroTerm.Services;
using Avalonia.Controls;

/// <summary>
/// Production <see cref="ITabSessionContent"/> that wraps a
/// <see cref="TerminalSessionCoordinator"/> and hosts its
/// <see cref="TerminalControl"/> (plus the search overlay) inside a
/// <see cref="Grid"/> that stays attached to the visual tree for the
/// tab's lifetime.
/// </summary>
internal sealed class CoordinatorTabContent : ITabSessionContent
{
    private readonly TerminalSessionCoordinator coordinator;
    private readonly Grid host = new();
    private TerminalControl? terminal;
    private string title = "AeroTerm";
    private bool disposed;
    private bool started;

    /// <summary>
    /// Initializes a new instance of the <see cref="CoordinatorTabContent"/> class.
    /// </summary>
    /// <param name="coordinator">The coordinator this content wraps. Ownership
    /// transfers — <see cref="Dispose"/> will shut it down.</param>
    public CoordinatorTabContent(TerminalSessionCoordinator coordinator)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.coordinator.TerminalReady += this.OnTerminalReady;
        this.coordinator.TitleChanged += this.OnCoordinatorTitleChanged;
        this.coordinator.ProcessExitedNormally += this.OnCoordinatorProcessExited;
    }

    /// <inheritdoc />
    public event Action<string>? TitleChanged;

    /// <inheritdoc />
    public event Action? ProcessExitedNormally;

    /// <inheritdoc />
    public string Title => this.title;

    /// <inheritdoc />
    public Control Host => this.host;

    /// <inheritdoc />
    public TerminalSessionCoordinator? Coordinator => this.coordinator;

    /// <inheritdoc />
    public TerminalControl? Terminal => this.terminal;

    /// <inheritdoc />
    public void Start()
    {
        if (this.started || this.disposed)
        {
            return;
        }

        this.started = true;
        this.coordinator.Initialize();
    }

    /// <inheritdoc />
    public void FocusInput() => this.terminal?.Focus();

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.coordinator.TerminalReady -= this.OnTerminalReady;
        this.coordinator.TitleChanged -= this.OnCoordinatorTitleChanged;
        this.coordinator.ProcessExitedNormally -= this.OnCoordinatorProcessExited;
        this.coordinator.Shutdown();
    }

    private void OnTerminalReady(TerminalControl control)
    {
        this.terminal = control;
        this.host.Children.Add(control);
        this.host.Children.Add(control.SearchOverlayVisual);
    }

    private void OnCoordinatorTitleChanged(string newTitle)
    {
        var t = string.IsNullOrEmpty(newTitle) ? "AeroTerm" : newTitle;
        if (this.title == t)
        {
            return;
        }

        this.title = t;
        this.TitleChanged?.Invoke(t);
    }

    private void OnCoordinatorProcessExited()
    {
        this.ProcessExitedNormally?.Invoke();
    }
}
