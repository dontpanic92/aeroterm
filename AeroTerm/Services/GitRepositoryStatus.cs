// <copyright file="GitRepositoryStatus.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// Repository status shown by the Workbench Git view.
/// </summary>
/// <param name="WorkingDirectory">Terminal working directory used for detection.</param>
/// <param name="RepositoryRoot">Repository root, or <see langword="null"/> outside a repository.</param>
/// <param name="Branch">Current branch or detached commit description.</param>
/// <param name="Upstream">Upstream branch, if any.</param>
/// <param name="Ahead">Number of commits ahead of upstream.</param>
/// <param name="Behind">Number of commits behind upstream.</param>
/// <param name="Staged">Staged paths.</param>
/// <param name="Unstaged">Unstaged paths.</param>
/// <param name="Untracked">Untracked paths.</param>
/// <param name="ErrorMessage">User-visible error message, or <see langword="null"/> when status succeeded.</param>
internal sealed record GitRepositoryStatus(
    string WorkingDirectory,
    string? RepositoryRoot,
    string? Branch,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<GitFileStatus> Staged,
    IReadOnlyList<GitFileStatus> Unstaged,
    IReadOnlyList<GitFileStatus> Untracked,
    string? ErrorMessage)
{
    /// <summary>
    /// Gets a value indicating whether the working directory is inside a Git repository.
    /// </summary>
    internal bool IsRepository => this.RepositoryRoot is not null;
}
