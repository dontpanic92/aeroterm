// <copyright file="QuakeModeService.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using AeroTerm.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Owns the lifecycle of the Quake-mode drop-down window. Registers the
/// configured global hotkey (when enabled and supported by the platform)
/// and toggles the window's visibility on each firing.
/// <para>
/// The window is created lazily on the first toggle so app startup is
/// unaffected when Quake mode is enabled but the user has not yet
/// invoked it.
/// </para>
/// </summary>
public sealed class QuakeModeService : IDisposable
{
    private readonly AppSettings settings;
    private readonly IGlobalHotkeySource hotkeys;
    private readonly Func<object> windowFactory;
    private readonly Action<object> toggleAction;
    private readonly ILogger log;

    private IDisposable? registration;
    private object? window;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuakeModeService"/>
    /// class.
    /// </summary>
    /// <param name="settings">App settings driving <c>QuakeModeEnabled</c> / <c>QuakeHotkey</c>.</param>
    /// <param name="hotkeys">Hotkey source; in tests, a fake.</param>
    /// <param name="windowFactory">Factory that builds the Quake window instance.</param>
    /// <param name="toggleAction">Action invoked on each hotkey firing, given the window instance.</param>
    public QuakeModeService(
        AppSettings settings,
        IGlobalHotkeySource hotkeys,
        Func<object> windowFactory,
        Action<object> toggleAction)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        this.windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
        this.toggleAction = toggleAction ?? throw new ArgumentNullException(nameof(toggleAction));
        this.log = AppLogger.For<QuakeModeService>();
    }

    /// <summary>
    /// Gets a value indicating whether the global hotkey is currently
    /// registered. Intended for tests and diagnostics.
    /// </summary>
    public bool IsHotkeyRegistered => this.registration is not null;

    /// <summary>
    /// Gets the resident window instance if one has been created, else <c>null</c>.
    /// </summary>
    public object? Window => this.window;

    /// <summary>
    /// Evaluates <see cref="AppSettings.QuakeModeEnabled"/> and
    /// <see cref="AppSettings.QuakeHotkey"/> and (re-)registers the global
    /// hotkey to match. Safe to call on startup and on every settings
    /// change.
    /// </summary>
    public void ApplySettings()
    {
        this.ThrowIfDisposed();
        this.registration?.Dispose();
        this.registration = null;

        if (!this.settings.QuakeModeEnabled)
        {
            return;
        }

        if (!this.hotkeys.IsSupported)
        {
            this.log.LogInformation("Quake mode is enabled but the current platform does not support global hotkeys; feature disabled.");
            return;
        }

        if (!KeyChordParser.TryParse(this.settings.QuakeHotkey, out var chord) || chord is null)
        {
            this.log.LogWarning("Quake hotkey '{Hotkey}' is not a valid chord; feature disabled.", this.settings.QuakeHotkey);
            return;
        }

        if (!this.hotkeys.TryRegister(chord, this.Toggle, out var reg))
        {
            this.log.LogWarning("Failed to register Quake hotkey '{Hotkey}' — another application may already own it.", this.settings.QuakeHotkey);
            return;
        }

        this.registration = reg;
    }

    /// <summary>
    /// Toggles the Quake window on the Avalonia UI thread. Creates the
    /// window if it does not yet exist.
    /// </summary>
    public void Toggle()
    {
        this.ThrowIfDisposed();
        try
        {
            this.window ??= this.windowFactory();
            this.toggleAction(this.window);
        }
        catch (Exception ex)
        {
            this.log.LogWarning(ex, "Quake toggle failed.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.registration?.Dispose();
        this.registration = null;
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(QuakeModeService));
        }
    }
}
