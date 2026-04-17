// <copyright file="KeyChord.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using Avalonia.Input;

/// <summary>
/// A single keyboard chord: a set of <see cref="KeyModifiers"/> plus a <see cref="Key"/>.
/// </summary>
/// <param name="Modifiers">The modifier keys held down.</param>
/// <param name="Key">The non-modifier key.</param>
public sealed record KeyChord(KeyModifiers Modifiers, Key Key);
