// <copyright file="NotchBarWindow.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System;
using AeroTerm.WindowEffects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

/// <summary>
/// Borderless overlay window that occupies the band macOS leaves unused
/// above a native full-screen window on a display with a camera housing.
/// <para>
/// AppKit clamps native full-screen windows to the safe area and paints the
/// leftover strip black; that clamp cannot be lifted. This window floats
/// over the strip instead — the approach "dynamic island" utilities use —
/// so the tab strip can occupy space that would otherwise be wasted. It is
/// deliberately never activated, leaving keyboard focus with the terminal in
/// the full-screen window while still receiving mouse input itself.
/// </para>
/// </summary>
public sealed class NotchBarWindow : Window
{
    private readonly Border host;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotchBarWindow"/> class.
    /// </summary>
    public NotchBarWindow()
    {
        this.WindowDecorations = WindowDecorations.None;
        this.CanResize = false;

        // Manual placement only: any startup-location logic would re-centre
        // the band when Avalonia reshows the window after a space switch.
        this.WindowStartupLocation = WindowStartupLocation.Manual;
        this.ShowInTaskbar = false;
        this.ShowActivated = false;

        // Deliberately NOT Topmost: Avalonia drives the NSWindow level for
        // topmost windows and would overwrite the higher level applied in
        // ApplyNativeConfiguration, dropping the overlay below the menu bar.
        // The overlay must be OPAQUE. macOS reveals the menu bar based purely
        // on pointer position, so it still slides in underneath this window;
        // with a transparent background it would simply show through and look
        // as though the menu bar were covering the tab strip.
        this.TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
        this.TransparencyBackgroundFallback = Brushes.Black;
        this.Background = Brushes.Black;

        this.host = new Border { Background = Brushes.Black };
        this.Content = this.host;
    }

    /// <summary>
    /// Gets the container that hosts the re-parented tab strip.
    /// </summary>
    public Border Host => this.host;

    /// <summary>
    /// Gets the overlay's current native window level, for diagnostics. It
    /// must stay above <c>NSMainMenuWindowLevel</c> (24) or the auto-
    /// revealing menu bar paints over the tab strip. Returns 0 when the
    /// platform handle is unavailable.
    /// </summary>
    public long NativeWindowLevel
    {
        get
        {
            var handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            return handle == IntPtr.Zero ? 0 : MacOSInterop.GetNSWindowLevel(handle);
        }
    }

    /// <summary>
    /// Paints the band with an opaque colour so the auto-revealing menu bar
    /// underneath stays hidden. Alpha is forced to fully opaque even when the
    /// supplied colour scheme is translucent.
    /// </summary>
    /// <param name="background">The colour scheme's background colour.</param>
    public void ApplyBackgroundColor(Color background)
    {
        var opaque = Color.FromRgb(background.R, background.G, background.B);
        var brush = new SolidColorBrush(opaque);
        this.Background = brush;
        this.host.Background = brush;
    }

    /// <summary>
    /// Applies the native window level and collection behavior that let the
    /// overlay share the full-screen space and sit above the auto-revealing
    /// menu bar. Safe to call repeatedly.
    /// </summary>
    public void ApplyNativeConfiguration()
    {
        var handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            MacOSInterop.ConfigureNotchOverlayWindow(handle);
        }
    }

    /// <summary>
    /// Reapplies the native configuration if Avalonia has since reset the
    /// window level. Without this the menu bar would eventually reveal on
    /// top of the tab strip, because Avalonia re-asserts its own level when
    /// it reapplies window properties or reshows the window.
    /// </summary>
    /// <returns><c>true</c> when the configuration had to be reapplied.</returns>
    public bool EnsureNativeConfiguration()
    {
        var handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || MacOSInterop.IsNotchOverlayLevelIntact(handle))
        {
            return false;
        }

        MacOSInterop.ConfigureNotchOverlayWindow(handle);
        return true;
    }
}
