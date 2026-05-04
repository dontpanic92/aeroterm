// <copyright file="ColorSchemeTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.Models;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="ColorScheme"/>.
/// </summary>
[TestFixture]
public class ColorSchemeTests
{
    /// <summary>
    /// The display string for a color scheme is its user-visible name.
    /// </summary>
    [Test]
    public void ToString_ReturnsName()
    {
        var scheme = new ColorScheme(
            "Example",
            Foreground: 0xFFFFFF,
            Background: 0x000000,
            Palette: new int[ColorScheme.PaletteSize]);

        Assert.That(scheme.ToString(), Is.EqualTo("Example"));
    }
}
