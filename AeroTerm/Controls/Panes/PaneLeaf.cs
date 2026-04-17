// <copyright file="PaneLeaf.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Panes;

/// <summary>
/// A leaf node in the pane split tree. Owns a single
/// <see cref="ITabSessionContent"/> — one PTY + one
/// <see cref="TerminalControl"/>.
/// </summary>
public sealed class PaneLeaf : PaneNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaneLeaf"/> class.
    /// </summary>
    /// <param name="content">The terminal-session content this leaf
    /// owns. Ownership transfers — the leaf's eventual disposal (via
    /// <see cref="PaneTree.Dispose"/> or <see cref="PaneTree.CloseActive"/>)
    /// disposes the content.</param>
    internal PaneLeaf(ITabSessionContent content)
    {
        this.Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// Gets the terminal-session content hosted by this leaf.
    /// </summary>
    internal ITabSessionContent Content { get; }
}
