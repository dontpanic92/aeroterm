// <copyright file="Keybinding.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Binds a single <see cref="KeybindingAction"/> to a <see cref="KeyChord"/>.
/// </summary>
/// <param name="Action">The action fired when the chord is pressed.</param>
/// <param name="Chord">The key chord that triggers the action.</param>
public sealed record Keybinding(KeybindingAction Action, KeyChord Chord);
