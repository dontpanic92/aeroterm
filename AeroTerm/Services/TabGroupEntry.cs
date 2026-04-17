// <copyright file="TabGroupEntry.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// JSON-friendly DTO mirroring <see cref="TabGroup"/>. Kept separate so
/// the file format can evolve without breaking the in-memory model API.
/// </summary>
internal sealed class TabGroupEntry
{
    /// <summary>Gets or sets the stable group id.</summary>
    public string? Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the 24-bit RGB color.</summary>
    public int Color { get; set; }
}
