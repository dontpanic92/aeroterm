// <copyright file="QuakeHotkeyTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using AeroTerm.WindowEffects.QuakeHotkey;
using Avalonia.Input;
using NUnit.Framework;

/// <summary>
/// Smoke-tests <see cref="GlobalHotkey"/> against the real OS. Skipped on
/// Linux because the library has no backend there.
/// </summary>
[TestFixture]
[Platform(Include = "Win,MacOsX")]
public class QuakeHotkeyTests
{
    /// <summary>
    /// On Windows and macOS the facade must report
    /// <see cref="GlobalHotkey.IsSupported"/> as <see langword="true"/>.
    /// </summary>
    [Test]
    public void IsSupported_ReturnsTrue_OnSupportedPlatforms()
    {
        Assert.That(GlobalHotkey.IsSupported, Is.True);
    }

    /// <summary>
    /// Registering an unlikely-to-collide chord must succeed and the
    /// resulting <see cref="IDisposable"/> must unregister without
    /// throwing, freeing the chord for subsequent test runs.
    /// </summary>
    [Test]
    public void TryRegister_Succeeds_AndDisposeUnregisters()
    {
        // F12 + all modifiers: mapped on both Windows and macOS and
        // extremely unlikely to conflict with any running application.
        var mods = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt;
        var ok = GlobalHotkey.TryRegister(mods, Key.F12, () => { }, out var reg);
        Assert.That(ok, Is.True, "F12 + Ctrl+Shift+Alt should be registrable.");
        Assert.That(reg, Is.Not.Null);

        // Re-registering the same chord before release must fail.
        var dupe = GlobalHotkey.TryRegister(mods, Key.F12, () => { }, out var reg2);
        Assert.That(dupe, Is.False, "Registering a live chord twice must fail.");
        Assert.That(reg2, Is.Null);

        Assert.DoesNotThrow(() => reg!.Dispose());

        // Double-dispose is a no-op.
        Assert.DoesNotThrow(() => reg!.Dispose());
    }
}
