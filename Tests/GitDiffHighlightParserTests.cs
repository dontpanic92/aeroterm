// <copyright file="GitDiffHighlightParserTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests for parsing full-file Git highlight ranges from unified diffs.
/// </summary>
[TestFixture]
public sealed class GitDiffHighlightParserTests
{
    /// <summary>
    /// Empty input returns no ranges.
    /// </summary>
    [Test]
    public void Parse_EmptyInput_ReturnsNoRanges()
    {
        var ranges = GitDiffHighlightParser.Parse(string.Empty);

        Assert.That(ranges.OldRanges, Is.Empty);
        Assert.That(ranges.NewRanges, Is.Empty);
    }

    /// <summary>
    /// Paired remove/add blocks are classified as modified on both sides.
    /// </summary>
    [Test]
    public void Parse_Modification_ProducesModifiedRangesOnBothSides()
    {
        var diff = string.Join(
            "\n",
            "diff --git a/file.txt b/file.txt",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -2,1 +2,1 @@",
            "-old",
            "+new");

        var ranges = GitDiffHighlightParser.Parse(diff);

        Assert.That(ranges.OldRanges, Has.Count.EqualTo(1));
        Assert.That(ranges.NewRanges, Has.Count.EqualTo(1));
        Assert.That(ranges.OldRanges[0], Is.EqualTo(new GitDiffHighlightRange(2, 1, GitDiffHighlightKind.Modified)));
        Assert.That(ranges.NewRanges[0], Is.EqualTo(new GitDiffHighlightRange(2, 1, GitDiffHighlightKind.Modified)));
    }

    /// <summary>
    /// Add-only and remove-only hunks are assigned to their respective sides.
    /// </summary>
    [Test]
    public void Parse_AddAndRemoveOnlyRanges_AreSideSpecific()
    {
        var diff = string.Join(
            "\n",
            "diff --git a/file.txt b/file.txt",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -3,2 +3,0 @@",
            "-gone one",
            "-gone two",
            "@@ -8,0 +6,2 @@",
            "+new one",
            "+new two");

        var ranges = GitDiffHighlightParser.Parse(diff);

        Assert.That(ranges.OldRanges, Is.EqualTo(new[] { new GitDiffHighlightRange(3, 2, GitDiffHighlightKind.Removed) }));
        Assert.That(ranges.NewRanges, Is.EqualTo(new[] { new GitDiffHighlightRange(6, 2, GitDiffHighlightKind.Added) }));
    }

    /// <summary>
    /// Context lines flush adjacent change groups into separate ranges.
    /// </summary>
    [Test]
    public void Parse_ContextSplitsChangeGroups()
    {
        var diff = string.Join(
            "\n",
            "diff --git a/file.txt b/file.txt",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,3 +1,3 @@",
            "-old one",
            "+new one",
            " context",
            "-old two",
            "+new two");

        var ranges = GitDiffHighlightParser.Parse(diff);

        Assert.That(ranges.OldRanges, Has.Count.EqualTo(2));
        Assert.That(ranges.NewRanges, Has.Count.EqualTo(2));
        Assert.That(ranges.OldRanges[0].StartLine, Is.EqualTo(1));
        Assert.That(ranges.OldRanges[1].StartLine, Is.EqualTo(3));
    }
}
