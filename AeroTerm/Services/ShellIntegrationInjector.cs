// <copyright file="ShellIntegrationInjector.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
/// Wires AeroTerm's OSC 133 shell-integration scripts into a child
/// shell launch by templating per-shell shim files into a per-user data
/// directory and modifying the launch arguments and environment so the
/// shell loads them at startup.
/// </summary>
/// <remarks>
/// Detection is based on the executable basename (<c>zsh</c>,
/// <c>bash</c>, <c>fish</c>, <c>pwsh</c> / <c>powershell</c>); any
/// other shell is left untouched. Injection is a no-op (returns the
/// inputs unchanged with <c>Injected = false</c>) when:
/// <list type="bullet">
///   <item><description>injection is disabled by the caller,</description></item>
///   <item><description>the shell is not recognised,</description></item>
///   <item><description>writing the shim files fails (we never want a
///   broken integration to also break the user's terminal).</description></item>
/// </list>
/// </remarks>
internal sealed class ShellIntegrationInjector
{
    /// <summary>
    /// Sentinel env var added so the integration scripts (and tests)
    /// can detect that AeroTerm injected them.
    /// </summary>
    public const string InjectedEnvVar = "AEROTERM_SHELL_INTEGRATION";

    private readonly Func<string> dataDirFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellIntegrationInjector"/> class.
    /// </summary>
    public ShellIntegrationInjector()
        : this(GetDefaultDataDirectory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellIntegrationInjector"/> class
    /// with a caller-supplied data directory factory. Used by tests to
    /// redirect script extraction into a temp dir.
    /// </summary>
    /// <param name="dataDirFactory">Factory returning the absolute path
    /// of the directory where shim and integration scripts are written.
    /// The directory is created if missing.</param>
    public ShellIntegrationInjector(Func<string> dataDirFactory)
    {
        this.dataDirFactory = dataDirFactory ?? throw new ArgumentNullException(nameof(dataDirFactory));
    }

    /// <summary>
    /// Detects the shell kind from the executable path's basename.
    /// </summary>
    /// <param name="command">Shell executable path.</param>
    /// <returns>The detected shell kind.</returns>
    public static ShellKind DetectShellKind(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        string basename = Path.GetFileNameWithoutExtension(command);
        if (string.IsNullOrEmpty(basename))
        {
            return ShellKind.Unknown;
        }

        return basename.ToLowerInvariant() switch
        {
            "zsh" => ShellKind.Zsh,
            "bash" => ShellKind.Bash,
            "fish" => ShellKind.Fish,
            "pwsh" or "powershell" => ShellKind.PowerShell,
            _ => ShellKind.Unknown,
        };
    }

    /// <summary>
    /// Attempts to inject AeroTerm shell integration into the launch
    /// parameters of a child shell. Returns the (possibly modified)
    /// command / args / environment along with a flag indicating
    /// whether anything was changed.
    /// </summary>
    /// <param name="command">Shell executable path.</param>
    /// <param name="args">Shell arguments.</param>
    /// <param name="env">Environment dictionary (will be copied; not mutated).</param>
    /// <returns>The injection result.</returns>
    public ShellInjectionResult Inject(string command, string[] args, IDictionary<string, string> env)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);

        // Always work on copies so a partial failure doesn't half-mutate
        // the caller's collections.
        var newEnv = new Dictionary<string, string>(env);
        var newArgs = (string[])args.Clone();

        ShellKind kind = DetectShellKind(command);
        if (kind == ShellKind.Unknown)
        {
            return new ShellInjectionResult(command, newArgs, newEnv, Injected: false);
        }

        string dataDir;
        try
        {
            dataDir = this.dataDirFactory();
            Directory.CreateDirectory(dataDir);
        }
        catch
        {
            // If we cannot even resolve the data dir, the user is better
            // served by an un-integrated shell than a broken one.
            return new ShellInjectionResult(command, newArgs, newEnv, Injected: false);
        }

        try
        {
            return kind switch
            {
                ShellKind.Zsh => this.InjectZsh(command, newArgs, newEnv, dataDir),
                ShellKind.Bash => this.InjectBash(command, newArgs, newEnv, dataDir),
                ShellKind.Fish => this.InjectFish(command, newArgs, newEnv, dataDir),
                ShellKind.PowerShell => this.InjectPowerShell(command, newArgs, newEnv, dataDir),
                _ => new ShellInjectionResult(command, newArgs, newEnv, Injected: false),
            };
        }
        catch (IOException)
        {
            return new ShellInjectionResult(command, newArgs, newEnv, Injected: false);
        }
        catch (UnauthorizedAccessException)
        {
            return new ShellInjectionResult(command, newArgs, newEnv, Injected: false);
        }
    }

    private static string GetDefaultDataDirectory()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".aeroterm");
        }

        return Path.Combine(root, "AeroTerm", "shell-integration");
    }

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> only
    /// when the existing file is missing or its contents differ. Avoids
    /// touching mtime on every launch and keeps user inspections clean.
    /// </summary>
    private static void WriteIfChanged(string path, string content)
    {
        if (File.Exists(path))
        {
            try
            {
                if (File.ReadAllText(path) == content)
                {
                    return;
                }
            }
            catch
            {
                // Fall through to overwrite.
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string SubstituteIntegrationPath(string template, string integrationPath)
        => template.Replace(ShellIntegrationScripts.IntegrationPathPlaceholder, integrationPath);

    private ShellInjectionResult InjectZsh(string command, string[] args, IDictionary<string, string> env, string dataDir)
    {
        string integrationPath = Path.Combine(dataDir, "aeroterm-integration.zsh");
        string shimDir = Path.Combine(dataDir, "zsh-shim");

        WriteIfChanged(integrationPath, ShellIntegrationScripts.ZshIntegration);
        WriteIfChanged(Path.Combine(shimDir, ".zshenv"), ShellIntegrationScripts.ZshShimZshenv);
        WriteIfChanged(Path.Combine(shimDir, ".zprofile"), ShellIntegrationScripts.ZshShimZprofile);
        WriteIfChanged(
            Path.Combine(shimDir, ".zshrc"),
            SubstituteIntegrationPath(ShellIntegrationScripts.ZshShimZshrc, integrationPath));
        WriteIfChanged(Path.Combine(shimDir, ".zlogin"), ShellIntegrationScripts.ZshShimZlogin);

        // Preserve the user's existing ZDOTDIR so the shim's .zshrc can
        // delegate to it. If unset, the shim falls back to $HOME.
        if (env.TryGetValue("ZDOTDIR", out string? existing) && !string.IsNullOrEmpty(existing))
        {
            env["USER_ZDOTDIR"] = existing;
        }
        else
        {
            env.Remove("USER_ZDOTDIR");
        }

        env["ZDOTDIR"] = shimDir;
        env[InjectedEnvVar] = "1";

        return new ShellInjectionResult(command, args, env, Injected: true);
    }

    private ShellInjectionResult InjectBash(string command, string[] args, IDictionary<string, string> env, string dataDir)
    {
        string integrationPath = Path.Combine(dataDir, "aeroterm-integration.bash");
        string rcfilePath = Path.Combine(dataDir, "aeroterm-bashrc");

        WriteIfChanged(integrationPath, ShellIntegrationScripts.BashIntegration);
        WriteIfChanged(rcfilePath, SubstituteIntegrationPath(ShellIntegrationScripts.BashShimRcfile, integrationPath));

        // Detect whether the original args asked for a login shell so the
        // shim sources .bash_profile instead of .bashrc.
        bool wantsLogin = false;
        var newArgList = new List<string>(args.Length + 4);
        foreach (string a in args)
        {
            if (a == "-l" || a == "--login")
            {
                wantsLogin = true;
                continue;
            }

            newArgList.Add(a);
        }

        // --rcfile is honoured only by interactive bash; ensure -i is set.
        if (!newArgList.Contains("-i") && !newArgList.Contains("--noprofile"))
        {
            newArgList.Insert(0, "-i");
        }

        newArgList.Insert(0, rcfilePath);
        newArgList.Insert(0, "--rcfile");

        if (wantsLogin)
        {
            env["AEROTERM_BASH_LOGIN"] = "1";
        }

        env[InjectedEnvVar] = "1";

        return new ShellInjectionResult(command, newArgList.ToArray(), env, Injected: true);
    }

    private ShellInjectionResult InjectFish(string command, string[] args, IDictionary<string, string> env, string dataDir)
    {
        string vendorDir = Path.Combine(dataDir, "fish", "vendor_conf.d");
        Directory.CreateDirectory(vendorDir);

        string integrationPath = Path.Combine(vendorDir, "aeroterm.fish");
        WriteIfChanged(integrationPath, ShellIntegrationScripts.FishIntegration);

        // fish reads vendor_conf.d under every dir in $XDG_DATA_DIRS.
        // Prepend our dir so user customizations still win.
        string xdgDataDirsKey = "XDG_DATA_DIRS";
        string injectionDir = Path.Combine(dataDir, "fish").TrimEnd(Path.DirectorySeparatorChar);

        // Wait -- fish looks for `fish/vendor_conf.d` *relative to* each
        // entry in XDG_DATA_DIRS. So the entry we add is `dataDir`, not
        // `dataDir/fish`.
        injectionDir = dataDir.TrimEnd(Path.DirectorySeparatorChar);

        string sep = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ";" : ":";
        if (env.TryGetValue(xdgDataDirsKey, out string? existing) && !string.IsNullOrEmpty(existing))
        {
            if (!existing.Split(sep[0]).Contains(injectionDir))
            {
                env[xdgDataDirsKey] = injectionDir + sep + existing;
            }
        }
        else
        {
            env[xdgDataDirsKey] = injectionDir;
        }

        env[InjectedEnvVar] = "1";

        return new ShellInjectionResult(command, args, env, Injected: true);
    }

    private ShellInjectionResult InjectPowerShell(string command, string[] args, IDictionary<string, string> env, string dataDir)
    {
        string integrationPath = Path.Combine(dataDir, "aeroterm-integration.ps1");
        WriteIfChanged(integrationPath, ShellIntegrationScripts.PowerShellIntegration);

        // Only inject if the caller hasn't already specified a custom
        // -Command / -File / -EncodedCommand. Honour -NoProfile so users
        // that opt-out of profile loading aren't surprised.
        foreach (string a in args)
        {
            string lower = a.ToLowerInvariant();
            if (lower is "-command" or "-c" or "-encodedcommand" or "-ec" or "-file" or "-f")
            {
                return new ShellInjectionResult(command, args, env, Injected: false);
            }
        }

        var newArgList = new List<string>(args.Length + 2);
        foreach (string a in args)
        {
            newArgList.Add(a);
        }

        if (!newArgList.Contains("-NoExit"))
        {
            newArgList.Add("-NoExit");
        }

        newArgList.Add("-Command");
        newArgList.Add($". '{integrationPath.Replace("'", "''")}'");

        env[InjectedEnvVar] = "1";

        return new ShellInjectionResult(command, newArgList.ToArray(), env, Injected: true);
    }
}
