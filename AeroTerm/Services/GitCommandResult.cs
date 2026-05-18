// <copyright file="GitCommandResult.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Result of a Git command run by the Workbench Git view.
/// </summary>
/// <param name="ExitCode">Git process exit code.</param>
/// <param name="Output">Standard output text.</param>
/// <param name="Error">Standard error text.</param>
internal sealed record GitCommandResult(
    int ExitCode,
    string Output,
    string Error)
{
    /// <summary>
    /// Gets a value indicating whether Git reported success.
    /// </summary>
    internal bool Succeeded => this.ExitCode == 0;

    /// <summary>
    /// Gets a user-visible message for failed commands.
    /// </summary>
    internal string ErrorMessage => string.IsNullOrWhiteSpace(this.Error) ? this.Output.Trim() : this.Error.Trim();
}
