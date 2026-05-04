// <copyright file="IShellProbeEnvironment.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Text;

/// <summary>
/// Filesystem / process probe seam used by <see cref="ShellDiscovery"/> so
/// tests can drive discovery without depending on the host machine.
/// </summary>
public interface IShellProbeEnvironment
{
    /// <summary>Gets a value indicating whether the host is Windows.</summary>
    bool IsWindows { get; }

    /// <summary>Gets a value indicating whether the host is macOS.</summary>
    bool IsMacOS { get; }

    /// <summary>Gets a value indicating whether the host is Linux.</summary>
    bool IsLinux { get; }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="path"/> refers
    /// to an existing file the discovery can probe.
    /// </summary>
    /// <param name="path">Absolute path to test.</param>
    /// <returns>Whether the file exists.</returns>
    bool FileExists(string path);

    /// <summary>
    /// Reads the contents of a text file. Returns <c>null</c> when the
    /// file is missing or cannot be read.
    /// </summary>
    /// <param name="path">Absolute path to read.</param>
    /// <returns>The file contents, or <c>null</c> on failure.</returns>
    string? TryReadAllText(string path);

    /// <summary>
    /// Looks up an environment variable. Returns <c>null</c> when unset.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable's value, or <c>null</c>.</returns>
    string? GetEnvironmentVariable(string name);

    /// <summary>
    /// Resolves a special folder (e.g. <c>ProgramFiles</c>,
    /// <c>UserProfile</c>) to an absolute path. Returns an empty string
    /// when the folder is not defined on the host.
    /// </summary>
    /// <param name="folder">The folder to resolve.</param>
    /// <returns>The resolved path or an empty string.</returns>
    string GetFolderPath(Environment.SpecialFolder folder);

    /// <summary>
    /// Enumerates immediate subdirectories of <paramref name="directory"/>
    /// matching <paramref name="pattern"/>. Returns an empty array on
    /// failure or when the directory does not exist.
    /// </summary>
    /// <param name="directory">Directory to enumerate.</param>
    /// <param name="pattern">Glob pattern.</param>
    /// <returns>Matching subdirectory paths.</returns>
    string[] EnumerateDirectories(string directory, string pattern);

    /// <summary>
    /// Runs <paramref name="executable"/> with the supplied arguments and
    /// captures stdout. Returns <c>null</c> when the process cannot be
    /// started or exceeds <paramref name="timeoutMs"/>.
    /// </summary>
    /// <param name="executable">Executable to run.</param>
    /// <param name="arguments">Argument string.</param>
    /// <param name="timeoutMs">Max wait in milliseconds.</param>
    /// <param name="encoding">Stdout encoding.</param>
    /// <returns>Captured stdout, or <c>null</c>.</returns>
    string? RunCapture(string executable, string arguments, int timeoutMs, Encoding encoding);
}
