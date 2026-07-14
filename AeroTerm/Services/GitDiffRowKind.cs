// <copyright file="GitDiffRowKind.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// The kind of change represented by a <see cref="GitDiffRow"/>.
/// </summary>
internal enum GitDiffRowKind
{
    /// <summary>
    /// An unchanged context line present on both sides.
    /// </summary>
    Context,

    /// <summary>
    /// A replacement row present on both sides with different text.
    /// </summary>
    Modified,

    /// <summary>
    /// A line removed from the old version (left side only).
    /// </summary>
    Removed,

    /// <summary>
    /// A line added in the new version (right side only).
    /// </summary>
    Added,
}
