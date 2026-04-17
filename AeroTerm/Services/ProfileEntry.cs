// <copyright file="ProfileEntry.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// JSON-friendly DTO mirroring <see cref="Profile"/>. Uses a concrete
/// <see cref="Dictionary{TKey, TValue}"/> for env overrides so the
/// source-generated serializer can handle it.
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

    /// <summary>Gets or sets environment variable overrides.</summary>
    public Dictionary<string, string>? EnvironmentOverrides { get; set; }

    /// <summary>Gets or sets the color scheme name.</summary>
    public string? ColorSchemeName { get; set; }

    /// <summary>Gets or sets the ordered font priority list.</summary>
    public string[]? FontFamilies { get; set; }

    /// <summary>Gets or sets the font size in points.</summary>
    public double? FontSize { get; set; }

    /// <summary>Gets or sets the window effect identifier.</summary>
    public string? WindowEffect { get; set; }
}
