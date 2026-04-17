// <copyright file="IGlobalHotkeySource.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;

/// <summary>
/// Abstraction over <see cref="AeroTerm.WindowEffects.QuakeHotkey.GlobalHotkey"/>
/// so quake-mode consumers can be unit-tested without touching
/// OS-level APIs. The production implementation lives in
/// <see cref="DefaultGlobalHotkeySource"/>.
/// </summary>
public interface IGlobalHotkeySource
{
    /// <summary>
    /// Gets a value indicating whether the backing platform can register
    /// system-wide hotkeys.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Attempts to register a system-global hotkey bound to
    /// <paramref name="chord"/>.
    /// </summary>
    /// <param name="chord">The chord to register.</param>
    /// <param name="handler">Callback invoked on the UI thread when the chord fires.</param>
    /// <param name="registration">A disposable that unregisters the chord on <see cref="IDisposable.Dispose"/>.</param>
    /// <returns><see langword="true"/> on successful registration.</returns>
    bool TryRegister(KeyChord chord, Action handler, out IDisposable? registration);
}
