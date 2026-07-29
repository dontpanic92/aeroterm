// <copyright file="MacOSInterop.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

using System.Runtime.InteropServices;

/// <summary>
/// Provides native macOS interop helpers for configuring NSWindow properties
/// that Avalonia does not expose through its public API.
/// </summary>
public static class MacOSInterop
{
    /// <summary>
    /// <c>NSWindowToolbarStyleUnifiedCompact</c> — gives a slim (~38pt)
    /// unified titlebar with vertically centered traffic-light buttons.
    /// </summary>
    private const long NSWindowToolbarStyleUnifiedCompact = 4;

    /// <summary>
    /// Window level for the full-screen notch overlay:
    /// <c>NSStatusWindowLevel</c> (25) + 1, which also places it above
    /// <c>NSMainMenuWindowLevel</c> (24) so the auto-revealing menu bar
    /// does not paint over it.
    /// </summary>
    private const long NotchOverlayWindowLevel = 26;

    /// <summary><c>NSWindowStyleMaskFullScreen</c> (1 &lt;&lt; 14).</summary>
    private const long NSWindowStyleMaskFullScreen = 1 << 14;

    /// <summary><c>NSApplicationPresentationAutoHideDock</c> (1 &lt;&lt; 0).</summary>
    private const long NSApplicationPresentationAutoHideDock = 1 << 0;

    /// <summary><c>NSApplicationPresentationHideDock</c> (1 &lt;&lt; 1).</summary>
    private const long NSApplicationPresentationHideDock = 1 << 1;

    /// <summary><c>NSApplicationPresentationAutoHideMenuBar</c> (1 &lt;&lt; 2).</summary>
    private const long NSApplicationPresentationAutoHideMenuBar = 1 << 2;

    /// <summary><c>NSApplicationPresentationHideMenuBar</c> (1 &lt;&lt; 3).</summary>
    private const long NSApplicationPresentationHideMenuBar = 1 << 3;

    /// <summary><c>NSApplicationPresentationFullScreen</c> (1 &lt;&lt; 10).</summary>
    private const long NSApplicationPresentationFullScreen = 1 << 10;

    /// <summary>
    /// Collection behavior for the full-screen notch overlay:
    /// <c>IgnoresCycle</c> (1 &lt;&lt; 6) | <c>FullScreenAuxiliary</c>
    /// (1 &lt;&lt; 8).
    /// <para>
    /// <c>FullScreenAuxiliary</c> is what lets the overlay share the main
    /// window's full-screen space. <c>CanJoinAllSpaces</c> and
    /// <c>Stationary</c> are deliberately NOT set: together they pin the
    /// overlay to every space and hold it still during a space switch, so it
    /// hangs stale over the incoming desktop for the whole swipe animation.
    /// Without them the band belongs to the full-screen space and slides away
    /// with it, as any ordinary window of that space would.
    /// </para>
    /// </summary>
    private const long NotchOverlayCollectionBehavior = 64 | 256;

    /// <summary>
    /// Tag value previously used to mark our backdrop. Retained as a
    /// constant for documentation purposes only — we now identify the
    /// installed instance by class lookup (<c>isKindOfClass:</c>) because
    /// <c>NSView</c>'s <c>tag</c> property is read-only and
    /// <c>setTag:</c> raises an unrecognized-selector exception on
    /// <c>NSGlassEffectView</c>.
    /// </summary>
    private const long LiquidGlassBackdropTag = 0x4145524F; // 'AERO'

    /// <summary>
    /// Cached result of <see cref="IsMacOS26OrLater"/>. Reset only on
    /// process exit (the macOS version doesn't change at runtime).
    /// </summary>
    private static bool? isMacOS26OrLaterCached;

    /// <summary>
    /// Returns the current <c>isOpaque</c> flag of an NSWindow. Useful
    /// for diagnostics — if a transparent window appears solid, this
    /// flag returning <c>true</c> indicates AppKit (or some other code
    /// path) re-asserted <c>setOpaque:YES</c>. Returns <c>false</c> on
    /// non-macOS platforms.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <returns><c>true</c> if the window is currently opaque.</returns>
    public static bool IsNSWindowOpaque(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.ObjCMsgSendBoolRet(
            nsWindow,
            NativeMethods.SelRegisterName("isOpaque"));
    }

    /// <summary>
    /// Returns whether the NSWindow currently matches its screen's
    /// visible frame (i.e. is "zoomed" in AppKit terms). Mirrors
    /// <c>[NSWindow isZoomed]</c>.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <returns><c>true</c> when the window is at the zoomed frame.</returns>
    public static bool IsNSWindowZoomed(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.ObjCMsgSendBoolRet(
            nsWindow,
            NativeMethods.SelRegisterName("isZoomed"));
    }

    /// <summary>
    /// Returns the current NSWindow frame in screen coordinates. Returns
    /// an empty rect on non-macOS platforms or when the handle is null.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <returns>The current frame.</returns>
    public static NSRect GetNSWindowFrame(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return default;
        }

        return NativeMethods.ObjCMsgSendRectRet(
            nsWindow,
            NativeMethods.SelRegisterName("frame"));
    }

    /// <summary>
    /// Returns the visible frame of the screen currently containing the
    /// NSWindow (the screen rect minus menu bar and dock). Used as the
    /// target frame when maximizing without animation.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <returns>The screen's visible frame, or empty on non-macOS.</returns>
    public static NSRect GetNSWindowScreenVisibleFrame(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return default;
        }

        IntPtr screen = NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("screen"));
        if (screen == IntPtr.Zero)
        {
            return default;
        }

        return NativeMethods.ObjCMsgSendRectRet(
            screen,
            NativeMethods.SelRegisterName("visibleFrame"));
    }

    /// <summary>
    /// Queries the top safe-area geometry of the screen currently hosting
    /// the NSWindow, for laying out chrome in macOS native full screen when
    /// the app has opted out of display safe-area compatibility mode (see
    /// <c>NSPrefersDisplaySafeAreaCompatibilityMode</c> in <c>Info.plist</c>).
    /// <para>
    /// With that opt-out the full-screen window spans the entire display,
    /// including the band occupied by the camera housing, so the app becomes
    /// responsible for keeping interactive chrome out from under the notch.
    /// The returned rectangle boundaries are converted into window-local
    /// points, measured from the window's left edge.
    /// </para>
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <param name="area">The resolved safe-area geometry when successful.</param>
    /// <returns>
    /// <c>true</c> when the hosting screen has a camera housing and its
    /// geometry could be resolved; <c>false</c> on non-macOS platforms, on
    /// macOS releases without the required APIs (pre-12), and on displays
    /// without a notch (external monitors, older built-in displays).
    /// </returns>
    public static bool TryGetScreenTopSafeArea(IntPtr nsWindow, out MacTopSafeArea area)
    {
        area = default;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return false;
        }

        IntPtr screen = NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("screen"));
        if (screen == IntPtr.Zero)
        {
            return false;
        }

        // safeAreaInsets / auxiliaryTop*Area are macOS 12+. The bundle
        // declares LSMinimumSystemVersion 11.0, so probe before sending.
        var safeAreaInsetsSel = NativeMethods.SelRegisterName("safeAreaInsets");
        var auxLeftSel = NativeMethods.SelRegisterName("auxiliaryTopLeftArea");
        var auxRightSel = NativeMethods.SelRegisterName("auxiliaryTopRightArea");
        var respondsSel = NativeMethods.SelRegisterName("respondsToSelector:");

        if (!NativeMethods.ObjCMsgSendPtrRetBool(screen, respondsSel, safeAreaInsetsSel) ||
            !NativeMethods.ObjCMsgSendPtrRetBool(screen, respondsSel, auxLeftSel) ||
            !NativeMethods.ObjCMsgSendPtrRetBool(screen, respondsSel, auxRightSel))
        {
            return false;
        }

        NSEdgeInsets insets = NativeMethods.ObjCMsgSendEdgeInsetsRet(screen, safeAreaInsetsSel);
        if (insets.Top <= 0)
        {
            // No camera housing on this display.
            return false;
        }

        // Both auxiliary rects are in screen coordinates. The notch spans
        // the horizontal gap between them; either rect being empty means
        // AppKit could not describe the housing, so bail rather than guess.
        NSRect auxLeft = NativeMethods.ObjCMsgSendRectRet(screen, auxLeftSel);
        NSRect auxRight = NativeMethods.ObjCMsgSendRectRet(screen, auxRightSel);
        if (auxLeft.Width <= 0 || auxRight.Width <= 0)
        {
            return false;
        }

        double notchLeft = auxLeft.X + auxLeft.Width;
        double notchRight = auxRight.X;
        if (notchRight <= notchLeft)
        {
            return false;
        }

        NSRect windowFrame = NativeMethods.ObjCMsgSendRectRet(
            nsWindow,
            NativeMethods.SelRegisterName("frame"));

        area = new MacTopSafeArea(
            insets.Top,
            notchLeft - windowFrame.X,
            notchRight - windowFrame.X);
        return true;
    }

    /// <summary>
    /// Queries the unused band left above a macOS native full-screen window
    /// on a display with a camera housing.
    /// <para>
    /// AppKit clamps native full-screen windows to the safe area and paints
    /// the remainder black; the clamp cannot be lifted (neither
    /// <c>setFrame:</c> nor <c>window:willUseFullScreenContentSize:</c> can
    /// grow the window into it). A floating auxiliary window can however be
    /// placed over that band, which is how this band geometry is used.
    /// </para>
    /// </summary>
    /// <param name="nsWindow">The full-screen NSWindow handle.</param>
    /// <param name="band">The resolved band geometry when successful.</param>
    /// <returns>
    /// <c>true</c> when the window currently leaves an unused band above
    /// itself and the hosting screen reports a camera housing; otherwise
    /// <c>false</c> (non-macOS, pre-macOS 12, no notch, or not full screen).
    /// </returns>
    public static bool TryGetFullScreenNotchBand(IntPtr nsWindow, out MacNotchBand band)
    {
        band = default;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return false;
        }

        IntPtr screen = NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("screen"));
        if (screen == IntPtr.Zero)
        {
            return false;
        }

        var auxLeftSel = NativeMethods.SelRegisterName("auxiliaryTopLeftArea");
        var auxRightSel = NativeMethods.SelRegisterName("auxiliaryTopRightArea");
        var respondsSel = NativeMethods.SelRegisterName("respondsToSelector:");

        // auxiliaryTop*Area are macOS 12+; the bundle targets 11.0.
        if (!NativeMethods.ObjCMsgSendPtrRetBool(screen, respondsSel, auxLeftSel) ||
            !NativeMethods.ObjCMsgSendPtrRetBool(screen, respondsSel, auxRightSel))
        {
            return false;
        }

        NSRect screenFrame = NativeMethods.ObjCMsgSendRectRet(
            screen,
            NativeMethods.SelRegisterName("frame"));
        NSRect windowFrame = NativeMethods.ObjCMsgSendRectRet(
            nsWindow,
            NativeMethods.SelRegisterName("frame"));

        // The band is whatever vertical space the (clamped) full-screen
        // window leaves above itself. Derived from the actual frames rather
        // than safeAreaInsets.top, which is one point smaller in practice.
        double height = screenFrame.Height - windowFrame.Height;
        if (height <= 0)
        {
            return false;
        }

        NSRect auxLeft = NativeMethods.ObjCMsgSendRectRet(screen, auxLeftSel);
        NSRect auxRight = NativeMethods.ObjCMsgSendRectRet(screen, auxRightSel);
        if (auxLeft.Width <= 0 || auxRight.Width <= 0)
        {
            return false;
        }

        double notchLeft = (auxLeft.X + auxLeft.Width) - screenFrame.X;
        double notchRight = auxRight.X - screenFrame.X;
        if (notchRight <= notchLeft)
        {
            return false;
        }

        band = new MacNotchBand(
            height,
            screenFrame.Width,
            notchLeft,
            notchRight,
            screenFrame.Y + screenFrame.Height,
            screenFrame.X);
        return true;
    }

    /// <summary>
    /// Configures a borderless NSWindow so it floats inside the band above a
    /// native full-screen window, in the manner used by "dynamic island"
    /// style utilities.
    /// <para>
    /// The level is raised above <c>NSStatusWindowLevel</c> — and therefore
    /// above <c>NSMainMenuWindowLevel</c> — so the overlay is not hidden by
    /// the menu bar when it auto-reveals, and
    /// <c>NSWindowCollectionBehaviorFullScreenAuxiliary</c> lets it share the
    /// full-screen space instead of being pushed to another one.
    /// </para>
    /// </summary>
    /// <param name="nsWindow">The overlay NSWindow handle.</param>
    public static void ConfigureNotchOverlayWindow(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        // NSStatusWindowLevel (25) + 1 — above the menu bar's level (24).
        NativeMethods.ObjCMsgSendLong(
            nsWindow,
            NativeMethods.SelRegisterName("setLevel:"),
            NotchOverlayWindowLevel);

        NativeMethods.ObjCMsgSendLong(
            nsWindow,
            NativeMethods.SelRegisterName("setCollectionBehavior:"),
            NotchOverlayCollectionBehavior);

        // Keep the overlay alive across app deactivation; visibility is
        // driven explicitly by the owner instead.
        NativeMethods.ObjCMsgSendBool(
            nsWindow,
            NativeMethods.SelRegisterName("setHidesOnDeactivate:"),
            false);

        NativeMethods.ObjCMsgSendBool(
            nsWindow,
            NativeMethods.SelRegisterName("setOpaque:"),
            false);

        // orderFrontRegardless — order in without activating the app, which
        // would steal key focus from the terminal in the full-screen window.
        NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("orderFrontRegardless"));
    }

    /// <summary>
    /// Hides or restores the auto-revealing macOS full-screen chrome (menu
    /// bar and window title bar) for the duration of the notch overlay.
    /// <para>
    /// AppKit reveals both whenever the pointer reaches the top of the
    /// screen — precisely where the tab strip lives once it moves into the
    /// notch band. The reveal cannot be blocked by suppressing AppKit's
    /// detection window (it is recreated) nor by resetting the title bar's
    /// alpha (that merely races the fade-in and flickers). Switching the
    /// presentation options from the auto-hide variants to the outright
    /// hidden ones stops the reveal from happening at all.
    /// </para>
    /// </summary>
    /// <param name="nsWindow">The full-screen NSWindow handle.</param>
    /// <param name="hidden">
    /// <c>true</c> to prevent the chrome from revealing; <c>false</c> to
    /// restore the standard auto-hide behavior.
    /// </param>
    /// <returns><c>true</c> when the options were applied.</returns>
    public static bool SetFullScreenChromeHidden(IntPtr nsWindow, bool hidden)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return false;
        }

        // Only valid while the window is genuinely in a full-screen space —
        // AppKit raises an exception for combinations that do not match the
        // current state, and an Objective-C exception cannot be caught here.
        long styleMask = (long)(nint)NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("styleMask"));
        if ((styleMask & NSWindowStyleMaskFullScreen) == 0)
        {
            return false;
        }

        IntPtr appClass = NativeMethods.ObjCGetClass("NSApplication");
        if (appClass == IntPtr.Zero)
        {
            return false;
        }

        IntPtr app = NativeMethods.ObjCMsgSend(
            appClass,
            NativeMethods.SelRegisterName("sharedApplication"));
        if (app == IntPtr.Zero)
        {
            return false;
        }

        long options = hidden
            ? NSApplicationPresentationFullScreen | NSApplicationPresentationHideDock | NSApplicationPresentationHideMenuBar
            : NSApplicationPresentationFullScreen | NSApplicationPresentationAutoHideDock | NSApplicationPresentationAutoHideMenuBar;

        NativeMethods.ObjCMsgSendLong(
            app,
            NativeMethods.SelRegisterName("setPresentationOptions:"),
            options);
        return true;
    }

    /// <summary>
    /// Returns whether this application is the active (frontmost) one,
    /// mirroring <c>[NSApp isActive]</c>. Used to tell a genuine app switch
    /// apart from focus merely moving to the app's own notch overlay.
    /// </summary>
    /// <returns><c>true</c> when the application is active.</returns>
    public static bool IsApplicationActive()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return true;
        }

        IntPtr appClass = NativeMethods.ObjCGetClass("NSApplication");
        if (appClass == IntPtr.Zero)
        {
            return true;
        }

        IntPtr app = NativeMethods.ObjCMsgSend(
            appClass,
            NativeMethods.SelRegisterName("sharedApplication"));
        if (app == IntPtr.Zero)
        {
            return true;
        }

        return NativeMethods.ObjCMsgSendBoolRet(
            app,
            NativeMethods.SelRegisterName("isActive"));
    }

    /// <summary>
    /// Returns the NSWindow's current window level, mirroring
    /// <c>[NSWindow level]</c>. Exposed for diagnostics: the notch overlay
    /// must outrank <c>NSMainMenuWindowLevel</c> (24) to keep the
    /// auto-revealing menu bar from painting over it.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <returns>The window level, or 0 on non-macOS platforms.</returns>
    public static long GetNSWindowLevel(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return 0;
        }

        return (long)(nint)NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("level"));
    }

    /// <summary>
    /// Returns whether the NSWindow still carries the window level applied
    /// by <see cref="ConfigureNotchOverlayWindow"/>.
    /// <para>
    /// Avalonia re-asserts its own level whenever it (re)applies window
    /// properties such as topmost, or when the window is shown again after
    /// being hidden. If that lands after our configuration the overlay drops
    /// below <c>NSMainMenuWindowLevel</c> and the auto-revealing menu bar
    /// starts painting over the tab strip, so callers poll this and
    /// reconfigure when it returns <c>false</c>.
    /// </para>
    /// </summary>
    /// <param name="nsWindow">The overlay NSWindow handle.</param>
    /// <returns><c>true</c> when the overlay level is still in effect.</returns>
    public static bool IsNotchOverlayLevelIntact(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return true;
        }

        long level = (long)(nint)NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("level"));

        return level == NotchOverlayWindowLevel;
    }

    /// <summary>
    /// Returns the current pointer location in AppKit's global (bottom-left
    /// origin) screen coordinates, mirroring <c>[NSEvent mouseLocation]</c>.
    /// Polling this is how the notch overlay decides to step aside for the
    /// menu bar, since it cannot rely on tracking areas while hidden.
    /// </summary>
    /// <returns>The pointer location, or the origin on non-macOS platforms.</returns>
    public static NSPoint GetMouseLocation()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return default;
        }

        IntPtr nsEvent = NativeMethods.ObjCGetClass("NSEvent");
        if (nsEvent == IntPtr.Zero)
        {
            return default;
        }

        return NativeMethods.ObjCMsgSendPointRet(
            nsEvent,
            NativeMethods.SelRegisterName("mouseLocation"));
    }

    /// <summary>
    /// Sets the NSWindow frame directly with
    /// <c>setFrame:display:YES animate:NO</c>, bypassing AppKit's
    /// animated zoom. This eliminates the resize-snapshot flash that
    /// AppKit produces during an animated <c>performZoom:</c>, in which
    /// our semi-transparent chrome is briefly rendered at full opacity
    /// inside the snapshot, and which can also leave Avalonia's
    /// <c>NSVisualEffectView</c> (used for Acrylic blur) in a state
    /// where it no longer renders blur. The instantaneous frame swap
    /// avoids both problems.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <param name="frame">The target frame, in screen coordinates.</param>
    public static void SetNSWindowFrameNoAnimation(IntPtr nsWindow, NSRect frame)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ObjCMsgSendRectBoolBool(
            nsWindow,
            NativeMethods.SelRegisterName("setFrame:display:animate:"),
            frame,
            true,
            false);
    }

    /// <summary>
    /// Resets the frame of any behind-window <c>NSVisualEffectView</c>
    /// in the contentView's immediate subview tree to match the
    /// contentView's bounds. Avalonia installs one such view as
    /// <c>AutoFitContentView._blurBehind</c> for its
    /// <c>WindowTransparencyLevel.AcrylicBlur</c> support, and that
    /// view can end up with a stale frame across AppKit's animated
    /// zoom (its autoresizing-mask-based growth is delta-based and
    /// drifts during rapid resize streams), causing the Acrylic
    /// backdrop to stop covering the new client area and leaving the
    /// window transparent-but-not-blurred. Restoring its frame to
    /// match the new bounds restores the blur. No-op on non-macOS
    /// platforms.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <param name="behindWindowHidden">
    /// Whether the behind-window <c>NSVisualEffectView</c> should be
    /// hidden. <c>false</c> for Avalonia's Acrylic backdrop; <c>true</c>
    /// for blur types that don't use Avalonia's behind-window blur
    /// (Transparent, LiquidGlass, or any non-Avalonia-managed mode).
    /// </param>
    public static void RefitWindowEffectViews(IntPtr nsWindow, bool behindWindowHidden)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        IntPtr contentView = NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return;
        }

        IntPtr visualEffectClass = NativeMethods.ObjCGetClass("NSVisualEffectView");
        if (visualEffectClass == IntPtr.Zero)
        {
            return;
        }

        IntPtr subviews = NativeMethods.ObjCMsgSend(
            contentView,
            NativeMethods.SelRegisterName("subviews"));
        if (subviews == IntPtr.Zero)
        {
            return;
        }

        IntPtr countSel = NativeMethods.SelRegisterName("count");
        IntPtr objectAtIndexSel = NativeMethods.SelRegisterName("objectAtIndex:");
        IntPtr isKindOfClassSel = NativeMethods.SelRegisterName("isKindOfClass:");
        IntPtr blendingModeSel = NativeMethods.SelRegisterName("blendingMode");
        IntPtr setFrameSel = NativeMethods.SelRegisterName("setFrame:");
        IntPtr setHiddenSel = NativeMethods.SelRegisterName("setHidden:");
        IntPtr boundsSel = NativeMethods.SelRegisterName("bounds");

        NSRect contentBounds = NativeMethods.ObjCMsgSendRectRet(contentView, boundsSel);

        long count = (long)(nint)NativeMethods.ObjCMsgSend(subviews, countSel);
        for (long i = 0; i < count; i++)
        {
            IntPtr subview = NativeMethods.ObjCMsgSendLongRetPtr(subviews, objectAtIndexSel, i);
            if (subview == IntPtr.Zero)
            {
                continue;
            }

            if (!NativeMethods.ObjCMsgSendPtrRetBool(subview, isKindOfClassSel, visualEffectClass))
            {
                continue;
            }

            // NSVisualEffectBlendingModeBehindWindow = 0
            // NSVisualEffectBlendingModeWithinWindow = 1
            // Only resize the behind-window view; the within-window
            // titlebar material is positioned and hidden by Avalonia's
            // AutoFitContentView.setFrameSize and our
            // SetTitleBarMaterialHidden helper respectively.
            long blendingMode = (long)(nint)NativeMethods.ObjCMsgSend(subview, blendingModeSel);
            if (blendingMode != 0)
            {
                continue;
            }

            NativeMethods.ObjCMsgSendRect(subview, setFrameSel, contentBounds);
            NativeMethods.ObjCMsgSendBool(subview, setHiddenSel, behindWindowHidden);
        }
    }

    /// <summary>
    /// Sets the NSWindow's opacity flag. An opaque window lets the macOS
    /// compositor skip blending it against everything behind it, which
    /// removes continuous WindowServer work proportional to the window area.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <param name="opaque">Whether the window content is fully opaque.</param>
    public static void SetWindowOpaque(IntPtr nsWindow, bool opaque)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ObjCMsgSendBool(nsWindow, NativeMethods.SelRegisterName("setOpaque:"), opaque);
    }

    /// <summary>
    /// Sets the NSWindow's background color, including its alpha.
    /// </summary>
    /// <remarks>
    /// For a translucent window this should match what the content actually
    /// paints. A fully clear background (alpha 0) is the most expensive
    /// option because the compositor can cache nothing for the window;
    /// matching the content's real alpha keeps the window correctly
    /// see-through at a fraction of the cost.
    /// </remarks>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <param name="red">Red channel, 0-255.</param>
    /// <param name="green">Green channel, 0-255.</param>
    /// <param name="blue">Blue channel, 0-255.</param>
    /// <param name="alpha">Alpha channel, 0-255.</param>
    public static void SetWindowBackgroundColor(IntPtr nsWindow, byte red, byte green, byte blue, byte alpha)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        IntPtr nsColorClass = NativeMethods.ObjCGetClass("NSColor");
        IntPtr color = NativeMethods.ObjCMsgSendColor(
            nsColorClass,
            NativeMethods.SelRegisterName("colorWithSRGBRed:green:blue:alpha:"),
            red / 255.0,
            green / 255.0,
            blue / 255.0,
            alpha / 255.0);
        if (color != IntPtr.Zero)
        {
            NativeMethods.ObjCMsgSendIntPtr(nsWindow, NativeMethods.SelRegisterName("setBackgroundColor:"), color);
        }
    }

    /// <summary>
    /// Replaces the NSWindow's background color with an opaque color.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <param name="red">Red channel, 0-255.</param>
    /// <param name="green">Green channel, 0-255.</param>
    /// <param name="blue">Blue channel, 0-255.</param>
    public static void SetOpaqueWindowBackgroundColor(IntPtr nsWindow, byte red, byte green, byte blue)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        IntPtr nsColorClass = NativeMethods.ObjCGetClass("NSColor");
        IntPtr color = NativeMethods.ObjCMsgSendColor(
            nsColorClass,
            NativeMethods.SelRegisterName("colorWithSRGBRed:green:blue:alpha:"),
            red / 255.0,
            green / 255.0,
            blue / 255.0,
            1.0);
        if (color != IntPtr.Zero)
        {
            NativeMethods.ObjCMsgSendIntPtr(nsWindow, NativeMethods.SelRegisterName("setBackgroundColor:"), color);
        }
    }

    /// <summary>
    /// Configures the NSWindow for a fully transparent background while
    /// preserving native traffic light buttons. Sets the window as non-opaque
    /// with a clear background color, makes the titlebar transparent with a
    /// hidden title, removes the titlebar separator, and ensures the window
    /// shadow is preserved.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    public static void SetTransparentTitlebar(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        // Make the window non-opaque so macOS composites transparency.
        // [window setOpaque:NO]
        NativeMethods.ObjCMsgSendBool(nsWindow, NativeMethods.SelRegisterName("setOpaque:"), false);

        // NOTE: deliberately does NOT set the window's background color to
        // [NSColor clearColor]. A clear window background forces the macOS
        // compositor to treat the whole window as per-pixel transparent,
        // which defeats the cached vibrancy backdrop that AcrylicBlur
        // normally uses. Measured on a maximized Retina window: with the
        // clear background the window cost ~81% GPU continuously, versus
        // ~28% with setOpaque:NO alone (18% baseline). The titlebar still
        // shows through because setTitlebarAppearsTransparent: is set below
        // and Avalonia's content view extends underneath it.

        // [window setTitlebarSeparatorStyle:NSTitlebarSeparatorStyleNone] (0)
        NativeMethods.ObjCMsgSendLong(nsWindow, NativeMethods.SelRegisterName("setTitlebarSeparatorStyle:"), 0);

        // [window setTitlebarAppearsTransparent:YES]
        NativeMethods.ObjCMsgSendBool(nsWindow, NativeMethods.SelRegisterName("setTitlebarAppearsTransparent:"), true);

        // [window setTitleVisibility:NSWindowTitleHidden] (1)
        NativeMethods.ObjCMsgSendLong(nsWindow, NativeMethods.SelRegisterName("setTitleVisibility:"), 1);

        // Ensure native traffic light buttons are visible since we use
        // a custom window template (Avalonia won't manage them for us).
        ShowTrafficLightButtons(nsWindow);
    }

    /// <summary>
    /// Enables macOS's "unified compact" titlebar style by attaching an empty
    /// <c>NSToolbar</c> and setting <c>NSWindowToolbarStyleUnifiedCompact</c>
    /// on the window. AppKit grows the titlebar region (~38pt) and centers
    /// the native traffic-light cluster vertically inside it, which is what
    /// Safari / Terminal.app / iTerm2 use to align traffic lights with their
    /// own tab strip / toolbar content. With <c>FullSizeContentView</c> our
    /// custom chrome continues to draw across the entire titlebar area, so
    /// the empty toolbar contributes only the height effect.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    public static void EnableUnifiedTitleBar(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        // -[NSWindow toolbar] returns the current toolbar (or nil). Skip if
        // we've already installed one — re-attaching on every activation
        // would leak NSToolbar instances and reset user-visible state.
        IntPtr existingToolbar = NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("toolbar"));
        if (existingToolbar != IntPtr.Zero)
        {
            // Re-assert the style in case Avalonia reset it during a
            // window-state transition.
            NativeMethods.ObjCMsgSendLong(
                nsWindow,
                NativeMethods.SelRegisterName("setToolbarStyle:"),
                NSWindowToolbarStyleUnifiedCompact);
            return;
        }

        IntPtr toolbarClass = NativeMethods.ObjCGetClass("NSToolbar");
        if (toolbarClass == IntPtr.Zero)
        {
            return;
        }

        // [[NSToolbar alloc] init] via the +new shortcut. Returns retained.
        IntPtr toolbar = NativeMethods.ObjCMsgSend(
            toolbarClass,
            NativeMethods.SelRegisterName("new"));
        if (toolbar == IntPtr.Zero)
        {
            return;
        }

        // Hide the baseline separator (no-op on Big Sur+, harmless before).
        NativeMethods.ObjCMsgSendBool(
            toolbar,
            NativeMethods.SelRegisterName("setShowsBaselineSeparator:"),
            false);

        // [window setToolbar:toolbar] — window retains the toolbar.
        NativeMethods.ObjCMsgSendIntPtr(
            nsWindow,
            NativeMethods.SelRegisterName("setToolbar:"),
            toolbar);

        // [window setToolbarStyle:NSWindowToolbarStyleUnifiedCompact]
        NativeMethods.ObjCMsgSendLong(
            nsWindow,
            NativeMethods.SelRegisterName("setToolbarStyle:"),
            NSWindowToolbarStyleUnifiedCompact);

        // Balance the +1 retain from +new now that the window owns it.
        NativeMethods.ObjCMsgSend(toolbar, NativeMethods.SelRegisterName("release"));
    }

    /// <summary>
    /// Removes any <c>NSToolbar</c> previously attached by
    /// <see cref="EnableUnifiedTitleBar(IntPtr)"/>. Required when entering
    /// macOS native full-screen: the unified-style toolbar otherwise renders
    /// its own <c>NSVisualEffectView</c> material across the top of the
    /// window, making the (transparent) custom tab bar look opaque.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    public static void DetachToolbar(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        // [window setToolbar:nil] — the window releases its toolbar
        // reference, taking the toolbar's material backdrop with it.
        NativeMethods.ObjCMsgSendIntPtr(
            nsWindow,
            NativeMethods.SelRegisterName("setToolbar:"),
            IntPtr.Zero);
    }

    /// <summary>
    /// Configures the NSWindow for macOS full screen mode so the native
    /// titlebar auto-shows when the user moves the mouse to the top of the
    /// screen. Restores the window to opaque, makes the titlebar visible
    /// and non-transparent so that the system reveals a usable titlebar
    /// alongside the menu bar.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    public static void ConfigureForFullScreen(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        // Restore opaque window for full screen (content is fully opaque).
        // [window setOpaque:YES]
        NativeMethods.ObjCMsgSendBool(nsWindow, NativeMethods.SelRegisterName("setOpaque:"), true);

        // [window setTitlebarAppearsTransparent:NO]
        NativeMethods.ObjCMsgSendBool(nsWindow, NativeMethods.SelRegisterName("setTitlebarAppearsTransparent:"), false);

        // [window setTitleVisibility:NSWindowTitleVisible] (0)
        NativeMethods.ObjCMsgSendLong(nsWindow, NativeMethods.SelRegisterName("setTitleVisibility:"), 0);

        // [window setTitlebarSeparatorStyle:NSTitlebarSeparatorStyleAutomatic] (1)
        NativeMethods.ObjCMsgSendLong(nsWindow, NativeMethods.SelRegisterName("setTitlebarSeparatorStyle:"), 1);

        ShowTrafficLightButtons(nsWindow);
    }

    /// <summary>
    /// Controls the visibility of Avalonia's internal titlebar material view
    /// and the <c>NSWindowStyleMaskTexturedBackground</c> style mask flag.
    /// <para>
    /// In Avalonia 12 with <c>ExtendClientAreaToDecorationsHint="True"</c>
    /// and <c>WindowDecorations="Full"</c>, the native layer adds
    /// <c>NSWindowStyleMaskTexturedBackground</c> to the style mask and calls
    /// <c>ShowTitleBar:YES</c> on the <c>AutoFitContentView</c>, inserting an
    /// <c>NSVisualEffectView</c> with <c>NSVisualEffectMaterialTitlebar</c>.
    /// Both of these contribute an opaque titlebar background that breaks
    /// <c>WindowTransparencyLevel.Transparent</c>.
    /// </para>
    /// <para>
    /// Calling this with <paramref name="hidden"/> = <c>true</c> replicates
    /// the Avalonia 11 <c>NoChrome</c> behavior: the textured-background
    /// style flag is stripped and the titlebar material view is hidden,
    /// resulting in a fully transparent titlebar while keeping native
    /// traffic light buttons.
    /// </para>
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <param name="hidden">
    /// <c>true</c> to hide the titlebar material (Transparent mode);
    /// <c>false</c> to restore it (Acrylic / no blur).
    /// </param>
    public static void SetTitleBarMaterialHidden(IntPtr nsWindow, bool hidden)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        // NSWindowStyleMaskTexturedBackground = 1 << 8
        const long texturedBackgroundMask = 1 << 8;
        long currentMask = (long)(nint)NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("styleMask"));

        if (hidden)
        {
            NativeMethods.ObjCMsgSendLong(
                nsWindow,
                NativeMethods.SelRegisterName("setStyleMask:"),
                currentMask & ~texturedBackgroundMask);
        }
        else if ((currentMask & texturedBackgroundMask) == 0)
        {
            NativeMethods.ObjCMsgSendLong(
                nsWindow,
                NativeMethods.SelRegisterName("setStyleMask:"),
                currentMask | texturedBackgroundMask);
        }

        // Call Avalonia's ShowTitleBar: on AutoFitContentView (the
        // window's contentView) to hide/show the _titleBarMaterial
        // NSVisualEffectView and _titleBarUnderline NSBox.
        IntPtr contentView = NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("contentView"));
        if (contentView != IntPtr.Zero)
        {
            NativeMethods.ObjCMsgSendBool(
                contentView,
                NativeMethods.SelRegisterName("ShowTitleBar:"),
                !hidden);
        }

        ShowTrafficLightButtons(nsWindow);
    }

    /// <summary>
    /// Forces the window's blur effect to render in its active (vibrant)
    /// state regardless of window activation. Call before showing a child
    /// dialog so that the main window's blur stays fully active while
    /// focus is on the dialog.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    public static void ForceBlurActive(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        // NSVisualEffectStateActive = 1
        SetVisualEffectViewState(nsWindow, 1);
    }

    /// <summary>
    /// Resets the window's blur effect to follow the window's active state,
    /// restoring the default macOS behavior. Call after a child dialog closes.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    public static void ResetBlurState(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        // NSVisualEffectStateFollowsActiveState = 0
        SetVisualEffectViewState(nsWindow, 0);
    }

    /// <summary>
    /// Forces <c>NSApplication</c>'s <c>applicationIconImage</c> to the
    /// <c>.icns</c> file shipped in the running app bundle. Avalonia's
    /// macOS backend bypasses AppKit's automatic <c>CFBundleIconFile</c>
    /// loading on <c>NSApp finishLaunching</c>, leaving the running
    /// process with the generic placeholder icon — which is what Stage
    /// Manager (台前调度), Cmd+Tab, and the Dock then display. Calling
    /// <c>[NSApp setApplicationIconImage:]</c> explicitly with an
    /// <c>NSImage</c> loaded from the bundle's icon resource fixes all
    /// three surfaces. No-op when not running on macOS, when the process
    /// is not running inside an <c>.app</c> bundle (e.g. <c>dotnet run</c>
    /// during development), or when the bundle does not contain a
    /// matching <c>&lt;resourceName&gt;.icns</c> file.
    /// </summary>
    /// <param name="resourceName">
    /// The base name of the icon resource (without extension) as
    /// declared by the bundle's <c>CFBundleIconFile</c> key. Defaults
    /// to <c>aeroterm</c>, matching this app's <c>Info.plist</c>.
    /// </param>
    public static void SetApplicationIconFromBundle(string resourceName = "aeroterm")
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        IntPtr img = LoadBundleIconImage(resourceName);
        if (img == IntPtr.Zero)
        {
            return;
        }

        // [NSApp setApplicationIconImage:img]
        IntPtr nsAppClass = NativeMethods.ObjCGetClass("NSApplication");
        IntPtr nsApp = NativeMethods.ObjCMsgSend(
            nsAppClass,
            NativeMethods.SelRegisterName("sharedApplication"));
        if (nsApp != IntPtr.Zero)
        {
            NativeMethods.ObjCMsgSendIntPtr(
                nsApp,
                NativeMethods.SelRegisterName("setApplicationIconImage:"),
                img);
        }

        // Balance the +alloc/-initWithContentsOfFile: ownership; NSApp
        // retains the image internally for as long as it needs it.
        NativeMethods.ObjCMsgSend(img, NativeMethods.SelRegisterName("release"));
    }

    /// <summary>
    /// Sets the supplied <see cref="IntPtr"/> NSWindow's
    /// <c>miniwindowImage</c> to the bundled <c>.icns</c> icon. AppKit uses
    /// this image when it needs a per-window icon representation outside the
    /// regular content render — minimised Dock tile, Mission Control window
    /// list, and (most importantly for us) the Stage Manager / 台前调度
    /// window strip overlay. Without it, Stage Manager falls back to its
    /// generic placeholder even when <c>NSApp applicationIconImage</c> is
    /// correctly assigned, because Stage Manager reads from the per-window
    /// image rather than from the running app icon. No-op on non-macOS,
    /// when <paramref name="nsWindow"/> is null, or when the bundle does
    /// not contain a matching icon resource.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <param name="resourceName">
    /// The base name of the icon resource (without extension) as
    /// declared by the bundle's <c>CFBundleIconFile</c> key. Defaults
    /// to <c>aeroterm</c>, matching this app's <c>Info.plist</c>.
    /// </param>
    public static void SetWindowIconFromBundle(IntPtr nsWindow, string resourceName = "aeroterm")
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        IntPtr img = LoadBundleIconImage(resourceName);
        if (img == IntPtr.Zero)
        {
            return;
        }

        // [window setMiniwindowImage:img]
        NativeMethods.ObjCMsgSendIntPtr(
            nsWindow,
            NativeMethods.SelRegisterName("setMiniwindowImage:"),
            img);

        // Balance the +alloc/-initWithContentsOfFile: ownership; NSWindow
        // retains the image internally for as long as it needs it.
        NativeMethods.ObjCMsgSend(img, NativeMethods.SelRegisterName("release"));
    }

    /// <summary>
    /// Returns <c>true</c> when the current process is running on macOS 26
    /// (Tahoe) or later. Uses the managed
    /// <see cref="Environment.OSVersion"/> probe (which .NET 10 maps to the
    /// real product version on macOS) so the check works even in
    /// processes that have not loaded AppKit yet (e.g. unit tests). The
    /// result is cached for the lifetime of the process. Always returns
    /// <c>false</c> off macOS.
    /// </summary>
    /// <returns><c>true</c> if Liquid Glass APIs are available.</returns>
    public static bool IsMacOS26OrLater()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        if (isMacOS26OrLaterCached.HasValue)
        {
            return isMacOS26OrLaterCached.Value;
        }

        bool present = Environment.OSVersion.Version.Major >= 26;
        isMacOS26OrLaterCached = present;
        return present;
    }

    /// <summary>
    /// Installs (or refreshes) an <c>NSGlassEffectView</c> as the back-most
    /// subview of the NSWindow's <c>contentView</c>, providing a window-wide
    /// Liquid Glass surface. The view is sized to the contentView bounds
    /// and configured with width-/height-flexible autoresizing so it tracks
    /// window resizes. A no-op on macOS &lt; 26 or off macOS.
    /// </summary>
    /// <remarks>
    /// AeroTerm's existing transparent-titlebar configuration leaves the
    /// window non-opaque with a clear background, so the glass surface is
    /// visible behind everything Avalonia renders. Subsequent calls reuse
    /// the previously installed instance (located by view tag) so this is
    /// safe to call from window activation / full-screen restore handlers.
    /// </remarks>
    /// <param name="nsWindow">The NSWindow handle.</param>
    public static void InstallLiquidGlassBackdrop(IntPtr nsWindow)
    {
        if (!IsMacOS26OrLater() || nsWindow == IntPtr.Zero)
        {
            return;
        }

        IntPtr contentView = NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return;
        }

        IntPtr existing = FindGlassBackdrop(contentView);
        if (existing != IntPtr.Zero)
        {
            // Re-assert frame in case Avalonia rebuilt the contentView tree.
            NSRect bounds = NativeMethods.ObjCMsgSendRectRet(
                contentView,
                NativeMethods.SelRegisterName("bounds"));
            NativeMethods.ObjCMsgSendRect(
                existing,
                NativeMethods.SelRegisterName("setFrame:"),
                bounds);

            // Re-promote to back-most. AppKit can reorder contentView's
            // subviews across [NSWindow zoom:] and fullscreen restores,
            // which would otherwise let Avalonia's compositor surface
            // paint over the glass backdrop. NSWindowBelow = -1.
            NativeMethods.ObjCMsgSendPtrLongPtr(
                contentView,
                NativeMethods.SelRegisterName("addSubview:positioned:relativeTo:"),
                existing,
                -1,
                IntPtr.Zero);
            return;
        }

        IntPtr glassClass = NativeMethods.ObjCGetClass("NSGlassEffectView");
        if (glassClass == IntPtr.Zero)
        {
            return;
        }

        NSRect contentBounds = NativeMethods.ObjCMsgSendRectRet(
            contentView,
            NativeMethods.SelRegisterName("bounds"));

        IntPtr alloc = NativeMethods.ObjCMsgSend(
            glassClass,
            NativeMethods.SelRegisterName("alloc"));
        if (alloc == IntPtr.Zero)
        {
            return;
        }

        // [[NSGlassEffectView alloc] initWithFrame:contentView.bounds]
        IntPtr glass = NativeMethods.ObjCMsgSendRectRetPtr(
            alloc,
            NativeMethods.SelRegisterName("initWithFrame:"),
            contentBounds);
        if (glass == IntPtr.Zero)
        {
            return;
        }

        // NSViewWidthSizable (1<<1) | NSViewHeightSizable (1<<4) = 18
        NativeMethods.ObjCMsgSendLong(
            glass,
            NativeMethods.SelRegisterName("setAutoresizingMask:"),
            18);

        // [contentView addSubview:glass positioned:NSWindowBelow relativeTo:nil]
        // NSWindowBelow = -1 ensures the glass is the back-most subview.
        NativeMethods.ObjCMsgSendPtrLongPtr(
            contentView,
            NativeMethods.SelRegisterName("addSubview:positioned:relativeTo:"),
            glass,
            -1,
            IntPtr.Zero);

        // contentView retained the subview; balance the +1 from alloc/init.
        NativeMethods.ObjCMsgSend(glass, NativeMethods.SelRegisterName("release"));
    }

    /// <summary>
    /// Removes any previously installed Liquid Glass backdrop from the
    /// NSWindow's <c>contentView</c>. Safe to call repeatedly. A no-op on
    /// macOS &lt; 26 or off macOS.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    public static void RemoveLiquidGlassBackdrop(IntPtr nsWindow)
    {
        if (!IsMacOS26OrLater() || nsWindow == IntPtr.Zero)
        {
            return;
        }

        IntPtr contentView = NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return;
        }

        IntPtr existing = FindGlassBackdrop(contentView);
        if (existing != IntPtr.Zero)
        {
            NativeMethods.ObjCMsgSend(
                existing,
                NativeMethods.SelRegisterName("removeFromSuperview"));
        }
    }

    /// <summary>
    /// Sets the <c>NSWindow.appearance</c> to either
    /// <c>NSAppearanceNameAqua</c> (light) or
    /// <c>NSAppearanceNameDarkAqua</c> (dark). The window's
    /// <c>NSVisualEffectView</c>(s) automatically pick the matching
    /// vibrancy material from the window's effective appearance.
    /// </summary>
    /// <remarks>
    /// Independent of Avalonia's <c>RequestedThemeVariant</c>: setting
    /// this overrides the inherited app-level appearance for this
    /// window only. Silently no-ops on non-macOS platforms.
    /// </remarks>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <param name="dark"><c>true</c> for dark tone, <c>false</c> for light.</param>
    public static void SetWindowAppearance(IntPtr nsWindow, bool dark)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        IntPtr nsAppearanceClass = NativeMethods.ObjCGetClass("NSAppearance");
        if (nsAppearanceClass == IntPtr.Zero)
        {
            return;
        }

        IntPtr nameNsString = CreateNSString(dark ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua");
        if (nameNsString == IntPtr.Zero)
        {
            return;
        }

        IntPtr appearance = NativeMethods.ObjCMsgSendPtrRetPtr(
            nsAppearanceClass,
            NativeMethods.SelRegisterName("appearanceNamed:"),
            nameNsString);
        if (appearance == IntPtr.Zero)
        {
            return;
        }

        // [window setAppearance:appearance]
        NativeMethods.ObjCMsgSendPtrRetPtr(
            nsWindow,
            NativeMethods.SelRegisterName("setAppearance:"),
            appearance);
    }

    /// <summary>
    /// Locates a previously installed Liquid Glass backdrop among the
    /// direct subviews of <paramref name="contentView"/> by class
    /// (<c>isKindOfClass:NSGlassEffectView</c>). Avalonia does not insert
    /// instances of this class itself, so the first match is ours.
    /// </summary>
    /// <param name="contentView">The NSWindow's contentView.</param>
    /// <returns>The glass view pointer, or <see cref="IntPtr.Zero"/>.</returns>
    private static IntPtr FindGlassBackdrop(IntPtr contentView)
    {
        IntPtr glassClass = NativeMethods.ObjCGetClass("NSGlassEffectView");
        if (glassClass == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr subviews = NativeMethods.ObjCMsgSend(
            contentView,
            NativeMethods.SelRegisterName("subviews"));
        if (subviews == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr countSel = NativeMethods.SelRegisterName("count");
        IntPtr objectAtIndexSel = NativeMethods.SelRegisterName("objectAtIndex:");
        IntPtr isKindOfClassSel = NativeMethods.SelRegisterName("isKindOfClass:");

        long count = (long)(nint)NativeMethods.ObjCMsgSend(subviews, countSel);
        for (long i = 0; i < count; i++)
        {
            IntPtr subview = NativeMethods.ObjCMsgSendLongRetPtr(subviews, objectAtIndexSel, i);
            if (subview == IntPtr.Zero)
            {
                continue;
            }

            if (NativeMethods.ObjCMsgSendPtrRetBool(subview, isKindOfClassSel, glassClass))
            {
                return subview;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Loads the bundled <c>.icns</c> icon resource and returns a retained
    /// <c>NSImage</c> pointer. The caller owns one reference and is
    /// responsible for releasing it after assigning it to its target.
    /// Returns <see cref="IntPtr.Zero"/> when not running on macOS, when
    /// the process is not running inside an <c>.app</c> bundle (e.g.
    /// <c>dotnet run</c> during development), or when the bundle does not
    /// contain a matching <c>&lt;resourceName&gt;.icns</c> file.
    /// </summary>
    /// <param name="resourceName">The icon resource base name.</param>
    /// <returns>A retained NSImage pointer, or zero on failure.</returns>
    private static IntPtr LoadBundleIconImage(string resourceName)
    {
        // path = [[NSBundle mainBundle] pathForResource:resourceName ofType:@"icns"]
        IntPtr nsBundleClass = NativeMethods.ObjCGetClass("NSBundle");
        IntPtr mainBundle = NativeMethods.ObjCMsgSend(
            nsBundleClass,
            NativeMethods.SelRegisterName("mainBundle"));
        if (mainBundle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr nsName = CreateNSString(resourceName);
        IntPtr nsType = CreateNSString("icns");
        if (nsName == IntPtr.Zero || nsType == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr path = NativeMethods.ObjCMsgSendPtrPtrRetPtr(
            mainBundle,
            NativeMethods.SelRegisterName("pathForResource:ofType:"),
            nsName,
            nsType);
        if (path == IntPtr.Zero)
        {
            // Not running from a bundle, or icon resource missing.
            return IntPtr.Zero;
        }

        // NSImage *img = [[NSImage alloc] initWithContentsOfFile:path]
        IntPtr nsImageClass = NativeMethods.ObjCGetClass("NSImage");
        IntPtr img = NativeMethods.ObjCMsgSend(
            nsImageClass,
            NativeMethods.SelRegisterName("alloc"));
        img = NativeMethods.ObjCMsgSendPtrRetPtr(
            img,
            NativeMethods.SelRegisterName("initWithContentsOfFile:"),
            path);
        return img;
    }

    /// <summary>
    /// Walks the NSView hierarchy from the window's content view and sets
    /// the <c>state</c> property on every <c>NSVisualEffectView</c> found.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    /// <param name="state">
    /// 0 = followsWindowActiveState, 1 = active, 2 = inactive.
    /// </param>
    private static void SetVisualEffectViewState(IntPtr nsWindow, long state)
    {
        IntPtr contentView = NativeMethods.ObjCMsgSend(
            nsWindow,
            NativeMethods.SelRegisterName("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return;
        }

        IntPtr effectViewClass = NativeMethods.ObjCGetClass("NSVisualEffectView");
        if (effectViewClass == IntPtr.Zero)
        {
            return;
        }

        // Cache selectors used during the recursive walk.
        IntPtr isKindOfClassSel = NativeMethods.SelRegisterName("isKindOfClass:");
        IntPtr setStateSel = NativeMethods.SelRegisterName("setState:");
        IntPtr subviewsSel = NativeMethods.SelRegisterName("subviews");
        IntPtr countSel = NativeMethods.SelRegisterName("count");
        IntPtr objectAtIndexSel = NativeMethods.SelRegisterName("objectAtIndex:");

        ApplyVisualEffectState(
            contentView,
            effectViewClass,
            isKindOfClassSel,
            setStateSel,
            subviewsSel,
            countSel,
            objectAtIndexSel,
            state);
    }

    /// <summary>
    /// Recursively walks the NSView tree starting from <paramref name="view"/>
    /// and sets the <c>state</c> property on any <c>NSVisualEffectView</c>.
    /// </summary>
    /// <param name="view">The current NSView to inspect.</param>
    /// <param name="effectViewClass">The NSVisualEffectView class pointer.</param>
    /// <param name="isKindOfClassSel">Cached <c>isKindOfClass:</c> selector.</param>
    /// <param name="setStateSel">Cached <c>setState:</c> selector.</param>
    /// <param name="subviewsSel">Cached <c>subviews</c> selector.</param>
    /// <param name="countSel">Cached <c>count</c> selector.</param>
    /// <param name="objectAtIndexSel">Cached <c>objectAtIndex:</c> selector.</param>
    /// <param name="state">The visual effect state value to apply.</param>
    private static void ApplyVisualEffectState(
        IntPtr view,
        IntPtr effectViewClass,
        IntPtr isKindOfClassSel,
        IntPtr setStateSel,
        IntPtr subviewsSel,
        IntPtr countSel,
        IntPtr objectAtIndexSel,
        long state)
    {
        if (NativeMethods.ObjCMsgSendPtrRetBool(view, isKindOfClassSel, effectViewClass))
        {
            NativeMethods.ObjCMsgSendLong(view, setStateSel, state);
        }

        IntPtr subviews = NativeMethods.ObjCMsgSend(view, subviewsSel);
        if (subviews == IntPtr.Zero)
        {
            return;
        }

        long count = (long)(nint)NativeMethods.ObjCMsgSend(subviews, countSel);
        for (long i = 0; i < count; i++)
        {
            IntPtr subview = NativeMethods.ObjCMsgSendLongRetPtr(subviews, objectAtIndexSel, i);
            if (subview != IntPtr.Zero)
            {
                ApplyVisualEffectState(
                    subview,
                    effectViewClass,
                    isKindOfClassSel,
                    setStateSel,
                    subviewsSel,
                    countSel,
                    objectAtIndexSel,
                    state);
            }
        }
    }

    /// <summary>
    /// Allocates an autoreleased <c>NSString</c> from a managed UTF-8
    /// string by invoking <c>+[NSString stringWithUTF8String:]</c>.
    /// </summary>
    /// <param name="value">The managed string to wrap.</param>
    /// <returns>
    /// The autoreleased <c>NSString</c> pointer, or <see cref="IntPtr.Zero"/>
    /// when class lookup fails.
    /// </returns>
    private static IntPtr CreateNSString(string value)
    {
        IntPtr nsStringClass = NativeMethods.ObjCGetClass("NSString");
        if (nsStringClass == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return NativeMethods.ObjCMsgSendStringRetPtr(
            nsStringClass,
            NativeMethods.SelRegisterName("stringWithUTF8String:"),
            value);
    }

    /// <summary>
    /// Ensures the native macOS traffic light buttons (close, miniaturize,
    /// zoom) are visible. Called when using NoChrome so Avalonia does not
    /// manage them, but the NSWindow still owns the standard button instances.
    /// </summary>
    /// <param name="nsWindow">The NSWindow handle.</param>
    private static void ShowTrafficLightButtons(IntPtr nsWindow)
    {
        IntPtr standardWindowButtonSel = NativeMethods.SelRegisterName("standardWindowButton:");
        IntPtr setHiddenSel = NativeMethods.SelRegisterName("setHidden:");

        // NSWindowCloseButton = 0, NSWindowMiniaturizeButton = 1, NSWindowZoomButton = 2
        for (long buttonType = 0; buttonType <= 2; buttonType++)
        {
            IntPtr button = NativeMethods.ObjCMsgSendLongRetPtr(nsWindow, standardWindowButtonSel, buttonType);
            if (button != IntPtr.Zero)
            {
                NativeMethods.ObjCMsgSendBool(button, setHiddenSel, false);
            }
        }
    }

    /// <summary>
    /// Contains P/Invoke declarations for the Objective-C runtime.
    /// </summary>
    private static class NativeMethods
    {
        /// <summary>
        /// Returns a pointer to the class definition identified by name.
        /// </summary>
        /// <param name="name">The class name.</param>
        /// <returns>The class pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
        public static extern IntPtr ObjCGetClass(string name);

        /// <summary>
        /// Registers a selector with the Objective-C runtime.
        /// </summary>
        /// <param name="name">The selector name.</param>
        /// <returns>The selector pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
        public static extern IntPtr SelRegisterName(string name);

        /// <summary>
        /// Sends a message with no arguments to an Objective-C object and returns a pointer.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <returns>The return value as a pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSend(IntPtr receiver, IntPtr selector);

        /// <summary>
        /// Sends a message with a pointer argument to an Objective-C object.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The pointer argument.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        /// <summary>
        /// Sends a message with four double arguments and a pointer return,
        /// used for <c>+[NSColor colorWithSRGBRed:green:blue:alpha:]</c>.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="red">Red component, 0-1.</param>
        /// <param name="green">Green component, 0-1.</param>
        /// <param name="blue">Blue component, 0-1.</param>
        /// <param name="alpha">Alpha component, 0-1.</param>
        /// <returns>The resulting object pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendColor(
            IntPtr receiver,
            IntPtr selector,
            double red,
            double green,
            double blue,
            double alpha);

        /// <summary>
        /// Sends a message with a boolean argument to an Objective-C object.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The boolean argument.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);

        /// <summary>
        /// Sends a message with no arguments to an Objective-C object
        /// and returns a boolean result.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <returns>The boolean return value.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ObjCMsgSendBoolRet(IntPtr receiver, IntPtr selector);

        /// <summary>
        /// Sends a message with a long integer argument to an Objective-C object.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The long integer argument.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendLong(IntPtr receiver, IntPtr selector, long arg);

        /// <summary>
        /// Sends a message with a long integer argument to an Objective-C object
        /// and returns a pointer result.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The long integer argument.</param>
        /// <returns>The return value as a pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendLongRetPtr(IntPtr receiver, IntPtr selector, long arg);

        /// <summary>
        /// Sends a message with a pointer argument to an Objective-C object
        /// and returns a pointer result.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The pointer argument.</param>
        /// <returns>The return value as a pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendPtrRetPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        /// <summary>
        /// Sends a message with a pointer argument to an Objective-C object
        /// and returns a boolean result.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The pointer argument.</param>
        /// <returns>The boolean return value.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ObjCMsgSendPtrRetBool(IntPtr receiver, IntPtr selector, IntPtr arg);

        /// <summary>
        /// Sends a message with two pointer arguments to an Objective-C
        /// object and returns a pointer result.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg1">The first pointer argument.</param>
        /// <param name="arg2">The second pointer argument.</param>
        /// <returns>The return value as a pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendPtrPtrRetPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

        /// <summary>
        /// Sends a message with a UTF-8 string argument to an Objective-C
        /// object and returns a pointer result.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The UTF-8 string argument, marshalled to a C string.</param>
        /// <returns>The return value as a pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendStringRetPtr(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

        /// <summary>
        /// Sends a message with an <see cref="NSRect"/> argument to an
        /// Objective-C object (e.g. <c>setFrame:</c>).
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="rect">The rectangle argument.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendRect(IntPtr receiver, IntPtr selector, NSRect rect);

        /// <summary>
        /// Sends a message with an <see cref="NSRect"/> argument to an
        /// Objective-C object and returns a pointer result (e.g.
        /// <c>initWithFrame:</c>).
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="rect">The rectangle argument.</param>
        /// <returns>The return value as a pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendRectRetPtr(IntPtr receiver, IntPtr selector, NSRect rect);

        /// <summary>
        /// Sends a no-argument message to an Objective-C object and returns
        /// an <see cref="NSRect"/> (e.g. <c>bounds</c>, <c>frame</c>). The
        /// .NET runtime emits the AArch64 ABI hidden out-pointer (x8) for
        /// this large-struct return; on x86_64 the equivalent
        /// <c>objc_msgSend_stret</c> entry would be required, so this helper
        /// is intended for arm64-mac (the codebase's primary target).
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <returns>The returned rectangle.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern NSRect ObjCMsgSendRectRet(IntPtr receiver, IntPtr selector);

        /// <summary>
        /// Sends a no-argument message to an Objective-C object and returns
        /// an <see cref="NSEdgeInsets"/> (e.g. <c>safeAreaInsets</c>). Same
        /// large-struct-return ABI caveat as
        /// <see cref="ObjCMsgSendRectRet"/>.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <returns>The returned edge insets.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern NSEdgeInsets ObjCMsgSendEdgeInsetsRet(IntPtr receiver, IntPtr selector);

        /// <summary>
        /// Sends a no-argument message to an Objective-C object and returns
        /// an <see cref="NSPoint"/> (e.g. <c>mouseLocation</c>). Same
        /// large-struct-return ABI caveat as
        /// <see cref="ObjCMsgSendRectRet"/>.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <returns>The returned point.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern NSPoint ObjCMsgSendPointRet(IntPtr receiver, IntPtr selector);

        /// <summary>
        /// Sends a no-argument message returning a <c>CGFloat</c> (e.g.
        /// <c>alphaValue</c>).
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <returns>The returned value.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern double ObjCMsgSendDoubleRet(IntPtr receiver, IntPtr selector);

        /// <summary>
        /// Sends a message with a single <c>CGFloat</c> argument (e.g.
        /// <c>setAlphaValue:</c>).
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The value argument.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendDouble(IntPtr receiver, IntPtr selector, double arg);

        /// <summary>
        /// Sends a message with one pointer, one long, and one pointer
        /// argument to an Objective-C object (e.g.
        /// <c>addSubview:positioned:relativeTo:</c>).
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg1">First pointer argument.</param>
        /// <param name="arg2">Long integer argument.</param>
        /// <param name="arg3">Third pointer argument.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendPtrLongPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, long arg2, IntPtr arg3);

        /// <summary>
        /// Sends a message with one <see cref="NSRect"/> argument and two
        /// boolean arguments (e.g. <c>setFrame:display:animate:</c>).
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="rect">The rectangle argument.</param>
        /// <param name="arg1">First boolean argument.</param>
        /// <param name="arg2">Second boolean argument.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendRectBoolBool(
            IntPtr receiver,
            IntPtr selector,
            NSRect rect,
            [MarshalAs(UnmanagedType.I1)] bool arg1,
            [MarshalAs(UnmanagedType.I1)] bool arg2);
    }
}
