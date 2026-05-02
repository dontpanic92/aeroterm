// <copyright file="MacOSPopupGlass.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Helpers;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Minimal native menu material interop for macOS popup windows.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia 12 has no first-class support for macOS 26 Liquid Glass menus yet.
/// AppKit's native menus use the menu material rather than a generic glass
/// surface, so AeroTerm installs an <c>NSVisualEffectView</c> with
/// <c>NSVisualEffectMaterialMenu</c> for menu-like popup windows.
/// </para>
/// <para>
/// Off macOS, every method on this class is a no-op.
/// </para>
/// </remarks>
public static class MacOSPopupGlass
{
    private static bool? isMacOS26Cached;

    /// <summary>
    /// <c>NSGlassEffectView.style</c> values. <c>Regular</c> is the variant
    /// NSMenu/NSPopover use; <c>Clear</c> is a brighter variant suitable
    /// for content overlays.
    /// </summary>
    public enum Style : long
    {
        /// <summary>System default glass — matches NSMenu.</summary>
        Regular = 0,

        /// <summary>Brighter, less-tinted glass.</summary>
        Clear = 1,
    }

    /// <summary>
    /// <c>NSVisualEffectMaterial</c> values from AppKit. <see cref="Menu"/>
    /// is the system NSMenu vibrancy material on macOS &lt; 26.
    /// </summary>
    public enum VisualEffectMaterial : long
    {
        /// <summary>NSVisualEffectMaterialMenu — system menu material.</summary>
        Menu = 5,

        /// <summary>NSVisualEffectMaterialPopover — popover material (slightly brighter).</summary>
        Popover = 6,

        /// <summary>NSVisualEffectMaterialHUDWindow — HUD-style transient surface.</summary>
        HUDWindow = 11,
    }

    /// <summary>
    /// Returns <c>true</c> when running on macOS 26 (Tahoe) or later,
    /// where <c>NSGlassEffectView</c> is available.
    /// </summary>
    /// <returns><c>true</c> if Liquid Glass is available.</returns>
    public static bool IsAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        if (isMacOS26Cached.HasValue)
        {
            return isMacOS26Cached.Value;
        }

        bool available = Environment.OSVersion.Version.Major >= 26;
        isMacOS26Cached = available;
        return available;
    }

    /// <summary>
    /// Configures the given NSWindow as non-opaque with a clear background.
    /// Safe on every macOS version (just <c>setOpaque:NO</c> +
    /// <c>setBackgroundColor:[NSColor clearColor]</c>); silently no-ops off
    /// macOS or with a zero handle. Required so any backdrop installed
    /// behind Avalonia's content (Liquid Glass on macOS 26+,
    /// <c>NSVisualEffectView</c> via Avalonia's <c>AcrylicBlur</c>
    /// transparency level on older macOS) actually shows through.
    /// </summary>
    /// <param name="nsWindow">NSWindow handle.</param>
    public static void MakePopupWindowNonOpaque(IntPtr nsWindow)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return;
        }

        ConfigureWindowForTransparency(nsWindow);
    }

    /// <summary>
    /// Makes the popup window non-opaque with a clear background, then
    /// installs (or refreshes) an <c>NSGlassEffectView</c> with the given
    /// corner radius and style as the back-most subview of its
    /// <c>contentView</c>. Subsequent calls reuse the previously
    /// installed view located by class lookup.
    /// </summary>
    /// <param name="nsWindow">NSWindow handle from <c>TopLevel.TryGetPlatformHandle()</c>.</param>
    /// <param name="cornerRadius">Glass corner radius to match the popup body.</param>
    /// <param name="style">Glass style variant.</param>
    /// <returns><c>true</c> when the glass view was installed or refreshed; <c>false</c> when the platform doesn't support it.</returns>
    public static bool Install(IntPtr nsWindow, double cornerRadius, Style style = Style.Regular)
    {
        if (!IsAvailable() || nsWindow == IntPtr.Zero)
        {
            return false;
        }

        IntPtr glassClass = NativeMethods.ObjCGetClass("NSGlassEffectView");
        if (glassClass == IntPtr.Zero)
        {
            return false;
        }

        ConfigureWindowForTransparency(nsWindow);

        IntPtr contentView = NativeMethods.ObjCMsgSend(nsWindow, NativeMethods.SelRegisterName("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return false;
        }

        NSRect bounds = MsgSendRectRet(contentView, NativeMethods.SelRegisterName("bounds"));

        IntPtr existing = FindGlass(contentView, glassClass);
        if (existing != IntPtr.Zero)
        {
            NativeMethods.ObjCMsgSendRect(existing, NativeMethods.SelRegisterName("setFrame:"), bounds);
            NativeMethods.ObjCMsgSendDouble(existing, NativeMethods.SelRegisterName("setCornerRadius:"), cornerRadius);
            NativeMethods.ObjCMsgSendLong(existing, NativeMethods.SelRegisterName("setStyle:"), (long)style);
            return true;
        }

        IntPtr alloc = NativeMethods.ObjCMsgSend(glassClass, NativeMethods.SelRegisterName("alloc"));
        if (alloc == IntPtr.Zero)
        {
            return false;
        }

        IntPtr glass = NativeMethods.ObjCMsgSendRectRetPtr(alloc, NativeMethods.SelRegisterName("initWithFrame:"), bounds);
        if (glass == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.ObjCMsgSendDouble(glass, NativeMethods.SelRegisterName("setCornerRadius:"), cornerRadius);
        NativeMethods.ObjCMsgSendLong(glass, NativeMethods.SelRegisterName("setStyle:"), (long)style);

        // NSViewWidthSizable (1<<1) | NSViewHeightSizable (1<<4) = 18.
        NativeMethods.ObjCMsgSendLong(glass, NativeMethods.SelRegisterName("setAutoresizingMask:"), 18);

        // [contentView addSubview:glass positioned:NSWindowBelow relativeTo:nil] — Below = -1.
        NativeMethods.ObjCMsgSendPtrLongPtr(
            contentView,
            NativeMethods.SelRegisterName("addSubview:positioned:relativeTo:"),
            glass,
            -1,
            IntPtr.Zero);

        NativeMethods.ObjCMsgSend(glass, NativeMethods.SelRegisterName("release"));
        return true;
    }

    /// <summary>
    /// Installs the AppKit native menu material as the back-most subview of
    /// the popup <c>NSWindow</c>'s <c>contentView</c>.
    /// </summary>
    /// <param name="nsWindow">NSWindow handle.</param>
    /// <param name="cornerRadius">Material corner radius to match the popup body.</param>
    /// <returns><c>true</c> on success.</returns>
    public static bool InstallNativeMenuMaterial(IntPtr nsWindow, double cornerRadius)
    {
        return InstallVisualEffectView(nsWindow, VisualEffectMaterial.Menu, cornerRadius);
    }

    /// <summary>
    /// Installs (or refreshes) an <c>NSVisualEffectView</c> as the back-most
    /// subview of the popup <c>NSWindow</c>'s <c>contentView</c>. Also makes
    /// the window non-opaque with a clear background. Subsequent calls reuse
    /// the previously installed view located by class lookup.
    /// </summary>
    /// <param name="nsWindow">NSWindow handle.</param>
    /// <param name="material">Vibrancy material (default <see cref="VisualEffectMaterial.Menu"/>).</param>
    /// <param name="cornerRadius">View corner radius, or <c>0</c> for square corners.</param>
    /// <returns><c>true</c> on success.</returns>
    public static bool InstallVisualEffectView(
        IntPtr nsWindow,
        VisualEffectMaterial material = VisualEffectMaterial.Menu,
        double cornerRadius = 0)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || nsWindow == IntPtr.Zero)
        {
            return false;
        }

        IntPtr vfxClass = NativeMethods.ObjCGetClass("NSVisualEffectView");
        if (vfxClass == IntPtr.Zero)
        {
            return false;
        }

        ConfigureWindowForTransparency(nsWindow);

        IntPtr contentView = NativeMethods.ObjCMsgSend(nsWindow, NativeMethods.SelRegisterName("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return false;
        }

        NSRect bounds = MsgSendRectRet(contentView, NativeMethods.SelRegisterName("bounds"));

        IntPtr existing = FindSubviewOfClass(contentView, vfxClass);
        if (existing != IntPtr.Zero)
        {
            NativeMethods.ObjCMsgSendRect(existing, NativeMethods.SelRegisterName("setFrame:"), bounds);
            NativeMethods.ObjCMsgSendLong(existing, NativeMethods.SelRegisterName("setMaterial:"), (long)material);
            ApplyLayerCornerRadius(existing, cornerRadius);
            return true;
        }

        IntPtr alloc = NativeMethods.ObjCMsgSend(vfxClass, NativeMethods.SelRegisterName("alloc"));
        if (alloc == IntPtr.Zero)
        {
            return false;
        }

        IntPtr view = NativeMethods.ObjCMsgSendRectRetPtr(alloc, NativeMethods.SelRegisterName("initWithFrame:"), bounds);
        if (view == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.ObjCMsgSendLong(view, NativeMethods.SelRegisterName("setMaterial:"), (long)material);

        // NSVisualEffectBlendingModeBehindWindow = 0 — blur the desktop behind the popup.
        NativeMethods.ObjCMsgSendLong(view, NativeMethods.SelRegisterName("setBlendingMode:"), 0);

        // NSVisualEffectStateActive = 1 — always vibrant regardless of window focus.
        NativeMethods.ObjCMsgSendLong(view, NativeMethods.SelRegisterName("setState:"), 1);

        // setWantsLayer:YES so corner radius applies cleanly.
        NativeMethods.ObjCMsgSendBool(view, NativeMethods.SelRegisterName("setWantsLayer:"), true);
        ApplyLayerCornerRadius(view, cornerRadius);

        // NSViewWidthSizable | NSViewHeightSizable.
        NativeMethods.ObjCMsgSendLong(view, NativeMethods.SelRegisterName("setAutoresizingMask:"), 18);

        // [contentView addSubview:view positioned:NSWindowBelow relativeTo:nil]
        NativeMethods.ObjCMsgSendPtrLongPtr(
            contentView,
            NativeMethods.SelRegisterName("addSubview:positioned:relativeTo:"),
            view,
            -1,
            IntPtr.Zero);

        NativeMethods.ObjCMsgSend(view, NativeMethods.SelRegisterName("release"));
        return true;
    }

    private static void ApplyLayerCornerRadius(IntPtr view, double cornerRadius)
    {
        if (cornerRadius <= 0)
        {
            return;
        }

        NativeMethods.ObjCMsgSendBool(view, NativeMethods.SelRegisterName("setWantsLayer:"), true);

        IntPtr layer = NativeMethods.ObjCMsgSend(view, NativeMethods.SelRegisterName("layer"));
        if (layer == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ObjCMsgSendDouble(layer, NativeMethods.SelRegisterName("setCornerRadius:"), cornerRadius);
        NativeMethods.ObjCMsgSendBool(layer, NativeMethods.SelRegisterName("setMasksToBounds:"), true);
    }

    private static IntPtr FindSubviewOfClass(IntPtr contentView, IntPtr classPtr)
    {
        IntPtr subviews = NativeMethods.ObjCMsgSend(contentView, NativeMethods.SelRegisterName("subviews"));
        if (subviews == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr countSel = NativeMethods.SelRegisterName("count");
        IntPtr objAtIdxSel = NativeMethods.SelRegisterName("objectAtIndex:");
        IntPtr isKindOfClassSel = NativeMethods.SelRegisterName("isKindOfClass:");

        long count = (long)(nint)NativeMethods.ObjCMsgSend(subviews, countSel);
        for (long i = 0; i < count; i++)
        {
            IntPtr subview = NativeMethods.ObjCMsgSendLongRetPtr(subviews, objAtIdxSel, i);
            if (subview == IntPtr.Zero)
            {
                continue;
            }

            if (NativeMethods.ObjCMsgSendPtrRetBool(subview, isKindOfClassSel, classPtr))
            {
                return subview;
            }
        }

        return IntPtr.Zero;
    }

    private static void ConfigureWindowForTransparency(IntPtr nsWindow)
    {
        NativeMethods.ObjCMsgSendBool(nsWindow, NativeMethods.SelRegisterName("setOpaque:"), false);

        IntPtr nsColorClass = NativeMethods.ObjCGetClass("NSColor");
        if (nsColorClass == IntPtr.Zero)
        {
            return;
        }

        IntPtr clearColor = NativeMethods.ObjCMsgSend(nsColorClass, NativeMethods.SelRegisterName("clearColor"));
        if (clearColor == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ObjCMsgSendPtrRetPtr(nsWindow, NativeMethods.SelRegisterName("setBackgroundColor:"), clearColor);
    }

    private static IntPtr FindGlass(IntPtr contentView, IntPtr glassClass)
    {
        IntPtr subviews = NativeMethods.ObjCMsgSend(contentView, NativeMethods.SelRegisterName("subviews"));
        if (subviews == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr countSel = NativeMethods.SelRegisterName("count");
        IntPtr objAtIdxSel = NativeMethods.SelRegisterName("objectAtIndex:");
        IntPtr isKindOfClassSel = NativeMethods.SelRegisterName("isKindOfClass:");

        long count = (long)(nint)NativeMethods.ObjCMsgSend(subviews, countSel);
        for (long i = 0; i < count; i++)
        {
            IntPtr subview = NativeMethods.ObjCMsgSendLongRetPtr(subviews, objAtIdxSel, i);
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

    private static NSRect MsgSendRectRet(IntPtr receiver, IntPtr sel)
    {
        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            return NativeMethods.ObjCMsgSendRectRetDirect(receiver, sel);
        }

        NativeMethods.ObjCMsgSendStretRect(out NSRect rect, receiver, sel);
        return rect;
    }

    /// <summary>NSRect layout matching libobjc / AppKit ABI.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NSRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    /// <summary>P/Invoke surface used by this helper.</summary>
    private static class NativeMethods
    {
        private const string Lib = "/usr/lib/libobjc.A.dylib";

        [DllImport(Lib, EntryPoint = "objc_getClass")]
        public static extern IntPtr ObjCGetClass(string name);

        [DllImport(Lib, EntryPoint = "sel_registerName")]
        public static extern IntPtr SelRegisterName(string name);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSend(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendPtrRetPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendLong(IntPtr receiver, IntPtr selector, long value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendLongRetPtr(IntPtr receiver, IntPtr selector, long value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendDouble(IntPtr receiver, IntPtr selector, double value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendRect(IntPtr receiver, IntPtr selector, NSRect rect);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendRectRetPtr(IntPtr receiver, IntPtr selector, NSRect rect);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendPtrLongPtr(IntPtr receiver, IntPtr selector, IntPtr p1, long n, IntPtr p2);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ObjCMsgSendPtrRetBool(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern NSRect ObjCMsgSendRectRetDirect(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend_stret")]
        public static extern void ObjCMsgSendStretRect(out NSRect outRect, IntPtr receiver, IntPtr selector);
    }
}
