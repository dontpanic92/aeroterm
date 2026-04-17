// <copyright file="LaunchSpec.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Immutable snapshot of the parameters used to launch a shell inside a
/// <see cref="TerminalSessionCoordinator"/>: working directory, executable,
/// argument vector, and environment variables. Captured at startup so that
/// features like "duplicate tab" can spawn a new, independent session with
/// the exact same configuration as the source.
/// </summary>
internal sealed class LaunchSpec
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchSpec"/> class.
    /// </summary>
    /// <param name="cwd">Working directory the child process should start in.</param>
    /// <param name="command">Absolute path (or resolvable name) of the executable.</param>
    /// <param name="args">Command-line argument vector. A defensive copy is taken.</param>
    /// <param name="env">Environment variable map. A defensive copy is taken.</param>
    public LaunchSpec(string cwd, string command, IEnumerable<string> args, IEnumerable<KeyValuePair<string, string>> env)
    {
        this.Cwd = cwd ?? throw new ArgumentNullException(nameof(cwd));
        this.Command = command ?? throw new ArgumentNullException(nameof(command));
        this.Args = args is null ? Array.Empty<string>() : args.ToArray();
        var envCopy = new Dictionary<string, string>();
        if (env is not null)
        {
            foreach (var kv in env)
            {
                envCopy[kv.Key] = kv.Value;
            }
        }

        this.Env = envCopy;
    }

    /// <summary>
    /// Gets the working directory that will be passed to the child shell.
    /// </summary>
    public string Cwd { get; }

    /// <summary>
    /// Gets the executable path / name.
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Gets the command-line argument vector passed to the child shell.
    /// </summary>
    public IReadOnlyList<string> Args { get; }

    /// <summary>
    /// Gets the environment variable snapshot handed to the child shell.
    /// </summary>
    public IReadOnlyDictionary<string, string> Env { get; }

    /// <summary>
    /// Creates a copy of this spec with <see cref="Cwd"/> replaced.
    /// </summary>
    /// <param name="newCwd">The replacement working directory.</param>
    /// <returns>A new <see cref="LaunchSpec"/> instance.</returns>
    public LaunchSpec WithCwd(string newCwd) => new(newCwd, this.Command, this.Args, this.Env);
}
