// <copyright file="AcrylicColorMath.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

using System;
using Avalonia.Media;

/// <summary>
/// Pure functions that compute the color and alpha of a single
/// <see cref="SolidColorBrush"/> approximating Avalonia's two-layer
/// <see cref="ExperimentalAcrylicMaterial"/> compositing model when
/// painted over an OS-managed acrylic / blur backdrop.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia layers a luminosity-corrected gray "material" brush under
/// a colored "tint" brush; AeroTerm's main window cannot stack two
/// brushes (it uses OS-level acrylic with a single
/// <see cref="SolidColorBrush"/> on top), so we fold the two layers
/// into one by analytically Porter-Duff <c>over</c>-compositing them.
/// The result is a single ARGB color whose hue, lightness, and alpha
/// respond differently to the two opacity dials, matching Avalonia's
/// non-commutative semantics.
/// </para>
/// <para>
/// <b>Why port the math instead of using
/// <see cref="ExperimentalAcrylicMaterial"/> directly?</b> The
/// experimental material is awkward to drive with live preview from
/// the settings dialog — it interacts with Avalonia's render-thread
/// caching of effective tint/luminosity colors, with the platform
/// backdrop acquisition lifecycle, and with the
/// <c>WindowTransparencyLevel</c> hint we already manage ourselves.
/// Computing the composite color analytically and emitting a plain
/// <see cref="SolidColorBrush"/> lets Skia draw it as just another
/// solid fill: every slider tick yields an immediate, deterministic
/// repaint with no extra moving parts.
/// </para>
/// <para>
/// <b>Attribution.</b> Portions of this file — specifically the
/// <see cref="ComputeTintOpacityModifier"/> algorithm and the
/// luminosity-color lightness remap used inside <see cref="Compose"/>,
/// along with their constants — are derived from
/// <c>src/Avalonia.Base/Media/ExperimentalAcrylicMaterial.cs</c> in
/// AvaloniaUI/Avalonia (https://github.com/AvaloniaUI/Avalonia),
/// distributed under the MIT License. The required copyright and
/// permission notice is reproduced in <c>THIRD_PARTY_NOTICES.md</c>
/// at the repository root.
/// </para>
/// </remarks>
internal static class AcrylicColorMath
{
    /// <summary>
    /// Composes the tint and luminosity layers into a single
    /// <see cref="SolidColorBrush"/>-friendly <see cref="Color"/>.
    /// </summary>
    /// <param name="tintColor">The user-chosen background color (RGB; alpha is ignored).</param>
    /// <param name="tintOpacity">The tint slider value in <c>[0, 1]</c>.</param>
    /// <param name="materialOpacity">The material slider value in <c>[0, 1]</c>.</param>
    /// <returns>
    /// The composited color (RGB premultiplied-divided back) and its
    /// effective alpha in <c>[0, 1]</c>. When both sliders are zero
    /// the alpha is zero, so the OS backdrop shows through unmodified.
    /// </returns>
    public static (Color Color, float Alpha) Compose(Color tintColor, double tintOpacity, double materialOpacity)
    {
        double tintMod = ComputeTintOpacityModifier(tintColor);
        double tintA = Clamp01(tintOpacity * tintMod);

        // Luminosity (gray) layer — lightness curve and 0.13/0.74 remap
        // ported from Avalonia's ExperimentalAcrylicMaterial.GetLuminosityColor
        // (MIT; see THIRD_PARTY_NOTICES.md).
        double maxC = Math.Max(tintColor.R, Math.Max(tintColor.G, tintColor.B)) / 255.0;
        double minC = Math.Min(tintColor.R, Math.Min(tintColor.G, tintColor.B)) / 255.0;
        double lightness = (maxC + minC) / 2.0;
        lightness = 1.0 - ((1.0 - lightness) * Clamp01(materialOpacity));
        lightness = 0.13 + (lightness * 0.74);
        byte gray = ToByte(lightness);
        double lumA = Clamp01(materialOpacity);

        // Porter-Duff "over": tint over luminosity over backdrop.
        double finalA = lumA + (tintA * (1.0 - lumA));
        if (finalA <= 1e-6)
        {
            return (Color.FromArgb(0, 0, 0, 0), 0f);
        }

        double tintWeight = tintA * (1.0 - lumA);
        double r = ((gray * lumA) + (tintColor.R * tintWeight)) / finalA;
        double g = ((gray * lumA) + (tintColor.G * tintWeight)) / finalA;
        double b = ((gray * lumA) + (tintColor.B * tintWeight)) / finalA;

        return (
            Color.FromRgb(ClampToByte(r), ClampToByte(g), ClampToByte(b)),
            (float)finalA);
    }

    /// <summary>
    /// Computes the saturation- and luminosity-aware compression factor
    /// applied to <c>TintOpacity</c>. Mirrors Avalonia's
    /// <c>GetTintOpacityModifier</c> so a user-set slider of <c>1.0</c>
    /// maps to ~0.20 alpha for pure white, ~0.85 for pure black, and
    /// ~0.45 for mid-gray, with the suppression cancelled out as the
    /// tint becomes more saturated.
    /// </summary>
    /// <remarks>
    /// Ported from
    /// <c>src/Avalonia.Base/Media/ExperimentalAcrylicMaterial.cs</c> in
    /// AvaloniaUI/Avalonia (MIT). See <c>THIRD_PARTY_NOTICES.md</c>.
    /// </remarks>
    /// <param name="tintColor">The tint color (alpha is ignored).</param>
    /// <returns>The opacity modifier in <c>[0, 1]</c>.</returns>
    public static double ComputeTintOpacityModifier(Color tintColor)
    {
        const double midPoint = 0.5;
        const double whiteMaxOpacity = 0.2;
        const double midPointMaxOpacity = 0.45;
        const double blackMaxOpacity = 0.85;

        double maxC = Math.Max(tintColor.R, Math.Max(tintColor.G, tintColor.B)) / 255.0;
        double minC = Math.Min(tintColor.R, Math.Min(tintColor.G, tintColor.B)) / 255.0;
        double v = maxC;
        double s = maxC <= 0 ? 0 : (maxC - minC) / maxC;

        if (Math.Abs(v - midPoint) < 1e-9)
        {
            return midPointMaxOpacity;
        }

        double lowestMaxOpacity;
        double maxDeviation;
        if (v > midPoint)
        {
            lowestMaxOpacity = whiteMaxOpacity;
            maxDeviation = 1.0 - midPoint;
        }
        else
        {
            lowestMaxOpacity = blackMaxOpacity;
            maxDeviation = midPoint;
        }

        double maxOpacitySuppression = midPointMaxOpacity - lowestMaxOpacity;
        double normalizedDeviation = Math.Abs(v - midPoint) / maxDeviation;

        if (s > 0)
        {
            maxOpacitySuppression *= Math.Max(1.0 - (s * 2.0), 0.0);
        }

        return midPointMaxOpacity - (maxOpacitySuppression * normalizedDeviation);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

    private static byte ToByte(double normalized)
    {
        double scaled = Math.Floor(normalized * 256.0);
        return (byte)Math.Clamp(scaled, 0.0, 255.0);
    }

    private static byte ClampToByte(double value) => (byte)Math.Clamp(value, 0.0, 255.0);
}
