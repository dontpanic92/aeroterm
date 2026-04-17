// <copyright file="TerminalSessionCoordinator.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Runtime.InteropServices;
using AeroTerm.Controls;
using AeroTerm.Diagnostics;
using AeroTerm.Models;
using AeroTerm.Utilities;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

/// <summary>
/// Coordinates terminal session lifecycle: shell detection, PTY creation,
/// TerminalControl instantiation, event wiring, and shutdown.
/// </summary>
internal sealed class TerminalSessionCoordinator : IDisposable
{
    private readonly AppSettings settings;
    private readonly ILogger log;
    private readonly LaunchSpec? launchOverride;
    private TerminalControl? terminalControl;
    private System.ComponentModel.PropertyChangedEventHandler? settingsHandler;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalSessionCoordinator"/> class.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    public TerminalSessionCoordinator(AppSettings settings)
        : this(settings, launchOverride: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalSessionCoordinator"/> class
    /// with an explicit launch specification that overrides the defaults normally
    /// derived from the environment (used by "duplicate tab").
    /// </summary>
    /// <param name="settings">Application settings.</param>
    /// <param name="launchOverride">Launch spec that takes precedence over the
    /// default shell / cwd / env detection when non-null.</param>
    internal TerminalSessionCoordinator(AppSettings settings, LaunchSpec? launchOverride)
    {
        this.settings = settings;
        this.log = AppLogger.For<TerminalSessionCoordinator>();
        this.launchOverride = launchOverride;
    }

    /// <summary>
    /// Raised when the <see cref="TerminalControl"/> is created and ready to
    /// be placed in the visual tree.
    /// </summary>
    public event Action<TerminalControl>? TerminalReady;

    /// <summary>
    /// Raised when the terminal reports a title change.
    /// </summary>
    public event Action<string>? TitleChanged;

    /// <summary>
    /// Raised when the terminal reports a background color change.
    /// </summary>
    public event Action<int>? BackgroundColorChanged;

    /// <summary>
    /// Raised when the shell process exits cleanly (exit code 0).
    /// </summary>
    public event Action? ProcessExitedNormally;

    /// <summary>
    /// Raised when the terminal receives a BEL (0x07) control character.
    /// Always invoked on the UI thread.
    /// </summary>
    public event Action? BellRaised;

    /// <summary>
    /// Gets the active terminal control, or <c>null</c> if not yet initialized.
    /// </summary>
    public TerminalControl? Control => this.terminalControl;

    /// <summary>
    /// Gets the <see cref="LaunchSpec"/> that was actually used to start the
    /// child shell. <c>null</c> until <see cref="Initialize"/> has run.
    /// Consumed by the "duplicate tab" feature so a sibling session can be
    /// spawned with the same cwd / command / args / env as the source.
    /// </summary>
    internal LaunchSpec? LastLaunchSpec { get; private set; }

    /// <summary>
    /// Detects the default shell, creates the <see cref="TerminalControl"/>,
    /// wires events, and starts the shell process.
    /// The control is added to the visual tree before the process starts so
    /// that Avalonia layout runs and the control has valid bounds. Without
    /// this ordering the PTY would be created at 1×1 and the shell welcome
    /// message would be truncated.
    /// </summary>
    public void Initialize()
    {
        string shell;
        string[] args;
        string cwd;
        IDictionary<string, string> env;

        if (this.launchOverride is { } spec)
        {
            shell = spec.Command;
            args = spec.Args.ToArray();
            cwd = spec.Cwd;
            env = new Dictionary<string, string>(spec.Env);
        }
        else
        {
            shell = DetectShell();
            args = GetShellArgs(shell);
            cwd = GetWorkingDirectory();
            env = GetEnvironment();
        }

        this.LastLaunchSpec = new LaunchSpec(cwd, shell, args, env);
        this.log.LogInformation("Starting shell: {Shell}", shell);

        this.terminalControl = new TerminalControl();
        this.terminalControl.EnableLigature = this.settings.EnableLigature;
        this.terminalControl.ScrollbackLimit = this.settings.ScrollbackLines;
        this.ApplyFontSettings();

        var scheme = ColorSchemePresets.FindByName(this.settings.ColorSchemeName) ?? ColorSchemePresets.Default;
        this.terminalControl.ApplyColorScheme(scheme);

        this.settingsHandler = (s, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.ColorSchemeName))
            {
                var newScheme = ColorSchemePresets.FindByName(this.settings.ColorSchemeName) ?? ColorSchemePresets.Default;
                Dispatcher.UIThread.Post(() =>
                {
                    this.terminalControl?.ApplyColorScheme(newScheme);
                    this.settings.ForegroundColor = newScheme.Foreground;
                    this.settings.BackgroundColor = newScheme.Background;
                });
            }

            if (e.PropertyName is nameof(AppSettings.FallbackFonts)
                or nameof(AppSettings.FontSize)
                or nameof(AppSettings.FontFamily))
            {
                Dispatcher.UIThread.Post(() => this.ApplyFontSettings());
            }

            if (e.PropertyName is nameof(AppSettings.EnableLigature))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (this.terminalControl is not null)
                    {
                        this.terminalControl.EnableLigature = this.settings.EnableLigature;
                        this.terminalControl.InvalidateVisual();
                    }
                });
            }

            if (e.PropertyName is nameof(AppSettings.ScrollbackLines))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (this.terminalControl is not null)
                    {
                        this.terminalControl.ScrollbackLimit = this.settings.ScrollbackLines;
                    }
                });
            }
        };
        this.settings.PropertyChanged += this.settingsHandler;

        this.terminalControl.TitleChanged += title =>
            Dispatcher.UIThread.Post(() => this.TitleChanged?.Invoke(title));
        this.terminalControl.BackgroundColorChanged += color =>
            Dispatcher.UIThread.Post(() => this.BackgroundColorChanged?.Invoke(color));
        this.terminalControl.ProcessExited += this.OnProcessExited;
        this.terminalControl.BellRaised += () =>
            Dispatcher.UIThread.Post(() => this.BellRaised?.Invoke());

        // Add the control to the visual tree first so Avalonia can lay it
        // out and assign real bounds before we read DesiredColCount/DesiredRowCount.
        this.TerminalReady?.Invoke(this.terminalControl);

        // Force a layout pass so the control gets its actual size.
        Dispatcher.UIThread.RunJobs();

        this.terminalControl.StartProcess(shell, args, env, cwd);
    }

    /// <summary>
    /// Disposes the terminal control and releases resources. Safe to call
    /// multiple times.
    /// </summary>
    public void Shutdown()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        if (this.settingsHandler is not null)
        {
            this.settings.PropertyChanged -= this.settingsHandler;
            this.settingsHandler = null;
        }

        this.terminalControl?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() => this.Shutdown();

    /// <summary>
    /// Attempts to read the live working directory of the running child
    /// shell. Falls back to the launch cwd when the live lookup is not
    /// supported on the current platform.
    /// </summary>
    /// <returns>The current working directory, or <c>null</c> if nothing
    /// sensible can be determined (e.g. before <see cref="Initialize"/>).</returns>
    internal string? TryGetCurrentWorkingDirectory()
    {
        string? live = this.TryReadLiveCwd();
        if (!string.IsNullOrEmpty(live))
        {
            return live;
        }

        return this.LastLaunchSpec?.Cwd;
    }

    private static string DetectShell()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string? comspec = Environment.GetEnvironmentVariable("COMSPEC");
            return comspec ?? "cmd.exe";
        }

        string? shell = Environment.GetEnvironmentVariable("SHELL");
        return shell ?? "/bin/sh";
    }

    private static string[] GetShellArgs(string shell)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Array.Empty<string>();
        }

        // Login shell on Unix
        return new[] { "-l" };
    }

    private static string GetWorkingDirectory()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static IDictionary<string, string> GetEnvironment()
    {
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
            }
        }

        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";
        return env;
    }

    private static string? ReadMacOsCwdViaLsof(int pid)
    {
        // `lsof -a -p <pid> -d cwd -Fn` prints machine-parseable records;
        // the `n<path>` line after a `pPID` line holds the cwd. Shelling
        // out keeps us free of the private proc_pidinfo ABI surface.
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/usr/sbin/lsof",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add("cwd");
        psi.ArgumentList.Add("-Fn");

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc is null)
        {
            return null;
        }

        string stdout = proc.StandardOutput.ReadToEnd();
        if (!proc.WaitForExit(1500))
        {
            try
            {
                proc.Kill(entireProcessTree: false);
            }
            catch
            {
                // Best-effort cleanup.
            }

            return null;
        }

        foreach (var rawLine in stdout.Split('\n'))
        {
            if (rawLine.Length > 1 && rawLine[0] == 'n')
            {
                return rawLine.Substring(1).TrimEnd('\r');
            }
        }

        return null;
    }

    private void ApplyFontSettings()
    {
        if (this.terminalControl is null)
        {
            return;
        }

        var fontList = new List<string>(this.settings.FallbackFonts);
        if (!string.IsNullOrWhiteSpace(this.settings.FontFamily))
        {
            fontList.Insert(0, this.settings.FontFamily);
        }

        var normalized = FontPriorityList.Normalize(fontList);
        var expanded = FontPriorityList.Expand(normalized);
        this.terminalControl.ApplyFontChange(expanded, this.settings.FontSize);
    }

    private void OnProcessExited()
    {
        Dispatcher.UIThread.Post(() => this.ProcessExitedNormally?.Invoke());
    }

    /// <summary>
    /// Platform-specific best-effort lookup of the child shell's live cwd.
    /// Returns <c>null</c> on failure; callers fall back to the launch cwd.
    /// Linux reads the <c>/proc/&lt;pid&gt;/cwd</c> symlink; macOS shells out
    /// to <c>lsof</c>; Windows has no reliable userland mechanism without
    /// elevated permissions, so we always return <c>null</c> there.
    /// </summary>
    private string? TryReadLiveCwd()
    {
        int? pid = this.terminalControl?.ChildPid;
        if (pid is null)
        {
            return null;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string link = $"/proc/{pid.Value}/cwd";
                var info = new FileInfo(link);
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                return target?.FullName;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return ReadMacOsCwdViaLsof(pid.Value);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Diagnostics.Tracing.EventSourceException)
        {
            this.log.LogDebug(ex, "Failed to resolve live cwd for pid {Pid}.", pid.Value);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            this.log.LogDebug(ex, "Failed to resolve live cwd for pid {Pid}.", pid.Value);
        }

        return null;
    }
}
