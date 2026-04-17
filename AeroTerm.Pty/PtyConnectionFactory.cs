// <copyright file="PtyConnectionFactory.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// Creates platform-appropriate PTY connections. This type is retained as a
/// static convenience shim; new code should prefer injecting
/// <see cref="IPtyConnectionFactory"/>.
/// </summary>
public static class PtyConnectionFactory
{
    /// <summary>
    /// Create a PTY connection for the current platform.
    /// </summary>
    /// <param name="app">Absolute path to the executable.</param>
    /// <param name="args">Command-line arguments excluding argv[0].</param>
    /// <param name="environment">Environment variables for the child process.</param>
    /// <param name="cwd">Working directory for the child process.</param>
    /// <param name="rows">Initial terminal row count.</param>
    /// <param name="cols">Initial terminal column count.</param>
    /// <returns>The created PTY connection.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/>,
    /// <paramref name="args"/>, <paramref name="environment"/>, or <paramref name="cwd"/>
    /// is <see langword="null"/>.</exception>
    public static IPtyConnection Create(
        string app,
        string[] args,
        IDictionary<string, string> environment,
        string cwd,
        int rows,
        int cols)
    {
        return DefaultPtyConnectionFactory.Instance.Create(app, args, environment, cwd, rows, cols);
    }
}
