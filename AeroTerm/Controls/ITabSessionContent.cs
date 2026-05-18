// <copyright file="ITabSessionContent.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using AeroTerm.Services;
using Avalonia.Controls;

/// <summary>
/// Test seam for <see cref="TabSession"/>. Production code wires a
/// <see cref="TerminalSessionCoordinator"/>; tests provide a fake.
/// </summary>
internal interface ITabSessionContent : IDisposable
{
    /// <summary>
    /// Raised when the underlying content's title changes (e.g. OSC 0/2).
    /// </summary>
    event Action<string>? TitleChanged;

    /// <summary>
    /// Raised when the underlying shell process exits normally.
    /// </summary>
    event Action? ProcessExitedNormally;

    /// <summary>
    /// Raised when the underlying terminal session reports a new current
    /// working directory.
    /// </summary>
    event Action<string>? CurrentWorkingDirectoryChanged;

    /// <summary>
    /// Gets the current title as reported by the content.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Gets the visual host that is kept attached to the visual tree for
    /// the lifetime of the tab (hidden when the tab is not active).
    /// </summary>
    Control Host { get; }

    /// <summary>
    /// Gets the underlying coordinator, or <c>null</c> for fake content.
    /// </summary>
    TerminalSessionCoordinator? Coordinator { get; }

    /// <summary>
    /// Gets the underlying <see cref="TerminalControl"/>, once created.
    /// Returns <c>null</c> for fake content or before <see cref="Start"/>.
    /// </summary>
    TerminalControl? Terminal { get; }

    /// <summary>
    /// Gets the latest known working directory for the underlying session,
    /// or <c>null</c> when it is not yet known.
    /// </summary>
    string? CurrentWorkingDirectory { get; }

    /// <summary>
    /// Starts the underlying session (spawning the shell).
    /// </summary>
    void Start();

    /// <summary>
    /// Moves keyboard focus to the underlying input surface.
    /// </summary>
    void FocusInput();

    /// <summary>
    /// Creates a new, independent <see cref="ITabSessionContent"/> configured
    /// to launch a sibling session based on this one — typically reusing the
    /// same shell / args / env snapshot and a best-effort live working
    /// directory. The caller is responsible for hosting and starting the
    /// returned content. The resulting content is NOT yet started.
    /// </summary>
    /// <returns>A fresh content instance to wrap in a new <see cref="TabSession"/>.</returns>
    ITabSessionContent Duplicate();
}
