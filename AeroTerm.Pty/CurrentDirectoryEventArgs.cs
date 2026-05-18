// <copyright file="CurrentDirectoryEventArgs.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// Event data raised when the terminal stream reports a current working
/// directory, such as via OSC 7 or shell-integration prompt metadata.
/// </summary>
public sealed class CurrentDirectoryEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CurrentDirectoryEventArgs"/> class.
    /// </summary>
    /// <param name="currentDirectory">The reported working directory.</param>
    public CurrentDirectoryEventArgs(string currentDirectory)
    {
        this.CurrentDirectory = currentDirectory;
    }

    /// <summary>
    /// Gets the reported working directory.
    /// </summary>
    public string CurrentDirectory { get; }
}
