// <copyright file="AccessibilityTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System;
using System.Linq;
using AeroTerm.Models;
using NUnit.Framework;

/// <summary>
/// Validates the WCAG contrast characteristics of the built-in
/// high-contrast color schemes.
/// </summary>
[TestFixture]
public class AccessibilityTests
{
    private const double AaaRatio = 7.0;
    private const double AaRatio = 4.5;

    /// <summary>
    /// The <see cref="ColorSchemePresets.All"/> list must contain both
    /// named high-contrast variants.
    /// </summary>
    [Test]
    public void AllContainsHighContrastDark()
    {
        Assert.That(
            ColorSchemePresets.All.Any(s => s.Name == "High Contrast (Dark)"),
            Is.True,
            "'High Contrast (Dark)' preset is missing from ColorSchemePresets.All");
    }

    /// <summary>
    /// See <see cref="AllContainsHighContrastDark"/>.
    /// </summary>
    [Test]
    public void AllContainsHighContrastLight()
    {
        Assert.That(
            ColorSchemePresets.All.Any(s => s.Name == "High Contrast (Light)"),
            Is.True,
            "'High Contrast (Light)' preset is missing from ColorSchemePresets.All");
    }

    /// <summary>
    /// <see cref="ColorSchemePresets.FindByName(string)"/> must round-trip
    /// both HC scheme names.
    /// </summary>
    [Test]
    public void HighContrastSchemesDiscoverableByName()
    {
        Assert.That(ColorSchemePresets.FindByName("High Contrast (Dark)"), Is.Not.Null);
        Assert.That(ColorSchemePresets.FindByName("High Contrast (Light)"), Is.Not.Null);
    }

    /// <summary>
    /// WCAG AAA text contrast (≥7:1) between default foreground and
    /// background on HC dark.
    /// </summary>
    [Test]
    public void HighContrastDarkForegroundMeetsAaaContrast()
    {
        var scheme = ColorSchemePresets.HighContrastDark;
        double ratio = ContrastRatio(scheme.Foreground, scheme.Background);
        Assert.That(ratio, Is.GreaterThanOrEqualTo(AaaRatio), $"fg/bg contrast was {ratio:F2}");
    }

    /// <summary>
    /// See <see cref="HighContrastDarkForegroundMeetsAaaContrast"/>.
    /// </summary>
    [Test]
    public void HighContrastLightForegroundMeetsAaaContrast()
    {
        var scheme = ColorSchemePresets.HighContrastLight;
        double ratio = ContrastRatio(scheme.Foreground, scheme.Background);
        Assert.That(ratio, Is.GreaterThanOrEqualTo(AaaRatio), $"fg/bg contrast was {ratio:F2}");
    }

    /// <summary>
    /// Each of the 16 ANSI palette entries on HC dark must clear the
    /// WCAG AA 4.5:1 bar against the scheme's background.
    /// </summary>
    [Test]
    public void HighContrastDarkPaletteMeetsAaContrast()
    {
        AssertPaletteMeetsContrast(ColorSchemePresets.HighContrastDark, AaRatio);
    }

    /// <summary>
    /// See <see cref="HighContrastDarkPaletteMeetsAaContrast"/>.
    /// </summary>
    [Test]
    public void HighContrastLightPaletteMeetsAaContrast()
    {
        AssertPaletteMeetsContrast(ColorSchemePresets.HighContrastLight, AaRatio);
    }

    /// <summary>
    /// Both HC schemes must have exactly 16 palette entries (the contract
    /// of <see cref="ColorScheme.PaletteSize"/>).
    /// </summary>
    [Test]
    public void HighContrastSchemesHaveFullPalette()
    {
        Assert.That(ColorSchemePresets.HighContrastDark.Palette.Length, Is.EqualTo(ColorScheme.PaletteSize));
        Assert.That(ColorSchemePresets.HighContrastLight.Palette.Length, Is.EqualTo(ColorScheme.PaletteSize));
    }

    /// <summary>
    /// Sanity-check the contrast calculator: pure black vs pure white must
    /// land at the canonical 21:1 ratio.
    /// </summary>
    [Test]
    public void ContrastRatioBlackWhiteIs21()
    {
        double r = ContrastRatio(0x000000, 0xFFFFFF);
        Assert.That(r, Is.EqualTo(21.0).Within(0.001));
    }

    /// <summary>
    /// The two HC schemes must be visibly distinct (different bg colors).
    /// </summary>
    [Test]
    public void HighContrastSchemesAreDistinct()
    {
        Assert.That(
            ColorSchemePresets.HighContrastDark.Background,
            Is.Not.EqualTo(ColorSchemePresets.HighContrastLight.Background));
    }

    private static void AssertPaletteMeetsContrast(ColorScheme scheme, double minRatio)
    {
        for (int i = 0; i < scheme.Palette.Length; i++)
        {
            double ratio = ContrastRatio(scheme.Palette[i], scheme.Background);
            Assert.That(
                ratio,
                Is.GreaterThanOrEqualTo(minRatio),
                $"ANSI index {i} (#{scheme.Palette[i]:X6}) contrast was {ratio:F2} vs bg #{scheme.Background:X6} in '{scheme.Name}'");
        }
    }

    private static double ContrastRatio(int fgRgb, int bgRgb)
    {
        double l1 = RelativeLuminance(fgRgb);
        double l2 = RelativeLuminance(bgRgb);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(int rgb)
    {
        double r = SrgbToLinear(((rgb >> 16) & 0xFF) / 255.0);
        double g = SrgbToLinear(((rgb >> 8) & 0xFF) / 255.0);
        double b = SrgbToLinear((rgb & 0xFF) / 255.0);
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static double SrgbToLinear(double c)
    {
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
