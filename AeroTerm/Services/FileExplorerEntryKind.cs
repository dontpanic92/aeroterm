// <copyright file="FileExplorerEntryKind.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Type of entry displayed by the Workbench file explorer.
/// </summary>
internal enum FileExplorerEntryKind
{
    /// <summary>
    /// A directory entry.
    /// </summary>
    Directory,

    /// <summary>
    /// A file entry.
    /// </summary>
    File,
}
