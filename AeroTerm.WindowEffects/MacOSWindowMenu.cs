// <copyright file="MacOSWindowMenu.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

/// <summary>
/// Populates the macOS Dock tile's right-click menu with an entry for each
/// registered Avalonia <see cref="Window"/> plus a top-level "New Window"
/// command. The menu reflects open windows, their current titles, and
/// check-marks the frontmost window.
/// </summary>
/// <remarks>
/// <para>
/// AppKit resolves the Dock menu by calling <c>applicationDockMenu:</c>
/// on <c>NSApp</c>'s delegate. Since Avalonia owns the delegate class and
/// does not expose it, this class installs an implementation of that
/// selector onto the running delegate's class via the Objective-C runtime
/// the first time a window registers. All native work is a no-op on
/// non-macOS platforms.
/// </para>
/// </remarks>
public static class MacOSWindowMenu
{
    private static readonly object SyncRoot = new();
    private static readonly List<Window> RegisteredWindows = new();
    private static Action? newWindowHandler;
    private static bool dockMenuInstalled;
    private static IntPtr targetInstance = IntPtr.Zero;
    private static IntPtr newWindowSelector = IntPtr.Zero;
    private static IntPtr activateWindowSelector = IntPtr.Zero;

    /// <summary>
    /// Registers the handler invoked when the Dock menu's "New Window"
    /// item is chosen. May be called before or after any windows register.
    /// Safe to call on any platform; only macOS will surface the handler.
    /// </summary>
    /// <param name="handler">
    /// A delegate executed on the Avalonia UI thread whenever the user
    /// picks the "New Window" entry in the Dock tile's right-click menu.
    /// </param>
    public static void SetNewWindowHandler(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (SyncRoot)
        {
            newWindowHandler = handler;
        }
    }

    /// <summary>
    /// Registers an Avalonia window so it participates in the Dock
    /// right-click menu. No-op on non-macOS. Safe to call multiple times
    /// for the same window.
    /// </summary>
    /// <param name="avaloniaWindow">The Avalonia window to track.</param>
    public static void RegisterWindow(Window avaloniaWindow)
    {
        ArgumentNullException.ThrowIfNull(avaloniaWindow);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        lock (SyncRoot)
        {
            if (RegisteredWindows.Contains(avaloniaWindow))
            {
                return;
            }

            RegisteredWindows.Add(avaloniaWindow);
            avaloniaWindow.PropertyChanged += OnWindowPropertyChanged;
            EnsureDockMenuInstalled();
        }
    }

    /// <summary>
    /// Removes an Avalonia window from the Dock right-click menu. Should
    /// be called when the window closes. No-op on non-macOS or if the
    /// window was never registered.
    /// </summary>
    /// <param name="avaloniaWindow">The Avalonia window to forget.</param>
    public static void UnregisterWindow(Window avaloniaWindow)
    {
        ArgumentNullException.ThrowIfNull(avaloniaWindow);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        lock (SyncRoot)
        {
            if (!RegisteredWindows.Remove(avaloniaWindow))
            {
                return;
            }

            avaloniaWindow.PropertyChanged -= OnWindowPropertyChanged;
        }
    }

    private static void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Menu content is rebuilt lazily every time the Dock is opened,
        // so no eager rebuild is needed. This subscription exists so that
        // a Title change does not require the caller to re-register.
        _ = e;
    }

    private static void EnsureDockMenuInstalled()
    {
        if (dockMenuInstalled)
        {
            return;
        }

        try
        {
            InstallNativeDockMenuHook();
            dockMenuInstalled = true;
        }
        catch
        {
            // If swizzling fails for any reason (e.g. delegate not yet
            // available, unsupported runtime), leave dockMenuInstalled
            // false so the next registration retries.
        }
    }

    private static void InstallNativeDockMenuHook()
    {
        IntPtr nsAppClass = NativeMethods.ObjCGetClass("NSApplication");
        IntPtr sharedAppSel = NativeMethods.SelRegisterName("sharedApplication");
        IntPtr nsApp = NativeMethods.ObjCMsgSend(nsAppClass, sharedAppSel);
        if (nsApp == IntPtr.Zero)
        {
            return;
        }

        IntPtr delegateSel = NativeMethods.SelRegisterName("delegate");
        IntPtr appDelegate = NativeMethods.ObjCMsgSend(nsApp, delegateSel);
        if (appDelegate == IntPtr.Zero)
        {
            return;
        }

        IntPtr delegateClass = NativeMethods.ObjectGetClass(appDelegate);
        if (delegateClass == IntPtr.Zero)
        {
            return;
        }

        // Create the target class that owns the action methods invoked by
        // NSMenuItem clicks. Done once per process.
        CreateMenuTargetClassIfNeeded();

        // Install -applicationDockMenu: on the Avalonia delegate class.
        IntPtr dockMenuSel = NativeMethods.SelRegisterName("applicationDockMenu:");
        unsafe
        {
            delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr> imp = &DockMenuCallback;
            NativeMethods.ClassReplaceMethod(
                delegateClass,
                dockMenuSel,
                (IntPtr)imp,
                "@@:@");
        }

        // Install -applicationShouldHandleReopen:hasVisibleWindows: so that
        // clicking the Dock icon while no windows are open creates a new
        // window instead of being silently ignored. AppKit calls this
        // selector whenever the user activates an already-running app.
        IntPtr reopenSel = NativeMethods.SelRegisterName("applicationShouldHandleReopen:hasVisibleWindows:");
        unsafe
        {
            delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte, byte> imp = &ReopenCallback;
            NativeMethods.ClassReplaceMethod(
                delegateClass,
                reopenSel,
                (IntPtr)imp,
                "c@:@c");
        }
    }

    private static void CreateMenuTargetClassIfNeeded()
    {
        if (targetInstance != IntPtr.Zero)
        {
            return;
        }

        IntPtr nsObjectClass = NativeMethods.ObjCGetClass("NSObject");
        IntPtr cls = NativeMethods.ObjCAllocateClassPair(nsObjectClass, "AeroTermDockMenuTarget", IntPtr.Zero);
        if (cls == IntPtr.Zero)
        {
            // Class may already exist from a previous (failed) registration.
            cls = NativeMethods.ObjCGetClass("AeroTermDockMenuTarget");
            if (cls == IntPtr.Zero)
            {
                return;
            }
        }

        newWindowSelector = NativeMethods.SelRegisterName("aeroNewWindow:");
        activateWindowSelector = NativeMethods.SelRegisterName("aeroActivateWindow:");

        unsafe
        {
            delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> newWindowImp = &NewWindowCallback;
            delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> activateImp = &ActivateWindowCallback;
            NativeMethods.ClassAddMethod(cls, newWindowSelector, (IntPtr)newWindowImp, "v@:@");
            NativeMethods.ClassAddMethod(cls, activateWindowSelector, (IntPtr)activateImp, "v@:@");
        }

        // Register is idempotent-safe only if the class was freshly allocated.
        NativeMethods.ObjCRegisterClassPair(cls);

        IntPtr allocSel = NativeMethods.SelRegisterName("alloc");
        IntPtr initSel = NativeMethods.SelRegisterName("init");
        IntPtr allocated = NativeMethods.ObjCMsgSend(cls, allocSel);
        targetInstance = NativeMethods.ObjCMsgSend(allocated, initSel);
    }

    [UnmanagedCallersOnly]
    private static IntPtr DockMenuCallback(IntPtr self, IntPtr sel, IntPtr sender)
    {
        _ = self;
        _ = sel;
        _ = sender;
        try
        {
            return BuildDockMenu();
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly]
    private static byte ReopenCallback(IntPtr self, IntPtr sel, IntPtr sender, byte hasVisibleWindows)
    {
        _ = self;
        _ = sel;
        _ = sender;

        // If AppKit reports visible windows, defer to its default behaviour
        // (deminiaturize / order-front) by returning YES.
        if (hasVisibleWindows != 0)
        {
            return 1;
        }

        Action? handler;
        lock (SyncRoot)
        {
            handler = newWindowHandler;
        }

        if (handler is null)
        {
            return 1;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                handler();
            }
            catch
            {
                // Swallow to avoid an unhandled exception propagating back
                // through the Objective-C runtime.
            }
        });

        // Return NO so AppKit takes no further action; the new window we
        // post above is sufficient to satisfy the user's reopen intent.
        return 0;
    }

    [UnmanagedCallersOnly]
    private static void NewWindowCallback(IntPtr self, IntPtr sel, IntPtr sender)
    {
        _ = self;
        _ = sel;
        _ = sender;
        Action? handler;
        lock (SyncRoot)
        {
            handler = newWindowHandler;
        }

        if (handler is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                handler();
            }
            catch
            {
                // Swallow to avoid an unhandled exception propagating back
                // through the Objective-C runtime.
            }
        });
    }

    [UnmanagedCallersOnly]
    private static void ActivateWindowCallback(IntPtr self, IntPtr sel, IntPtr sender)
    {
        _ = self;
        _ = sel;
        long tag = (long)NativeMethods.ObjCMsgSend(sender, NativeMethods.SelRegisterName("tag"));
        Window? window = null;
        lock (SyncRoot)
        {
            if (tag >= 0 && tag < RegisteredWindows.Count)
            {
                window = RegisteredWindows[(int)tag];
            }
        }

        if (window is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                window.Activate();
            }
            catch
            {
                // Ignore — window may have been closed between click
                // dispatch and UI-thread execution.
            }
        });
    }

    private static IntPtr BuildDockMenu()
    {
        IntPtr nsMenuClass = NativeMethods.ObjCGetClass("NSMenu");
        IntPtr nsMenuItemClass = NativeMethods.ObjCGetClass("NSMenuItem");
        IntPtr allocSel = NativeMethods.SelRegisterName("alloc");
        IntPtr initSel = NativeMethods.SelRegisterName("init");
        IntPtr autoreleaseSel = NativeMethods.SelRegisterName("autorelease");
        IntPtr addItemSel = NativeMethods.SelRegisterName("addItem:");
        IntPtr separatorSel = NativeMethods.SelRegisterName("separatorItem");
        IntPtr initItemSel = NativeMethods.SelRegisterName("initWithTitle:action:keyEquivalent:");
        IntPtr setTargetSel = NativeMethods.SelRegisterName("setTarget:");
        IntPtr setTagSel = NativeMethods.SelRegisterName("setTag:");
        IntPtr setStateSel = NativeMethods.SelRegisterName("setState:");

        IntPtr menu = NativeMethods.ObjCMsgSend(NativeMethods.ObjCMsgSend(nsMenuClass, allocSel), initSel);
        menu = NativeMethods.ObjCMsgSend(menu, autoreleaseSel);

        IntPtr emptyString = CreateNsString(string.Empty);

        // "New Window" entry.
        if (newWindowHandler is not null)
        {
            IntPtr title = CreateNsString("New Window");
            IntPtr newItem = NativeMethods.ObjCMsgSend(nsMenuItemClass, allocSel);
            newItem = NativeMethods.ObjCMsgSend4(newItem, initItemSel, title, newWindowSelector, emptyString);
            newItem = NativeMethods.ObjCMsgSend(newItem, autoreleaseSel);
            NativeMethods.ObjCMsgSendIntPtr(newItem, setTargetSel, targetInstance);
            NativeMethods.ObjCMsgSendIntPtr(menu, addItemSel, newItem);

            IntPtr separator = NativeMethods.ObjCMsgSend(nsMenuItemClass, separatorSel);
            NativeMethods.ObjCMsgSendIntPtr(menu, addItemSel, separator);
        }

        // One entry per registered window. Snapshot under the lock so we
        // iterate a stable list without holding the lock during AppKit calls.
        Window[] snapshot;
        lock (SyncRoot)
        {
            snapshot = RegisteredWindows.ToArray();
        }

        IntPtr keyWindow = GetKeyNsWindow();

        for (int i = 0; i < snapshot.Length; i++)
        {
            Window w = snapshot[i];
            string label = string.IsNullOrEmpty(w.Title) ? "AeroTerm" : w.Title!;
            IntPtr title = CreateNsString(label);
            IntPtr item = NativeMethods.ObjCMsgSend(nsMenuItemClass, allocSel);
            item = NativeMethods.ObjCMsgSend4(item, initItemSel, title, activateWindowSelector, emptyString);
            item = NativeMethods.ObjCMsgSend(item, autoreleaseSel);
            NativeMethods.ObjCMsgSendIntPtr(item, setTargetSel, targetInstance);
            NativeMethods.ObjCMsgSendLong(item, setTagSel, i);

            IntPtr ns = w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (ns != IntPtr.Zero && ns == keyWindow)
            {
                // NSControlStateValueOn = 1
                NativeMethods.ObjCMsgSendLong(item, setStateSel, 1);
            }

            NativeMethods.ObjCMsgSendIntPtr(menu, addItemSel, item);
        }

        return menu;
    }

    private static IntPtr GetKeyNsWindow()
    {
        IntPtr nsAppClass = NativeMethods.ObjCGetClass("NSApplication");
        IntPtr nsApp = NativeMethods.ObjCMsgSend(nsAppClass, NativeMethods.SelRegisterName("sharedApplication"));
        if (nsApp == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return NativeMethods.ObjCMsgSend(nsApp, NativeMethods.SelRegisterName("keyWindow"));
    }

    private static IntPtr CreateNsString(string value)
    {
        IntPtr nsStringClass = NativeMethods.ObjCGetClass("NSString");
        IntPtr sel = NativeMethods.SelRegisterName("stringWithUTF8String:");
        return NativeMethods.ObjCMsgSendUtf8(nsStringClass, sel, value);
    }

    /// <summary>
    /// Contains P/Invoke declarations for the Objective-C runtime used by
    /// <see cref="MacOSWindowMenu"/>.
    /// </summary>
    private static class NativeMethods
    {
        /// <summary>Returns a pointer to a class by name.</summary>
        /// <param name="name">The class name.</param>
        /// <returns>The class pointer, or <see cref="IntPtr.Zero"/>.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
        public static extern IntPtr ObjCGetClass(string name);

        /// <summary>Registers a selector with the Objective-C runtime.</summary>
        /// <param name="name">The selector name.</param>
        /// <returns>The selector pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
        public static extern IntPtr SelRegisterName(string name);

        /// <summary>Returns the class pointer for an instance.</summary>
        /// <param name="obj">The instance.</param>
        /// <returns>The class pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "object_getClass")]
        public static extern IntPtr ObjectGetClass(IntPtr obj);

        /// <summary>Allocates a new class pair (subclass of superclass).</summary>
        /// <param name="superclass">The superclass.</param>
        /// <param name="name">The new class name.</param>
        /// <param name="extraBytes">Extra instance bytes.</param>
        /// <returns>The new class, or zero if the name is taken.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_allocateClassPair")]
        public static extern IntPtr ObjCAllocateClassPair(IntPtr superclass, string name, IntPtr extraBytes);

        /// <summary>Registers a previously allocated class pair.</summary>
        /// <param name="cls">The class to register.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_registerClassPair")]
        public static extern void ObjCRegisterClassPair(IntPtr cls);

        /// <summary>Adds an instance method to a class.</summary>
        /// <param name="cls">The target class.</param>
        /// <param name="selector">The selector being implemented.</param>
        /// <param name="imp">The function pointer providing the implementation.</param>
        /// <param name="types">The Objective-C type encoding for the method.</param>
        /// <returns><c>true</c> if the method was added.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "class_addMethod")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ClassAddMethod(IntPtr cls, IntPtr selector, IntPtr imp, string types);

        /// <summary>Adds or replaces an instance method on a class.</summary>
        /// <param name="cls">The target class.</param>
        /// <param name="selector">The selector being implemented.</param>
        /// <param name="imp">The new implementation pointer.</param>
        /// <param name="types">The Objective-C type encoding.</param>
        /// <returns>The previous implementation, if any.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "class_replaceMethod")]
        public static extern IntPtr ClassReplaceMethod(IntPtr cls, IntPtr selector, IntPtr imp, string types);

        /// <summary>Sends a nullary message to an object and returns a pointer.</summary>
        /// <param name="receiver">The receiver.</param>
        /// <param name="selector">The selector.</param>
        /// <returns>The return value as a pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSend(IntPtr receiver, IntPtr selector);

        /// <summary>Sends a unary pointer message.</summary>
        /// <param name="receiver">The receiver.</param>
        /// <param name="selector">The selector.</param>
        /// <param name="arg">The pointer argument.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        /// <summary>Sends a unary long-integer message.</summary>
        /// <param name="receiver">The receiver.</param>
        /// <param name="selector">The selector.</param>
        /// <param name="arg">The long argument.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendLong(IntPtr receiver, IntPtr selector, long arg);

        /// <summary>Sends a unary UTF-8-string message (e.g. +stringWithUTF8String:).</summary>
        /// <param name="receiver">The receiver.</param>
        /// <param name="selector">The selector.</param>
        /// <param name="arg">The UTF-8-encoded C string argument.</param>
        /// <returns>The returned object pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend", CharSet = CharSet.Ansi)]
        public static extern IntPtr ObjCMsgSendUtf8(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

        /// <summary>Sends a ternary pointer message (three pointer arguments).</summary>
        /// <param name="receiver">The receiver.</param>
        /// <param name="selector">The selector.</param>
        /// <param name="arg1">The first pointer argument.</param>
        /// <param name="arg2">The second pointer argument.</param>
        /// <param name="arg3">The third pointer argument.</param>
        /// <returns>The returned object pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSend4(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);
    }
}
