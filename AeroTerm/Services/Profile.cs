// <copyright file="Profile.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// User-editable bundle of launch + appearance settings that can be fired
/// as a one-click "new tab" preset. Any property left unset (null) is
/// inherited from the application defaults at tab creation time, so a
/// freshly-synthesized profile behaves identically to the pre-profile
/// shell experience.
/// </summary>
public sealed class Profile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Profile"/> class with a
    /// fresh GUID identifier.
    /// </summary>
    public Profile()
    {
        this.Id = Guid.NewGuid().ToString("N");
        this.Name = "Default";
    }

    /// <summary>
    /// Gets the stable identifier used to reference this profile from other
    /// settings (e.g. the default profile pointer). Generated once on
    /// creation and persisted verbatim.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Gets or sets the human-readable profile name shown in menus.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the executable path (or resolvable command name) to
    /// launch. <c>null</c> → use the platform default shell.
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Gets or sets the command-line argument vector passed to the shell.
    /// <c>null</c> → use the fallback's args.
    /// </summary>
    public string[]? Args { get; set; }

    /// <summary>
    /// Gets or sets the working directory the child process should start
    /// in. <c>null</c> → use the fallback's cwd (typically the user home).
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets additional environment variables layered on top of the
    /// inherited environment. Keys present here override the fallback;
    /// keys absent here keep the fallback value. <c>null</c> → no
    /// overrides.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentOverrides { get; set; }

    /// <summary>
    /// Gets or sets the name of the color scheme to apply to tabs created
    /// from this profile. <c>null</c> → use the application default.
    /// </summary>
    public string? ColorSchemeName { get; set; }

    /// <summary>
    /// Gets or sets the ordered font family priority list. <c>null</c> →
    /// use the application default.
    /// </summary>
    public string[]? FontFamilies { get; set; }

    /// <summary>
    /// Gets or sets the font size in points. <c>null</c> → use the
    /// application default.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the window effect identifier (parsed as the
    /// <see cref="WindowEffects.BlurType"/> name). <c>null</c> → use the
    /// application default.
    /// </summary>
    public string? WindowEffect { get; set; }
}
