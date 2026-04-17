// <copyright file="GlobalHotkey.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects.QuakeHotkey;

using System;
using System.Runtime.InteropServices;
using Avalonia.Input;

/// <summary>
/// Process-wide registry of system-global keyboard hotkeys.
/// <para>
/// Backed by <c>RegisterHotKey</c> / <c>RegisterEventHotKey</c> on Windows
/// and macOS respectively. Linux/X11 and Wayland are currently unsupported
/// (<see cref="IsSupported"/> returns <see langword="false"/>), so the
/// Quake-mode feature simply does not engage on those platforms — the rest
/// of the application is unaffected.
/// </para>
/// </summary>
public static class GlobalHotkey
{
    private static readonly object SyncRoot = new();
    private static IHotkeyPlatform? platform;
    private static bool probed;

    /// <summary>
    /// Gets a value indicating whether system-global hotkeys can be
    /// registered on the current OS / session. Lazy-initialized on first
    /// access so probing interop does not run during static init.
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            EnsurePlatformProbed();
            return platform is not null;
        }
    }

    /// <summary>
    /// Attempts to register a system-wide hotkey. Returns
    /// <see langword="false"/> on unsupported platforms, failing interop,
    /// or conflict with a hotkey already owned by another process.
    /// </summary>
    /// <param name="modifiers">The modifier mask.</param>
    /// <param name="key">The non-modifier key.</param>
    /// <param name="handler">Callback invoked on the Avalonia UI thread when the chord fires.</param>
    /// <param name="registration">A disposable whose <see cref="IDisposable.Dispose"/> unregisters the hotkey.</param>
    /// <returns><see langword="true"/> on success.</returns>
    public static bool TryRegister(KeyModifiers modifiers, Key key, Action handler, out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(handler);
        registration = null;
        EnsurePlatformProbed();
        if (platform is null)
        {
            return false;
        }

        try
        {
            return platform.TryRegister(modifiers, key, handler, out registration);
        }
        catch (Exception)
        {
            registration = null;
            return false;
        }
    }

    private static void EnsurePlatformProbed()
    {
        lock (SyncRoot)
        {
            if (probed)
            {
                return;
            }

            probed = true;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    platform = new WindowsHotkeyPlatform();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    platform = new MacOSHotkeyPlatform();
                }
                else
                {
                    // Linux/X11 and Wayland: no built-in backend. Leaving
                    // platform null keeps IsSupported false and TryRegister
                    // always returns false, which the Quake-mode service
                    // treats as "feature unavailable".
                    platform = null;
                }
            }
            catch (Exception)
            {
                platform = null;
            }
        }
    }
}
