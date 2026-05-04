// <copyright file="ProfileEntry.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// JSON-friendly DTO mirroring <see cref="Profile"/>. Removed legacy
/// fields (env overrides, color scheme, fonts, window effect) are
/// silently ignored when reading older profiles.json files.
/// </summary>
internal sealed class ProfileEntry
{
    /// <summary>Gets or sets the stable profile id.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the shell command.</summary>
    public string? Command { get; set; }

    /// <summary>Gets or sets the shell argument vector.</summary>
    public string[]? Args { get; set; }

    /// <summary>Gets or sets the initial working directory.</summary>
    public string? WorkingDirectory { get; set; }
}
