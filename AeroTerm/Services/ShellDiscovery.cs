// <copyright file="ShellDiscovery.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AeroTerm.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>
/// Discovers shells available on the current machine across Windows,
/// macOS, and Linux. The result is intended to seed <c>profiles.json</c>
/// on first run; existing user profiles are never overwritten.
/// </summary>
public sealed class ShellDiscovery
{
    private static readonly string[] UnixShellCandidates =
    {
        "bash", "zsh", "fish", "sh", "dash", "tcsh", "ksh", "pwsh",
    };

    private readonly IShellProbeEnvironment env;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellDiscovery"/>
    /// class with the supplied probe environment.
    /// </summary>
    /// <param name="env">The probe seam (real I/O for production, fake
    /// for tests).</param>
    public ShellDiscovery(IShellProbeEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        this.env = env;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellDiscovery"/>
    /// class bound to the live host environment.
    /// </summary>
    public ShellDiscovery()
        : this(new SystemShellProbeEnvironment())
    {
    }

    /// <summary>
    /// Probes the host for known shells. Always returns at least one
    /// entry on a healthy machine; an empty list signals the caller
    /// should fall back to a synthesized default profile.
    /// </summary>
    /// <returns>The discovered shells in display order.</returns>
    public IReadOnlyList<DiscoveredShell> Discover()
    {
        var log = AppLogger.For<ShellDiscovery>();
        var results = new List<DiscoveredShell>();
        var seenCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (this.env.IsWindows)
            {
                this.DiscoverWindows(results, seenCommands, log);
            }
            else
            {
                this.DiscoverUnix(results, seenCommands, log);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Shell discovery aborted unexpectedly.");
        }

        return results;
    }

    private static string ParseProgramFromShellsLine(string line)
    {
        // /etc/shells lines may contain comments after the path; strip them.
        int hash = line.IndexOf('#');
        if (hash >= 0)
        {
            line = line[..hash];
        }

        return line.Trim();
    }

    private static string PrettyNameForUnixShell(string command)
    {
        var name = Path.GetFileNameWithoutExtension(command);
        return name switch
        {
            "bash" => "Bash",
            "zsh" => "Zsh",
            "fish" => "fish",
            "sh" => "sh",
            "dash" => "Dash",
            "tcsh" => "Tcsh",
            "ksh" => "Ksh",
            "pwsh" => "PowerShell",
            _ => string.IsNullOrEmpty(name) ? command : char.ToUpperInvariant(name[0]) + name[1..],
        };
    }

    private static IEnumerable<string> ParseWslDistros(string output)
    {
        // Strip BOM if present.
        if (output.Length > 0 && output[0] == '\uFEFF')
        {
            output = output[1..];
        }

        foreach (var raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // wsl.exe pads names with NULs in some shells; trim them too.
            var line = raw.Trim().Trim('\0').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            yield return line;
        }
    }

    private void DiscoverWindows(List<DiscoveredShell> results, HashSet<string> seen, ILogger log)
    {
        // cmd.exe via COMSPEC.
        var comspec = this.env.GetEnvironmentVariable("COMSPEC");
        if (!string.IsNullOrWhiteSpace(comspec) && this.env.FileExists(comspec))
        {
            this.AddIfNew(results, seen, new DiscoveredShell("Command Prompt", comspec, Array.Empty<string>(), null));
        }

        // Windows PowerShell.
        var systemRoot = this.env.GetEnvironmentVariable("SystemRoot")
            ?? this.env.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(systemRoot))
        {
            var ps = Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (this.env.FileExists(ps))
            {
                this.AddIfNew(results, seen, new DiscoveredShell("Windows PowerShell", ps, Array.Empty<string>(), null));
            }
        }

        // PowerShell 7 (pwsh) under Program Files.
        foreach (var folder in new[]
        {
            this.env.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            this.env.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        })
        {
            if (string.IsNullOrEmpty(folder))
            {
                continue;
            }

            var pwshRoot = Path.Combine(folder, "PowerShell");
            foreach (var versionDir in this.env.EnumerateDirectories(pwshRoot, "*"))
            {
                var pwsh = Path.Combine(versionDir, "pwsh.exe");
                if (this.env.FileExists(pwsh))
                {
                    this.AddIfNew(results, seen, new DiscoveredShell("PowerShell", pwsh, Array.Empty<string>(), null));
                    break;
                }
            }
        }

        // Git Bash.
        foreach (var candidate in this.EnumerateGitBashCandidates())
        {
            if (this.env.FileExists(candidate))
            {
                this.AddIfNew(results, seen, new DiscoveredShell("Git Bash", candidate, new[] { "--login", "-i" }, null));
                break;
            }
        }

        // WSL distros.
        try
        {
            var wsl = Path.Combine(systemRoot ?? string.Empty, "System32", "wsl.exe");
            if (this.env.FileExists(wsl))
            {
                // wsl.exe -l -q emits UTF-16 LE (with BOM) on Windows.
                var output = this.env.RunCapture(wsl, "-l -q", timeoutMs: 1500, Encoding.Unicode);
                if (!string.IsNullOrEmpty(output))
                {
                    foreach (var distro in ParseWslDistros(output))
                    {
                        var name = $"{distro} (WSL)";
                        var profile = new DiscoveredShell(name, wsl, new[] { "-d", distro }, null);

                        // Multiple distros share the same wsl.exe command —
                        // disambiguate seen-key by including the distro arg.
                        var seenKey = wsl + "|" + distro;
                        if (seen.Add(seenKey))
                        {
                            results.Add(profile);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "WSL distro probe failed.");
        }
    }

    private IEnumerable<string> EnumerateGitBashCandidates()
    {
        foreach (var folder in new[]
        {
            this.env.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            this.env.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            this.env.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        })
        {
            if (string.IsNullOrEmpty(folder))
            {
                continue;
            }

            yield return Path.Combine(folder, "Git", "bin", "bash.exe");
            yield return Path.Combine(folder, "Git", "usr", "bin", "bash.exe");
            yield return Path.Combine(folder, "Programs", "Git", "bin", "bash.exe");
        }
    }

    private void DiscoverUnix(List<DiscoveredShell> results, HashSet<string> seen, ILogger log)
    {
        // /etc/shells: one path per line.
        var shellsContent = this.env.TryReadAllText("/etc/shells");
        if (shellsContent is not null)
        {
            foreach (var rawLine in shellsContent.Split('\n'))
            {
                var line = ParseProgramFromShellsLine(rawLine);
                if (line.Length == 0 || !line.StartsWith('/'))
                {
                    continue;
                }

                if (this.env.FileExists(line))
                {
                    this.AddIfNew(results, seen, new DiscoveredShell(PrettyNameForUnixShell(line), line, Array.Empty<string>(), null));
                }
            }
        }

        // PATH probe of well-known shell names. Catches shells not registered
        // in /etc/shells (common on minimal containers) and keeps macOS in
        // sync with whatever the user installed via Homebrew.
        var path = this.env.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = ':';
        foreach (var candidate in UnixShellCandidates)
        {
            foreach (var dir in path.Split(separator, StringSplitOptions.RemoveEmptyEntries))
            {
                // Use explicit '/' joining rather than Path.Combine so tests
                // running on Windows (where Path.Combine uses '\\') still
                // probe the seam with stable Unix-style paths.
                var trimmed = dir.TrimEnd('/');
                var full = trimmed + "/" + candidate;
                if (this.env.FileExists(full))
                {
                    this.AddIfNew(results, seen, new DiscoveredShell(PrettyNameForUnixShell(full), full, Array.Empty<string>(), null));
                    break;
                }
            }
        }

        _ = log;
    }

    private void AddIfNew(List<DiscoveredShell> results, HashSet<string> seen, DiscoveredShell shell)
    {
        if (seen.Add(shell.Command))
        {
            results.Add(shell);
        }
    }

    /// <summary>
    /// Default <see cref="IShellProbeEnvironment"/> implementation that
    /// drives the real host filesystem and process APIs.
    /// </summary>
    private sealed class SystemShellProbeEnvironment : IShellProbeEnvironment
    {
        public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        public bool FileExists(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                return File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public string? TryReadAllText(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

        public string GetFolderPath(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);

        public string[] EnumerateDirectories(string directory, string pattern)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return Array.Empty<string>();
                }

                return Directory.GetDirectories(directory, pattern);
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        public string? RunCapture(string executable, string arguments, int timeoutMs, Encoding encoding)
        {
            try
            {
                var psi = new ProcessStartInfo(executable, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = encoding,
                };

                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    return null;
                }

                var stdout = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(timeoutMs))
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                    catch (Exception)
                    {
                        // Ignore — process is already gone or unkillable.
                    }

                    return null;
                }

                return stdout;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
