// <copyright file="PromptMarkKind.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// Kind of shell-integration prompt mark emitted via OSC 133 (or the
/// compatible VS Code variant OSC 633).
/// </summary>
/// <remarks>
/// Mirrors <see cref="ShellIntegrationKind"/> but uses the vocabulary
/// adopted by the roadmap for shell-integration navigation: prompt →
/// command → output → end. The two enums intentionally coexist so the
/// pre-existing <see cref="VtParser.ShellIntegrationReceived"/> event
/// keeps working; new consumers should prefer
/// <see cref="VtParser.PromptMarkRaised"/>.
/// </remarks>
public enum PromptMarkKind
{
    /// <summary>OSC 133;A — start of a shell prompt.</summary>
    PromptStart,

    /// <summary>OSC 133;B — start of user input (prompt finished).</summary>
    CommandStart,

    /// <summary>OSC 133;C — command executed; command output begins.</summary>
    OutputStart,

    /// <summary>OSC 133;D — command finished; optional exit code.</summary>
    CommandEnd,
}
