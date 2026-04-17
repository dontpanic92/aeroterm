// <copyright file="TabGroup.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;

/// <summary>
/// A named, colored bucket that tabs may be assigned to. Groups are
/// user-managed: they have a stable <see cref="Id"/> (referenced from
/// <c>TabSession.GroupId</c>) and a short, human-readable
/// <see cref="Name"/>. <see cref="Color"/> is a 24-bit RGB integer that
/// drives the colored pill on grouped tab headers.
/// </summary>
public sealed class TabGroup
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TabGroup"/> class
    /// with a fresh GUID identifier.
    /// </summary>
    public TabGroup()
    {
        this.Id = Guid.NewGuid().ToString("N");
        this.Name = "Group";
        this.Color = DefaultPalette[0];
    }

    /// <summary>
    /// Gets the stable identifier referenced by
    /// <c>TabSession.GroupId</c>. Generated once on creation and
    /// persisted verbatim.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Gets or sets the human-readable group name shown in the
    /// right-click "Add to group" submenu and the command palette.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the 24-bit RGB color used for the group's pill on
    /// member tab headers.
    /// </summary>
    public int Color { get; set; }

    /// <summary>
    /// Gets the palette used when auto-assigning colors to freshly
    /// created groups. Exposed so the store can cycle through it
    /// deterministically and tests can assert stability.
    /// </summary>
    internal static IReadOnlyList<int> DefaultPalette { get; } = new[]
    {
        0x4FA3FF, // blue
        0x62C554, // green
        0xE9B949, // amber
        0xE0664C, // coral
        0xB66BD5, // purple
        0x5AC8D0, // teal
        0xE27DAE, // pink
        0xA3A3A3, // neutral
    };
}
