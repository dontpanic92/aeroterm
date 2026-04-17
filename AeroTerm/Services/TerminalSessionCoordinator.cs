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
internal sealed class TerminalSessionCoordinator
{
    private readonly AppSettings settings;
    private readonly ILogger log;
    private TerminalControl? terminalControl;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalSessionCoordinator"/> class.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    public TerminalSessionCoordinator(AppSettings settings)
    {
        this.settings = settings;
        this.log = AppLogger.For<TerminalSessionCoordinator>();
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
    /// Detects the default shell, creates the <see cref="TerminalControl"/>,
    /// wires events, and starts the shell process.
    /// The control is added to the visual tree before the process starts so
    /// that Avalonia layout runs and the control has valid bounds. Without
    /// this ordering the PTY would be created at 1×1 and the shell welcome
    /// message would be truncated.
    /// </summary>
    public void Initialize()
    {
        string shell = DetectShell();
        string[] args = GetShellArgs(shell);
        string cwd = GetWorkingDirectory();
        var env = GetEnvironment();

        this.log.LogInformation("Starting shell: {Shell}", shell);

        this.terminalControl = new TerminalControl();
        this.terminalControl.EnableLigature = this.settings.EnableLigature;
        this.ApplyFontSettings();

        var scheme = ColorSchemePresets.FindByName(this.settings.ColorSchemeName) ?? ColorSchemePresets.Default;
        this.terminalControl.ApplyColorScheme(scheme);

        this.settings.PropertyChanged += (s, e) =>
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
        };

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
    /// Disposes the terminal control and releases resources.
    /// </summary>
    public void Shutdown()
    {
        this.terminalControl?.Dispose();
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
}
