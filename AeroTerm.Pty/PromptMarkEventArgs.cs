// <copyright file="PromptMarkEventArgs.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// Event data raised by <see cref="VtParser.PromptMarkRaised"/> when an
/// OSC 133 (or OSC 633) prompt-mark sequence is recognised.
/// </summary>
public sealed class PromptMarkEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PromptMarkEventArgs"/> class.
    /// </summary>
    /// <param name="kind">The mark kind (A/B/C/D).</param>
    /// <param name="exitCode">Exit code from <c>OSC 133;D;&lt;n&gt;</c>, or
    /// <see langword="null"/> when not supplied or not applicable.</param>
    /// <param name="currentDirectory">Working directory from
    /// <c>OSC 133;A;cwd=…</c> (and VS Code's <c>OSC 633;A;…</c>), or
    /// <see langword="null"/> when not supplied.</param>
    /// <param name="rawPayload">The raw OSC payload following the kind
    /// letter (after the first semicolon), useful for diagnostics.</param>
    public PromptMarkEventArgs(
        PromptMarkKind kind,
        int? exitCode,
        string? currentDirectory,
        string? rawPayload)
    {
        this.Kind = kind;
        this.ExitCode = exitCode;
        this.CurrentDirectory = currentDirectory;
        this.RawPayload = rawPayload;
    }

    /// <summary>Gets the mark kind.</summary>
    public PromptMarkKind Kind { get; }

    /// <summary>
    /// Gets the optional exit code (only meaningful for
    /// <see cref="PromptMarkKind.CommandEnd"/>).
    /// </summary>
    public int? ExitCode { get; }

    /// <summary>
    /// Gets the optional working directory hint supplied alongside
    /// <see cref="PromptMarkKind.PromptStart"/>.
    /// </summary>
    public string? CurrentDirectory { get; }

    /// <summary>
    /// Gets the raw trailing payload (everything after <c>A;</c>/<c>D;</c>
    /// etc.), preserved for downstream consumers that want to read custom
    /// <c>key=value</c> fields not modelled by this class.
    /// </summary>
    public string? RawPayload { get; }
}
