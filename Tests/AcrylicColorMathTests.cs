// <copyright file="AcrylicColorMathTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm.WindowEffects;
using Avalonia.Media;
using NUnit.Framework;

/// <summary>
/// Tests for <see cref="AcrylicColorMath"/>, which Porter-Duff
/// composes Avalonia's two-layer acrylic model into a single
/// <see cref="SolidColorBrush"/> color/alpha.
/// </summary>
[TestFixture]
public class AcrylicColorMathTests
{
    private static readonly Color DarkTint = Color.FromRgb(0x1E, 0x1E, 0x1E);

    /// <summary>
    /// With both opacity dials at zero the brush is fully transparent so
    /// the OS backdrop shows through unmodified (no gray wash, no tint).
    /// </summary>
    [Test]
    public void Compose_BothOpacitiesZero_YieldsTransparent()
    {
        var (_, alpha) = AcrylicColorMath.Compose(DarkTint, 0.0, 0.0);
        Assert.That(alpha, Is.EqualTo(0f));
    }

    /// <summary>
    /// With only the material slider engaged the brush is a pure
    /// grayscale layer (no tint hue contribution) at full alpha.
    /// Avalonia's formula pulls the luminosity toward the tint's own
    /// lightness as material → 1 (and toward near-white as material → 0),
    /// so for a dark tint at material=1 the gray is dark, not bright —
    /// the assertion here is just "purely grayscale".
    /// </summary>
    [Test]
    public void Compose_TintZeroMaterialOne_IsPureGrayscaleAtFullAlpha()
    {
        var (color, alpha) = AcrylicColorMath.Compose(DarkTint, 0.0, 1.0);
        Assert.That(alpha, Is.EqualTo(1f).Within(1e-4));
        Assert.That(color.R, Is.EqualTo(color.G));
        Assert.That(color.G, Is.EqualTo(color.B));
    }

    /// <summary>
    /// Lowering MaterialOpacity should pull the gray toward near-white
    /// (Avalonia's <c>0.13 + L * 0.74</c> remap with the inverted
    /// <c>1 - (1 - L) * M</c> mixing). We pick a small but non-zero
    /// material so alpha remains comparable.
    /// </summary>
    [Test]
    public void Compose_LowMaterial_LightensGrayTowardWhite()
    {
        var (low, _) = AcrylicColorMath.Compose(DarkTint, 0.0, 0.1);
        var (high, _) = AcrylicColorMath.Compose(DarkTint, 0.0, 1.0);
        Assert.That(low.R, Is.GreaterThan(high.R));
        Assert.That(low.R, Is.GreaterThan(180));
    }

    /// <summary>
    /// With only the tint slider engaged the brush is the tint color
    /// at the modifier-suppressed alpha (≤ ~0.85 for very dark tints,
    /// not the full slider value).
    /// </summary>
    [Test]
    public void Compose_TintOneMaterialZero_IsTintColorWithSuppressedAlpha()
    {
        var (color, alpha) = AcrylicColorMath.Compose(DarkTint, 1.0, 0.0);
        Assert.That(color.R, Is.EqualTo(DarkTint.R));
        Assert.That(color.G, Is.EqualTo(DarkTint.G));
        Assert.That(color.B, Is.EqualTo(DarkTint.B));
        Assert.That(alpha, Is.LessThan(1f));
        Assert.That(alpha, Is.GreaterThan(0f));
    }

    /// <summary>
    /// The two sliders must not be commutative — that's the whole
    /// point of porting Avalonia's two-layer model. Swapping their
    /// values produces different output for typical inputs.
    /// </summary>
    [Test]
    public void Compose_IsNotCommutative()
    {
        var (colorA, alphaA) = AcrylicColorMath.Compose(DarkTint, 0.85, 0.25);
        var (colorB, alphaB) = AcrylicColorMath.Compose(DarkTint, 0.25, 0.85);

        bool sameColor = colorA.R == colorB.R && colorA.G == colorB.G && colorA.B == colorB.B;
        bool sameAlpha = System.Math.Abs(alphaA - alphaB) < 1e-3;
        Assert.That(sameColor && sameAlpha, Is.False, "Tint and Material sliders should produce visibly different results when swapped.");
    }

    /// <summary>
    /// The tint-opacity modifier should match Avalonia's published
    /// reference points: ~0.20 at pure white, ~0.85 at pure black,
    /// and ~0.45 at mid-gray.
    /// </summary>
    [Test]
    public void ComputeTintOpacityModifier_MatchesAvaloniaReferencePoints()
    {
        Assert.That(AcrylicColorMath.ComputeTintOpacityModifier(Colors.White), Is.EqualTo(0.20).Within(1e-6));
        Assert.That(AcrylicColorMath.ComputeTintOpacityModifier(Colors.Black), Is.EqualTo(0.85).Within(1e-6));

        var midGray = Color.FromRgb(127, 127, 127);
        Assert.That(AcrylicColorMath.ComputeTintOpacityModifier(midGray), Is.EqualTo(0.45).Within(0.005));
    }
}
