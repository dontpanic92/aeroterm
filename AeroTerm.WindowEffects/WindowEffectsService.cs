// <copyright file="WindowEffectsService.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

/// <summary>
/// Manages all blur, transparency, and opacity behavior for the main window.
/// Subscribes to <see cref="IWindowEffectsSettings"/> property changes for live preview
/// of blur-related settings changes.
/// </summary>
public sealed class WindowEffectsService
{
    private readonly Window window;
    private readonly IWindowEffectsSettings settings;
    private readonly ILogger<WindowEffectsService> logger;
    private bool isMacFullScreen;
    private bool isDialogOpen;
    private bool liquidGlassFallbackLogged;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowEffectsService"/> class.
    /// </summary>
    /// <param name="window">The main window to manage effects for.</param>
    /// <param name="settings">Window effects settings.</param>
    /// <param name="logger">Logger instance.</param>
    public WindowEffectsService(Window window, IWindowEffectsSettings settings, ILogger<WindowEffectsService> logger)
    {
        this.window = window;
        this.settings = settings;
        this.logger = logger;

        this.settings.PropertyChanged += this.OnSettingsPropertyChanged;
    }

    /// <summary>
    /// Raised when the background brush needs to be applied to the window
    /// chrome and editor border. The handler receives the computed brush.
    /// </summary>
    public event Action<IBrush>? BackgroundBrushChanged;

    /// <summary>
    /// Raised when the background alpha value changes. The handler receives
    /// the computed alpha byte (0–255).
    /// </summary>
    public event Action<byte>? BackgroundAlphaChanged;

    /// <summary>
    /// Raised when the macOS full-screen state changes. The handler receives
    /// <c>true</c> when entering full screen, <c>false</c> when leaving.
    /// </summary>
    public event Action<bool>? MacOSFullScreenChanged;

    /// <summary>
    /// Raised when the actual transparency level does not match the requested
    /// level. The handler receives the requested and actual transparency levels
    /// so the consumer can display an appropriate notification.
    /// </summary>
    public event Action<WindowTransparencyLevel, WindowTransparencyLevel>? TransparencyMismatchDetected;

    /// <summary>
    /// Gets or sets the current background color (RGB integer).
    /// </summary>
    public int CurrentBackgroundColor { get; set; }

    /// <summary>
    /// Gets a value indicating whether macOS full-screen mode is active.
    /// </summary>
    public bool IsMacFullScreen => this.isMacFullScreen;

    /// <summary>
    /// Configures initial blur and transparency settings on the window.
    /// </summary>
    public void SetupBlurBehind()
    {
        this.window.TransparencyBackgroundFallback = Brushes.Transparent;
        this.window.Background = Brushes.Transparent;
        this.UpdateTransparencyLevelHint();
        this.UpdateBackgroundOpacity();
        this.ApplyMaterialTone();
    }

    /// <summary>
    /// Defers macOS native transparency setup to
    /// <see cref="DispatcherPriority.Background"/> so that Avalonia finishes
    /// processing the transparency level hint before we override NSWindow
    /// properties. A no-op on non-macOS platforms.
    /// </summary>
    public void DeferMacOSNativeTransparency()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var nsWindow = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (nsWindow == IntPtr.Zero)
                {
                    this.logger.LogInformation("macOS platform handle unavailable, skipping native transparency setup.");
                    return;
                }

                MacOSInterop.SetTransparentTitlebar(nsWindow);
                MacOSInterop.SetTitleBarMaterialHidden(nsWindow, this.ShouldHideTitleBarMaterial());
                MacOSInterop.EnableUnifiedTitleBar(nsWindow);
                MacOSInterop.SetWindowIconFromBundle(nsWindow);
                this.UpdateLiquidGlassBackdrop(nsWindow);
                this.ApplyMaterialTone(nsWindow);
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Handles macOS window activation by reapplying transparent titlebar
    /// settings after Avalonia finishes any internal NSWindow style resets.
    /// Skipped during full screen since macOS manages the titlebar.
    /// A no-op on non-macOS platforms.
    /// </summary>
    public async void HandleMacOSActivation()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || this.isMacFullScreen)
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var nsWindow = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (nsWindow == IntPtr.Zero)
            {
                this.logger.LogInformation("macOS platform handle unavailable in HandleMacOSActivation.");
                return;
            }

            MacOSInterop.SetTransparentTitlebar(nsWindow);
            MacOSInterop.SetTitleBarMaterialHidden(nsWindow, this.ShouldHideTitleBarMaterial());
            MacOSInterop.EnableUnifiedTitleBar(nsWindow);
            this.UpdateLiquidGlassBackdrop(nsWindow);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "HandleMacOSActivation failed: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Handles macOS full-screen state transitions by configuring native
    /// window properties and updating background opacity.
    /// </summary>
    /// <summary>
    /// Reacts to a macOS full-screen state transition. On entering full
    /// screen the unified-style <c>NSToolbar</c> is detached so its
    /// translucent material no longer renders behind our custom tab bar.
    /// On leaving full screen the transparent-titlebar configuration and
    /// the unified toolbar are reapplied so the in-window appearance
    /// matches the pre-fullscreen state.
    /// </summary>
    /// <param name="isFullScreen"><c>true</c> when entering full screen.</param>
    public void HandleMacOSFullScreenTransition(bool isFullScreen)
    {
        var nsWindow = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (nsWindow == IntPtr.Zero)
        {
            this.logger.LogInformation("macOS platform handle unavailable during WindowState change.");
            return;
        }

        this.isMacFullScreen = isFullScreen;

        if (isFullScreen)
        {
            MacOSInterop.DetachToolbar(nsWindow);
            MacOSInterop.RemoveLiquidGlassBackdrop(nsWindow);
        }
        else
        {
            MacOSInterop.SetTransparentTitlebar(nsWindow);
            MacOSInterop.SetTitleBarMaterialHidden(nsWindow, this.ShouldHideTitleBarMaterial());
            MacOSInterop.EnableUnifiedTitleBar(nsWindow);
            this.UpdateLiquidGlassBackdrop(nsWindow);
        }

        this.UpdateBackgroundOpacity();
        this.MacOSFullScreenChanged?.Invoke(isFullScreen);
    }

    /// <summary>
    /// Activates platform-specific blur preservation so the window's
    /// acrylic/mica/blur effect stays fully active while a child dialog
    /// has focus.
    /// </summary>
    /// <returns>The native handle used for preservation, for passing to <see cref="EndDialogBlurPreservation"/>.</returns>
    public IntPtr BeginDialogBlurPreservation()
    {
        this.isDialogOpen = true;

        if (!this.settings.EnableBlurBehind)
        {
            return IntPtr.Zero;
        }

        IntPtr nativeHandle = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (nativeHandle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSInterop.ForceBlurActive(nativeHandle);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            int dwmBackdropType = this.MapBlurTypeToDwmBackdrop();
            WindowsInterop.EnableBlurPreservation(nativeHandle, dwmBackdropType);
        }

        return nativeHandle;
    }

    /// <summary>
    /// Deactivates platform-specific blur preservation, restoring normal
    /// window activation behavior. Then forces a transparency re-negotiation
    /// with the compositor.
    /// </summary>
    /// <param name="nativeHandle">
    /// The native handle returned by <see cref="BeginDialogBlurPreservation"/>.
    /// </param>
    public void EndDialogBlurPreservation(IntPtr nativeHandle)
    {
        this.isDialogOpen = false;

        if (nativeHandle != IntPtr.Zero)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                MacOSInterop.ResetBlurState(nativeHandle);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                WindowsInterop.DisableBlurPreservation(nativeHandle);
            }
        }

        // Force Avalonia to re-negotiate the transparency level with the compositor.
        this.window.TransparencyLevelHint = [WindowTransparencyLevel.None];
        this.SetupBlurBehind();
    }

    /// <summary>
    /// Checks whether the actual transparency level matches the requested
    /// level and raises <see cref="TransparencyMismatchDetected"/> if they differ.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CheckTransparencyMismatchAsync()
    {
        if (!this.settings.EnableBlurBehind)
        {
            return;
        }

        var requestedLevel = this.GetRequestedTransparencyLevel();

        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        for (int i = 0; i < 5; i++)
        {
            if (this.window.ActualTransparencyLevel == requestedLevel)
            {
                return;
            }

            await Task.Delay(100);
        }

        var actualLevel = this.window.ActualTransparencyLevel;
        if (actualLevel == requestedLevel)
        {
            return;
        }

        this.TransparencyMismatchDetected?.Invoke(requestedLevel, actualLevel);
    }

    /// <summary>
    /// Recalculates and applies background opacity and raises
    /// <see cref="BackgroundBrushChanged"/> and <see cref="BackgroundAlphaChanged"/>.
    /// </summary>
    public void UpdateBackgroundOpacity()
    {
        Color tintColor = PlatformHelper.GetAvaloniaColor(this.CurrentBackgroundColor, 1f);
        Color brushColor;
        float opacity;

        if (this.settings.EnableBlurBehind)
        {
            (brushColor, opacity) = AcrylicColorMath.Compose(
                tintColor,
                this.settings.BackgroundTintOpacity,
                this.settings.BackgroundMaterialOpacity);
        }
        else
        {
            brushColor = tintColor;
            opacity = 1f;
        }

        var argb = Color.FromArgb((byte)(opacity * 255), brushColor.R, brushColor.G, brushColor.B);
        IBrush backgroundBrush = new SolidColorBrush(argb);

        this.BackgroundBrushChanged?.Invoke(backgroundBrush);
        this.BackgroundAlphaChanged?.Invoke((byte)(opacity * 255));
    }

    private WindowTransparencyLevel GetRequestedTransparencyLevel()
    {
        return this.settings.BlurType switch
        {
            BlurType.Gaussian => WindowTransparencyLevel.Blur,
            BlurType.Acrylic => WindowTransparencyLevel.AcrylicBlur,
            BlurType.Mica => WindowTransparencyLevel.Mica,
            BlurType.Transparent => WindowTransparencyLevel.Transparent,

            // Liquid Glass needs no Avalonia-managed material behind the
            // window — we install our own NSGlassEffectView. Request
            // Transparent so Avalonia removes its NSVisualEffectView and
            // the glass surface shows through. On non-macOS / macOS < 26
            // this path is still selected but InstallLiquidGlassBackdrop
            // no-ops, leaving a plain transparent window (matching the
            // documented fallback behavior of BlurType.LiquidGlass).
            BlurType.LiquidGlass => WindowTransparencyLevel.Transparent,
            _ => WindowTransparencyLevel.None,
        };
    }

    private void UpdateTransparencyLevelHint()
    {
        if (this.settings.EnableBlurBehind)
        {
            this.window.TransparencyLevelHint = [this.GetRequestedTransparencyLevel()];
        }
        else
        {
            this.window.TransparencyLevelHint = [WindowTransparencyLevel.None];
        }
    }

    private int MapBlurTypeToDwmBackdrop()
    {
        return this.settings.BlurType switch
        {
            BlurType.Gaussian => 0,
            BlurType.Acrylic => 3, // DWMSBT_TRANSIENTWINDOW
            BlurType.Mica => 2, // DWMSBT_MAINWINDOW
            BlurType.Transparent => 0,
            BlurType.LiquidGlass => 0, // macOS-only effect; no DWM equivalent.
            _ => 0,
        };
    }

    /// <summary>
    /// Returns whether Avalonia's internal titlebar material view should be
    /// hidden. It must be hidden when the user selects the Transparent or
    /// LiquidGlass blur type on macOS, because the
    /// <c>NSVisualEffectMaterialTitlebar</c> view that Avalonia inserts
    /// renders as opaque without a behind-window blur, which would obscure
    /// the (transparent) titlebar area or stack on top of the Liquid Glass
    /// backdrop.
    /// </summary>
    private bool ShouldHideTitleBarMaterial()
    {
        return this.settings.EnableBlurBehind
            && (this.settings.BlurType == BlurType.Transparent
                || this.settings.BlurType == BlurType.LiquidGlass);
    }

    /// <summary>
    /// Installs or removes the macOS Liquid Glass backdrop in response to
    /// the current settings. When <see cref="BlurType.LiquidGlass"/> is
    /// selected and the OS supports it (macOS 26+), an
    /// <c>NSGlassEffectView</c> is inserted as the back-most subview of
    /// the NSWindow's contentView. On older macOS the effect silently
    /// falls back to a plain transparent window and a single info-level
    /// log message is emitted (per-process).
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    private void UpdateLiquidGlassBackdrop(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        bool wantGlass = this.settings.EnableBlurBehind
            && this.settings.BlurType == BlurType.LiquidGlass;

        if (!wantGlass)
        {
            MacOSInterop.RemoveLiquidGlassBackdrop(nsWindow);
            return;
        }

        if (!MacOSInterop.IsMacOS26OrLater())
        {
            if (!this.liquidGlassFallbackLogged)
            {
                this.liquidGlassFallbackLogged = true;
                this.logger.LogInformation(
                    "BlurType.LiquidGlass requested but NSGlassEffectView is unavailable on this macOS version; falling back to a plain transparent window.");
            }

            return;
        }

        MacOSInterop.InstallLiquidGlassBackdrop(nsWindow);
    }

    private void UpdateBlurPreservationForCurrentSettings()
    {
        if (!this.isDialogOpen || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        int dwmBackdropType = this.MapBlurTypeToDwmBackdrop();
        IntPtr hwnd = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        WindowsInterop.UpdateStoredBackdropType(hwnd, dwmBackdropType);
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IWindowEffectsSettings.EnableBlurBehind):
            case nameof(IWindowEffectsSettings.BlurType):
                Dispatcher.UIThread.Post(() =>
                {
                    this.UpdateBlurPreservationForCurrentSettings();
                    this.SetupBlurBehind();
                    this.DeferMacOSNativeTransparency();
                    if (!this.isDialogOpen)
                    {
                        Dispatcher.UIThread.InvokeAsync(async Task () =>
                            await this.CheckTransparencyMismatchAsync());
                    }
                });
                break;

            case nameof(IWindowEffectsSettings.BackgroundTintOpacity):
            case nameof(IWindowEffectsSettings.BackgroundMaterialOpacity):
                Dispatcher.UIThread.Post(() => this.UpdateBackgroundOpacity());
                break;

            case nameof(IWindowEffectsSettings.MaterialTone):
                Dispatcher.UIThread.Post(() => this.ApplyMaterialTone());
                break;
        }
    }

    /// <summary>
    /// Applies <see cref="IWindowEffectsSettings.MaterialTone"/> to the
    /// underlying native window. No-op when blur is disabled or when
    /// <see cref="BlurType.Transparent"/> is selected (no material to
    /// tint), and no-op on Linux.
    /// </summary>
    private void ApplyMaterialTone()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var nsWindow = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            this.ApplyMaterialTone(nsWindow);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var hwnd = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            this.ApplyMaterialTone(hwnd);
        }
    }

    /// <summary>
    /// Platform-specific application of
    /// <see cref="IWindowEffectsSettings.MaterialTone"/>, called with a
    /// pre-resolved native window handle (HWND on Windows, NSWindow* on
    /// macOS) to avoid resolving the handle twice when called from the
    /// macOS deferred dispatcher path.
    /// </summary>
    /// <param name="nativeHandle">The native window handle.</param>
    private void ApplyMaterialTone(IntPtr nativeHandle)
    {
        if (nativeHandle == IntPtr.Zero
            || !this.settings.EnableBlurBehind
            || this.settings.BlurType == BlurType.Transparent)
        {
            return;
        }

        bool dark = this.settings.MaterialTone == MaterialTone.Dark;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsInterop.SetImmersiveDarkMode(nativeHandle, dark);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            MacOSInterop.SetWindowAppearance(nativeHandle, dark);
        }
    }
}
