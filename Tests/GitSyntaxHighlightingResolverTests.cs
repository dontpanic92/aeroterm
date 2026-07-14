// <copyright file="GitSyntaxHighlightingResolverTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests Git diff syntax-highlighting resolution.
/// </summary>
[TestFixture]
public sealed class GitSyntaxHighlightingResolverTests
{
    /// <summary>
    /// Built-in extensions and compatible aliases resolve to the expected definitions.
    /// </summary>
    /// <param name="path">The file path to resolve.</param>
    /// <param name="expectedName">The expected highlighting definition name.</param>
    [TestCase("Source.cs", "C#")]
    [TestCase("component.TSX", "JavaScript")]
    [TestCase("Directory.Build.props", "XML")]
    [TestCase("view.axaml", "XML")]
    [TestCase("schema.jsonc", "Json")]
    [TestCase(".eslintrc", "Json")]
    [TestCase("query.ddl", "TSQL")]
    public void Resolve_KnownPath_ReturnsExpectedDefinition(string path, string expectedName)
    {
        var definition = GitSyntaxHighlightingResolver.Resolve(path);

        Assert.That(definition, Is.Not.Null);
        Assert.That(definition!.Name, Is.EqualTo(expectedName));
    }

    /// <summary>
    /// Markdown aliases remain plain text.
    /// </summary>
    /// <param name="path">The Markdown path to resolve.</param>
    [TestCase("README.md")]
    [TestCase("guide.MARKDOWN")]
    [TestCase("notes.mkd")]
    public void Resolve_MarkdownPath_ReturnsNull(string path)
    {
        Assert.That(GitSyntaxHighlightingResolver.Resolve(path), Is.Null);
    }

    /// <summary>
    /// Unsupported languages remain plain text.
    /// </summary>
    [Test]
    public void Resolve_UnsupportedPath_ReturnsNull()
    {
        Assert.That(GitSyntaxHighlightingResolver.Resolve("main.rs"), Is.Null);
    }
}
