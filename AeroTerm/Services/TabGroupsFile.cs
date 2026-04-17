// <copyright file="TabGroupsFile.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// On-disk representation of <c>groups.json</c>. Kept separate from
/// <see cref="TabGroup"/> so the file format can evolve without
/// breaking the in-memory model API.
/// </summary>
internal sealed class TabGroupsFile
{
    /// <summary>
    /// Gets or sets the schema version. Always <c>1</c> at present.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets the group list.
    /// </summary>
    public List<TabGroupEntry>? Groups { get; set; }
}
