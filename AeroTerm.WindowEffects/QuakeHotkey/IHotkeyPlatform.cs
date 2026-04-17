// <copyright file="IHotkeyPlatform.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects.QuakeHotkey;

using System;
using Avalonia.Input;

/// <summary>
/// Internal seam abstracting the per-OS global-hotkey backend used by
/// <see cref="GlobalHotkey"/>. One implementation exists per supported
/// platform (Windows, macOS). Unsupported platforms simply do not have a
/// backend and <see cref="GlobalHotkey.IsSupported"/> returns
/// <see langword="false"/>.
/// </summary>
internal interface IHotkeyPlatform
{
    /// <summary>
    /// Attempts to register a system-wide hotkey.
    /// </summary>
    /// <param name="modifiers">The modifier mask.</param>
    /// <param name="key">The non-modifier key.</param>
    /// <param name="handler">Callback invoked (on the Avalonia UI thread) when the chord fires.</param>
    /// <param name="registration">The registration token; <c>null</c> on failure.</param>
    /// <returns><see langword="true"/> if registration succeeded.</returns>
    bool TryRegister(KeyModifiers modifiers, Key key, Action handler, out IDisposable? registration);
}
