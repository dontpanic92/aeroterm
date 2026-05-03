// <copyright file="MacOSNativeMessageBoxAdapter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.NativeMessageBoxes;

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AeroTerm.Theme.Controls;
using Avalonia.Controls;

/// <summary>
/// AppKit-backed message-box adapter for macOS.
/// </summary>
internal sealed class MacOSNativeMessageBoxAdapter : INativeMessageBoxPlatformAdapter
{
    private const long NSAlertFirstButtonReturn = 1000;
    private const long NSAlertStyleWarning = 0;
    private const long NSAlertStyleInformational = 1;
    private const string NSImageNameCaution = "NSCaution";
    private const string NSImageNameInfo = "NSInfo";

    private readonly AvaloniaNativeMessageBoxAdapter fallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="MacOSNativeMessageBoxAdapter"/> class.
    /// </summary>
    /// <param name="fallback">The fallback adapter.</param>
    public MacOSNativeMessageBoxAdapter(AvaloniaNativeMessageBoxAdapter fallback)
    {
        this.fallback = fallback;
    }

    /// <inheritdoc/>
    public Task<NativeMessageBoxResult> ShowAsync(Window owner, NativeMessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(options);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return this.fallback.ShowAsync(owner, options);
        }

        try
        {
            return Task.FromResult(this.ShowAppKitAlert(options));
        }
        catch (DllNotFoundException)
        {
            return this.fallback.ShowAsync(owner, options);
        }
        catch (EntryPointNotFoundException)
        {
            return this.fallback.ShowAsync(owner, options);
        }
    }

    private NativeMessageBoxResult ShowAppKitAlert(NativeMessageBoxOptions options)
    {
        IntPtr nsAlertClass = NativeMethods.ObjCGetClass("NSAlert");
        if (nsAlertClass == IntPtr.Zero)
        {
            throw new EntryPointNotFoundException("NSAlert");
        }

        IntPtr alert = NativeMethods.ObjCMsgSend(nsAlertClass, NativeMethods.SelRegisterName("alloc"));
        alert = NativeMethods.ObjCMsgSend(alert, NativeMethods.SelRegisterName("init"));
        if (alert == IntPtr.Zero)
        {
            throw new EntryPointNotFoundException("NSAlert init");
        }

        try
        {
            this.SetAlertStyle(alert, options);
            this.SetAlertIcon(alert, options);
            this.SetString(alert, "setMessageText:", options.Title);
            this.SetString(alert, "setInformativeText:", options.Message);
            this.AddButtons(alert, options);

            long response = NativeMethods.ObjCMsgSendRetLong(alert, NativeMethods.SelRegisterName("runModal"));
            return this.MapResponse(options, response);
        }
        finally
        {
            NativeMethods.ObjCMsgSend(alert, NativeMethods.SelRegisterName("release"));
        }
    }

    private NativeMessageBoxResult MapResponse(NativeMessageBoxOptions options, long response)
    {
        return options.Buttons switch
        {
            NativeMessageBoxButtons.Ok => NativeMessageBoxResult.Ok,
            NativeMessageBoxButtons.YesNo when response == NSAlertFirstButtonReturn => NativeMessageBoxResult.Yes,
            NativeMessageBoxButtons.YesNo => NativeMessageBoxResult.No,
            _ => options.CancelResult,
        };
    }

    private void SetAlertStyle(IntPtr alert, NativeMessageBoxOptions options)
    {
        long style = options.Buttons == NativeMessageBoxButtons.YesNo
            ? NSAlertStyleWarning
            : NSAlertStyleInformational;
        NativeMethods.ObjCMsgSendLong(alert, NativeMethods.SelRegisterName("setAlertStyle:"), style);
    }

    private void SetAlertIcon(IntPtr alert, NativeMessageBoxOptions options)
    {
        IntPtr nsImageClass = NativeMethods.ObjCGetClass("NSImage");
        if (nsImageClass == IntPtr.Zero)
        {
            return;
        }

        string iconName = options.Buttons == NativeMessageBoxButtons.YesNo
            ? NSImageNameCaution
            : NSImageNameInfo;
        IntPtr imageName = this.CreateNsString(iconName);
        IntPtr image = NativeMethods.ObjCMsgSendIntPtrRetIntPtr(
            nsImageClass,
            NativeMethods.SelRegisterName("imageNamed:"),
            imageName);
        if (image != IntPtr.Zero)
        {
            NativeMethods.ObjCMsgSendIntPtr(alert, NativeMethods.SelRegisterName("setIcon:"), image);
        }
    }

    private void AddButtons(IntPtr alert, NativeMessageBoxOptions options)
    {
        this.AddButton(alert, options.PrimaryButtonText);
        if (options.Buttons == NativeMessageBoxButtons.YesNo)
        {
            this.AddButton(alert, options.SecondaryButtonText ?? "No");
        }
    }

    private void AddButton(IntPtr alert, string text)
    {
        IntPtr nsText = this.CreateNsString(text);
        NativeMethods.ObjCMsgSendIntPtr(alert, NativeMethods.SelRegisterName("addButtonWithTitle:"), nsText);
    }

    private void SetString(IntPtr receiver, string selectorName, string value)
    {
        IntPtr nsString = this.CreateNsString(value);
        NativeMethods.ObjCMsgSendIntPtr(receiver, NativeMethods.SelRegisterName(selectorName), nsString);
    }

    private IntPtr CreateNsString(string value)
    {
        IntPtr nsStringClass = NativeMethods.ObjCGetClass("NSString");
        return nsStringClass == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.ObjCMsgSendUtf8(nsStringClass, NativeMethods.SelRegisterName("stringWithUTF8String:"), value);
    }

    /// <summary>
    /// P/Invoke declarations for Objective-C/AppKit message-box interop.
    /// </summary>
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
        public static extern IntPtr ObjCMsgSendUtf8(IntPtr receiver, IntPtr selector, string value);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr ObjCMsgSendIntPtrRetIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern void ObjCMsgSendLong(IntPtr receiver, IntPtr selector, long arg);

        [DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern long ObjCMsgSendRetLong(IntPtr receiver, IntPtr selector);
    }
}
