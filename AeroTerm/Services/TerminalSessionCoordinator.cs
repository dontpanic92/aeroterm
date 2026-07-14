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
    private string? shellReportedCwd;
    private string? cwdFilePath;
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
    /// Raised when the running terminal reports a new current working
    /// directory through shell integration or OSC 7.
    /// </summary>
    internal event Action<string>? CurrentWorkingDirectoryChanged;

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

        // Inject AeroTerm shell-integration scripts so the child shell
        // emits OSC 133 prompt marks. Strict input-deletion features
        // (Cmd+Backspace etc.) depend on this. Failure to inject is
        // non-fatal: the shell launches as if integration was disabled.
        if (this.settings.EnableShellIntegration)
        {
            try
            {
                var injector = new ShellIntegrationInjector();
                var result = injector.Inject(shell, args, env);
                if (result.Injected)
                {
                    args = result.Args;
                    env = result.Env;
                    env.TryGetValue(ShellIntegrationInjector.CwdFileEnvVar, out this.cwdFilePath);
                    this.log.LogInformation("Shell integration injected for {Shell}", shell);
                }
            }
            catch (Exception ex)
            {
                this.log.LogWarning(ex, "Shell integration injection failed; launching without it");
            }
        }

        this.terminalControl = new TerminalControl();
        this.terminalControl.EnableLigature = this.settings.EnableLigature;
        this.terminalControl.ScrollbackLimit = this.settings.ScrollbackLines;
        this.terminalControl.MiddleClickPastes = this.settings.MiddleClickPastes;
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

            if (e.PropertyName is nameof(AppSettings.MiddleClickPastes))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (this.terminalControl is not null)
                    {
                        this.terminalControl.MiddleClickPastes = this.settings.MiddleClickPastes;
                    }
                });
            }
        };
        this.settings.PropertyChanged += this.settingsHandler;

        this.terminalControl.TitleChanged += title =>
            Dispatcher.UIThread.Post(() => this.TitleChanged?.Invoke(title));
        this.terminalControl.BackgroundColorChanged += color =>
            Dispatcher.UIThread.Post(() => this.BackgroundColorChanged?.Invoke(color));
        this.terminalControl.CurrentDirectoryChanged += this.OnTerminalCurrentDirectoryChanged;
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

        if (!string.IsNullOrEmpty(this.cwdFilePath))
        {
            try
            {
                File.Delete(this.cwdFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this.log.LogDebug(ex, "Failed to delete cwd side-channel file {Path}.", this.cwdFilePath);
            }
        }
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
        if (!string.IsNullOrEmpty(this.shellReportedCwd))
        {
            return this.shellReportedCwd;
        }

        string? fromFile = this.TryReadCwdFile();
        if (!string.IsNullOrEmpty(fromFile))
        {
            return fromFile;
        }

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

        TerminalEnvironment.ApplyDefaults(env);
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

    /// <summary>
    /// Reads the live current directory of a Windows process by walking its
    /// PEB. Requires only same-user access rights (no elevation). Returns
    /// <c>null</c> when the lookup is not possible (e.g. mismatched bitness or
    /// access denied), letting callers fall back to the launch cwd.
    /// </summary>
    /// <param name="pid">Target process id.</param>
    /// <returns>The current directory, or <c>null</c> on failure.</returns>
    private static string? ReadWindowsCwd(int pid)
    {
        // 64-bit-only: the PEB / RTL_USER_PROCESS_PARAMETERS offsets below are
        // for the x64 layout. Reading across a WOW64 boundary needs a separate
        // path we deliberately skip.
        if (!Environment.Is64BitProcess)
        {
            return null;
        }

        const int ProcessQueryInformation = 0x0400;
        const int ProcessVmRead = 0x0010;

        // x64 layout offsets.
        const int PebProcessParametersOffset = 0x20;
        const int ProcessParametersCurrentDirectoryOffset = 0x38; // CURDIR.DosPath
        const int UnicodeStringBufferOffset = 0x08; // PWSTR within UNICODE_STRING

        IntPtr handle = NativeMethods.OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var basicInformation = default(NativeMethods.ProcessBasicInformation);
            int status = NativeMethods.NtQueryInformationProcess(
                handle,
                0,
                ref basicInformation,
                Marshal.SizeOf<NativeMethods.ProcessBasicInformation>(),
                out _);
            if (status != 0 || basicInformation.PebBaseAddress == IntPtr.Zero)
            {
                return null;
            }

            if (!TryReadPointer(handle, basicInformation.PebBaseAddress + PebProcessParametersOffset, out IntPtr processParameters) ||
                processParameters == IntPtr.Zero)
            {
                return null;
            }

            IntPtr currentDirectory = processParameters + ProcessParametersCurrentDirectoryOffset;
            if (!TryReadUInt16(handle, currentDirectory, out ushort length) || length == 0)
            {
                return null;
            }

            if (!TryReadPointer(handle, currentDirectory + UnicodeStringBufferOffset, out IntPtr buffer) ||
                buffer == IntPtr.Zero)
            {
                return null;
            }

            var raw = new byte[length];
            if (!NativeMethods.ReadProcessMemory(handle, buffer, raw, length, out IntPtr read) ||
                read.ToInt64() != length)
            {
                return null;
            }

            string path = System.Text.Encoding.Unicode.GetString(raw).TrimEnd('\0', '\\');
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static bool TryReadPointer(IntPtr process, IntPtr address, out IntPtr value)
    {
        var buffer = new byte[IntPtr.Size];
        if (NativeMethods.ReadProcessMemory(process, address, buffer, buffer.Length, out IntPtr read) &&
            read.ToInt64() == buffer.Length)
        {
            value = new IntPtr(BitConverter.ToInt64(buffer, 0));
            return true;
        }

        value = IntPtr.Zero;
        return false;
    }

    private static bool TryReadUInt16(IntPtr process, IntPtr address, out ushort value)
    {
        var buffer = new byte[sizeof(ushort)];
        if (NativeMethods.ReadProcessMemory(process, address, buffer, buffer.Length, out IntPtr read) &&
            read.ToInt64() == buffer.Length)
        {
            value = BitConverter.ToUInt16(buffer, 0);
            return true;
        }

        value = 0;
        return false;
    }

    private void ApplyFontSettings()
    {
        if (this.terminalControl is null)
        {
            return;
        }

        var expanded = FontPriorityList.Resolve(
            this.settings.FontFamily,
            this.settings.FallbackFonts);
        this.terminalControl.ApplyFontChange(expanded, this.settings.FontSize);
    }

    private void OnProcessExited()
    {
        Dispatcher.UIThread.Post(() => this.ProcessExitedNormally?.Invoke());
    }

    private void OnTerminalCurrentDirectoryChanged(string cwd)
    {
        if (string.IsNullOrEmpty(cwd) || this.shellReportedCwd == cwd)
        {
            return;
        }

        this.shellReportedCwd = cwd;
        Dispatcher.UIThread.Post(() => this.CurrentWorkingDirectoryChanged?.Invoke(cwd));
    }

    /// <summary>
    /// Reads the working directory the shell-integration script writes to a
    /// per-session side-channel file on every prompt. This is the most
    /// reliable cwd signal for shells (notably Windows PowerShell) whose
    /// <c>cd</c> neither updates the OS process directory nor reaches the host
    /// via escape sequences under ConPTY.
    /// </summary>
    /// <returns>The reported directory, or <c>null</c> when unavailable.</returns>
    private string? TryReadCwdFile()
    {
        if (string.IsNullOrEmpty(this.cwdFilePath))
        {
            return null;
        }

        try
        {
            if (!File.Exists(this.cwdFilePath))
            {
                return null;
            }

            using var stream = new FileStream(
                this.cwdFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string content = reader.ReadToEnd().Trim();
            return string.IsNullOrEmpty(content) ? null : content;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Platform-specific best-effort lookup of the child shell's live cwd.
    /// Returns <c>null</c> on failure; callers fall back to the launch cwd.
    /// Linux reads the <c>/proc/&lt;pid&gt;/cwd</c> symlink; macOS shells out
    /// to <c>lsof</c>; Windows reads the child process's PEB to recover the
    /// current directory (works for same-user processes without elevation).
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

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return ReadWindowsCwd(pid.Value);
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

    /// <summary>
    /// P/Invoke declarations for reading a Windows process's PEB.
    /// </summary>
    private static class NativeMethods
    {
        /// <summary>
        /// Opens an existing local process object.
        /// </summary>
        /// <param name="desiredAccess">Access rights mask.</param>
        /// <param name="inheritHandle">Whether the handle is inheritable.</param>
        /// <param name="processId">Target process id.</param>
        /// <returns>A process handle, or <see cref="IntPtr.Zero"/> on failure.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        /// <summary>
        /// Closes an open object handle.
        /// </summary>
        /// <param name="handle">Handle to close.</param>
        /// <returns><see langword="true"/> on success.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        /// <summary>
        /// Reads memory from another process's address space.
        /// </summary>
        /// <param name="process">Process handle.</param>
        /// <param name="baseAddress">Address to read from.</param>
        /// <param name="buffer">Destination buffer.</param>
        /// <param name="size">Number of bytes to read.</param>
        /// <param name="bytesRead">Bytes actually read.</param>
        /// <returns><see langword="true"/> on success.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, byte[] buffer, int size, out IntPtr bytesRead);

        /// <summary>
        /// Retrieves information about the specified process.
        /// </summary>
        /// <param name="process">Process handle.</param>
        /// <param name="processInformationClass">Information class (0 = basic).</param>
        /// <param name="processInformation">Receives the basic information.</param>
        /// <param name="processInformationLength">Buffer length in bytes.</param>
        /// <param name="returnLength">Bytes returned.</param>
        /// <returns>An NTSTATUS code (0 on success).</returns>
        [DllImport("ntdll.dll")]
        public static extern int NtQueryInformationProcess(
            IntPtr process,
            int processInformationClass,
            ref ProcessBasicInformation processInformation,
            int processInformationLength,
            out int returnLength);

        /// <summary>
        /// Subset of PROCESS_BASIC_INFORMATION exposing the PEB base address.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessBasicInformation
        {
            /// <summary>
            /// Reserved exit-status field.
            /// </summary>
            public IntPtr ExitStatus;

            /// <summary>
            /// Base address of the process environment block.
            /// </summary>
            public IntPtr PebBaseAddress;

            /// <summary>
            /// Reserved affinity-mask field.
            /// </summary>
            public IntPtr AffinityMask;

            /// <summary>
            /// Reserved base-priority field.
            /// </summary>
            public IntPtr BasePriority;

            /// <summary>
            /// Unique process id.
            /// </summary>
            public IntPtr UniqueProcessId;

            /// <summary>
            /// Parent process id.
            /// </summary>
            public IntPtr InheritedFromUniqueProcessId;
        }
    }
}
