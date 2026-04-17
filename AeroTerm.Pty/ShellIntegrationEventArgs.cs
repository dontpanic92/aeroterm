// <copyright file="ShellIntegrationEventArgs.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// Kind of shell-integration prompt mark emitted via OSC 133.
/// </summary>
public enum ShellIntegrationKind
{
    /// <summary>
    /// OSC 133;A — marks the start of a shell prompt.
    /// </summary>
    PromptStart,

    /// <summary>
    /// OSC 133;B — marks the start of user input (end of prompt).
    /// </summary>
    CommandStart,

    /// <summary>
    /// OSC 133;C — marks that the command is executing (user input finished).
    /// </summary>
    CommandExecuted,

    /// <summary>
    /// OSC 133;D — marks that the command finished; payload optionally contains
    /// the exit code as a decimal string.
    /// </summary>
    CommandFinished,
}

/// <summary>
/// Event data raised when a shell-integration (OSC 133) sequence is received.
/// </summary>
public class ShellIntegrationEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShellIntegrationEventArgs"/> class.
    /// </summary>
    /// <param name="kind">The kind of prompt mark.</param>
    /// <param name="payload">Optional payload string (e.g. exit code for CommandFinished).</param>
    public ShellIntegrationEventArgs(ShellIntegrationKind kind, string? payload)
    {
        this.Kind = kind;
        this.Payload = payload;
    }

    /// <summary>
    /// Gets the kind of prompt mark.
    /// </summary>
    public ShellIntegrationKind Kind { get; }

    /// <summary>
    /// Gets the optional payload string associated with this mark.
    /// </summary>
    public string? Payload { get; }

    /// <summary>
    /// Gets the parsed exit code when <see cref="Kind"/> is
    /// <see cref="ShellIntegrationKind.CommandFinished"/> and the payload
    /// encodes a decimal integer; otherwise <see langword="null"/>.
    /// </summary>
    public int? ExitCode =>
        this.Kind == ShellIntegrationKind.CommandFinished
        && int.TryParse(this.Payload, out int code)
            ? code
            : null;
}
