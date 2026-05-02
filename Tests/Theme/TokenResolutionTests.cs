// <copyright file="TokenResolutionTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests.Theme;

using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using NUnit.Framework;

/// <summary>
/// Headless resource-resolution coverage for AeroTerm theme tokens.
/// </summary>
[TestFixture]
public class TokenResolutionTests
{
    private static readonly string[] BrushKeys =
    [
        "SurfaceBackgroundBrush",
        "SurfaceLayer1Brush",
        "SurfaceLayer2Brush",
        "SurfaceOverlayBrush",
        "SurfaceScrimBrush",
        "StrokeDefaultBrush",
        "StrokeMutedBrush",
        "StrokeFocusBrush",
        "StrokeAccentBrush",
        "TextPrimaryBrush",
        "TextSecondaryBrush",
        "TextTertiaryBrush",
        "TextDisabledBrush",
        "TextOnAccentBrush",
        "TextLinkBrush",
        "AccentPrimaryBrush",
        "AccentSecondaryBrush",
        "AccentMutedBrush",
        "AccentPressedBrush",
        "AccentDisabledBrush",
        "ControlFillRestBrush",
        "ControlFillHoverBrush",
        "ControlFillPressedBrush",
        "ControlFillDisabledBrush",
        "ControlFillSubtleHoverBrush",
        "ControlFillSubtlePressedBrush",
        "ControlBorderRestBrush",
        "ControlBorderHoverBrush",
        "ControlBorderFocusBrush",
        "ControlBorderDisabledBrush",
        "SuccessFillBrush",
        "SuccessForegroundBrush",
        "WarningFillBrush",
        "WarningForegroundBrush",
        "DangerFillBrush",
        "DangerForegroundBrush",
        "InfoFillBrush",
        "InfoForegroundBrush",
        "SelectionFillBrush",
        "SelectionFillInactiveBrush",
        "SelectionForegroundBrush",
        "ListItemHoverBrush",
        "ListItemPressedBrush",
        "ListItemSelectedBrush",
        "ListItemSelectedHoverBrush",
        "TitleBarForegroundBrush",
        "TitleBarButtonHoverBrush",
        "TitleBarButtonPressedBrush",
        "TitleBarCloseHoverBrush",
        "TitleBarClosePressedBrush",
        "PaletteBackground",
        "PaletteForeground",
        "PaletteBorder",
        "PaletteSelection",
        "PaletteMuted",
        "SearchOverlayBackground",
        "SearchOverlayForeground",
        "SearchOverlayBorder",
        "SearchOverlayMuted",
        "SearchOverlayButtonHover",
        "SearchOverlayButtonPressed",
        "SearchOverlayToggleOn",
        "TabStripDividerBrush",
        "TabStripCloseHoverBrush",
        "TabStripActiveAccentBrush",
        "TabStripForegroundBrush",
        "TabStripMutedForegroundBrush",
        "AeroTermSplitButtonPartHoverBrush",
        "AeroTermSplitButtonPartPressedBrush",
        "AeroTermSplitButtonPartForegroundBrush",
        "AeroTermSplitButtonSeparatorBrush",
    ];

    /// <summary>
    /// Gets the light and dark theme variants tested by resource smoke tests.
    /// </summary>
    public static IEnumerable<TestCaseData> ThemeVariants
    {
        get
        {
            yield return new TestCaseData(ThemeVariant.Light).SetName("ThemeResourcesResolve_Light");
            yield return new TestCaseData(ThemeVariant.Dark).SetName("ThemeResourcesResolve_Dark");
        }
    }

    /// <summary>
    /// Verifies all brush tokens and compatibility keys resolve for a theme variant.
    /// </summary>
    /// <param name="variant">The requested theme variant.</param>
    [AvaloniaTest]
    [TestCaseSource(nameof(ThemeVariants))]
    public void ThemeResourcesResolve(ThemeVariant variant)
    {
        var window = new Window
        {
            RequestedThemeVariant = variant,
        };
        window.Show();

        foreach (var key in BrushKeys)
        {
            AssertResource(window, key, variant, typeof(IBrush));
        }

        AssertResource(window, "SystemAccentColor", variant, typeof(Color));
        AssertResource(window, "SystemAccentColorBrush", variant, typeof(IBrush));

        window.Close();
    }

    private static void AssertResource(Window window, string key, ThemeVariant variant, Type expectedType)
    {
        Assert.That(window.TryFindResource(key, variant, out var value), Is.True, $"Resource '{key}' should resolve.");
        Assert.That(value, Is.Not.Null, $"Resource '{key}' should not be null.");
        Assert.That(value, Is.AssignableTo(expectedType), $"Resource '{key}' should be a {expectedType.Name}.");
    }
}
