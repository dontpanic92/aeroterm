// <copyright file="FontPriorityListTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Utilities;
using NUnit.Framework;

/// <summary>
/// Tests unified terminal font priority resolution.
/// </summary>
[TestFixture]
public sealed class FontPriorityListTests
{
    /// <summary>
    /// The legacy primary font precedes configured fallbacks and expanded system fonts.
    /// </summary>
    [Test]
    public void Resolve_PrimaryAndFallbacks_PreservesPriority()
    {
        var resolved = FontPriorityList.Resolve(
            "Legacy Mono",
            new[] { "User Fallback", FontPriorityList.SystemMonoSentinel });

        Assert.That(resolved[0], Is.EqualTo("Legacy Mono"));
        Assert.That(resolved[1], Is.EqualTo("User Fallback"));
        Assert.That(
            resolved.Skip(2),
            Is.EqualTo(FontPriorityList.GetDefaultPlatformFonts()));
    }

    /// <summary>
    /// An empty configuration still resolves the platform monospace chain.
    /// </summary>
    [Test]
    public void Resolve_EmptyConfiguration_UsesSystemMonospace()
    {
        var resolved = FontPriorityList.Resolve(null, Array.Empty<string>());

        Assert.That(resolved, Is.EqualTo(FontPriorityList.GetDefaultPlatformFonts()));
    }
}
