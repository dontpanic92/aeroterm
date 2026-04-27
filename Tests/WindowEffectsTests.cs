// <copyright file="WindowEffectsTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Runtime.InteropServices;
using AeroTerm.WindowEffects;
using NUnit.Framework;

/// <summary>
/// Tests for the <see cref="AeroTerm.WindowEffects"/> package, focusing on
/// the enum contract for <see cref="BlurType"/> and the macOS Liquid Glass
/// runtime probe.
/// </summary>
[TestFixture]
public class WindowEffectsTests
{
    /// <summary>
    /// Locks the numeric value of every <see cref="BlurType"/> enumerator.
    /// These values are persisted to disk (via <c>AppSettings</c>) and
    /// must not change without a migration.
    /// </summary>
    [Test]
    public void BlurType_EnumValues_AreStable()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int)BlurType.Gaussian, Is.EqualTo(0));
            Assert.That((int)BlurType.Acrylic, Is.EqualTo(1));
            Assert.That((int)BlurType.Mica, Is.EqualTo(2));
            Assert.That((int)BlurType.Transparent, Is.EqualTo(3));
            Assert.That((int)BlurType.LiquidGlass, Is.EqualTo(4));
        });
    }

    /// <summary>
    /// On non-macOS platforms <see cref="MacOSInterop.IsMacOS26OrLater"/>
    /// must always return <c>false</c> without performing any native call.
    /// </summary>
    [Test]
    public void IsMacOS26OrLater_NonMacOS_ReturnsFalse()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Ignore("This test only runs on non-macOS platforms.");
        }

        Assert.That(MacOSInterop.IsMacOS26OrLater(), Is.False);
    }

    /// <summary>
    /// On macOS the probe is consistent across calls (the result is cached).
    /// On macOS 26+ the probe must report <c>true</c>; on earlier versions
    /// it must report <c>false</c>. The expected value is derived from the
    /// reported OS version so the test is self-checking.
    /// </summary>
    [Test]
    public void IsMacOS26OrLater_MacOS_MatchesReportedOSVersion()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Ignore("This test only runs on macOS.");
        }

        bool expected = Environment.OSVersion.Version.Major >= 26;

        bool first = MacOSInterop.IsMacOS26OrLater();
        bool second = MacOSInterop.IsMacOS26OrLater();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(expected));
            Assert.That(second, Is.EqualTo(first), "Probe result must be cached and stable.");
        });
    }

    /// <summary>
    /// <see cref="MacOSInterop.InstallLiquidGlassBackdrop"/> and its remove
    /// counterpart must tolerate a zero handle without throwing — both are
    /// invoked during teardown paths where the platform handle may already
    /// be gone.
    /// </summary>
    [Test]
    public void LiquidGlassBackdrop_ZeroHandle_DoesNotThrow()
    {
        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => MacOSInterop.InstallLiquidGlassBackdrop(IntPtr.Zero));
            Assert.DoesNotThrow(() => MacOSInterop.RemoveLiquidGlassBackdrop(IntPtr.Zero));
        });
    }
}
