// <copyright file="KeybindingEntry.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// A single on-disk keybinding entry.
/// </summary>
internal sealed class KeybindingEntry
{
    /// <summary>
    /// Gets or sets the serialized <see cref="KeybindingAction"/> name.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized chord (see <see cref="KeyChordParser"/>).
    /// </summary>
    public string Chord { get; set; } = string.Empty;
}
