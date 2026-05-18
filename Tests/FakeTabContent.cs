// <copyright file="FakeTabContent.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls;
using AeroTerm.Services;
using Avalonia.Controls;

/// <summary>
/// Minimal <see cref="ITabSessionContent"/> fake. Counts disposals and
/// lets tests raise title / exit events synchronously. Used by
/// <see cref="TabViewTests"/> so those tests do not spin up a real PTY.
/// </summary>
internal sealed class FakeTabContent : ITabSessionContent
{
    private readonly Grid host = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeTabContent"/> class.
    /// </summary>
    /// <param name="title">Initial title value.</param>
    public FakeTabContent(string title)
    {
        this.Title = title;
    }

    /// <inheritdoc />
    public event Action<string>? TitleChanged;

    /// <inheritdoc />
    public event Action? ProcessExitedNormally;

    /// <inheritdoc />
    public event Action<string>? CurrentWorkingDirectoryChanged;

    /// <inheritdoc />
    public string Title { get; private set; }

    /// <inheritdoc />
    public Control Host => this.host;

    /// <inheritdoc />
    public TerminalSessionCoordinator? Coordinator => null;

    /// <inheritdoc />
    public TerminalControl? Terminal => null;

    /// <inheritdoc />
    public string? CurrentWorkingDirectory { get; private set; }

    /// <summary>
    /// Gets the number of times <see cref="Dispose"/> has been invoked.
    /// </summary>
    public int DisposeCount { get; private set; }

    /// <summary>
    /// Raises the <see cref="TitleChanged"/> event.
    /// </summary>
    /// <param name="t">The new title.</param>
    public void RaiseTitle(string t)
    {
        this.Title = t;
        this.TitleChanged?.Invoke(t);
    }

    /// <summary>
    /// Raises the <see cref="ProcessExitedNormally"/> event.
    /// </summary>
    public void RaiseExit()
    {
        this.ProcessExitedNormally?.Invoke();
    }

    /// <summary>
    /// Raises the <see cref="CurrentWorkingDirectoryChanged"/> event.
    /// </summary>
    /// <param name="cwd">The new current working directory.</param>
    public void RaiseCurrentWorkingDirectory(string cwd)
    {
        this.CurrentWorkingDirectory = cwd;
        this.CurrentWorkingDirectoryChanged?.Invoke(cwd);
    }

    /// <inheritdoc />
    public void Start()
    {
    }

    /// <inheritdoc />
    public void FocusInput()
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.DisposeCount++;
    }

    /// <inheritdoc />
    public ITabSessionContent Duplicate()
    {
        return new FakeTabContent(this.Title);
    }
}
