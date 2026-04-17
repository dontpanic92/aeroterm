// <copyright file="MacOSWindowMenuTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Tests;

using AeroTerm.WindowEffects;
using NUnit.Framework;

/// <summary>
/// Cross-platform tests for <see cref="MacOSWindowMenu"/>. All native
/// interop paths are macOS-guarded, so on other platforms the API must be
/// callable without side effects or exceptions.
/// </summary>
[TestFixture]
public class MacOSWindowMenuTests
{
    /// <summary>Verifies the handler setter rejects null.</summary>
    [Test]
    public void SetNewWindowHandler_NullHandler_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MacOSWindowMenu.SetNewWindowHandler(null!));
    }

    /// <summary>
    /// On non-macOS hosts, RegisterWindow / UnregisterWindow with null
    /// must still argument-check.
    /// </summary>
    [Test]
    public void RegisterWindow_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MacOSWindowMenu.RegisterWindow(null!));
        Assert.Throws<ArgumentNullException>(() => MacOSWindowMenu.UnregisterWindow(null!));
    }

    /// <summary>
    /// SetNewWindowHandler with a real delegate should complete without
    /// throwing on any platform (macOS work is deferred until a window
    /// registers).
    /// </summary>
    [Test]
    public void SetNewWindowHandler_ValidHandler_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => MacOSWindowMenu.SetNewWindowHandler(() => { }));
    }
}
