// <copyright file="GitDiffHighlightKind.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Type of line highlight shown in a full-file Git comparison editor.
/// </summary>
internal enum GitDiffHighlightKind
{
    /// <summary>
    /// The line exists only on the new side.
    /// </summary>
    Added,

    /// <summary>
    /// The line exists only on the old side.
    /// </summary>
    Removed,

    /// <summary>
    /// The line exists on both sides but changed.
    /// </summary>
    Modified,
}
