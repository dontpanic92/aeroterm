// <copyright file="HyperlinkBehaviorTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Controls;
using AeroTerm.Pty;
using NUnit.Framework;

/// <summary>
/// Unit tests for <see cref="HyperlinkBehavior"/>.
/// </summary>
public class HyperlinkBehaviorTests
{
    /// <summary>
    /// Contiguous cells sharing a non-empty hyperlink id are grouped into a
    /// single run bounded by their columns.
    /// </summary>
    [Test]
    public void GetRunAt_ContiguousSameId_ReturnsFullRun()
    {
        var cells = MakeRow("click here now", linkStart: 0, linkEnd: 13, uri: "https://example.com", id: "L1");

        var run = HyperlinkBehavior.GetRunAt(cells, 0, 5);

        Assert.That(run, Is.Not.Null);
        Assert.That(run!.Value.Row, Is.EqualTo(0));
        Assert.That(run.Value.StartCol, Is.EqualTo(0));
        Assert.That(run.Value.EndCol, Is.EqualTo(13));
        Assert.That(run.Value.Uri, Is.EqualTo("https://example.com"));
        Assert.That(run.Value.Id, Is.EqualTo("L1"));
    }

    /// <summary>
    /// A differing hyperlink id terminates the run even when the URIs match,
    /// because two distinct ids denote two distinct logical hyperlinks.
    /// </summary>
    [Test]
    public void GetRunAt_DifferentId_BreaksRun()
    {
        // Two adjacent links with the same URI but different ids.
        var cells = new Cell[1, 10];
        for (int c = 0; c < 10; c++)
        {
            cells[0, c] = new Cell("x", default);
        }

        for (int c = 0; c < 4; c++)
        {
            cells[0, c].HyperlinkUri = "https://example.com";
            cells[0, c].HyperlinkId = "A";
        }

        for (int c = 4; c < 8; c++)
        {
            cells[0, c].HyperlinkUri = "https://example.com";
            cells[0, c].HyperlinkId = "B";
        }

        var run = HyperlinkBehavior.GetRunAt(cells, 0, 2);
        Assert.That(run, Is.Not.Null);
        Assert.That(run!.Value.StartCol, Is.EqualTo(0));
        Assert.That(run.Value.EndCol, Is.EqualTo(3));
        Assert.That(run.Value.Id, Is.EqualTo("A"));

        var run2 = HyperlinkBehavior.GetRunAt(cells, 0, 6);
        Assert.That(run2, Is.Not.Null);
        Assert.That(run2!.Value.StartCol, Is.EqualTo(4));
        Assert.That(run2.Value.EndCol, Is.EqualTo(7));
        Assert.That(run2.Value.Id, Is.EqualTo("B"));
    }

    /// <summary>
    /// When ids are empty, the run falls back to consecutive cells with an
    /// identical URI, stopping at a cell whose URI differs.
    /// </summary>
    [Test]
    public void GetRunAt_EmptyId_FallsBackToUriEquality()
    {
        var cells = new Cell[1, 10];
        for (int c = 0; c < 10; c++)
        {
            cells[0, c] = new Cell("x", default);
        }

        for (int c = 1; c < 5; c++)
        {
            cells[0, c].HyperlinkUri = "https://foo.test";
            cells[0, c].HyperlinkId = null;
        }

        for (int c = 5; c < 9; c++)
        {
            cells[0, c].HyperlinkUri = "https://bar.test";
            cells[0, c].HyperlinkId = null;
        }

        var run = HyperlinkBehavior.GetRunAt(cells, 0, 3);
        Assert.That(run, Is.Not.Null);
        Assert.That(run!.Value.StartCol, Is.EqualTo(1));
        Assert.That(run.Value.EndCol, Is.EqualTo(4));
        Assert.That(run.Value.Uri, Is.EqualTo("https://foo.test"));
    }

    /// <summary>
    /// A null grid, out-of-range coordinates, or a cell with no hyperlink
    /// all yield a null run without throwing.
    /// </summary>
    [Test]
    public void GetRunAt_NullOrOutOfRange_ReturnsNull()
    {
        Assert.That(HyperlinkBehavior.GetRunAt(null, 0, 0), Is.Null);

        var cells = MakeRow("hello", linkStart: 1, linkEnd: 3, uri: "https://example.com", id: "z");

        Assert.That(HyperlinkBehavior.GetRunAt(cells, -1, 0), Is.Null);
        Assert.That(HyperlinkBehavior.GetRunAt(cells, 0, -1), Is.Null);
        Assert.That(HyperlinkBehavior.GetRunAt(cells, 5, 0), Is.Null);
        Assert.That(HyperlinkBehavior.GetRunAt(cells, 0, 20), Is.Null);

        // Cell without hyperlink.
        Assert.That(HyperlinkBehavior.GetRunAt(cells, 0, 0), Is.Null);
    }

    /// <summary>
    /// Only web-like and mail/file schemes are allowed by
    /// <see cref="HyperlinkBehavior.IsAllowedUri"/>.
    /// </summary>
    /// <param name="uri">The candidate URI.</param>
    /// <param name="expected">Whether the URI should be accepted.</param>
    [TestCase("http://example.com", true)]
    [TestCase("https://example.com", true)]
    [TestCase("HTTPS://EXAMPLE.COM", true)]
    [TestCase("ftp://ftp.example.com/file", true)]
    [TestCase("mailto:user@example.com", true)]
    [TestCase("file:///etc/hosts", true)]
    [TestCase("javascript:alert(1)", false)]
    [TestCase("data:text/html,<b>x</b>", false)]
    [TestCase("vbscript:msgbox", false)]
    [TestCase("custom-app://open", false)]
    [TestCase("not a uri", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsAllowedUri_ValidatesScheme(string? uri, bool expected)
    {
        Assert.That(HyperlinkBehavior.IsAllowedUri(uri), Is.EqualTo(expected));
    }

    /// <summary>
    /// <see cref="HyperlinkBehavior.Activate"/> refuses to launch anything
    /// with a disallowed scheme, returning false and not throwing.
    /// </summary>
    [Test]
    public void Activate_DisallowedScheme_ReturnsFalse()
    {
        Assert.That(HyperlinkBehavior.Activate("javascript:alert(1)"), Is.False);
        Assert.That(HyperlinkBehavior.Activate(null), Is.False);
        Assert.That(HyperlinkBehavior.Activate(string.Empty), Is.False);
    }

    private static Cell[,] MakeRow(string text, int linkStart, int linkEnd, string uri, string? id)
    {
        var cells = new Cell[1, text.Length];
        for (int c = 0; c < text.Length; c++)
        {
            cells[0, c] = new Cell(text[c].ToString(), default);
        }

        for (int c = linkStart; c <= linkEnd && c < text.Length; c++)
        {
            cells[0, c].HyperlinkUri = uri;
            cells[0, c].HyperlinkId = id;
        }

        return cells;
    }
}
