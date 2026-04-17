// <copyright file="ProfilesFile.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// On-disk representation of <c>profiles.json</c>. Kept separate from
/// <see cref="Profile"/> so the file format can evolve without breaking
/// the in-memory model API.
/// </summary>
internal sealed class ProfilesFile
{
    /// <summary>
    /// Gets or sets the schema version. Always <c>1</c> at present.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets the id of the profile that should be used for new
    /// tabs when the user does not pick one explicitly. <c>null</c> →
    /// fall back to the first profile in the list.
    /// </summary>
    public string? DefaultProfileId { get; set; }

    /// <summary>
    /// Gets or sets the profile list.
    /// </summary>
    public List<ProfileEntry>? Profiles { get; set; }
}
