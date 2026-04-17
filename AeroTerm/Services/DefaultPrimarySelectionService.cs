// <copyright file="DefaultPrimarySelectionService.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AeroTerm.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Default <see cref="IPrimarySelectionService"/> implementation.
/// On Linux with an X11 session and either <c>xclip</c> or <c>xsel</c>
/// present on <c>PATH</c>, PRIMARY is routed through that helper. On
/// every other platform (macOS, Windows, Wayland-only Linux, or Linux
/// without a helper binary) reads return <c>null</c> and writes are
/// no-ops — callers should treat that as a signal to fall back to the
/// regular clipboard.
/// </summary>
internal sealed class DefaultPrimarySelectionService : IPrimarySelectionService
{
    /// <summary>
    /// Process-wide singleton. Tests inject a different implementation
    /// via <see cref="AeroTerm.Controls.TerminalControl.PrimarySelectionService"/>.
    /// </summary>
    public static readonly DefaultPrimarySelectionService Instance = new();

    private readonly string? helperPath;
    private readonly HelperKind helperKind;

    private DefaultPrimarySelectionService()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        // Wayland-only sessions don't own an X11 PRIMARY selection. The
        // wl-clipboard project exposes wl-paste --primary but not every
        // Wayland compositor honours it, so we log once and let the
        // caller fall back to the regular clipboard.
        string? waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        string? xDisplay = Environment.GetEnvironmentVariable("DISPLAY");

        if (!string.IsNullOrEmpty(waylandDisplay) && string.IsNullOrEmpty(xDisplay))
        {
            AppLogger.For<DefaultPrimarySelectionService>().LogDebug(
                "Wayland session detected (WAYLAND_DISPLAY set, DISPLAY empty); PRIMARY selection disabled, middle-click will paste the regular clipboard.");
            return;
        }

        if (string.IsNullOrEmpty(xDisplay))
        {
            return;
        }

        // Resolve xclip first (more portable CLI), then xsel.
        string? resolved = ResolveOnPath("xclip");
        if (resolved is not null)
        {
            this.helperPath = resolved;
            this.helperKind = HelperKind.Xclip;
            return;
        }

        resolved = ResolveOnPath("xsel");
        if (resolved is not null)
        {
            this.helperPath = resolved;
            this.helperKind = HelperKind.Xsel;
            return;
        }

        AppLogger.For<DefaultPrimarySelectionService>().LogDebug(
            "Neither xclip nor xsel is on PATH; PRIMARY selection disabled.");
    }

    private enum HelperKind
    {
        None,
        Xclip,
        Xsel,
    }

    /// <inheritdoc />
    public bool IsAvailable => this.helperPath is not null;

    /// <inheritdoc />
    public async Task WriteAsync(string text)
    {
        if (this.helperPath is null || text is null)
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = this.helperPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (this.helperKind == HelperKind.Xclip)
        {
            startInfo.ArgumentList.Add("-selection");
            startInfo.ArgumentList.Add("primary");
            startInfo.ArgumentList.Add("-in");
        }
        else
        {
            startInfo.ArgumentList.Add("--primary");
            startInfo.ArgumentList.Add("--input");
        }

        try
        {
            using var proc = Process.Start(startInfo);
            if (proc is null)
            {
                return;
            }

            await proc.StandardInput.WriteAsync(text).ConfigureAwait(false);
            proc.StandardInput.Close();
            await proc.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            AppLogger.For<DefaultPrimarySelectionService>().LogDebug(ex, "Failed to write PRIMARY selection.");
        }
    }

    /// <inheritdoc />
    public async Task<string?> ReadAsync()
    {
        if (this.helperPath is null)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = this.helperPath,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (this.helperKind == HelperKind.Xclip)
        {
            startInfo.ArgumentList.Add("-selection");
            startInfo.ArgumentList.Add("primary");
            startInfo.ArgumentList.Add("-out");
        }
        else
        {
            startInfo.ArgumentList.Add("--primary");
            startInfo.ArgumentList.Add("--output");
        }

        try
        {
            using var proc = Process.Start(startInfo);
            if (proc is null)
            {
                return null;
            }

            string output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await proc.WaitForExitAsync().ConfigureAwait(false);
            return proc.ExitCode == 0 ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            AppLogger.For<DefaultPrimarySelectionService>().LogDebug(ex, "Failed to read PRIMARY selection.");
            return null;
        }
    }

    private static string? ResolveOnPath(string binary)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir))
            {
                continue;
            }

            try
            {
                string candidate = Path.Combine(dir, binary);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Skip malformed PATH entries.
            }
        }

        return null;
    }
}
