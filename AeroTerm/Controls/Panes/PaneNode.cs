// <copyright file="PaneNode.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Panes;

/// <summary>
/// Base type for nodes in a tab's pane split tree. Either a
/// <see cref="PaneLeaf"/> (single terminal session) or a
/// <see cref="PaneSplit"/> (binary split with orientation + ratio).
/// The tree is strictly binary and parent links are maintained by
/// <see cref="PaneTree"/>.
/// </summary>
public abstract class PaneNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaneNode"/> class.
    /// </summary>
    private protected PaneNode()
    {
    }

    /// <summary>
    /// Gets the parent node, or <see langword="null"/> when this node
    /// is the root of the tree. Setter is internal and driven by
    /// <see cref="PaneTree"/>'s mutations.
    /// </summary>
    public PaneSplit? Parent { get; internal set; }
}
