// <copyright file="QuakeModeServiceTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Exercises <see cref="QuakeModeService"/> via the <see cref="FakeGlobalHotkey"/>
/// double so registration, toggling, and disposal can be verified without
/// starting the Avalonia headless host or touching any OS interop.
/// </summary>
[TestFixture]
public class QuakeModeServiceTests
{
    /// <summary>
    /// When <see cref="AppSettings.QuakeModeEnabled"/> is <c>false</c>
    /// the service must not register a hotkey.
    /// </summary>
    [Test]
    public void ApplySettings_DisabledByDefault_DoesNotRegister()
    {
        var settings = new AppSettings();
        var hotkeys = new FakeGlobalHotkey();
        using var svc = Build(settings, hotkeys, out _);

        svc.ApplySettings();

        Assert.That(svc.IsHotkeyRegistered, Is.False);
        Assert.That(hotkeys.RegisterSuccesses, Is.Zero);
    }

    /// <summary>
    /// Registering a valid chord produces a live registration that can be
    /// fired, toggling the window visibility on each call.
    /// </summary>
    [Test]
    public void ApplySettings_ValidChord_RegistersAndToggles()
    {
        var settings = new AppSettings { QuakeModeEnabled = true, QuakeHotkey = "Ctrl+Oem3" };
        var hotkeys = new FakeGlobalHotkey();
        using var svc = Build(settings, hotkeys, out var box);

        svc.ApplySettings();

        Assert.That(svc.IsHotkeyRegistered, Is.True);
        Assert.That(hotkeys.Registered, Has.Count.EqualTo(1));

        var chord = System.Linq.Enumerable.First(hotkeys.Registered);
        hotkeys.Fire(chord);
        Assert.That(box.Visible, Is.True);
        hotkeys.Fire(chord);
        Assert.That(box.Visible, Is.False);
    }

    /// <summary>
    /// An unparseable chord leaves the hotkey unregistered without
    /// throwing — mis-typed settings should not crash the app.
    /// </summary>
    [Test]
    public void ApplySettings_InvalidChord_DoesNotRegister()
    {
        var settings = new AppSettings { QuakeModeEnabled = true, QuakeHotkey = "this is not a chord" };
        var hotkeys = new FakeGlobalHotkey();
        using var svc = Build(settings, hotkeys, out _);

        svc.ApplySettings();

        Assert.That(svc.IsHotkeyRegistered, Is.False);
    }

    /// <summary>
    /// When <see cref="IGlobalHotkeySource.IsSupported"/> is
    /// <see langword="false"/> (e.g. Linux) the service silently
    /// declines to register.
    /// </summary>
    [Test]
    public void ApplySettings_UnsupportedPlatform_DoesNotRegister()
    {
        var settings = new AppSettings { QuakeModeEnabled = true, QuakeHotkey = "Ctrl+Oem3" };
        var hotkeys = new FakeGlobalHotkey { Supported = false };
        using var svc = Build(settings, hotkeys, out _);

        svc.ApplySettings();

        Assert.That(svc.IsHotkeyRegistered, Is.False);
    }

    /// <summary>
    /// Re-applying settings after a valid-chord registration unregisters
    /// the previous hotkey before (re-)registering, avoiding leaks.
    /// </summary>
    [Test]
    public void ApplySettings_CalledTwice_ReleasesPriorRegistration()
    {
        var settings = new AppSettings { QuakeModeEnabled = true, QuakeHotkey = "Ctrl+Oem3" };
        var hotkeys = new FakeGlobalHotkey();
        using var svc = Build(settings, hotkeys, out _);

        svc.ApplySettings();
        svc.ApplySettings();

        Assert.That(hotkeys.RegisterSuccesses, Is.EqualTo(2));
        Assert.That(hotkeys.UnregisterCount, Is.EqualTo(1));
        Assert.That(hotkeys.Registered, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Disposing the service unregisters the hotkey and prevents further
    /// operations.
    /// </summary>
    [Test]
    public void Dispose_UnregistersHotkey()
    {
        var settings = new AppSettings { QuakeModeEnabled = true, QuakeHotkey = "Ctrl+Oem3" };
        var hotkeys = new FakeGlobalHotkey();
        var svc = Build(settings, hotkeys, out _);
        svc.ApplySettings();

        svc.Dispose();

        Assert.That(hotkeys.Registered, Is.Empty);
        Assert.That(hotkeys.UnregisterCount, Is.EqualTo(1));
    }

    private static QuakeModeService Build(AppSettings settings, FakeGlobalHotkey hotkeys, out Box box)
    {
        var local = new Box();
        box = local;
        return new QuakeModeService(
            settings,
            hotkeys,
            () => local,
            w => ((Box)w).Visible = !((Box)w).Visible);
    }

    private sealed class Box
    {
        public bool Visible { get; set; }
    }
}
