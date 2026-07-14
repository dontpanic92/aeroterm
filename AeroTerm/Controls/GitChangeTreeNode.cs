// <copyright file="GitChangeTreeNode.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Collections.Generic;

/// <summary>
/// One data-backed node in the Git changes tree.
/// </summary>
/// <param name="Title">Text displayed for a group node.</param>
/// <param name="Item">File item for a leaf node, or <see langword="null"/> for a group.</param>
/// <param name="Children">Child nodes.</param>
/// <param name="ShowParentPath">Whether a file leaf should show its parent path for disambiguation.</param>
internal sealed record GitChangeTreeNode(
    string Title,
    GitChangeItem? Item,
    IReadOnlyList<GitChangeTreeNode> Children,
    bool ShowParentPath = false)
{
    /// <summary>
    /// Gets or sets a value indicating whether this node is expanded.
    /// </summary>
    internal bool IsExpanded { get; set; } = true;
}
