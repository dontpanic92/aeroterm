// <copyright file="PaneTreeTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Linq;
using AeroTerm.Controls.Panes;
using NUnit.Framework;

/// <summary>
/// Pure-logic tests for <see cref="PaneTree"/>. The tree is
/// unit-testable without Avalonia because it only consumes the
/// <c>ITabSessionContent</c> seam, which <see cref="FakeTabContent"/>
/// fulfils without any real visual construction.
/// </summary>
[TestFixture]
public class PaneTreeTests
{
    /// <summary>
    /// A freshly-constructed tree is a single leaf whose content is
    /// the one supplied to the constructor.
    /// </summary>
    [Test]
    public void NewTree_IsSingleLeaf()
    {
        var c = new FakeTabContent("a");
        var t = new PaneTree(c);
        Assert.That(t.IsSingleLeaf, Is.True);
        Assert.That(t.Root, Is.InstanceOf<PaneLeaf>());
        Assert.That(t.ActiveLeaf, Is.SameAs(t.Root));
        Assert.That(t.EnumerateLeaves().Count(), Is.EqualTo(1));
    }

    /// <summary>
    /// Splitting a single-leaf tree replaces the root with a
    /// <see cref="PaneSplit"/> whose first child is the original leaf
    /// and whose second is a new leaf wrapping the supplied content.
    /// The new leaf becomes active.
    /// </summary>
    [Test]
    public void SplitActive_ReplacesRootWithSplit()
    {
        var a = new FakeTabContent("a");
        var b = new FakeTabContent("b");
        var t = new PaneTree(a);
        var originalLeaf = (PaneLeaf)t.Root;

        var newLeaf = t.SplitActive(PaneOrientation.Vertical, b);

        Assert.That(t.IsSingleLeaf, Is.False);
        Assert.That(t.Root, Is.InstanceOf<PaneSplit>());
        var split = (PaneSplit)t.Root;
        Assert.That(split.Orientation, Is.EqualTo(PaneOrientation.Vertical));
        Assert.That(split.First, Is.SameAs(originalLeaf));
        Assert.That(split.Second, Is.SameAs(newLeaf));
        Assert.That(t.ActiveLeaf, Is.SameAs(newLeaf));
        Assert.That(t.EnumerateLeaves().Count(), Is.EqualTo(2));
    }

    /// <summary>
    /// Splitting raises <see cref="PaneTree.LeafAdded"/>,
    /// <see cref="PaneTree.StructureChanged"/>, and
    /// <see cref="PaneTree.ActiveLeafChanged"/> in order.
    /// </summary>
    [Test]
    public void SplitActive_RaisesEvents()
    {
        var t = new PaneTree(new FakeTabContent("a"));
        int added = 0, structure = 0, active = 0;
        t.LeafAdded += _ => added++;
        t.StructureChanged += () => structure++;
        t.ActiveLeafChanged += _ => active++;

        t.SplitActive(PaneOrientation.Horizontal, new FakeTabContent("b"));

        Assert.That(added, Is.EqualTo(1));
        Assert.That(structure, Is.EqualTo(1));
        Assert.That(active, Is.EqualTo(1));
    }

    /// <summary>
    /// A nested split replaces the active leaf (not the root) with a
    /// new split.
    /// </summary>
    [Test]
    public void SplitActive_NestedReplacesActiveLeafNotRoot()
    {
        var a = new FakeTabContent("a");
        var b = new FakeTabContent("b");
        var c = new FakeTabContent("c");
        var t = new PaneTree(a);
        t.SplitActive(PaneOrientation.Vertical, b);    // active = b
        var newLeaf = t.SplitActive(PaneOrientation.Horizontal, c); // splits b

        Assert.That(t.Root, Is.InstanceOf<PaneSplit>());
        var outer = (PaneSplit)t.Root;
        Assert.That(outer.Orientation, Is.EqualTo(PaneOrientation.Vertical));
        Assert.That(outer.First, Is.InstanceOf<PaneLeaf>());
        Assert.That(((PaneLeaf)outer.First).Content, Is.SameAs(a));
        Assert.That(outer.Second, Is.InstanceOf<PaneSplit>());
        var inner = (PaneSplit)outer.Second;
        Assert.That(inner.Orientation, Is.EqualTo(PaneOrientation.Horizontal));
        Assert.That(inner.First, Is.InstanceOf<PaneLeaf>());
        Assert.That(((PaneLeaf)inner.First).Content, Is.SameAs(b));
        Assert.That(inner.Second, Is.SameAs(newLeaf));
        Assert.That(((PaneLeaf)inner.Second).Content, Is.SameAs(c));
        Assert.That(t.EnumerateLeaves().Count(), Is.EqualTo(3));
    }

    /// <summary>
    /// Closing a leaf inside a split collapses the sibling into the
    /// parent slot and disposes the closed leaf's content.
    /// </summary>
    [Test]
    public void CloseActive_CollapsesSiblingIntoParentSlot()
    {
        var a = new FakeTabContent("a");
        var b = new FakeTabContent("b");
        var t = new PaneTree(a);
        t.SplitActive(PaneOrientation.Vertical, b); // active = b

        bool alive = t.CloseActive();

        Assert.That(alive, Is.True);
        Assert.That(t.IsSingleLeaf, Is.True);
        Assert.That(t.Root, Is.InstanceOf<PaneLeaf>());
        Assert.That(((PaneLeaf)t.Root).Content, Is.SameAs(a));
        Assert.That(t.ActiveLeaf, Is.SameAs(t.Root));
        Assert.That(b.DisposeCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Closing the only leaf returns <see langword="false"/> so the
    /// caller knows to fall back to tab-level "last tab closed"
    /// behaviour.
    /// </summary>
    [Test]
    public void CloseActive_LastLeaf_ReturnsFalse()
    {
        var a = new FakeTabContent("a");
        var t = new PaneTree(a);

        bool alive = t.CloseActive();

        Assert.That(alive, Is.False);
        Assert.That(a.DisposeCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Splitting horizontally (stacked), then focusing up/down, moves
    /// between the two panes.
    /// </summary>
    [Test]
    public void FocusDirection_TraversesHorizontalSplit()
    {
        var a = new FakeTabContent("a");
        var b = new FakeTabContent("b");
        var t = new PaneTree(a);
        var bLeaf = t.SplitActive(PaneOrientation.Horizontal, b); // a on top, b below
        var aLeaf = (PaneLeaf)((PaneSplit)t.Root).First;

        Assert.That(t.ActiveLeaf, Is.SameAs(bLeaf));
        Assert.That(t.FocusDirection(PaneDirection.Up), Is.True);
        Assert.That(t.ActiveLeaf, Is.SameAs(aLeaf));
        Assert.That(t.FocusDirection(PaneDirection.Down), Is.True);
        Assert.That(t.ActiveLeaf, Is.SameAs(bLeaf));
        Assert.That(t.FocusDirection(PaneDirection.Left), Is.False);
        Assert.That(t.FocusDirection(PaneDirection.Right), Is.False);
    }

    /// <summary>
    /// Splitting vertically (side-by-side), then focusing left/right,
    /// moves between the two panes.
    /// </summary>
    [Test]
    public void FocusDirection_TraversesVerticalSplit()
    {
        var a = new FakeTabContent("a");
        var b = new FakeTabContent("b");
        var t = new PaneTree(a);
        var bLeaf = t.SplitActive(PaneOrientation.Vertical, b);
        var aLeaf = (PaneLeaf)((PaneSplit)t.Root).First;

        Assert.That(t.ActiveLeaf, Is.SameAs(bLeaf));
        Assert.That(t.FocusDirection(PaneDirection.Left), Is.True);
        Assert.That(t.ActiveLeaf, Is.SameAs(aLeaf));
        Assert.That(t.FocusDirection(PaneDirection.Right), Is.True);
        Assert.That(t.ActiveLeaf, Is.SameAs(bLeaf));
        Assert.That(t.FocusDirection(PaneDirection.Up), Is.False);
        Assert.That(t.FocusDirection(PaneDirection.Down), Is.False);
    }

    /// <summary>
    /// Nested splits traverse correctly: from a pane in a horizontal
    /// sub-split nested inside the right side of a vertical split,
    /// Left moves out of the horizontal split into the outer left
    /// pane.
    /// </summary>
    [Test]
    public void FocusDirection_CrossesNestedSplits()
    {
        var a = new FakeTabContent("a");
        var b = new FakeTabContent("b");
        var c = new FakeTabContent("c");
        var t = new PaneTree(a);
        t.SplitActive(PaneOrientation.Vertical, b);     // a | b   active = b
        t.SplitActive(PaneOrientation.Horizontal, c);   // a | (b / c)  active = c
        var aLeaf = (PaneLeaf)((PaneSplit)t.Root).First;

        Assert.That(t.FocusDirection(PaneDirection.Left), Is.True);
        Assert.That(t.ActiveLeaf, Is.SameAs(aLeaf));
    }

    /// <summary>
    /// Ratios survive a close: splitting, mutating the outer ratio,
    /// then closing the inner split's active leaf preserves the
    /// outer ratio.
    /// </summary>
    [Test]
    public void Ratio_PersistsAcrossClose()
    {
        var a = new FakeTabContent("a");
        var b = new FakeTabContent("b");
        var c = new FakeTabContent("c");
        var t = new PaneTree(a);
        t.SplitActive(PaneOrientation.Vertical, b);
        var outer = (PaneSplit)t.Root;
        outer.Ratio = 0.3;

        // Now split the second leaf (b) and close the new pane.
        t.SplitActive(PaneOrientation.Horizontal, c);
        bool alive = t.CloseActive();

        Assert.That(alive, Is.True);
        Assert.That(t.Root, Is.SameAs(outer));
        Assert.That(outer.Ratio, Is.EqualTo(0.3).Within(1e-9));
    }

    /// <summary>
    /// Ratio clamps out-of-range values to <c>[0.05, 0.95]</c>.
    /// </summary>
    [Test]
    public void Ratio_ClampsOutOfRange()
    {
        var split = new PaneTree(new FakeTabContent("a"));
        split.SplitActive(PaneOrientation.Vertical, new FakeTabContent("b"));
        var s = (PaneSplit)split.Root;

        s.Ratio = -1;
        Assert.That(s.Ratio, Is.EqualTo(0.05).Within(1e-9));
        s.Ratio = 2;
        Assert.That(s.Ratio, Is.EqualTo(0.95).Within(1e-9));
        s.Ratio = double.NaN;
        Assert.That(s.Ratio, Is.EqualTo(0.5).Within(1e-9));
    }

    /// <summary>
    /// Disposing the tree disposes every leaf's content exactly once.
    /// </summary>
    [Test]
    public void Dispose_DisposesEveryLeafOnce()
    {
        var a = new FakeTabContent("a");
        var b = new FakeTabContent("b");
        var c = new FakeTabContent("c");
        var t = new PaneTree(a);
        t.SplitActive(PaneOrientation.Vertical, b);
        t.SplitActive(PaneOrientation.Horizontal, c);

        t.Dispose();

        Assert.That(a.DisposeCount, Is.EqualTo(1));
        Assert.That(b.DisposeCount, Is.EqualTo(1));
        Assert.That(c.DisposeCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Closing a pane activates the former sibling (nearest remaining
    /// leaf). When the sibling is itself a split, descent picks its
    /// first leaf deterministically.
    /// </summary>
    [Test]
    public void CloseActive_ActivatesSiblingLeaf()
    {
        var a = new FakeTabContent("a");
        var b = new FakeTabContent("b");
        var c = new FakeTabContent("c");
        var t = new PaneTree(a);

        // Build:  a | b  (active=b), then split a horizontally: (a1/a2) | b.
        t.SplitActive(PaneOrientation.Vertical, b);
        var outer = (PaneSplit)t.Root;

        // Move active back to the first leaf 'a' so we can split it.
        t.SetActive((PaneLeaf)outer.First);
        t.SplitActive(PaneOrientation.Horizontal, c);

        // Active is now 'c' (second of inner split); sibling 'a' stays.

        // Close the active pane (c). Sibling 'a' (a PaneLeaf) collapses
        // into the parent slot on the left.
        t.CloseActive();

        Assert.That(t.Root, Is.InstanceOf<PaneSplit>());
        var outerNow = (PaneSplit)t.Root;
        Assert.That(outerNow.First, Is.InstanceOf<PaneLeaf>());
        Assert.That(((PaneLeaf)outerNow.First).Content, Is.SameAs(a));
        Assert.That(t.ActiveLeaf, Is.SameAs(outerNow.First));
    }
}
