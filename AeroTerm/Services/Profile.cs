// <copyright file="Profile.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;

/// <summary>
/// User-editable launch preset that can be fired as a one-click "new tab"
/// action. The MVP profile model carries only the fields needed to launch
/// a shell — name, executable, arguments, and working directory. Any
/// property left unset (null) is inherited from the application defaults
/// at tab creation time, so a freshly-synthesized profile behaves
/// identically to the pre-profile shell experience.
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
    /// Returns the human-readable profile name. Overridden so UI controls
    /// that fall back to <see cref="object.ToString"/> (e.g. when reflection
    /// on <see cref="Name"/> is trimmed away under PublishAot) still render
    /// a meaningful label instead of the type's full name.
    /// </summary>
    /// <returns>The profile <see cref="Name"/>.</returns>
    public override string ToString() => this.Name;
}
