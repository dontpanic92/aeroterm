// <copyright file="ShellIntegrationInjectorTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Collections.Generic;
using System.IO;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Unit tests for <see cref="ShellIntegrationInjector"/>.
/// </summary>
[TestFixture]
public class ShellIntegrationInjectorTests
{
    private string tempDir = string.Empty;

    /// <summary>Creates an isolated temp dir for each test.</summary>
    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(Path.GetTempPath(), "aeroterm-shell-injector-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);
    }

    /// <summary>Removes the temp dir after each test.</summary>
    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(this.tempDir, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>Recognised shells map to their <see cref="ShellKind"/>.</summary>
    [Test]
    public void DetectShellKind_RecognisesKnownShells()
    {
        Assert.That(ShellIntegrationInjector.DetectShellKind("/bin/zsh"), Is.EqualTo(ShellKind.Zsh));
        Assert.That(ShellIntegrationInjector.DetectShellKind("/usr/local/bin/bash"), Is.EqualTo(ShellKind.Bash));
        Assert.That(ShellIntegrationInjector.DetectShellKind("/opt/homebrew/bin/fish"), Is.EqualTo(ShellKind.Fish));
        Assert.That(ShellIntegrationInjector.DetectShellKind("/usr/local/bin/pwsh"), Is.EqualTo(ShellKind.PowerShell));
        Assert.That(ShellIntegrationInjector.DetectShellKind("powershell.exe"), Is.EqualTo(ShellKind.PowerShell));
        Assert.That(ShellIntegrationInjector.DetectShellKind("/bin/sh"), Is.EqualTo(ShellKind.Unknown));
        Assert.That(ShellIntegrationInjector.DetectShellKind("/bin/cmd.exe"), Is.EqualTo(ShellKind.Unknown));
    }

    /// <summary>
    /// Unknown shells (sh, cmd, busybox, etc.) are returned unchanged
    /// with <c>Injected = false</c>.
    /// </summary>
    [Test]
    public void Inject_UnknownShell_NoOp()
    {
        var injector = new ShellIntegrationInjector(() => this.tempDir);
        var args = new[] { "-l" };
        var env = new Dictionary<string, string> { ["TERM"] = "xterm-256color" };

        var result = injector.Inject("/bin/sh", args, env);

        Assert.That(result.Injected, Is.False);
        Assert.That(result.Args, Is.EqualTo(args));
        Assert.That(result.Env.ContainsKey(ShellIntegrationInjector.InjectedEnvVar), Is.False);

        // No files written.
        Assert.That(Directory.GetFiles(this.tempDir, "*", SearchOption.AllDirectories), Is.Empty);
    }

    /// <summary>
    /// zsh injection writes the shim ZDOTDIR + integration script and
    /// sets <c>ZDOTDIR</c> / <c>USER_ZDOTDIR</c> / sentinel env vars
    /// without mutating the caller's dictionaries.
    /// </summary>
    [Test]
    public void Inject_Zsh_WritesShimAndSetsEnv()
    {
        var injector = new ShellIntegrationInjector(() => this.tempDir);
        var args = new[] { "-l" };
        var env = new Dictionary<string, string> { ["HOME"] = "/Users/test" };

        var result = injector.Inject("/bin/zsh", args, env);

        Assert.That(result.Injected, Is.True);
        Assert.That(result.Args, Is.EqualTo(args), "zsh injection works through env, not args");

        string shimDir = Path.Combine(this.tempDir, "zsh-shim");
        Assert.That(File.Exists(Path.Combine(this.tempDir, "aeroterm-integration.zsh")), Is.True);
        Assert.That(File.Exists(Path.Combine(shimDir, ".zshrc")), Is.True);
        Assert.That(File.Exists(Path.Combine(shimDir, ".zshenv")), Is.True);
        Assert.That(File.Exists(Path.Combine(shimDir, ".zprofile")), Is.True);
        Assert.That(File.Exists(Path.Combine(shimDir, ".zlogin")), Is.True);

        Assert.That(result.Env["ZDOTDIR"], Is.EqualTo(shimDir));
        Assert.That(result.Env[ShellIntegrationInjector.InjectedEnvVar], Is.EqualTo("1"));
        Assert.That(result.Env.ContainsKey("USER_ZDOTDIR"), Is.False, "no prior ZDOTDIR -> no USER_ZDOTDIR");

        // Caller's env was NOT mutated.
        Assert.That(env.ContainsKey("ZDOTDIR"), Is.False);
        Assert.That(env.ContainsKey(ShellIntegrationInjector.InjectedEnvVar), Is.False);

        // Shim .zshrc has the integration path substituted in.
        string zshrc = File.ReadAllText(Path.Combine(shimDir, ".zshrc"));
        Assert.That(zshrc, Does.Not.Contain(ShellIntegrationScripts.IntegrationPathPlaceholder));
        Assert.That(zshrc, Does.Contain(Path.Combine(this.tempDir, "aeroterm-integration.zsh")));
    }

    /// <summary>
    /// When the user already has <c>ZDOTDIR</c> set the injector
    /// preserves it under <c>USER_ZDOTDIR</c> so the shim can delegate.
    /// </summary>
    [Test]
    public void Inject_Zsh_PreservesExistingZdotdir()
    {
        var injector = new ShellIntegrationInjector(() => this.tempDir);
        var env = new Dictionary<string, string> { ["ZDOTDIR"] = "/Users/test/dotfiles" };

        var result = injector.Inject("/bin/zsh", Array.Empty<string>(), env);

        Assert.That(result.Injected, Is.True);
        Assert.That(result.Env["USER_ZDOTDIR"], Is.EqualTo("/Users/test/dotfiles"));
        Assert.That(result.Env["ZDOTDIR"], Is.EqualTo(Path.Combine(this.tempDir, "zsh-shim")));
    }

    /// <summary>
    /// bash injection prepends <c>--rcfile &lt;path&gt; -i</c>, drops
    /// <c>-l</c>, and signals login mode via <c>AEROTERM_BASH_LOGIN</c>
    /// when the original args contained it.
    /// </summary>
    [Test]
    public void Inject_Bash_LoginShellPrependsRcfileAndSignals()
    {
        var injector = new ShellIntegrationInjector(() => this.tempDir);
        var args = new[] { "-l" };
        var env = new Dictionary<string, string>();

        var result = injector.Inject("/bin/bash", args, env);

        Assert.That(result.Injected, Is.True);
        string rcfile = Path.Combine(this.tempDir, "aeroterm-bashrc");
        Assert.That(File.Exists(rcfile), Is.True);

        Assert.That(result.Args, Is.EqualTo(new[] { "--rcfile", rcfile, "-i" }));
        Assert.That(result.Env["AEROTERM_BASH_LOGIN"], Is.EqualTo("1"));
        Assert.That(result.Env[ShellIntegrationInjector.InjectedEnvVar], Is.EqualTo("1"));
    }

    /// <summary>
    /// Non-login bash invocation just gets the rcfile + interactive flag,
    /// no <c>AEROTERM_BASH_LOGIN</c>.
    /// </summary>
    [Test]
    public void Inject_Bash_NoLoginArg()
    {
        var injector = new ShellIntegrationInjector(() => this.tempDir);

        var result = injector.Inject("/bin/bash", Array.Empty<string>(), new Dictionary<string, string>());

        Assert.That(result.Injected, Is.True);
        Assert.That(result.Args, Does.Contain("--rcfile"));
        Assert.That(result.Args, Does.Contain("-i"));
        Assert.That(result.Env.ContainsKey("AEROTERM_BASH_LOGIN"), Is.False);
    }

    /// <summary>
    /// fish injection writes its script under
    /// <c>&lt;dataDir&gt;/fish/vendor_conf.d/aeroterm.fish</c> and
    /// prepends the data dir to <c>XDG_DATA_DIRS</c>.
    /// </summary>
    [Test]
    public void Inject_Fish_WritesVendorConfAndPrependsXdgDataDirs()
    {
        var injector = new ShellIntegrationInjector(() => this.tempDir);
        var env = new Dictionary<string, string> { ["XDG_DATA_DIRS"] = "/usr/local/share:/usr/share" };

        var result = injector.Inject("/usr/local/bin/fish", Array.Empty<string>(), env);

        Assert.That(result.Injected, Is.True);
        string scriptPath = Path.Combine(this.tempDir, "fish", "vendor_conf.d", "aeroterm.fish");
        Assert.That(File.Exists(scriptPath), Is.True);

        string sep = Path.PathSeparator.ToString();
        Assert.That(
            result.Env["XDG_DATA_DIRS"],
            Does.StartWith(this.tempDir.TrimEnd(Path.DirectorySeparatorChar) + sep));
    }

    /// <summary>
    /// PowerShell injection appends <c>-NoExit -Command "..."</c> and
    /// writes the integration script to disk.
    /// </summary>
    [Test]
    public void Inject_PowerShell_AppendsCommandArg()
    {
        var injector = new ShellIntegrationInjector(() => this.tempDir);

        var result = injector.Inject("/usr/local/bin/pwsh", Array.Empty<string>(), new Dictionary<string, string>());

        Assert.That(result.Injected, Is.True);
        Assert.That(File.Exists(Path.Combine(this.tempDir, "aeroterm-integration.ps1")), Is.True);
        Assert.That(result.Args, Does.Contain("-NoExit"));
        Assert.That(result.Args, Does.Contain("-Command"));
        Assert.That(result.Args[result.Args.Length - 1], Does.Contain("aeroterm-integration.ps1"));
    }

    /// <summary>
    /// PowerShell launches that already specify <c>-Command</c> /
    /// <c>-File</c> (custom invocations) are left untouched.
    /// </summary>
    [Test]
    public void Inject_PowerShell_PreservesCustomCommandArgs()
    {
        var injector = new ShellIntegrationInjector(() => this.tempDir);
        var args = new[] { "-Command", "Write-Host hi" };

        var result = injector.Inject("/usr/local/bin/pwsh", args, new Dictionary<string, string>());

        Assert.That(result.Injected, Is.False);
        Assert.That(result.Args, Is.EqualTo(args));
    }

    /// <summary>
    /// A second injection against the same data dir does not rewrite
    /// files (idempotent) — useful so launching tabs in quick succession
    /// doesn't churn the filesystem.
    /// </summary>
    [Test]
    public void Inject_Zsh_Idempotent_DoesNotRewriteUnchangedFiles()
    {
        var injector = new ShellIntegrationInjector(() => this.tempDir);

        injector.Inject("/bin/zsh", Array.Empty<string>(), new Dictionary<string, string>());
        string zshrcPath = Path.Combine(this.tempDir, "zsh-shim", ".zshrc");
        DateTime mtime1 = File.GetLastWriteTimeUtc(zshrcPath);

        // Sleep just enough for filesystem mtime granularity.
        System.Threading.Thread.Sleep(20);

        injector.Inject("/bin/zsh", Array.Empty<string>(), new Dictionary<string, string>());
        DateTime mtime2 = File.GetLastWriteTimeUtc(zshrcPath);

        Assert.That(mtime2, Is.EqualTo(mtime1));
    }

    /// <summary>
    /// The integration scripts contain the OSC 133 escape sequences
    /// (<c>\e]133;A\a</c> etc.) that downstream features depend on.
    /// </summary>
    [Test]
    public void Scripts_ContainExpectedOsc133Marks()
    {
        Assert.That(ShellIntegrationScripts.ZshIntegration, Does.Contain("133;A"));
        Assert.That(ShellIntegrationScripts.ZshIntegration, Does.Contain("133;B"));
        Assert.That(ShellIntegrationScripts.ZshIntegration, Does.Contain("133;C"));
        Assert.That(ShellIntegrationScripts.ZshIntegration, Does.Contain("133;D"));

        Assert.That(ShellIntegrationScripts.BashIntegration, Does.Contain("133;A"));
        Assert.That(ShellIntegrationScripts.BashIntegration, Does.Contain("133;B"));
        Assert.That(ShellIntegrationScripts.BashIntegration, Does.Contain("133;C"));
        Assert.That(ShellIntegrationScripts.BashIntegration, Does.Contain("133;D"));

        Assert.That(ShellIntegrationScripts.FishIntegration, Does.Contain("133;A"));
        Assert.That(ShellIntegrationScripts.FishIntegration, Does.Contain("133;B"));
        Assert.That(ShellIntegrationScripts.FishIntegration, Does.Contain("133;C"));
        Assert.That(ShellIntegrationScripts.FishIntegration, Does.Contain("133;D"));

        Assert.That(ShellIntegrationScripts.PowerShellIntegration, Does.Contain("133;A"));
        Assert.That(ShellIntegrationScripts.PowerShellIntegration, Does.Contain("133;B"));
        Assert.That(ShellIntegrationScripts.PowerShellIntegration, Does.Contain("133;C"));
        Assert.That(ShellIntegrationScripts.PowerShellIntegration, Does.Contain("133;D"));
    }
}
