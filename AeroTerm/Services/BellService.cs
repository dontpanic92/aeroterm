// <copyright file="BellService.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Runtime.InteropServices;
using AeroTerm.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reacts to terminal BEL events based on the user's configured
/// <see cref="Services.BellAction"/>. Currently supports visual flash
/// and a Windows console beep; OS notifications are logged but not yet
/// surfaced through a native toast.
/// </summary>
internal sealed class BellService
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
    /// <param name="window">The owning window (used for visual flash).</param>
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
        var action = this.settings.BellAction;
        if (action == BellAction.None)
        {
            return;
        }

        if (action == BellAction.Visual || action == BellAction.All)
        {
            this.FlashWindow();
        }

        if (action == BellAction.Audio || action == BellAction.All)
        {
            this.PlayBeep();
        }

        if (action == BellAction.Notification || action == BellAction.All)
        {
            this.PostNotification();
        }
    }

    private void FlashWindow()
    {
        if (this.flashTarget is null)
        {
            return;
        }

        int serial = ++this.flashSerial;
        var originalBrush = this.flashTarget.BorderBrush;
        var originalThickness = this.flashTarget.BorderThickness;

        this.flashTarget.BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 200, 64));
        this.flashTarget.BorderThickness = new Avalonia.Thickness(2);

        // Restore after a short delay, but only if no newer flash has started.
        DispatcherTimer.RunOnce(
            () =>
            {
                if (serial == this.flashSerial && this.flashTarget is not null)
                {
                    this.flashTarget.BorderBrush = originalBrush;
                    this.flashTarget.BorderThickness = originalThickness;
                }
            },
            TimeSpan.FromMilliseconds(180));
    }

    private void PlayBeep()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                Console.Beep();
                return;
            }
            catch
            {
                // Fall through to logging below.
            }
        }

        // On non-Windows we have no built-in system beep — fall back to
        // writing the BEL byte to stdout, which the hosting terminal (if any)
        // may relay. Applications launched from a GUI will simply drop it.
        Console.Write('\a');
    }

    private void PostNotification()
    {
        // Full native notification support is tracked as a follow-up
        // (see artifacts/ROADMAP.md §2.6). Log the event for now so it is
        // observable via the AppLogger log file.
        this.log.LogInformation("Terminal BEL received (window: {Title})", this.window.Title ?? "AeroTerm");
    }
}
