// <copyright file="MacOSHotkeyPlatform.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects.QuakeHotkey;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Input;
using Avalonia.Threading;

/// <summary>
/// macOS backend for <see cref="GlobalHotkey"/>, built on Carbon's
/// <c>RegisterEventHotKey</c> API. Carbon is officially legacy yet the
/// hotkey primitives remain the most pragmatic way to grab a system-wide
/// chord from an Avalonia app (modern replacements such as
/// <c>NSEvent.addGlobalMonitorForEventsMatchingMask</c> cannot swallow the
/// event, so the key still reaches the focused app).
/// </summary>
internal sealed class MacOSHotkeyPlatform : IHotkeyPlatform
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    private const uint EventClassKeyboard = 0x6B657962; // 'keyb'
    private const uint EventHotKeyPressed = 5;
    private const uint TypeEventHotKeyId = 0x686B6964; // 'hkid'
    private const uint ParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint SignatureAero = 0x4145524F;     // 'AERO'

    private readonly object syncRoot = new();
    private readonly Dictionary<uint, HotkeyRecord> records = new();
    private NativeMethods.EventHandlerProcDelegate? handlerDelegate;
    private IntPtr handlerRef;
    private int nextId;

    /// <inheritdoc />
    public bool TryRegister(KeyModifiers modifiers, Key key, Action handler, out IDisposable? registration)
    {
        registration = null;
        int vk = HotkeyKeyMap.ToMacVirtualKey(key);
        if (vk < 0)
        {
            return false;
        }

        uint mods = HotkeyKeyMap.ToMacModifiers(modifiers);
        if (!this.EnsureHandlerInstalled())
        {
            return false;
        }

        uint id = (uint)Interlocked.Increment(ref this.nextId);
        var hkid = new NativeMethods.EventHotKeyId
        {
            Signature = SignatureAero,
            Id = id,
        };

        int status = NativeMethods.RegisterEventHotKey(
            (uint)vk,
            mods,
            hkid,
            NativeMethods.GetEventDispatcherTarget(),
            0,
            out IntPtr hotKeyRef);
        if (status != 0 || hotKeyRef == IntPtr.Zero)
        {
            return false;
        }

        lock (this.syncRoot)
        {
            this.records[id] = new HotkeyRecord(hotKeyRef, handler);
        }

        registration = new Registration(this, id);
        return true;
    }

    private bool EnsureHandlerInstalled()
    {
        lock (this.syncRoot)
        {
            if (this.handlerRef != IntPtr.Zero)
            {
                return true;
            }

            this.handlerDelegate = this.HotkeyEventHandler;
            var specs = new[]
            {
                new NativeMethods.EventTypeSpec
                {
                    EventClass = EventClassKeyboard,
                    EventKind = EventHotKeyPressed,
                },
            };

            int status = NativeMethods.InstallEventHandler(
                NativeMethods.GetEventDispatcherTarget(),
                Marshal.GetFunctionPointerForDelegate(this.handlerDelegate),
                1,
                specs,
                IntPtr.Zero,
                out this.handlerRef);
            if (status != 0)
            {
                this.handlerDelegate = null;
                this.handlerRef = IntPtr.Zero;
                return false;
            }

            return true;
        }
    }

    private int HotkeyEventHandler(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        try
        {
            NativeMethods.EventHotKeyId hkid = default;
            int getStatus = NativeMethods.GetEventParameter(
                theEvent,
                ParamDirectObject,
                TypeEventHotKeyId,
                IntPtr.Zero,
                (uint)Marshal.SizeOf<NativeMethods.EventHotKeyId>(),
                IntPtr.Zero,
                ref hkid);
            if (getStatus != 0)
            {
                return 0;
            }

            Action? handler;
            lock (this.syncRoot)
            {
                handler = this.records.TryGetValue(hkid.Id, out var rec) ? rec.Handler : null;
            }

            if (handler is not null)
            {
                Dispatcher.UIThread.Post(handler);
            }
        }
        catch
        {
            // Never propagate back into Carbon's dispatcher.
        }

        return 0;
    }

    private void Unregister(uint id)
    {
        IntPtr hotKeyRef;
        lock (this.syncRoot)
        {
            if (!this.records.TryGetValue(id, out var rec))
            {
                return;
            }

            hotKeyRef = rec.HotKeyRef;
            this.records.Remove(id);
        }

        if (hotKeyRef != IntPtr.Zero)
        {
            NativeMethods.UnregisterEventHotKey(hotKeyRef);
        }
    }

    private static class NativeMethods
    {
        public delegate int EventHandlerProcDelegate(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

        [DllImport(Carbon)]
        public static extern IntPtr GetEventDispatcherTarget();

        [DllImport(Carbon)]
        public static extern int InstallEventHandler(
            IntPtr inTarget,
            IntPtr inHandler,
            uint inNumTypes,
            EventTypeSpec[] inList,
            IntPtr inUserData,
            out IntPtr outRef);

        [DllImport(Carbon)]
        public static extern int RegisterEventHotKey(
            uint inHotKeyCode,
            uint inHotKeyModifiers,
            EventHotKeyId inHotKeyId,
            IntPtr inTarget,
            uint inOptions,
            out IntPtr outRef);

        [DllImport(Carbon)]
        public static extern int UnregisterEventHotKey(IntPtr inHotKeyRef);

        [DllImport(Carbon)]
        public static extern int GetEventParameter(
            IntPtr inEvent,
            uint inName,
            uint inDesiredType,
            IntPtr outActualType,
            uint inBufferSize,
            IntPtr outActualSize,
            ref EventHotKeyId outData);

        [StructLayout(LayoutKind.Sequential)]
        public struct EventTypeSpec
        {
            public uint EventClass;
            public uint EventKind;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EventHotKeyId
        {
            public uint Signature;
            public uint Id;
        }
    }

    private sealed record HotkeyRecord(IntPtr HotKeyRef, Action Handler);

    private sealed class Registration : IDisposable
    {
        private readonly MacOSHotkeyPlatform owner;
        private readonly uint id;
        private int disposed;

        public Registration(MacOSHotkeyPlatform owner, uint id)
        {
            this.owner = owner;
            this.id = id;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) == 0)
            {
                this.owner.Unregister(this.id);
            }
        }
    }
}
