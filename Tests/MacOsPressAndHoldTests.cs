// <copyright file="MacOsPressAndHoldTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace Tests;

using System.Runtime.InteropServices;
using AeroTerm.Utilities;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="MacOsPressAndHold"/>, which disables the macOS
/// press-and-hold accent popup so held keys auto-repeat in the terminal.
/// </summary>
[TestFixture]
public class MacOsPressAndHoldTests
{
    private bool? originalValue;

    /// <summary>
    /// Captures the current press-and-hold override so it can be restored.
    /// </summary>
    [SetUp]
    public void CaptureOriginalValue()
    {
        this.originalValue = MacOsPressAndHold.ReadPressAndHoldEnabled();
    }

    /// <summary>
    /// Restores the machine's original press-and-hold state.
    /// </summary>
    [TearDown]
    public void RestoreOriginalValue()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        MacOsPressAndHold.ResetPressAndHoldOverrideForTesting();
        if (this.originalValue == true)
        {
            // The host had press-and-hold explicitly enabled; nothing in the
            // test suite should silently take that away.
            Assert.That(MacOsPressAndHold.ReadPressAndHoldEnabled(), Is.Not.EqualTo(false));
        }
    }

    /// <summary>
    /// On non-macOS platforms the helper must be an inert no-op rather than
    /// attempting any native call.
    /// </summary>
    [Test]
    public void EnsureKeyRepeatEnabled_OnNonMacOs_IsNoOp()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Ignore("macOS-specific behavior is covered by the macOS test.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(MacOsPressAndHold.EnsureKeyRepeatEnabled(), Is.False);
            Assert.That(MacOsPressAndHold.ReadPressAndHoldEnabled(), Is.Null);
        });
    }

    /// <summary>
    /// On macOS the helper must write the app-domain override and be safe to
    /// call repeatedly (the second call short-circuits without rewriting).
    /// </summary>
    [Test]
    public void EnsureKeyRepeatEnabled_OnMacOs_DisablesPressAndHoldIdempotently()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Ignore("Requires macOS.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(MacOsPressAndHold.EnsureKeyRepeatEnabled(), Is.True);
            Assert.That(MacOsPressAndHold.ReadPressAndHoldEnabled(), Is.False);
            Assert.That(MacOsPressAndHold.EnsureKeyRepeatEnabled(), Is.True);
            Assert.That(MacOsPressAndHold.ReadPressAndHoldEnabled(), Is.False);
        });
    }
}
