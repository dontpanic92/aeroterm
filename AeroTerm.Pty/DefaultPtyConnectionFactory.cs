// <copyright file="DefaultPtyConnectionFactory.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

using System.Runtime.InteropServices;

/// <summary>
/// Default <see cref="IPtyConnectionFactory"/> that selects the platform-native
/// backend (ConPTY on Windows, forkpty elsewhere).
/// </summary>
public sealed class DefaultPtyConnectionFactory : IPtyConnectionFactory
{
    /// <summary>
    /// Gets the shared singleton instance.
    /// </summary>
    public static DefaultPtyConnectionFactory Instance { get; } = new();

    /// <inheritdoc />
    public IPtyConnection Create(
        string app,
        string[] args,
        IDictionary<string, string> environment,
        string cwd,
        int rows,
        int cols)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsPtyConnection(app, args, environment, cwd, rows, cols);
        }

        return new NativePtyConnection(app, args, environment, cwd, rows, cols);
    }
}
