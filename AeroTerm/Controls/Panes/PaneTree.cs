// <copyright file="PaneTree.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Panes;

using System.Collections.Generic;

/// <summary>
/// Owns a tab's pane split tree and tracks the active leaf. Provides
/// split / close / focus-navigate operations and raises change events
/// so the hosting <see cref="PaneTreeView"/> can re-render and the
/// window can wire per-pane plumbing (bell, bg color, exit) on newly
/// added panes.
/// </summary>
internal sealed class PaneTree : IDisposable
{
    private PaneNode root;
    private PaneLeaf activeLeaf;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaneTree"/> class
    /// with a single leaf wrapping the supplied content.
    /// </summary>
    /// <param name="initialContent">The first pane's content. Ownership
    /// transfers — the tree disposes it on
    /// <see cref="Dispose"/> / <see cref="CloseActive"/>.</param>
    public PaneTree(ITabSessionContent initialContent)
    {
        ArgumentNullException.ThrowIfNull(initialContent);
        var leaf = new PaneLeaf(initialContent);
        this.root = leaf;
        this.activeLeaf = leaf;
    }

    /// <summary>
    /// Raised whenever the tree's structure changes (split inserted,
    /// leaf removed, or ratio mutated from code). The view subscribes
    /// to rebuild its visual. The active-leaf change is delivered via
    /// <see cref="ActiveLeafChanged"/> instead.
    /// </summary>
    public event Action? StructureChanged;

    /// <summary>
    /// Raised whenever <see cref="ActiveLeaf"/> changes, after
    /// <see cref="StructureChanged"/> has fired for any accompanying
    /// structural update.
    /// </summary>
    public event Action<PaneLeaf>? ActiveLeafChanged;

    /// <summary>
    /// Raised when a new leaf has been added to the tree (split
    /// creates a second leaf). The host wires per-pane event
    /// handlers (bell, bg color, exit) in response.
    /// </summary>
    public event Action<PaneLeaf>? LeafAdded;

    /// <summary>
    /// Raised immediately before a leaf is detached and disposed by
    /// <see cref="CloseActive"/>. The host unwires per-pane event
    /// handlers before the underlying content is torn down.
    /// </summary>
    public event Action<PaneLeaf>? LeafRemoving;

    /// <summary>
    /// Gets the root of the tree (either a <see cref="PaneLeaf"/> or a
    /// <see cref="PaneSplit"/>).
    /// </summary>
    public PaneNode Root => this.root;

    /// <summary>
    /// Gets the currently-active leaf — the one that receives user
    /// input and is visually highlighted as focused.
    /// </summary>
    public PaneLeaf ActiveLeaf => this.activeLeaf;

    /// <summary>
    /// Gets a value indicating whether the tree contains exactly one
    /// leaf (equivalent to <c>Root is PaneLeaf</c>).
    /// </summary>
    public bool IsSingleLeaf => this.root is PaneLeaf;

    /// <summary>
    /// Enumerates every leaf in the tree in depth-first order
    /// (first-then-second).
    /// </summary>
    /// <returns>The sequence of all leaves.</returns>
    public IEnumerable<PaneLeaf> EnumerateLeaves()
    {
        return EnumerateLeavesFrom(this.root);
    }

    /// <summary>
    /// Sets the active leaf. No-op if the leaf is not in the tree or
    /// is already active.
    /// </summary>
    /// <param name="leaf">The leaf to activate.</param>
    public void SetActive(PaneLeaf leaf)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        if (ReferenceEquals(this.activeLeaf, leaf))
        {
            return;
        }

        if (!this.Contains(leaf))
        {
            return;
        }

        this.activeLeaf = leaf;
        this.ActiveLeafChanged?.Invoke(leaf);
    }

    /// <summary>
    /// Splits the active leaf: replaces it with a <see cref="PaneSplit"/>
    /// whose <see cref="PaneSplit.First"/> is the old leaf and whose
    /// <see cref="PaneSplit.Second"/> is a new leaf wrapping
    /// <paramref name="newContent"/>. The new leaf becomes the active
    /// leaf.
    /// </summary>
    /// <param name="orientation">Divider orientation for the new split.</param>
    /// <param name="newContent">Content for the newly-created sibling
    /// leaf. Ownership transfers to the tree.</param>
    /// <returns>The newly-created leaf.</returns>
    public PaneLeaf SplitActive(PaneOrientation orientation, ITabSessionContent newContent)
    {
        ArgumentNullException.ThrowIfNull(newContent);
        this.ThrowIfDisposed();

        var oldLeaf = this.activeLeaf;
        var parent = oldLeaf.Parent;
        var newLeaf = new PaneLeaf(newContent);
        var split = new PaneSplit(orientation, oldLeaf, newLeaf);

        split.Parent = parent;
        if (parent is null)
        {
            // Old leaf was the root.
            this.root = split;
        }
        else if (ReferenceEquals(parent.First, oldLeaf))
        {
            parent.First = split;
        }
        else
        {
            parent.Second = split;
        }

        this.activeLeaf = newLeaf;

        this.LeafAdded?.Invoke(newLeaf);
        this.StructureChanged?.Invoke();
        this.ActiveLeafChanged?.Invoke(newLeaf);
        return newLeaf;
    }

    /// <summary>
    /// Closes the active leaf. If the tree collapses to empty,
    /// returns <see langword="false"/> and the caller should fall back
    /// to tab-level "last pane closed" behaviour. Otherwise the
    /// sibling of the closed leaf replaces its parent split and a
    /// nearby leaf becomes active.
    /// </summary>
    /// <returns><see langword="true"/> when at least one leaf remains;
    /// <see langword="false"/> when the tree is now empty (last leaf
    /// was just closed).</returns>
    public bool CloseActive()
    {
        this.ThrowIfDisposed();
        var leaf = this.activeLeaf;
        var parent = leaf.Parent;

        this.LeafRemoving?.Invoke(leaf);

        if (parent is null)
        {
            // Last leaf — caller closes the tab.
            leaf.Content.Dispose();
            return false;
        }

        var sibling = ReferenceEquals(parent.First, leaf) ? parent.Second : parent.First;
        var grandparent = parent.Parent;
        sibling.Parent = grandparent;
        if (grandparent is null)
        {
            this.root = sibling;
        }
        else if (ReferenceEquals(grandparent.First, parent))
        {
            grandparent.First = sibling;
        }
        else
        {
            grandparent.Second = sibling;
        }

        parent.First = null!;
        parent.Second = null!;
        parent.Parent = null;
        leaf.Parent = null;

        // Choose a new active leaf — descend into the former sibling
        // (nearest remaining leaf to the closed pane).
        this.activeLeaf = FirstLeaf(sibling);

        leaf.Content.Dispose();

        this.StructureChanged?.Invoke();
        this.ActiveLeafChanged?.Invoke(this.activeLeaf);
        return true;
    }

    /// <summary>
    /// Moves focus to the nearest pane in the requested direction.
    /// Walks up the tree looking for the first ancestor split that
    /// has the requested axis and where the active subtree lies on
    /// the "wrong" side of the divider. Then descends into the
    /// opposite subtree, picking the leaf closest to the divider.
    /// Returns <see langword="true"/> when focus actually moved.
    /// </summary>
    /// <param name="direction">The direction to move focus.</param>
    /// <returns><see langword="true"/> if focus moved.</returns>
    public bool FocusDirection(PaneDirection direction)
    {
        this.ThrowIfDisposed();
        var target = FindDirectionalNeighbour(this.activeLeaf, direction);
        if (target is null || ReferenceEquals(target, this.activeLeaf))
        {
            return false;
        }

        this.activeLeaf = target;
        this.ActiveLeafChanged?.Invoke(target);
        return true;
    }

    /// <summary>
    /// Notifies subscribers that a ratio has been mutated externally
    /// (e.g. by a <c>GridSplitter</c> in <see cref="PaneTreeView"/>).
    /// The structure event fires so any mirror of the tree refreshes,
    /// but no tree topology change occurs.
    /// </summary>
    public void NotifyRatioChanged()
    {
        this.StructureChanged?.Invoke();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        foreach (var leaf in this.EnumerateLeaves())
        {
            leaf.Content.Dispose();
        }
    }

    private static IEnumerable<PaneLeaf> EnumerateLeavesFrom(PaneNode node)
    {
        if (node is PaneLeaf leaf)
        {
            yield return leaf;
            yield break;
        }

        var split = (PaneSplit)node;
        foreach (var l in EnumerateLeavesFrom(split.First))
        {
            yield return l;
        }

        foreach (var l in EnumerateLeavesFrom(split.Second))
        {
            yield return l;
        }
    }

    private static PaneLeaf FirstLeaf(PaneNode node)
    {
        while (node is PaneSplit split)
        {
            node = split.First;
        }

        return (PaneLeaf)node;
    }

    private static PaneLeaf? FindDirectionalNeighbour(PaneLeaf start, PaneDirection direction)
    {
        // Axis + required child-side for "leaving" a subtree.
        // For Left: want to cross a Vertical split from Second side → enter First subtree (rightmost leaf).
        // For Right: Vertical split from First side → Second subtree (leftmost leaf).
        // For Up: Horizontal split from Second side → First subtree (bottommost leaf).
        // For Down: Horizontal split from First side → Second subtree (topmost leaf).
        PaneOrientation axis = direction == PaneDirection.Left || direction == PaneDirection.Right
            ? PaneOrientation.Vertical
            : PaneOrientation.Horizontal;
        bool movingTowardsFirst = direction == PaneDirection.Left || direction == PaneDirection.Up;

        PaneNode child = start;
        PaneSplit? parent = start.Parent;
        while (parent is not null)
        {
            if (parent.Orientation == axis)
            {
                bool childIsSecond = ReferenceEquals(parent.Second, child);
                if (movingTowardsFirst && childIsSecond)
                {
                    return DescendTowards(parent.First, axis, pickSecond: true);
                }

                if (!movingTowardsFirst && !childIsSecond)
                {
                    return DescendTowards(parent.Second, axis, pickSecond: false);
                }
            }

            child = parent;
            parent = parent.Parent;
        }

        return null;
    }

    private static PaneLeaf DescendTowards(PaneNode subtree, PaneOrientation axis, bool pickSecond)
    {
        // Descend picking the side closest to the divider we just
        // crossed. On the matching axis, that's the opposite of the
        // direction we came from; on the perpendicular axis, prefer
        // First so navigation is deterministic.
        while (subtree is PaneSplit s)
        {
            if (s.Orientation == axis)
            {
                subtree = pickSecond ? s.Second : s.First;
            }
            else
            {
                subtree = s.First;
            }
        }

        return (PaneLeaf)subtree;
    }

    private bool Contains(PaneLeaf leaf)
    {
        foreach (var l in this.EnumerateLeaves())
        {
            if (ReferenceEquals(l, leaf))
            {
                return true;
            }
        }

        return false;
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(PaneTree));
        }
    }
}
