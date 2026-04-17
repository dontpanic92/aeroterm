// <copyright file="KeybindingsFile.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// On-disk representation of <c>keybindings.json</c>.
/// </summary>
internal sealed class KeybindingsFile
{
    /// <summary>
    /// Gets or sets the schema version. Always <c>1</c> at present.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets the list of binding entries.
    /// </summary>
    public List<KeybindingEntry>? Bindings { get; set; }
}
