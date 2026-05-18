// <copyright file="GitStatusBucket.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Git status bucket used by the Workbench Git view.
/// </summary>
internal enum GitStatusBucket
{
    /// <summary>
    /// The path is staged in the index.
    /// </summary>
    Staged,

    /// <summary>
    /// The path has unstaged working-tree changes.
    /// </summary>
    Unstaged,

    /// <summary>
    /// The path is untracked.
    /// </summary>
    Untracked,
}
