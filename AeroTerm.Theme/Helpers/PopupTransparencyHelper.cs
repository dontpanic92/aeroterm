// <copyright file="PopupTransparencyHelper.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Helpers;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

/// <summary>
/// Applies platform-vibrancy transparency levels to popup hosts the
/// application creates.
/// </summary>
/// <remarks>
/// <para>
/// Setting <see cref="TopLevel.TransparencyLevelHint"/> through only a XAML
/// <c>ControlTheme</c> setter is unreliable for popup hosts on macOS: the
/// underlying <c>NSWindow</c> can be created before the styling pass attaches
/// the setter, so the selected backdrop may never appear and the popup renders
/// with an opaque surface.
/// </para>
/// <para>
/// This helper subscribes to <see cref="Popup.IsOpenProperty"/> as a class
/// handler. When a popup opens, it walks to the platform <see cref="TopLevel"/>
/// via the popup's child's visual root and applies the configured transparency
/// level list directly. Avalonia's platform impl picks the first level the OS
/// supports on non-Liquid-Glass paths, giving us:
/// </para>
/// <list type="bullet">
///   <item>Windows/Linux rounded opaque menus — Transparent host, avoiding
///   square backdrop corners around the rounded menu chrome.</item>
///   <item>Windows 11 non-menu popups — Mica.</item>
///   <item>Windows 10 non-menu popups — Acrylic Blur.</item>
///   <item>macOS &lt; 26 — Avalonia's Acrylic Blur / vibrancy.</item>
///   <item>Linux — Blur if the compositor supports it, otherwise Transparent.</item>
/// </list>
/// <para>
/// On macOS 26+, Avalonia is asked for a transparent popup window and an
/// AppKit's native menu material is installed behind the popup content,
/// matching the system menu Liquid Glass blur.
/// </para>
/// </remarks>
public static class PopupTransparencyHelper
{
    private static readonly IReadOnlyList<WindowTransparencyLevel> LiquidGlassLevels = new[]
    {
        WindowTransparencyLevel.Transparent,
    };

    private static readonly IReadOnlyList<WindowTransparencyLevel> TransparentLevels = new[]
    {
        WindowTransparencyLevel.Transparent,
    };

    private static readonly IReadOnlyList<WindowTransparencyLevel> PreferredLevels = new[]
    {
        WindowTransparencyLevel.Mica,
        WindowTransparencyLevel.AcrylicBlur,
        WindowTransparencyLevel.Blur,
        WindowTransparencyLevel.Transparent,
    };

    private static bool initialized;

    /// <summary>
    /// Registers the global class handler. Safe to call multiple times.
    /// </summary>
    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>(OnPopupIsOpenChanged);
    }

    /// <summary>
    /// Determines whether a popup child has its own opaque rounded menu chrome.
    /// </summary>
    /// <param name="child">The popup child visual.</param>
    /// <returns>
    /// <see langword="true"/> when the popup host should remain transparent
    /// instead of receiving a square blur/backdrop surface.
    /// </returns>
    internal static bool UsesOpaqueRoundedMenuChrome(Visual child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return child is ContextMenu or MenuFlyoutPresenter
            || child is Border { Name: "PART_MenuPopupChrome" };
    }

    private static void OnPopupIsOpenChanged(Popup popup, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => ApplyToHost(popup), DispatcherPriority.Loaded);
    }

    private static void ApplyToHost(Popup popup)
    {
        if (popup.IsUsingOverlayLayer)
        {
            return;
        }

        if (popup.Child is not Visual child)
        {
            return;
        }

        if (TopLevel.GetTopLevel(child) is not { } topLevel)
        {
            return;
        }

        topLevel.Background = Brushes.Transparent;
        topLevel.TransparencyBackgroundFallback = Brushes.Transparent;

        IntPtr nsWindow = topLevel.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && nsWindow != IntPtr.Zero
            && MacOSPopupGlass.IsAvailable())
        {
            // Liquid Glass is custom-installed, so keep Avalonia's own
            // NSVisualEffectView out of the popup window.
            topLevel.TransparencyLevelHint = LiquidGlassLevels;

            if (MacOSPopupGlass.InstallNativeMenuMaterial(nsWindow, cornerRadius: 8.0))
            {
                ApplyLiquidGlassChrome(child);
                return;
            }
        }

        if (UsesOpaqueRoundedMenuChrome(child))
        {
            topLevel.TransparencyLevelHint = TransparentLevels;
            return;
        }

        // Non-macOS, pre-macOS 26, or interop unavailable: ask Avalonia for
        // the most modern blur the platform supports.
        topLevel.TransparencyLevelHint = PreferredLevels;
    }

    private static void ApplyLiquidGlassChrome(Visual child)
    {
        switch (child)
        {
            case ContextMenu contextMenu:
                contextMenu.Background = Brushes.Transparent;
                contextMenu.BorderBrush = Brushes.Transparent;
                break;
            case MenuFlyoutPresenter presenter:
                presenter.Background = Brushes.Transparent;
                presenter.BorderBrush = Brushes.Transparent;
                break;
            case Border border:
                border.Background = Brushes.Transparent;
                border.BorderBrush = Brushes.Transparent;
                break;
        }
    }
}
