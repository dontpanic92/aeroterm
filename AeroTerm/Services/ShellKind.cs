// <copyright file="ShellKind.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Recognised shell kinds for which AeroTerm ships an OSC 133
/// integration script.
/// </summary>
internal enum ShellKind
{
    /// <summary>Unrecognised shell — leave untouched.</summary>
    Unknown,

    /// <summary>Z shell.</summary>
    Zsh,

    /// <summary>GNU bash.</summary>
    Bash,

    /// <summary>fish shell.</summary>
    Fish,

    /// <summary>PowerShell (Core <c>pwsh</c> or Windows <c>powershell</c>).</summary>
    PowerShell,
}
