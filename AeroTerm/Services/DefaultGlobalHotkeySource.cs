// <copyright file="DefaultGlobalHotkeySource.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using AeroTerm.WindowEffects.QuakeHotkey;

/// <summary>
/// Production implementation of <see cref="IGlobalHotkeySource"/> backed
/// by the <see cref="GlobalHotkey"/> platform facade in
/// <c>AeroTerm.WindowEffects</c>.
/// </summary>
public sealed class DefaultGlobalHotkeySource : IGlobalHotkeySource
{
    /// <inheritdoc />
    public bool IsSupported => GlobalHotkey.IsSupported;

    /// <inheritdoc />
    public bool TryRegister(KeyChord chord, Action handler, out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(chord);
        ArgumentNullException.ThrowIfNull(handler);
        return GlobalHotkey.TryRegister(chord.Modifiers, chord.Key, handler, out registration);
    }
}
