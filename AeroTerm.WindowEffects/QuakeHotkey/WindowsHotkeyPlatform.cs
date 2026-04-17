// <copyright file="WindowsHotkeyPlatform.cs">
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
/// Windows backend for <see cref="GlobalHotkey"/>. Owns a dedicated message
/// loop thread that creates a message-only window
/// (<c>HWND_MESSAGE</c>) and pumps <c>WM_HOTKEY</c>, dispatching each
/// firing to <see cref="Dispatcher.UIThread"/>.
/// </summary>
internal sealed class WindowsHotkeyPlatform : IHotkeyPlatform
{
    private const int WmHotkey = 0x0312;
    private const int WmApp = 0x8000;
    private const int WmAppRegister = WmApp + 1;
    private const int WmAppUnregister = WmApp + 2;

    private readonly object syncRoot = new();
    private readonly Dictionary<int, Action> handlers = new();
    private readonly ManualResetEventSlim ready = new(false);

    private Thread? pumpThread;
    private IntPtr hwnd;
    private uint threadId;
    private int nextId;
    private bool started;

    /// <inheritdoc />
    public bool TryRegister(KeyModifiers modifiers, Key key, Action handler, out IDisposable? registration)
    {
        registration = null;
        uint vk = HotkeyKeyMap.ToWin32VirtualKey(key);
        if (vk == 0)
        {
            return false;
        }

        uint mods = HotkeyKeyMap.ToWin32Modifiers(modifiers);
        this.EnsureStarted();
        if (this.hwnd == IntPtr.Zero)
        {
            return false;
        }

        int id = Interlocked.Increment(ref this.nextId);
        var request = new RegisterRequest(id, mods, vk);
        IntPtr requestPtr = GCHandle.ToIntPtr(GCHandle.Alloc(request));

        if (NativeMethods.PostThreadMessage(this.threadId, WmAppRegister, IntPtr.Zero, requestPtr) == 0)
        {
            GCHandle.FromIntPtr(requestPtr).Free();
            return false;
        }

        request.Complete.Wait();
        if (!request.Success)
        {
            return false;
        }

        lock (this.syncRoot)
        {
            this.handlers[id] = handler;
        }

        registration = new Registration(this, id);
        return true;
    }

    private void EnsureStarted()
    {
        lock (this.syncRoot)
        {
            if (this.started)
            {
                return;
            }

            this.started = true;
            this.pumpThread = new Thread(this.PumpLoop)
            {
                IsBackground = true,
                Name = "AeroTerm.GlobalHotkeyPump",
            };
            this.pumpThread.Start();
            this.ready.Wait(TimeSpan.FromSeconds(2));
        }
    }

    private void PumpLoop()
    {
        this.threadId = NativeMethods.GetCurrentThreadId();

        IntPtr hwndMessage = (IntPtr)(-3); // HWND_MESSAGE
        this.hwnd = NativeMethods.CreateWindowExW(
            0,
            "STATIC",
            "AeroTerm.HotkeyPump",
            0,
            0,
            0,
            0,
            0,
            hwndMessage,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        this.ready.Set();

        if (this.hwnd == IntPtr.Zero)
        {
            return;
        }

        while (NativeMethods.GetMessage(out NativeMethods.Msg msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.Message == WmHotkey)
            {
                int id = msg.WParam.ToInt32();
                Action? h;
                lock (this.syncRoot)
                {
                    this.handlers.TryGetValue(id, out h);
                }

                if (h is not null)
                {
                    Dispatcher.UIThread.Post(h);
                }
            }
            else if (msg.Message == WmAppRegister)
            {
                var handle = GCHandle.FromIntPtr(msg.LParam);
                var req = (RegisterRequest)handle.Target!;
                req.Success = NativeMethods.RegisterHotKey(this.hwnd, req.Id, req.Modifiers, req.VirtualKey);
                handle.Free();
                req.Complete.Set();
            }
            else if (msg.Message == WmAppUnregister)
            {
                int id = msg.WParam.ToInt32();
                NativeMethods.UnregisterHotKey(this.hwnd, id);
            }
            else
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }
    }

    private void Unregister(int id)
    {
        lock (this.syncRoot)
        {
            if (!this.handlers.Remove(id))
            {
                return;
            }
        }

        if (this.threadId != 0)
        {
            NativeMethods.PostThreadMessage(this.threadId, WmAppUnregister, (IntPtr)id, IntPtr.Zero);
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref Msg lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref Msg lpMsg);

        [DllImport("user32.dll")]
        public static extern int PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        public struct Msg
        {
            public IntPtr Hwnd;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int PtX;
            public int PtY;
        }
    }

    private sealed class RegisterRequest
    {
        public RegisterRequest(int id, uint modifiers, uint vk)
        {
            this.Id = id;
            this.Modifiers = modifiers;
            this.VirtualKey = vk;
            this.Complete = new ManualResetEventSlim(false);
        }

        public int Id { get; }

        public uint Modifiers { get; }

        public uint VirtualKey { get; }

        public ManualResetEventSlim Complete { get; }

        public bool Success { get; set; }
    }

    private sealed class Registration : IDisposable
    {
        private readonly WindowsHotkeyPlatform owner;
        private readonly int id;
        private int disposed;

        public Registration(WindowsHotkeyPlatform owner, int id)
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
