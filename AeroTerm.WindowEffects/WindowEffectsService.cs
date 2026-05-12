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

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // AppKit's animated zoom (`[NSWindow performZoom:]`, used by
            // Avalonia's WindowState.Maximized) emits a stream of
            // `windowDidResize` notifications during the animation. The
            // PropertyChanged-driven re-apply in
            // HandleMacOSWindowStateChanged runs once and at low
            // priority; it isn't enough on its own to keep the
            // transparent-titlebar / Liquid Glass / NSVisualEffectView
            // configuration intact across the animated transition.
            // Re-applying on every Resized event catches each frame of
            // the animation. The re-apply path is idempotent and only
            // makes a handful of objc_msgSend calls, so the per-frame
            // cost is negligible.
            this.window.Resized += this.OnMacOSWindowResized;
        }
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
    /// Gets a value indicating whether blur / transparency / Liquid Glass
    /// effects should currently be applied. False whenever the window is in
    /// macOS native full-screen mode — there is no desktop visible behind
    /// the window in that state, so the effects produce no visual benefit
    /// and only complicate the appearance of our chrome. The user-facing
    /// <see cref="IWindowEffectsSettings.EnableBlurBehind"/> setting is
    /// preserved unchanged and re-honoured on exit from full screen.
    /// </summary>
    private bool EffectiveBlurEnabled =>
        this.settings.EnableBlurBehind && !this.isMacFullScreen;

    /// <summary>
    /// Configures initial blur and transparency settings on the window.
    /// </summary>
    public void SetupBlurBehind()
    {
        this.window.TransparencyBackgroundFallback = Brushes.Transparent;
        this.window.Background = Brushes.Transparent;
        this.UpdateTransparencyLevelHint();
        this.UpdateBackgroundOpacity();
        this.ApplyDwmBackdrop();
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
    /// Reacts to a macOS <see cref="WindowState"/> transition by
    /// reapplying the native window configuration that AppKit can reset
    /// across these transitions.
    /// <para>
    /// On entering full screen the unified-style <c>NSToolbar</c> is
    /// detached so its translucent material no longer renders behind our
    /// custom tab bar in the AppKit-drawn fullscreen titlebar.
    /// <see cref="EffectiveBlurEnabled"/> is <c>false</c> while full
    /// screen, so the apply pipeline collapses transparency, opacity,
    /// and Liquid Glass to their "blur off" state.
    /// </para>
    /// <para>
    /// On leaving full screen, and also on <see cref="WindowState.Maximized"/>
    /// (which Avalonia implements via <c>[NSWindow zoom:]</c>) and
    /// <see cref="WindowState.Normal"/>, the transparent-titlebar
    /// configuration, the unified toolbar, the Liquid Glass backdrop,
    /// and the transparency level hint are reapplied. AppKit resets
    /// <c>setOpaque:</c>, the titlebar material view, and the content
    /// view's subview ordering when it zooms or returns the window to
    /// its standard frame, which silently degrades
    /// <c>ActualTransparencyLevel</c> to <c>None</c> unless we
    /// re-install our configuration. Each interop call is idempotent
    /// (e.g. <see cref="MacOSInterop.EnableUnifiedTitleBar"/> bails out
    /// if a toolbar is already attached).
    /// </para>
    /// </summary>
    /// <param name="state">The new <see cref="WindowState"/>.</param>
    public void HandleMacOSWindowStateChanged(WindowState state)
    {
        var nsWindow = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (nsWindow == IntPtr.Zero)
        {
            this.logger.LogInformation("macOS platform handle unavailable during WindowState change.");
            return;
        }

        bool isFullScreen = state == WindowState.FullScreen;
        bool wasFullScreen = this.isMacFullScreen;
        this.isMacFullScreen = isFullScreen;

        if (isFullScreen)
        {
            // Drop the unified NSToolbar so its translucent material does
            // not render behind our custom tab bar in the AppKit-drawn
            // fullscreen titlebar. Liquid Glass / transparency are handled
            // by the apply pipeline below via EffectiveBlurEnabled.
            MacOSInterop.DetachToolbar(nsWindow);
        }
        else
        {
            // Restore the in-window appearance. SetTransparentTitlebar /
            // unified toolbar must be reapplied for any non-fullscreen
            // state because AppKit resets these on exit from its
            // fullscreen space. The Maximized ↔ Normal transition is
            // bypassed via MacOSInterop.SetNSWindowFrameNoAnimation
            // (see MainWindow.ToggleMaximize), so the AppKit-animated
            // zoom is never invoked and no titlebar-state reset occurs
            // on that path — but a re-apply here is still cheap and
            // serves as a safety net.
            MacOSInterop.SetTransparentTitlebar(nsWindow);
            MacOSInterop.SetTitleBarMaterialHidden(nsWindow, this.ShouldHideTitleBarMaterial());
            MacOSInterop.EnableUnifiedTitleBar(nsWindow);
        }

        // Re-run the full apply pipeline. EffectiveBlurEnabled is false
        // in fullscreen, so this collapses transparency, opacity, and
        // Liquid Glass to their "blur off" state, and restores them on
        // exit / on a zoom that AppKit-reset the NSWindow configuration.
        this.UpdateTransparencyLevelHint();
        this.UpdateLiquidGlassBackdrop(nsWindow);
        this.UpdateBackgroundOpacity();

        // Schedule a second wave of re-applies *after* AppKit's animated
        // zoom completes. `[NSWindow performZoom:]` is animated; the
        // synchronous re-apply above runs while the animation is still
        // in flight, and the per-frame Resized handler stops once the
        // final frame is delivered. The staggered delays catch any
        // post-animation reset that AppKit may apply.
        _ = this.ReapplyAfterAnimationAsync(state);

        if (isFullScreen != wasFullScreen)
        {
            this.MacOSFullScreenChanged?.Invoke(isFullScreen);
        }
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

        if (!this.EffectiveBlurEnabled)
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
        if (!this.EffectiveBlurEnabled)
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

        if (this.EffectiveBlurEnabled)
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
        // On Windows 11 22H2+ we render Mica/Acrylic ourselves via DWM
        // (see ApplyDwmBackdrop) so that DWMWA_USE_IMMERSIVE_DARK_MODE
        // can drive the material's tonal variant. Avalonia's
        // compositional Mica/Acrylic surface paints on top of the DWM
        // backdrop and ignores the immersive dark mode attribute, which
        // is why MaterialTone has no visible effect when we let
        // Avalonia composite the material itself. Requesting Transparent
        // here tells Avalonia to leave the backdrop alone so the DWM
        // material — and our tone control — is what the user sees.
        if (this.UseDwmDirectBackdrop())
        {
            return WindowTransparencyLevel.Transparent;
        }

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
        if (this.EffectiveBlurEnabled)
        {
            this.window.TransparencyLevelHint = [this.GetRequestedTransparencyLevel()];
        }
        else
        {
            this.window.TransparencyLevelHint = [WindowTransparencyLevel.None];
        }
    }

    /// <summary>
    /// Returns whether we should bypass Avalonia's compositional blur on
    /// Windows and render Mica/Acrylic ourselves via
    /// <c>DWMWA_SYSTEMBACKDROP_TYPE</c>. This is the only path on which
    /// the user's <see cref="IWindowEffectsSettings.MaterialTone"/>
    /// choice can actually drive the backdrop tint, because Avalonia's
    /// composition surface paints the material itself and ignores the
    /// DWM immersive dark mode attribute. Requires Windows 11 22H2+ and
    /// the <see cref="BlurType.Mica"/> or <see cref="BlurType.Acrylic"/>
    /// material; older Windows builds and the Gaussian/Transparent
    /// blur types fall back to Avalonia's existing path.
    /// </summary>
    private bool UseDwmDirectBackdrop()
    {
        if (!this.EffectiveBlurEnabled)
        {
            return false;
        }

        if (!WindowsInterop.IsSystemBackdropTypeSupported())
        {
            return false;
        }

        return this.settings.BlurType == BlurType.Mica
            || this.settings.BlurType == BlurType.Acrylic;
    }

    /// <summary>
    /// Applies (or clears) the DWM-rendered system backdrop on Windows.
    /// When <see cref="UseDwmDirectBackdrop"/> is true the requested
    /// material is set via <c>DWMWA_SYSTEMBACKDROP_TYPE</c>; otherwise
    /// the attribute is reset to <c>DWMSBT_AUTO</c> so a previously
    /// installed backdrop does not linger after the user switches to a
    /// blur type Avalonia composites itself (Gaussian, Transparent) or
    /// disables blur entirely. A no-op on non-Windows platforms.
    /// </summary>
    private void ApplyDwmBackdrop()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var hwnd = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int backdropType = this.UseDwmDirectBackdrop()
            ? this.MapBlurTypeToDwmBackdrop()
            : 0;

        WindowsInterop.SetSystemBackdropType(hwnd, backdropType);
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
        return this.EffectiveBlurEnabled
            && (this.settings.BlurType == BlurType.Transparent
                || this.settings.BlurType == BlurType.LiquidGlass);
    }

    /// <summary>
    /// Returns whether Avalonia's behind-window
    /// <c>NSVisualEffectView</c> (used to back
    /// <see cref="WindowTransparencyLevel.AcrylicBlur"/>) should be
    /// hidden. It must be hidden for blur types we drive ourselves
    /// (Transparent / LiquidGlass) and unhidden for
    /// <see cref="BlurType.Acrylic"/>. After AppKit's animated zoom
    /// the behind-window view's <c>hidden</c> flag and frame can drift
    /// out of sync with our expected configuration; calling this
    /// alongside <see cref="MacOSInterop.RefitWindowEffectViews"/>
    /// keeps them aligned.
    /// </summary>
    private bool ShouldHideBehindWindowBlur()
    {
        return !(this.EffectiveBlurEnabled && this.settings.BlurType == BlurType.Acrylic);
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

        bool wantGlass = this.EffectiveBlurEnabled
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

    /// <summary>
    /// Re-applies the macOS native transparency configuration on every
    /// Avalonia <see cref="WindowBase.Resized"/> event. AppKit's
    /// animated <c>[NSWindow performZoom:]</c> streams resize
    /// notifications throughout the animation; this handler keeps our
    /// configuration intact across the transition. The per-frame cost
    /// is a handful of objc_msgSend calls.
    /// </summary>
    /// <param name="sender">The window raising the event.</param>
    /// <param name="e">The resize event arguments.</param>
    private void OnMacOSWindowResized(object? sender, WindowResizedEventArgs e)
    {
        if (this.isMacFullScreen)
        {
            return;
        }

        var nsWindow = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (nsWindow == IntPtr.Zero)
        {
            return;
        }

        MacOSInterop.SetTransparentTitlebar(nsWindow);
        MacOSInterop.SetTitleBarMaterialHidden(nsWindow, this.ShouldHideTitleBarMaterial());

        // Re-fit and re-promote Avalonia's behind-window NSVisualEffectView
        // (used for Acrylic blur) and our NSGlassEffectView so they
        // continue to cover the contentView after the zoom resizes it.
        MacOSInterop.RefitWindowEffectViews(nsWindow, this.ShouldHideBehindWindowBlur());
        this.UpdateLiquidGlassBackdrop(nsWindow);
    }

    private async Task ReapplyAfterAnimationAsync(WindowState targetState)
    {
        // 50 ms / 200 ms / 500 ms covers the standard AppKit zoom
        // animation duration (~250 ms) with margin on both sides. Each
        // re-apply path is idempotent; the cost is a handful of
        // objc_msgSend calls per tick.
        foreach (int delay in new[] { 50, 200, 500 })
        {
            await Task.Delay(delay).ConfigureAwait(true);

            // Abort if the user has moved on to a different WindowState
            // in the meantime — that transition will have its own
            // re-apply pipeline.
            if (this.window.WindowState != targetState)
            {
                return;
            }

            var nsWindow = this.window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (nsWindow == IntPtr.Zero || this.isMacFullScreen)
            {
                continue;
            }

            MacOSInterop.SetTransparentTitlebar(nsWindow);
            MacOSInterop.SetTitleBarMaterialHidden(nsWindow, this.ShouldHideTitleBarMaterial());
            MacOSInterop.EnableUnifiedTitleBar(nsWindow);
            MacOSInterop.RefitWindowEffectViews(nsWindow, this.ShouldHideBehindWindowBlur());
            this.UpdateLiquidGlassBackdrop(nsWindow);
        }
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
            || !this.EffectiveBlurEnabled
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
