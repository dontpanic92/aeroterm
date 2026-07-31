// <copyright file="MacOsPressAndHold.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Utilities;

using System.Runtime.InteropServices;
using AeroTerm.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Disables the macOS "press and hold" accent popup for AeroTerm so that
/// holding a key auto-repeats it, which is what a terminal emulator needs
/// (holding <c>j</c> in vim, holding Backspace, arrow-key navigation, ...).
/// <para>
/// The popup is produced by AppKit inside <c>NSTextInputContext</c> before any
/// event reaches Avalonia, so it cannot be suppressed at the control level.
/// The only reliable fix is to write <c>ApplePressAndHoldEnabled = false</c>
/// into AeroTerm's own <c>NSUserDefaults</c> domain, which takes precedence
/// over the user's <c>NSGlobalDomain</c> value and affects this app only.
/// </para>
/// </summary>
internal static class MacOsPressAndHold
{
    /// <summary>
    /// The AppKit user-defaults key that controls the press-and-hold accent
    /// popup. When <c>false</c>, holding a key auto-repeats instead.
    /// </summary>
    private const string PressAndHoldKey = "ApplePressAndHoldEnabled";

    /// <summary>
    /// Ensures that holding a key repeats the character instead of opening the
    /// macOS accent/candidate popup, by disabling press-and-hold for this
    /// application's user-defaults domain.
    /// <para>
    /// Must be called before <c>NSApplication</c> is created (i.e. before
    /// Avalonia starts) so AppKit picks the value up for the first window.
    /// The write is skipped when the value is already <c>false</c>, so repeated
    /// launches do not keep dirtying the preferences plist. Never throws.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> when press-and-hold is disabled for this application after
    /// the call; <c>false</c> on non-macOS platforms or when the native call
    /// failed.
    /// </returns>
    public static bool EnsureKeyRepeatEnabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        var log = AppLogger.For("MacOsPressAndHold");

        try
        {
            IntPtr defaults = GetStandardUserDefaults();
            if (defaults == IntPtr.Zero)
            {
                log.LogWarning("Could not obtain NSUserDefaults; key repeat may be unavailable.");
                return false;
            }

            IntPtr key = CreateNsString(PressAndHoldKey);
            if (key == IntPtr.Zero)
            {
                log.LogWarning("Could not create the NSString key; key repeat may be unavailable.");
                return false;
            }

            if (TryReadBool(defaults, key) == false)
            {
                log.LogDebug("Press-and-hold already disabled for this application.");
                return true;
            }

            NativeMethods.ObjCMsgSendBoolPtr(
                defaults,
                NativeMethods.SelRegisterName("setBool:forKey:"),
                false,
                key);

            NativeMethods.ObjCMsgSendBoolRet(
                defaults,
                NativeMethods.SelRegisterName("synchronize"));

            bool applied = TryReadBool(defaults, key) == false;
            if (applied)
            {
                log.LogInformation("Disabled macOS press-and-hold for AeroTerm; keys now auto-repeat.");
            }
            else
            {
                log.LogWarning("Writing {Key} did not take effect; keys may not auto-repeat.", PressAndHoldKey);
            }

            return applied;
        }
        catch (Exception ex)
        {
            // Startup must never fail because of an interop problem.
            log.LogWarning(ex, "Failed to disable macOS press-and-hold; keys may not auto-repeat.");
            return false;
        }
    }

    /// <summary>
    /// Reads the current value of the press-and-hold user default for this
    /// application. Used by diagnostics and tests.
    /// </summary>
    /// <returns>
    /// The stored value, or <c>null</c> when it is unset or the platform is
    /// not macOS.
    /// </returns>
    public static bool? ReadPressAndHoldEnabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return null;
        }

        try
        {
            IntPtr defaults = GetStandardUserDefaults();
            if (defaults == IntPtr.Zero)
            {
                return null;
            }

            IntPtr key = CreateNsString(PressAndHoldKey);
            return key == IntPtr.Zero ? null : TryReadBool(defaults, key);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes this application's press-and-hold override so the process falls
    /// back to the system-wide value. Exists so tests can restore the machine's
    /// original state after exercising the interop path.
    /// </summary>
    /// <returns><c>true</c> when the override was removed.</returns>
    public static bool ResetPressAndHoldOverrideForTesting()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        try
        {
            IntPtr defaults = GetStandardUserDefaults();
            IntPtr key = CreateNsString(PressAndHoldKey);
            if (defaults == IntPtr.Zero || key == IntPtr.Zero)
            {
                return false;
            }

            NativeMethods.ObjCMsgSendPtrRetPtr(
                defaults,
                NativeMethods.SelRegisterName("removeObjectForKey:"),
                key);

            NativeMethods.ObjCMsgSendBoolRet(
                defaults,
                NativeMethods.SelRegisterName("synchronize"));

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns <c>[NSUserDefaults standardUserDefaults]</c>.
    /// </summary>
    /// <returns>The shared defaults object, or <see cref="IntPtr.Zero"/>.</returns>
    private static IntPtr GetStandardUserDefaults()
    {
        IntPtr cls = NativeMethods.ObjCGetClass("NSUserDefaults");
        return cls == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.ObjCMsgSend(cls, NativeMethods.SelRegisterName("standardUserDefaults"));
    }

    /// <summary>
    /// Creates an autoreleased <c>NSString</c> from a managed string.
    /// </summary>
    /// <param name="value">The string value.</param>
    /// <returns>The NSString pointer, or <see cref="IntPtr.Zero"/>.</returns>
    private static IntPtr CreateNsString(string value)
    {
        IntPtr cls = NativeMethods.ObjCGetClass("NSString");
        return cls == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.ObjCMsgSendUtf8RetPtr(
                cls,
                NativeMethods.SelRegisterName("stringWithUTF8String:"),
                value);
    }

    /// <summary>
    /// Reads a boolean default, distinguishing "unset" from <c>false</c> by
    /// first probing with <c>objectForKey:</c>.
    /// </summary>
    /// <param name="defaults">The NSUserDefaults object.</param>
    /// <param name="key">The NSString key.</param>
    /// <returns>The stored value, or <c>null</c> when unset.</returns>
    private static bool? TryReadBool(IntPtr defaults, IntPtr key)
    {
        IntPtr stored = NativeMethods.ObjCMsgSendPtrRetPtr(
            defaults,
            NativeMethods.SelRegisterName("objectForKey:"),
            key);

        if (stored == IntPtr.Zero)
        {
            return null;
        }

        return NativeMethods.ObjCMsgSendPtrRetBool(
            defaults,
            NativeMethods.SelRegisterName("boolForKey:"),
            key);
    }

    /// <summary>
    /// Objective-C runtime entry points used by this helper.
    /// </summary>
    private static class NativeMethods
    {
        /// <summary>
        /// Returns a pointer to the class definition identified by name.
        /// </summary>
        /// <param name="name">The class name.</param>
        /// <returns>The class pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
        public static extern IntPtr ObjCGetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        /// <summary>
        /// Registers a selector with the Objective-C runtime.
        /// </summary>
        /// <param name="name">The selector name.</param>
        /// <returns>The selector pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
        public static extern IntPtr SelRegisterName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        /// <summary>
        /// Sends a message with no arguments and a pointer return value.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <returns>The return value as a pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSend(IntPtr receiver, IntPtr selector);

        /// <summary>
        /// Sends a message with no arguments and a boolean return value.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <returns>The boolean return value.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ObjCMsgSendBoolRet(IntPtr receiver, IntPtr selector);

        /// <summary>
        /// Sends a message with a UTF-8 C-string argument and a pointer return
        /// value, used for <c>+[NSString stringWithUTF8String:]</c>.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The string argument.</param>
        /// <returns>The return value as a pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendUtf8RetPtr(
            IntPtr receiver,
            IntPtr selector,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

        /// <summary>
        /// Sends a message with a pointer argument and a pointer return value.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The pointer argument.</param>
        /// <returns>The return value as a pointer.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendPtrRetPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        /// <summary>
        /// Sends a message with a pointer argument and a boolean return value.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="arg">The pointer argument.</param>
        /// <returns>The boolean return value.</returns>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool ObjCMsgSendPtrRetBool(IntPtr receiver, IntPtr selector, IntPtr arg);

        /// <summary>
        /// Sends a message with a boolean and a pointer argument, used for
        /// <c>-[NSUserDefaults setBool:forKey:]</c>.
        /// </summary>
        /// <param name="receiver">The target object.</param>
        /// <param name="selector">The selector to invoke.</param>
        /// <param name="value">The boolean argument.</param>
        /// <param name="key">The pointer argument.</param>
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendBoolPtr(
            IntPtr receiver,
            IntPtr selector,
            [MarshalAs(UnmanagedType.I1)] bool value,
            IntPtr key);
    }
}
