// <copyright file="GitDiffParserTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Linq;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests for the unified-diff to side-by-side model parser.
/// </summary>
[TestFixture]
public sealed class GitDiffParserTests
{
    /// <summary>
    /// Empty or null input yields no files.
    /// </summary>
    [Test]
    public void Parse_EmptyInput_ReturnsNoFiles()
    {
        Assert.That(GitDiffParser.Parse(null), Is.Empty);
        Assert.That(GitDiffParser.Parse(string.Empty), Is.Empty);
    }

    /// <summary>
    /// A simple modification aligns context, removed, and added rows with line numbers.
    /// </summary>
    [Test]
    public void Parse_SimpleModification_AlignsRowsAndNumbers()
    {
        var diff = string.Join(
            "\n",
            "diff --git a/file.txt b/file.txt",
            "index 1111111..2222222 100644",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,3 +1,3 @@",
            " context one",
            "-old line",
            "+new line",
            " context two");

        var files = GitDiffParser.Parse(diff);

        Assert.That(files, Has.Count.EqualTo(1));
        var file = files[0];
        Assert.That(file.Path, Is.EqualTo("file.txt"));
        Assert.That(file.IsBinary, Is.False);

        var rows = file.Rows;
        Assert.That(rows, Has.Count.EqualTo(4));

        Assert.That(rows[0].Kind, Is.EqualTo(GitDiffRowKind.Context));
        Assert.That(rows[0].OldLineNumber, Is.EqualTo(1));
        Assert.That(rows[0].NewLineNumber, Is.EqualTo(1));
        Assert.That(rows[0].OldText, Is.EqualTo("context one"));

        Assert.That(rows[1].Kind, Is.EqualTo(GitDiffRowKind.Removed));
        Assert.That(rows[1].OldLineNumber, Is.EqualTo(2));
        Assert.That(rows[1].OldText, Is.EqualTo("old line"));
        Assert.That(rows[1].NewLineNumber, Is.Null);
        Assert.That(rows[1].NewText, Is.Null);

        Assert.That(rows[2].Kind, Is.EqualTo(GitDiffRowKind.Added));
        Assert.That(rows[2].NewLineNumber, Is.EqualTo(2));
        Assert.That(rows[2].NewText, Is.EqualTo("new line"));
        Assert.That(rows[2].OldLineNumber, Is.Null);

        Assert.That(rows[3].Kind, Is.EqualTo(GitDiffRowKind.Context));
        Assert.That(rows[3].OldLineNumber, Is.EqualTo(3));
        Assert.That(rows[3].NewLineNumber, Is.EqualTo(3));
    }

    /// <summary>
    /// Additions-only hunks produce only added rows with new line numbers.
    /// </summary>
    [Test]
    public void Parse_AdditionsOnly_ProducesAddedRows()
    {
        var diff = string.Join(
            "\n",
            "diff --git a/new.txt b/new.txt",
            "new file mode 100644",
            "--- /dev/null",
            "+++ b/new.txt",
            "@@ -0,0 +1,2 @@",
            "+first",
            "+second");

        var rows = GitDiffParser.Parse(diff).Single().Rows;

        Assert.That(rows.Select(r => r.Kind), Is.All.EqualTo(GitDiffRowKind.Added));
        Assert.That(rows[0].NewLineNumber, Is.EqualTo(1));
        Assert.That(rows[1].NewLineNumber, Is.EqualTo(2));
        Assert.That(rows.All(r => r.OldLineNumber is null), Is.True);
    }

    /// <summary>
    /// Deletions-only hunks produce only removed rows with old line numbers.
    /// </summary>
    [Test]
    public void Parse_DeletionsOnly_ProducesRemovedRows()
    {
        var diff = string.Join(
            "\n",
            "diff --git a/gone.txt b/gone.txt",
            "deleted file mode 100644",
            "--- a/gone.txt",
            "+++ /dev/null",
            "@@ -1,2 +0,0 @@",
            "-alpha",
            "-beta");

        var rows = GitDiffParser.Parse(diff).Single().Rows;

        Assert.That(rows.Select(r => r.Kind), Is.All.EqualTo(GitDiffRowKind.Removed));
        Assert.That(rows[0].OldLineNumber, Is.EqualTo(1));
        Assert.That(rows[1].OldLineNumber, Is.EqualTo(2));
        Assert.That(rows.All(r => r.NewLineNumber is null), Is.True);
    }

    /// <summary>
    /// Multiple hunks reseed the old and new line counters from each header.
    /// </summary>
    [Test]
    public void Parse_MultipleHunks_ReseedsLineNumbers()
    {
        var diff = string.Join(
            "\n",
            "diff --git a/file.txt b/file.txt",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,1 +1,1 @@",
            "-a",
            "+A",
            "@@ -10,1 +10,1 @@",
            "-b",
            "+B");

        var rows = GitDiffParser.Parse(diff).Single().Rows;

        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows[0].OldLineNumber, Is.EqualTo(1));
        Assert.That(rows[2].OldLineNumber, Is.EqualTo(10));
        Assert.That(rows[3].NewLineNumber, Is.EqualTo(10));
    }

    /// <summary>
    /// Multiple files in one diff are split into separate models.
    /// </summary>
    [Test]
    public void Parse_MultipleFiles_SplitsByFile()
    {
        var diff = string.Join(
            "\n",
            "diff --git a/one.txt b/one.txt",
            "--- a/one.txt",
            "+++ b/one.txt",
            "@@ -1,1 +1,1 @@",
            "-1",
            "+one",
            "diff --git a/two.txt b/two.txt",
            "--- a/two.txt",
            "+++ b/two.txt",
            "@@ -1,1 +1,1 @@",
            "-2",
            "+two");

        var files = GitDiffParser.Parse(diff);

        Assert.That(files.Select(f => f.Path), Is.EqualTo(new[] { "one.txt", "two.txt" }));
        Assert.That(files[0].Rows, Has.Count.EqualTo(2));
        Assert.That(files[1].Rows, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// Binary files are flagged and emit no rows.
    /// </summary>
    [Test]
    public void Parse_BinaryFile_IsFlagged()
    {
        var diff = string.Join(
            "\n",
            "diff --git a/image.png b/image.png",
            "index 1111111..2222222 100644",
            "Binary files a/image.png and b/image.png differ");

        var file = GitDiffParser.Parse(diff).Single();

        Assert.That(file.IsBinary, Is.True);
        Assert.That(file.Rows, Is.Empty);
    }

    /// <summary>
    /// The no-newline marker is ignored and does not create a row.
    /// </summary>
    [Test]
    public void Parse_NoNewlineMarker_IsIgnored()
    {
        var diff = string.Join(
            "\n",
            "diff --git a/file.txt b/file.txt",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,1 +1,1 @@",
            "-old",
            "\\ No newline at end of file",
            "+new",
            "\\ No newline at end of file");

        var rows = GitDiffParser.Parse(diff).Single().Rows;

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].Kind, Is.EqualTo(GitDiffRowKind.Removed));
        Assert.That(rows[1].Kind, Is.EqualTo(GitDiffRowKind.Added));
    }

    /// <summary>
    /// Carriage returns in CRLF input are normalized away from row text.
    /// </summary>
    [Test]
    public void Parse_CrlfInput_NormalizesLineEndings()
    {
        var diff = string.Join(
            "\r\n",
            "diff --git a/file.txt b/file.txt",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,1 +1,1 @@",
            "-old",
            "+new");

        var rows = GitDiffParser.Parse(diff).Single().Rows;

        Assert.That(rows[0].OldText, Is.EqualTo("old"));
        Assert.That(rows[1].NewText, Is.EqualTo("new"));
    }
}
