// <copyright file="FileExplorerEntry.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;

/// <summary>
/// Describes one filesystem entry shown in the Workbench file explorer.
/// </summary>
/// <param name="Name">Display name of the entry.</param>
/// <param name="FullPath">Absolute filesystem path.</param>
/// <param name="Kind">Entry kind.</param>
/// <param name="IsHidden">Whether the entry is hidden.</param>
/// <param name="Length">File length in bytes, or <see langword="null"/> for directories.</param>
/// <param name="LastWriteTimeUtc">Last write time in UTC.</param>
internal sealed record FileExplorerEntry(
    string Name,
    string FullPath,
    FileExplorerEntryKind Kind,
    bool IsHidden,
    long? Length,
    DateTime LastWriteTimeUtc)
{
    /// <summary>
    /// Gets a value indicating whether this entry is a directory.
    /// </summary>
    internal bool IsDirectory => this.Kind == FileExplorerEntryKind.Directory;

    /// <inheritdoc/>
    public override string ToString()
    {
        return this.Name;
    }
}
