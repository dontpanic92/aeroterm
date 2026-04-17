// <copyright file="FuzzyMatcherTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Pure-logic tests for <see cref="FuzzyMatcher"/>.
/// </summary>
[TestFixture]
public class FuzzyMatcherTests
{
    /// <summary>Empty query matches anything with score 0 and no positions.</summary>
    [Test]
    public void EmptyQuery_MatchesEverything_ScoreZero()
    {
        var m = FuzzyMatcher.Score(string.Empty, "New tab");
        Assert.That(m, Is.Not.Null);
        Assert.That(m!.Value.Score, Is.EqualTo(0));
        Assert.That(m.Value.Positions, Is.Empty);
    }

    /// <summary>Case-insensitive subsequence match.</summary>
    [Test]
    public void CaseInsensitive_NtMatchesNewTab()
    {
        var m = FuzzyMatcher.Score("nt", "New tab");
        Assert.That(m, Is.Not.Null);
        Assert.That(m!.Value.Positions, Is.EqualTo(new[] { 0, 4 }));
    }

    /// <summary>Subsequence with gaps still matches.</summary>
    [Test]
    public void Subsequence_Clscm_MatchesCloseColorScheme()
    {
        var m = FuzzyMatcher.Score("clscm", "Close color scheme");
        Assert.That(m, Is.Not.Null);
        Assert.That(m!.Value.Positions.Length, Is.EqualTo(5));
    }

    /// <summary>Non-matching query returns null.</summary>
    [Test]
    public void NoMatch_ReturnsNull()
    {
        var m = FuzzyMatcher.Score("xyz", "Close tab");
        Assert.That(m, Is.Null);
    }

    /// <summary>Prefix match beats non-prefix match.</summary>
    [Test]
    public void Prefix_BeatsNonPrefix()
    {
        var best = FuzzyMatcher.Score("new", "New tab");
        var worse = FuzzyMatcher.Score("new", "Rename window");
        Assert.That(best, Is.Not.Null);
        Assert.That(worse, Is.Not.Null);
        Assert.That(best!.Value.Score, Is.LessThan(worse!.Value.Score));
    }

    /// <summary>Word-start positions beat mid-word positions.</summary>
    [Test]
    public void WordStart_BeatsMidWord()
    {
        var best = FuzzyMatcher.Score("nt", "New tab");
        var worse = FuzzyMatcher.Score("nt", "Entrant");
        Assert.That(best, Is.Not.Null);
        Assert.That(worse, Is.Not.Null);
        Assert.That(best!.Value.Score, Is.LessThan(worse!.Value.Score));
    }

    /// <summary>Returned positions are monotonic and correct.</summary>
    [Test]
    public void Positions_MonotonicAndCorrect()
    {
        const string Candidate = "Jump to tab 3";
        var m = FuzzyMatcher.Score("jtt3", Candidate);
        Assert.That(m, Is.Not.Null);
        var pos = m!.Value.Positions;
        Assert.That(pos.Length, Is.EqualTo(4));
        for (int i = 1; i < pos.Length; i++)
        {
            Assert.That(pos[i], Is.GreaterThan(pos[i - 1]), "positions must strictly increase");
        }

        Assert.That(char.ToLowerInvariant(Candidate[pos[0]]), Is.EqualTo('j'));
        Assert.That(char.ToLowerInvariant(Candidate[pos[1]]), Is.EqualTo('t'));
        Assert.That(char.ToLowerInvariant(Candidate[pos[2]]), Is.EqualTo('t'));
        Assert.That(Candidate[pos[3]], Is.EqualTo('3'));
    }

    /// <summary>Non-ASCII characters (e.g. diaeresis) match as expected.</summary>
    [Test]
    public void Unicode_QueryMatchesCandidate()
    {
        var m = FuzzyMatcher.Score("ä", "käse");
        Assert.That(m, Is.Not.Null);
        Assert.That(m!.Value.Positions, Is.EqualTo(new[] { 1 }));

        // Case-insensitive on the same non-ASCII character.
        var m2 = FuzzyMatcher.Score("Ä", "käse");
        Assert.That(m2, Is.Not.Null);
        Assert.That(m2!.Value.Positions[0], Is.EqualTo(1));
    }

    /// <summary>Exact consecutive match beats one with gaps of equal candidate length budget.</summary>
    [Test]
    public void TighterMatch_BeatsLooser()
    {
        var tight = FuzzyMatcher.Score("new", "new tab");
        var loose = FuzzyMatcher.Score("new", "now entering winter");
        Assert.That(tight, Is.Not.Null);
        Assert.That(loose, Is.Not.Null);
        Assert.That(tight!.Value.Score, Is.LessThan(loose!.Value.Score));
    }
}
