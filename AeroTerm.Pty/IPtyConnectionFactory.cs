// <copyright file="IPtyConnectionFactory.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// Abstraction over PTY creation. Allows tests to inject a fake PTY without
/// going through <c>forkpty</c> / ConPTY. Production callers receive an
/// instance backed by <see cref="DefaultPtyConnectionFactory"/>.
/// </summary>
public interface IPtyConnectionFactory
{
    /// <summary>
    /// Create a PTY connection.
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
    IPtyConnection Create(
        string app,
        string[] args,
        IDictionary<string, string> environment,
        string cwd,
        int rows,
        int cols);
}
