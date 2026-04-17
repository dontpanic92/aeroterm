// <copyright file="BellService.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AeroTerm.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reacts to terminal BEL events based on the user's configured
/// <see cref="Services.BellAction"/>. Implements <see cref="IBellOutputs"/>
/// so the individual reactions are composable and testable; routing is
/// performed by <see cref="BellDispatcher"/>.
/// </summary>
internal sealed class BellService : IBellOutputs
{
    private readonly AppSettings settings;
    private readonly Window window;
    private readonly ILogger log;
    private readonly Border? flashTarget;

    private int flashSerial;

    /// <summary>
    /// Initializes a new instance of the <see cref="BellService"/> class.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    /// <param name="window">The owning window (used for visual flash and as
    /// the notification title).</param>
    /// <param name="flashTarget">The border whose <c>BorderBrush</c> is briefly
    /// flashed for a visual bell. May be <see langword="null"/>, in which case
    /// visual-bell requests are silently skipped.</param>
    public BellService(AppSettings settings, Window window, Border? flashTarget)
    {
        this.settings = settings;
        this.window = window;
        this.flashTarget = flashTarget;
        this.log = AppLogger.For<BellService>();
    }

    /// <summary>
    /// Handle a BEL event from the terminal. Must be called on the UI thread.
    /// </summary>
    public void Handle()
    {
        BellDispatcher.Dispatch(this.settings.BellAction, this);
    }

    /// <inheritdoc />
    public void Visual()
    {
        if (this.flashTarget is null)
        {
            return;
        }

        int serial = ++this.flashSerial;
        var originalBrush = this.flashTarget.BorderBrush;
        var originalThickness = this.flashTarget.BorderThickness;

        // Prefer the current theme accent; fall back to an amber tint so
        // the cue is visible even in minimal themes.
        IBrush flashBrush = this.TryGetAccentBrush()
            ?? new SolidColorBrush(Color.FromArgb(200, 255, 200, 64));

        this.flashTarget.BorderBrush = flashBrush;
        this.flashTarget.BorderThickness = new Avalonia.Thickness(2);

        DispatcherTimer.RunOnce(
            () =>
            {
                if (serial == this.flashSerial && this.flashTarget is not null)
                {
                    this.flashTarget.BorderBrush = originalBrush;
                    this.flashTarget.BorderThickness = originalThickness;
                }
            },
            TimeSpan.FromMilliseconds(150));
    }

    /// <inheritdoc />
    public void Audio()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NativeMethods.MessageBeep(NativeMethods.MbOk);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                NativeMethods.NSBeep();
                return;
            }

            // Linux / other: no built-in system-beep equivalent is guaranteed
            // available. Emit the BEL byte to stdout so whatever launched us
            // (including a host compositor with audible-bell enabled) may
            // relay it. GUI launches will drop it — log so behavior is
            // observable via the app log.
            Console.Write('\a');
            this.log.LogDebug("Audio bell: no native beep on this platform; wrote BEL to stdout.");
        }
        catch (Exception ex)
        {
            this.log.LogDebug(ex, "Audio bell failed; swallowing.");
        }
    }

    /// <inheritdoc />
    public void Notify()
    {
        string title = string.IsNullOrWhiteSpace(this.window.Title) ? "AeroTerm" : this.window.Title!;
        const string Body = "Terminal bell";

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // `osascript` ships with macOS; use display notification to
                // surface a native Notification Center toast without taking
                // on a WinRT / Cocoa interop dependency.
                string script = $"display notification \"{Escape(Body)}\" with title \"{Escape(title)}\"";
                LaunchDetached("osascript", new[] { "-e", script });
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Minimal PowerShell one-liner that raises a balloon tip
                // via System.Windows.Forms.NotifyIcon. Avoids WinRT interop.
                string ps =
                    "[void][Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms');"
                    + $"$n = New-Object System.Windows.Forms.NotifyIcon;"
                    + "$n.Icon = [System.Drawing.SystemIcons]::Information;"
                    + "$n.Visible = $true;"
                    + $"$n.ShowBalloonTip(3000, '{Escape(title)}', '{Escape(Body)}', 'Info');"
                    + "Start-Sleep -Seconds 4; $n.Dispose();";
                LaunchDetached(
                    "powershell",
                    new[] { "-NoProfile", "-WindowStyle", "Hidden", "-Command", ps });
                return;
            }

            // Linux: freedesktop notify-send is the de facto standard.
            LaunchDetached("notify-send", new[] { title, Body });
        }
        catch (Exception ex)
        {
            this.log.LogDebug(ex, "Notification bell failed; swallowing.");
        }
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal)
             .Replace("'", "''", StringComparison.Ordinal);

    private static void LaunchDetached(string fileName, string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi);
        proc?.Dispose();
    }

    private IBrush? TryGetAccentBrush()
    {
        if (this.window.TryGetResource("SystemAccentColor", this.window.ActualThemeVariant, out var accent)
            && accent is Color accentColor)
        {
            return new SolidColorBrush(Color.FromArgb(200, accentColor.R, accentColor.G, accentColor.B));
        }

        if (this.window.TryGetResource("SystemAccentColorBrush", this.window.ActualThemeVariant, out var brush)
            && brush is IBrush br)
        {
            return br;
        }

        return null;
    }

    private static class NativeMethods
    {
        /// <summary>
        /// Parameter for <see cref="MessageBeep"/> requesting the default
        /// "OK" sound (the closest match to a console beep).
        /// </summary>
        public const uint MbOk = 0x00000000;

        [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit", EntryPoint = "NSBeep")]
        public static extern void NSBeep();

        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MessageBeep(uint uType);
    }
}
