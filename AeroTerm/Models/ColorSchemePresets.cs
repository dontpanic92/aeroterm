// <copyright file="ColorSchemePresets.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Models;

/// <summary>
/// Provides built-in terminal color scheme presets.
/// </summary>
public static class ColorSchemePresets
{
    /// <summary>
    /// Gets the VS Code Dark+ color scheme.
    /// </summary>
    public static ColorScheme VsCodeDarkPlus { get; } = new(
        "VS Code Dark+",
        Foreground: 0xCCCCCC,
        Background: 0x1E1E1E,
        Palette: new[]
        {
            0x000000, 0xCD3131, 0x0DBC79, 0xE5E510, 0x2472C8, 0xBC3FBC, 0x11A8CD, 0xE5E5E5,
            0x666666, 0xF14C4C, 0x23D18B, 0xF5F543, 0x3B8EEA, 0xD670D6, 0x29B8DB, 0xE5E5E5,
        });

    /// <summary>
    /// Gets the Solarized Dark color scheme.
    /// </summary>
    public static ColorScheme SolarizedDark { get; } = new(
        "Solarized Dark",
        Foreground: 0x839496,
        Background: 0x002B36,
        Palette: new[]
        {
            0x073642, 0xDC322F, 0x859900, 0xB58900, 0x268BD2, 0xD33682, 0x2AA198, 0xEEE8D5,
            0x002B36, 0xCB4B16, 0x586E75, 0x657B83, 0x839496, 0x6C71C4, 0x93A1A1, 0xFDF6E3,
        });

    /// <summary>
    /// Gets the Solarized Light color scheme.
    /// </summary>
    public static ColorScheme SolarizedLight { get; } = new(
        "Solarized Light",
        Foreground: 0x657B83,
        Background: 0xFDF6E3,
        Palette: new[]
        {
            0x073642, 0xDC322F, 0x859900, 0xB58900, 0x268BD2, 0xD33682, 0x2AA198, 0xEEE8D5,
            0x002B36, 0xCB4B16, 0x586E75, 0x657B83, 0x839496, 0x6C71C4, 0x93A1A1, 0xFDF6E3,
        });

    /// <summary>
    /// Gets the Dracula color scheme.
    /// </summary>
    public static ColorScheme Dracula { get; } = new(
        "Dracula",
        Foreground: 0xF8F8F2,
        Background: 0x282A36,
        Palette: new[]
        {
            0x21222C, 0xFF5555, 0x50FA7B, 0xF1FA8C, 0xBD93F9, 0xFF79C6, 0x8BE9FD, 0xF8F8F2,
            0x6272A4, 0xFF6E6E, 0x69FF94, 0xFFFFA5, 0xD6ACFF, 0xFF92DF, 0xA4FFFF, 0xFFFFFF,
        });

    /// <summary>
    /// Gets the Monokai color scheme.
    /// </summary>
    public static ColorScheme Monokai { get; } = new(
        "Monokai",
        Foreground: 0xF8F8F2,
        Background: 0x272822,
        Palette: new[]
        {
            0x272822, 0xF92672, 0xA6E22E, 0xF4BF75, 0x66D9EF, 0xAE81FF, 0xA1EFE4, 0xF8F8F2,
            0x75715E, 0xF92672, 0xA6E22E, 0xF4BF75, 0x66D9EF, 0xAE81FF, 0xA1EFE4, 0xF9F8F5,
        });

    /// <summary>
    /// Gets the Gruvbox Dark color scheme.
    /// </summary>
    public static ColorScheme GruvboxDark { get; } = new(
        "Gruvbox Dark",
        Foreground: 0xEBDBB2,
        Background: 0x282828,
        Palette: new[]
        {
            0x282828, 0xCC241D, 0x98971A, 0xD79921, 0x458588, 0xB16286, 0x689D6A, 0xA89984,
            0x928374, 0xFB4934, 0xB8BB26, 0xFABD2F, 0x83A598, 0xD3869B, 0x8EC07C, 0xEBDBB2,
        });

    /// <summary>
    /// Gets the Nord color scheme.
    /// </summary>
    public static ColorScheme Nord { get; } = new(
        "Nord",
        Foreground: 0xD8DEE9,
        Background: 0x2E3440,
        Palette: new[]
        {
            0x3B4252, 0xBF616A, 0xA3BE8C, 0xEBCB8B, 0x81A1C1, 0xB48EAD, 0x88C0D0, 0xE5E9F0,
            0x4C566A, 0xBF616A, 0xA3BE8C, 0xEBCB8B, 0x81A1C1, 0xB48EAD, 0x8FBCBB, 0xECEFF4,
        });

    /// <summary>
    /// Gets the One Dark color scheme.
    /// </summary>
    public static ColorScheme OneDark { get; } = new(
        "One Dark",
        Foreground: 0xABB2BF,
        Background: 0x282C34,
        Palette: new[]
        {
            0x282C34, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xABB2BF,
            0x545862, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xFFFFFF,
        });

    /// <summary>
    /// Gets the Tokyo Night color scheme.
    /// </summary>
    public static ColorScheme TokyoNight { get; } = new(
        "Tokyo Night",
        Foreground: 0xA9B1D6,
        Background: 0x1A1B26,
        Palette: new[]
        {
            0x15161E, 0xF7768E, 0x9ECE6A, 0xE0AF68, 0x7AA2F7, 0xBB9AF7, 0x7DCFFF, 0xA9B1D6,
            0x414868, 0xF7768E, 0x9ECE6A, 0xE0AF68, 0x7AA2F7, 0xBB9AF7, 0x7DCFFF, 0xC0CAF5,
        });

    /// <summary>
    /// Gets the Catppuccin Mocha color scheme.
    /// </summary>
    public static ColorScheme CatppuccinMocha { get; } = new(
        "Catppuccin Mocha",
        Foreground: 0xCDD6F4,
        Background: 0x1E1E2E,
        Palette: new[]
        {
            0x45475A, 0xF38BA8, 0xA6E3A1, 0xF9E2AF, 0x89B4FA, 0xF5C2E7, 0x94E2D5, 0xBAC2DE,
            0x585B70, 0xF38BA8, 0xA6E3A1, 0xF9E2AF, 0x89B4FA, 0xF5C2E7, 0x94E2D5, 0xA6ADC8,
        });

    /// <summary>
    /// Gets the One Half Dark color scheme.
    /// </summary>
    public static ColorScheme OneHalfDark { get; } = new(
        "One Half Dark",
        Foreground: 0xDCDFE4,
        Background: 0x282C34,
        Palette: new[]
        {
            0x282C34, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xDCDFE4,
            0x5A6374, 0xE06C75, 0x98C379, 0xE5C07B, 0x61AFEF, 0xC678DD, 0x56B6C2, 0xFFFFFF,
        });

    /// <summary>
    /// Gets the One Half Light color scheme.
    /// </summary>
    public static ColorScheme OneHalfLight { get; } = new(
        "One Half Light",
        Foreground: 0x383A42,
        Background: 0xFAFAFA,
        Palette: new[]
        {
            0x383A42, 0xE45649, 0x50A14F, 0xC18301, 0x0184BC, 0xA626A4, 0x0997B3, 0xFAFAFA,
            0x4F525D, 0xDF6C75, 0x50A14F, 0xC18301, 0x0184BC, 0xA626A4, 0x0997B3, 0xFFFFFF,
        });

    /// <summary>
    /// Gets the High Contrast (Dark) color scheme.
    /// </summary>
    /// <remarks>
    /// Hand-tuned so that the default foreground achieves a WCAG AAA
    /// contrast ratio (≥7:1) against the background and every ANSI
    /// palette entry clears the WCAG AA bar (≥4.5:1). Modelled after
    /// the Windows High Contrast Black defaults.
    /// </remarks>
    public static ColorScheme HighContrastDark { get; } = new(
        "High Contrast (Dark)",
        Foreground: 0xFFFFFF,
        Background: 0x000000,
        Palette: new[]
        {
            0x767676, 0xFF6E6E, 0x3FF23F, 0xFFFF00, 0x5599FF, 0xFF55FF, 0x00FFFF, 0xFFFFFF,
            0xA6A6A6, 0xFF9999, 0x80FF80, 0xFFFF80, 0x80C0FF, 0xFF99FF, 0x80FFFF, 0xFFFFFF,
        });

    /// <summary>
    /// Gets the High Contrast (Light) color scheme.
    /// </summary>
    /// <remarks>
    /// Pure white background with pure black foreground (21:1). Each
    /// ANSI palette entry is dark enough to meet WCAG AA (≥4.5:1).
    /// </remarks>
    public static ColorScheme HighContrastLight { get; } = new(
        "High Contrast (Light)",
        Foreground: 0x000000,
        Background: 0xFFFFFF,
        Palette: new[]
        {
            0x000000, 0xCC0000, 0x006400, 0x707000, 0x0000CC, 0x990099, 0x006666, 0x595959,
            0x333333, 0xCC3333, 0x007700, 0x8B6914, 0x3333CC, 0xB300B3, 0x008080, 0x000000,
        });

    /// <summary>
    /// Gets all available color scheme presets.
    /// </summary>
    public static IReadOnlyList<ColorScheme> All { get; } = new[]
    {
        VsCodeDarkPlus,
        SolarizedDark,
        SolarizedLight,
        Dracula,
        Monokai,
        GruvboxDark,
        Nord,
        OneDark,
        TokyoNight,
        CatppuccinMocha,
        OneHalfDark,
        OneHalfLight,
        HighContrastDark,
        HighContrastLight,
    };

    /// <summary>
    /// Gets the default color scheme.
    /// </summary>
    public static ColorScheme Default => VsCodeDarkPlus;

    /// <summary>
    /// Finds a color scheme preset by its display name.
    /// </summary>
    /// <param name="name">The display name to search for.</param>
    /// <returns>The matching color scheme, or <c>null</c> if not found.</returns>
    public static ColorScheme? FindByName(string name) => All.FirstOrDefault(s => s.Name == name);
}
