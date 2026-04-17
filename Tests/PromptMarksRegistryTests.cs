// <copyright file="PromptMarksRegistryTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Unit tests for <see cref="PromptMarksRegistry"/>: add, navigate
/// (previous / next), and prune.
/// </summary>
public class PromptMarksRegistryTests
{
    /// <summary>Empty registry returns null from all navigation queries.</summary>
    [Test]
    public void Empty_FindReturnsNull()
    {
        var reg = new PromptMarksRegistry();
        Assert.That(reg.FindPrevious(100), Is.Null);
        Assert.That(reg.FindNext(0), Is.Null);
        Assert.That(reg.Marks, Is.Empty);
    }

    /// <summary>Only navigable kinds (OutputStart / CommandStart) are picked.</summary>
    [Test]
    public void FindPrevious_SkipsNonNavigableKinds()
    {
        var reg = new PromptMarksRegistry();
        reg.Add(new PromptMark(PromptMarkKind.PromptStart, 5, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.CommandEnd, 6, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 7, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.CommandStart, 8, 0, null, null));

        var prev = reg.FindPrevious(currentAbsoluteRow: 20);
        Assert.That(prev, Is.Not.Null);
        Assert.That(prev!.AbsoluteRow, Is.EqualTo(8));
        Assert.That(prev.Kind, Is.EqualTo(PromptMarkKind.CommandStart));
    }

    /// <summary>FindPrevious picks the nearest mark strictly less than the anchor.</summary>
    [Test]
    public void FindPrevious_PicksNearestBelow()
    {
        var reg = new PromptMarksRegistry();
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 10, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 20, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 30, 0, null, null));

        Assert.That(reg.FindPrevious(25)!.AbsoluteRow, Is.EqualTo(20));
        Assert.That(reg.FindPrevious(30)!.AbsoluteRow, Is.EqualTo(20));
        Assert.That(reg.FindPrevious(10), Is.Null);
    }

    /// <summary>FindNext picks the nearest mark strictly greater than the anchor.</summary>
    [Test]
    public void FindNext_PicksNearestAbove()
    {
        var reg = new PromptMarksRegistry();
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 10, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 20, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 30, 0, null, null));

        Assert.That(reg.FindNext(5)!.AbsoluteRow, Is.EqualTo(10));
        Assert.That(reg.FindNext(10)!.AbsoluteRow, Is.EqualTo(20));
        Assert.That(reg.FindNext(30), Is.Null);
    }

    /// <summary>PruneBelow removes marks whose AbsoluteRow &lt; threshold.</summary>
    [Test]
    public void PruneBelow_RemovesMarksBelowThreshold()
    {
        var reg = new PromptMarksRegistry();
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 1, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 5, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 10, 0, null, null));

        reg.PruneBelow(5);

        Assert.That(reg.Marks, Has.Count.EqualTo(2));
        Assert.That(reg.Marks[0].AbsoluteRow, Is.EqualTo(5));
        Assert.That(reg.Marks[1].AbsoluteRow, Is.EqualTo(10));
    }

    /// <summary>Clear empties the registry.</summary>
    [Test]
    public void Clear_EmptiesRegistry()
    {
        var reg = new PromptMarksRegistry();
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 1, 0, null, null));
        reg.Clear();
        Assert.That(reg.Marks, Is.Empty);
    }

    /// <summary>Marks added out-of-order still return the correct nearest.</summary>
    [Test]
    public void FindPrevious_OutOfOrderInsertion_ReturnsGlobalMaxBelowAnchor()
    {
        var reg = new PromptMarksRegistry();
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 30, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 10, 0, null, null));
        reg.Add(new PromptMark(PromptMarkKind.OutputStart, 20, 0, null, null));

        Assert.That(reg.FindPrevious(25)!.AbsoluteRow, Is.EqualTo(20));
    }
}
