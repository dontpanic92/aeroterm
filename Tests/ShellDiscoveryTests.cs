// <copyright file="ShellDiscoveryTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Unit tests for <see cref="ShellDiscovery"/>. Drives the service via
/// the injected <see cref="IShellProbeEnvironment"/> seam so the tests
/// remain deterministic regardless of the host machine.
/// </summary>
[TestFixture]
public class ShellDiscoveryTests
{
    /// <summary>Windows discovery picks up cmd, PowerShell, pwsh, Git Bash, and WSL distros.</summary>
    [Test]
    public void Discover_Windows_FindsKnownShellsAndWslDistros()
    {
        var env = new FakeProbeEnvironment(isWindows: true)
        {
            EnvVars =
            {
                ["COMSPEC"] = @"C:\Windows\System32\cmd.exe",
                ["SystemRoot"] = @"C:\Windows",
            },
            Files =
            {
                @"C:\Windows\System32\cmd.exe",
                @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                @"C:\Windows\System32\wsl.exe",
                @"C:\Program Files\PowerShell\7\pwsh.exe",
                @"C:\Program Files\Git\bin\bash.exe",
            },
            Directories =
            {
                [@"C:\Program Files\PowerShell"] = new[] { @"C:\Program Files\PowerShell\7" },
            },
            FolderPaths =
            {
                [Environment.SpecialFolder.ProgramFiles] = @"C:\Program Files",
            },
            ProcessOutputs =
            {
                [("C:\\Windows\\System32\\wsl.exe", "-l -q")] = "\uFEFFUbuntu\r\nDebian\r\n",
            },
        };

        var shells = new ShellDiscovery(env).Discover();

        Assert.That(shells.Select(s => s.Name), Is.EquivalentTo(new[]
        {
            "Command Prompt",
            "Windows PowerShell",
            "PowerShell",
            "Git Bash",
            "Ubuntu (WSL)",
            "Debian (WSL)",
        }));

        var ubuntu = shells.First(s => s.Name == "Ubuntu (WSL)");
        Assert.That(ubuntu.Args, Is.EqualTo(new[] { "-d", "Ubuntu" }));
        Assert.That(ubuntu.Command, Is.EqualTo(@"C:\Windows\System32\wsl.exe"));

        var gitBash = shells.First(s => s.Name == "Git Bash");
        Assert.That(gitBash.Args, Is.EqualTo(new[] { "--login", "-i" }));
    }

    /// <summary>WSL parser handles BOMs, NUL padding, and empty lines.</summary>
    [Test]
    public void Discover_Windows_WslParser_StripsBomAndNulPadding()
    {
        var env = new FakeProbeEnvironment(isWindows: true)
        {
            EnvVars = { ["SystemRoot"] = @"C:\Windows", ["COMSPEC"] = @"C:\Windows\System32\cmd.exe" },
            Files = { @"C:\Windows\System32\cmd.exe", @"C:\Windows\System32\wsl.exe" },
            ProcessOutputs =
            {
                [("C:\\Windows\\System32\\wsl.exe", "-l -q")] =
                    "\uFEFFUbuntu\0\0\r\n\r\n  kali-linux  \r\n",
            },
        };

        var shells = new ShellDiscovery(env).Discover();

        Assert.That(shells.Select(s => s.Name), Does.Contain("Ubuntu (WSL)"));
        Assert.That(shells.Select(s => s.Name), Does.Contain("kali-linux (WSL)"));
    }

    /// <summary>Unix discovery merges /etc/shells with PATH-probed candidates and de-dups.</summary>
    [Test]
    public void Discover_Unix_MergesEtcShellsAndPathProbes()
    {
        var env = new FakeProbeEnvironment(isWindows: false)
        {
            EnvVars = { ["PATH"] = "/usr/bin:/bin:/usr/local/bin" },
            Files =
            {
                "/bin/bash",
                "/bin/zsh",
                "/usr/local/bin/fish",
            },
            FileTexts =
            {
                ["/etc/shells"] = "# /etc/shells: valid login shells\n/bin/bash\n/bin/zsh\n/sbin/nologin\n",
            },
        };

        var shells = new ShellDiscovery(env).Discover();

        var names = shells.Select(s => s.Command).ToArray();
        Assert.That(names, Does.Contain("/bin/bash"));
        Assert.That(names, Does.Contain("/bin/zsh"));
        Assert.That(names, Does.Contain("/usr/local/bin/fish"));

        // /sbin/nologin appears in /etc/shells but is not in the file probe
        // set, so FileExists returns false → it's skipped.
        Assert.That(names, Does.Not.Contain("/sbin/nologin"));

        // /bin/bash should appear exactly once even though it's referenced
        // by both /etc/shells and the PATH probe.
        Assert.That(names.Count(n => n == "/bin/bash"), Is.EqualTo(1));
    }

    /// <summary>Missing executables are silently skipped without throwing.</summary>
    [Test]
    public void Discover_Windows_MissingExecutables_AreSkipped()
    {
        var env = new FakeProbeEnvironment(isWindows: true)
        {
            EnvVars = { ["COMSPEC"] = @"C:\does-not-exist\cmd.exe" },

            // No files seeded → every existence probe returns false.
        };

        var shells = new ShellDiscovery(env).Discover();
        Assert.That(shells, Is.Empty);
    }

    /// <summary>Probe environment that never lists any files yields zero shells.</summary>
    [Test]
    public void Discover_Unix_EmptyEnvironment_ReturnsEmpty()
    {
        var env = new FakeProbeEnvironment(isWindows: false);
        var shells = new ShellDiscovery(env).Discover();
        Assert.That(shells, Is.Empty);
    }

    private sealed class FakeProbeEnvironment : IShellProbeEnvironment
    {
        public FakeProbeEnvironment(bool isWindows)
        {
            this.IsWindows = isWindows;
        }

        public bool IsWindows { get; }

        public bool IsMacOS => !this.IsWindows;

        public bool IsLinux => !this.IsWindows;

        public Dictionary<string, string> EnvVars { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> FileTexts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<Environment.SpecialFolder, string> FolderPaths { get; } = new();

        public Dictionary<(string Exe, string Args), string> ProcessOutputs { get; } = new();

        public bool FileExists(string path) => this.Files.Contains(path);

        public string? TryReadAllText(string path)
            => this.FileTexts.TryGetValue(path, out var text) ? text : null;

        public string? GetEnvironmentVariable(string name)
            => this.EnvVars.TryGetValue(name, out var v) ? v : null;

        public string GetFolderPath(Environment.SpecialFolder folder)
            => this.FolderPaths.TryGetValue(folder, out var v) ? v : string.Empty;

        public string[] EnumerateDirectories(string directory, string pattern)
            => this.Directories.TryGetValue(directory, out var dirs) ? dirs : Array.Empty<string>();

        public string? RunCapture(string executable, string arguments, int timeoutMs, Encoding encoding)
            => this.ProcessOutputs.TryGetValue((executable, arguments), out var v) ? v : null;
    }
}
