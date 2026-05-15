// <copyright file="MacOSNativeMenuAdapter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.NativeMenus;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AeroTerm.Theme.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ThemeNativeMenuItem = AeroTerm.Theme.Controls.NativeMenuItem;
using ThemeNativeMenuItemBase = AeroTerm.Theme.Controls.NativeMenuItemBase;

/// <summary>
/// AppKit-backed menu adapter for macOS.
/// </summary>
internal sealed class MacOSNativeMenuAdapter : INativeMenuPlatformAdapter
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<long, ThemeNativeMenuItem> PendingItems = new();
    private static long nextTag;
    private static IntPtr targetInstance = IntPtr.Zero;
    private static IntPtr itemInvokedSelector = IntPtr.Zero;

    private readonly AvaloniaNativeMenuAdapter fallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="MacOSNativeMenuAdapter"/> class.
    /// </summary>
    /// <param name="fallback">The fallback adapter.</param>
    public MacOSNativeMenuAdapter(AvaloniaNativeMenuAdapter fallback)
    {
        this.fallback = fallback;
    }

    /// <inheritdoc/>
    public bool ShowAt(NativeMenuFlyout flyout, Control target, bool showAtPointer)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return this.fallback.ShowAt(flyout, target, showAtPointer);
        }

        try
        {
            return this.ShowAppKitMenu(flyout, target);
        }
        catch (DllNotFoundException)
        {
            return this.fallback.ShowAt(flyout, target, showAtPointer);
        }
        catch (EntryPointNotFoundException)
        {
            return this.fallback.ShowAt(flyout, target, showAtPointer);
        }
    }

    /// <inheritdoc/>
    public bool Hide(NativeMenuFlyout flyout)
    {
        return this.fallback.Hide(flyout) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    [UnmanagedCallersOnly]
    private static void MenuItemInvokedCallback(IntPtr self, IntPtr sel, IntPtr sender)
    {
        _ = self;
        _ = sel;

        long tag = (long)NativeMethods.ObjCMsgSend(sender, NativeMethods.SelRegisterName("tag"));
        ThemeNativeMenuItem? item;
        lock (SyncRoot)
        {
            PendingItems.TryGetValue(tag, out item);
        }

        if (item is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(item.Invoke);
    }

    private bool ShowAppKitMenu(NativeMenuFlyout flyout, Control target)
    {
        if (!this.EnsureTarget())
        {
            return this.fallback.ShowAt(flyout, target, showAtPointer: false);
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(target);
        IntPtr nsWindow = topLevel?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (topLevel is null || nsWindow == IntPtr.Zero)
        {
            return this.fallback.ShowAt(flyout, target, showAtPointer: false);
        }

        IntPtr contentView = NativeMethods.ObjCMsgSend(nsWindow, NativeMethods.SelRegisterName("contentView"));
        if (contentView == IntPtr.Zero)
        {
            return this.fallback.ShowAt(flyout, target, showAtPointer: false);
        }

        var registeredTags = new List<long>();
        IntPtr menu = this.BuildMenu(flyout.Items, registeredTags);
        if (menu == IntPtr.Zero)
        {
            return this.fallback.ShowAt(flyout, target, showAtPointer: false);
        }

        NSPoint point = this.GetMenuPoint(flyout, target, topLevel);
        try
        {
            return NativeMethods.ObjCMsgSendPtrPointPtrRetBool(
                menu,
                NativeMethods.SelRegisterName("popUpMenuPositioningItem:atLocation:inView:"),
                IntPtr.Zero,
                point,
                contentView);
        }
        finally
        {
            lock (SyncRoot)
            {
                foreach (long tag in registeredTags)
                {
                    PendingItems.Remove(tag);
                }
            }
        }
    }

    private NSPoint GetMenuPoint(NativeMenuFlyout flyout, Control target, TopLevel topLevel)
    {
        // Default: anchor menu's top-left at the target's bottom-left.
        Point targetBottomLeft = target.TranslatePoint(new Point(0, target.Bounds.Height), topLevel)
            ?? new Point(0, target.Bounds.Height);
        Point targetBottomRight = target.TranslatePoint(new Point(target.Bounds.Width, target.Bounds.Height), topLevel)
            ?? new Point(target.Bounds.Width, target.Bounds.Height);

        double menuLeft = targetBottomLeft.X;

        if (flyout.PointerHintPosition is { } pointer)
        {
            // Shift the menu so its left edge sits near the cursor; AppKit menu items
            // have a built-in left inset, so back off slightly so the text — not the
            // edge — appears under the pointer. Clamp the left edge to stay within
            // the target's horizontal bounds so the menu doesn't leak past the box.
            const double LeftInset = 14.0;
            double clampedX = Math.Clamp(pointer.X - LeftInset, targetBottomLeft.X, targetBottomRight.X);
            menuLeft = clampedX;
        }

        return new NSPoint
        {
            X = menuLeft,
            Y = Math.Max(0, topLevel.Bounds.Height - targetBottomLeft.Y),
        };
    }

    private bool EnsureTarget()
    {
        if (targetInstance != IntPtr.Zero)
        {
            return true;
        }

        IntPtr nsObjectClass = NativeMethods.ObjCGetClass("NSObject");
        if (nsObjectClass == IntPtr.Zero)
        {
            return false;
        }

        bool allocated = true;
        IntPtr cls = NativeMethods.ObjCAllocateClassPair(nsObjectClass, "AeroTermNativeMenuTarget", IntPtr.Zero);
        if (cls == IntPtr.Zero)
        {
            allocated = false;
            cls = NativeMethods.ObjCGetClass("AeroTermNativeMenuTarget");
            if (cls == IntPtr.Zero)
            {
                return false;
            }
        }

        itemInvokedSelector = NativeMethods.SelRegisterName("aeroNativeMenuItemInvoked:");

        if (allocated)
        {
            unsafe
            {
                delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> itemInvokedImp = &MenuItemInvokedCallback;
                NativeMethods.ClassAddMethod(cls, itemInvokedSelector, (IntPtr)itemInvokedImp, "v@:@");
            }

            NativeMethods.ObjCRegisterClassPair(cls);
        }

        IntPtr allocatedInstance = NativeMethods.ObjCMsgSend(cls, NativeMethods.SelRegisterName("alloc"));
        targetInstance = NativeMethods.ObjCMsgSend(allocatedInstance, NativeMethods.SelRegisterName("init"));
        return targetInstance != IntPtr.Zero;
    }

    private IntPtr BuildMenu(IEnumerable<ThemeNativeMenuItemBase> items, List<long> registeredTags)
    {
        IntPtr nsMenuClass = NativeMethods.ObjCGetClass("NSMenu");
        if (nsMenuClass == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr menu = NativeMethods.ObjCMsgSend(nsMenuClass, NativeMethods.SelRegisterName("alloc"));
        menu = NativeMethods.ObjCMsgSend(menu, NativeMethods.SelRegisterName("init"));
        menu = NativeMethods.ObjCMsgSend(menu, NativeMethods.SelRegisterName("autorelease"));

        foreach (ThemeNativeMenuItemBase item in items)
        {
            if (this.BuildMenuItem(item, registeredTags) is { } nativeItem && nativeItem != IntPtr.Zero)
            {
                NativeMethods.ObjCMsgSendIntPtr(menu, NativeMethods.SelRegisterName("addItem:"), nativeItem);
            }
        }

        return menu;
    }

    private IntPtr BuildMenuItem(ThemeNativeMenuItemBase item, List<long> registeredTags)
    {
        IntPtr nsMenuItemClass = NativeMethods.ObjCGetClass("NSMenuItem");
        if (nsMenuItemClass == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (item is NativeMenuSeparator)
        {
            return NativeMethods.ObjCMsgSend(nsMenuItemClass, NativeMethods.SelRegisterName("separatorItem"));
        }

        if (item is not ThemeNativeMenuItem menuItem || !menuItem.IsVisible)
        {
            return IntPtr.Zero;
        }

        IntPtr title = this.CreateNsString(menuItem.Header?.ToString() ?? string.Empty);
        IntPtr emptyKey = this.CreateNsString(string.Empty);
        IntPtr action = menuItem.Items.Count == 0 ? itemInvokedSelector : IntPtr.Zero;
        IntPtr nativeItem = NativeMethods.ObjCMsgSend(nsMenuItemClass, NativeMethods.SelRegisterName("alloc"));
        nativeItem = NativeMethods.ObjCMsgSend4(
            nativeItem,
            NativeMethods.SelRegisterName("initWithTitle:action:keyEquivalent:"),
            title,
            action,
            emptyKey);
        nativeItem = NativeMethods.ObjCMsgSend(nativeItem, NativeMethods.SelRegisterName("autorelease"));

        NativeMethods.ObjCMsgSendBool(nativeItem, NativeMethods.SelRegisterName("setEnabled:"), menuItem.CanInvoke || menuItem.Items.Count > 0);

        if (menuItem.IsChecked)
        {
            NativeMethods.ObjCMsgSendLong(nativeItem, NativeMethods.SelRegisterName("setState:"), 1);
        }

        if (menuItem.Items.Count > 0)
        {
            IntPtr submenu = this.BuildMenu(menuItem.Items, registeredTags);
            if (submenu != IntPtr.Zero)
            {
                NativeMethods.ObjCMsgSendIntPtr(nativeItem, NativeMethods.SelRegisterName("setSubmenu:"), submenu);
            }

            return nativeItem;
        }

        long tag = this.RegisterCallback(menuItem);
        registeredTags.Add(tag);
        NativeMethods.ObjCMsgSendIntPtr(nativeItem, NativeMethods.SelRegisterName("setTarget:"), targetInstance);
        NativeMethods.ObjCMsgSendLong(nativeItem, NativeMethods.SelRegisterName("setTag:"), tag);
        return nativeItem;
    }

    private long RegisterCallback(ThemeNativeMenuItem item)
    {
        lock (SyncRoot)
        {
            long tag = ++nextTag;
            PendingItems[tag] = item;
            return tag;
        }
    }

    private IntPtr CreateNsString(string value)
    {
        IntPtr nsStringClass = NativeMethods.ObjCGetClass("NSString");
        return nsStringClass == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.ObjCMsgSendUtf8(nsStringClass, NativeMethods.SelRegisterName("stringWithUTF8String:"), value);
    }

    /// <summary>
    /// Native point layout used by AppKit.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NSPoint
    {
        public double X;
        public double Y;
    }

    /// <summary>
    /// P/Invoke declarations for Objective-C/AppKit menu interop.
    /// </summary>
    private static class NativeMethods
    {
        private const string Lib = "/usr/lib/libobjc.A.dylib";

        [DllImport(Lib, EntryPoint = "objc_getClass")]
        public static extern IntPtr ObjCGetClass(string name);

        [DllImport(Lib, EntryPoint = "sel_registerName")]
        public static extern IntPtr SelRegisterName(string name);

        [DllImport(Lib, EntryPoint = "objc_allocateClassPair")]
        public static extern IntPtr ObjCAllocateClassPair(IntPtr superclass, string name, IntPtr extraBytes);

        [DllImport(Lib, EntryPoint = "objc_registerClassPair")]
        public static extern void ObjCRegisterClassPair(IntPtr cls);

        [DllImport(Lib, EntryPoint = "class_addMethod")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ClassAddMethod(IntPtr cls, IntPtr selector, IntPtr imp, string types);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSend(IntPtr receiver, IntPtr selector);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendUtf8(IntPtr receiver, IntPtr selector, string value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSend4(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendLong(IntPtr receiver, IntPtr selector, long arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ObjCMsgSendPtrPointPtrRetBool(
            IntPtr receiver,
            IntPtr selector,
            IntPtr item,
            NSPoint point,
            IntPtr view);
    }
}
