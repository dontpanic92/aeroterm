// <copyright file="Cell.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// One cell in the editor screen grid.
/// </summary>
public struct Cell
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Cell"/> struct.
    /// </summary>
    /// <param name="character">The character in the cell.</param>
    /// <param name="style">The visual attributes.</param>
    public Cell(string? character, CellStyle style)
    {
        this.Set(character, style);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Cell"/> struct.
    /// </summary>
    /// <param name="character">The character in the cell.</param>
    /// <param name="foreground">Foreground color.</param>
    /// <param name="background">Background color.</param>
    /// <param name="special">Special color.</param>
    /// <param name="reverse">IsReverse.</param>
    /// <param name="italic">IsItalic.</param>
    /// <param name="bold">IsBold.</param>
    /// <param name="underline">IsUnderline.</param>
    /// <param name="undercurl">IsUnderCurl.</param>
    [Obsolete("Use Cell(string?, CellStyle) constructor instead.")]
    public Cell(string? character, int foreground, int background, int special, bool reverse, bool italic, bool bold, bool underline, bool undercurl)
    {
        this.ForegroundColor = foreground;
        this.BackgroundColor = background;
        this.SpecialColor = special;
        this.Reverse = reverse;
        this.Italic = italic;
        this.Bold = bold;
        this.Underline = underline;
        this.Undercurl = undercurl;
        this.Dim = false;
        this.Strikethrough = false;
        this.Hidden = false;
        this.Blink = false;
        this.Overline = false;
        this.Character = character;
    }

    /// <summary>
    /// Gets the logical foreground color. This is no longer a baked RGB:
    /// it is encoded per <see cref="ColorRef"/> and may be an RGB literal,
    /// a palette-index reference, or the
    /// <see cref="ColorRef.DefaultFg"/> sentinel. Call
    /// <see cref="ResolveForeground"/> to obtain a paintable RGB.
    /// </summary>
    public int ForegroundColor { get; private set; }

    /// <summary>
    /// Gets the logical background color. Encoded per <see cref="ColorRef"/>;
    /// see <see cref="ForegroundColor"/> for details.
    /// </summary>
    public int BackgroundColor { get; private set; }

    /// <summary>
    /// Gets the special / underline color (RGB). Special colors are not
    /// palette-tracked and are unaffected by color-scheme changes.
    /// </summary>
    public int SpecialColor { get; private set; }

    /// <summary>
    /// Gets the character in the cell.
    /// </summary>
    public string? Character { get; private set; }

    /// <summary>
    /// Gets a value indicating whether foreground color and background color need to reverse.
    /// </summary>
    public bool Reverse { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the text is italic.
    /// </summary>
    public bool Italic { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the text is bold.
    /// </summary>
    public bool Bold { get; private set; }

    /// <summary>
    /// Gets a value indicating whether Underline is needed.
    /// </summary>
    public bool Underline { get; private set; }

    /// <summary>
    /// Gets a value indicating whether Undercurl is needed.
    /// </summary>
    public bool Undercurl { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the text has a double underline
    /// (SGR 21 or SGR 4:2). Rendered distinctly from a single underline.
    /// </summary>
    public bool DoubleUnderline { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the text is dim (faint).
    /// </summary>
    public bool Dim { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the text has strikethrough.
    /// </summary>
    public bool Strikethrough { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the text is hidden (concealed).
    /// </summary>
    public bool Hidden { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the text is blinking.
    /// </summary>
    public bool Blink { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the text has an overline.
    /// </summary>
    public bool Overline { get; internal set; }

    /// <summary>
    /// Gets the effective underline decoration style for this cell, derived
    /// from the <see cref="Underline"/>, <see cref="DoubleUnderline"/>, and
    /// <see cref="Undercurl"/> flags. Only one style is active at a time;
    /// curly takes precedence over double, which takes precedence over single.
    /// </summary>
    public UnderlineStyle UnderlineStyle
    {
        get
        {
            if (this.Undercurl)
            {
                return UnderlineStyle.Curly;
            }

            if (this.DoubleUnderline)
            {
                return UnderlineStyle.Double;
            }

            if (this.Underline)
            {
                return UnderlineStyle.Single;
            }

            return UnderlineStyle.None;
        }
    }

    /// <summary>
    /// Gets the OSC 8 hyperlink URI associated with this cell, or <see langword="null"/>
    /// if the cell is not part of a hyperlink.
    /// </summary>
    public string? HyperlinkUri { get; internal set; }

    /// <summary>
    /// Gets the optional OSC 8 hyperlink identifier associated with this cell. Cells
    /// sharing the same non-null id are considered the same logical hyperlink even
    /// when they are not contiguous (e.g. across line wraps).
    /// </summary>
    public string? HyperlinkId { get; internal set; }

    /// <summary>
    /// Set cell properties from a <see cref="CellStyle"/>.
    /// </summary>
    /// <param name="character">The character in the cell.</param>
    /// <param name="style">The visual attributes.</param>
    public void Set(string? character, CellStyle style)
    {
        this.ForegroundColor = style.ForegroundColor;
        this.BackgroundColor = style.BackgroundColor;
        this.SpecialColor = style.SpecialColor;
        this.Reverse = style.Reverse;
        this.Italic = style.Italic;
        this.Bold = style.Bold;
        this.Underline = style.Underline;
        this.Undercurl = style.Undercurl;
        this.Dim = false;
        this.Strikethrough = false;
        this.Hidden = false;
        this.Blink = false;
        this.Overline = false;
        this.Character = character;
    }

    /// <summary>
    /// Set cell properties.
    /// </summary>
    /// <param name="character">The character in the cell.</param>
    /// <param name="foreground">Foreground color.</param>
    /// <param name="background">Background color.</param>
    /// <param name="special">Special color.</param>
    /// <param name="reverse">IsReverse.</param>
    /// <param name="italic">IsItalic.</param>
    /// <param name="bold">IsBold.</param>
    /// <param name="underline">IsUnderline.</param>
    /// <param name="undercurl">IsUnderCurl.</param>
    [Obsolete("Use Set(string?, CellStyle) overload instead.")]
    public void Set(string? character, int foreground, int background, int special, bool reverse, bool italic, bool bold, bool underline, bool undercurl)
    {
        this.ForegroundColor = foreground;
        this.BackgroundColor = background;
        this.SpecialColor = special;
        this.Reverse = reverse;
        this.Italic = italic;
        this.Bold = bold;
        this.Underline = underline;
        this.Undercurl = undercurl;
        this.Dim = false;
        this.Strikethrough = false;
        this.Hidden = false;
        this.Blink = false;
        this.Overline = false;
        this.Character = character;
    }

    /// <summary>
    /// Clear the cell.
    /// </summary>
    /// <param name="foreground">foreground color.</param>
    /// <param name="background">background color.</param>
    /// <param name="special">special color.</param>
    public void Clear(int foreground, int background, int special)
    {
        this.Set(" ", new CellStyle(foreground, background, special, false, false, false, false, false));
    }

    /// <summary>
    /// Resolves <see cref="ForegroundColor"/> to a paintable RGB using
    /// the supplied per-frame <paramref name="palette"/>.
    /// </summary>
    /// <param name="palette">A palette snapshot from <see cref="Screen.Palette"/>.</param>
    /// <returns>The resolved 24-bit RGB.</returns>
    public int ResolveForeground(in PaletteSnapshot palette) =>
        palette.ResolveForeground(this.ForegroundColor);

    /// <summary>
    /// Resolves <see cref="BackgroundColor"/> to a paintable RGB using
    /// the supplied per-frame <paramref name="palette"/>.
    /// </summary>
    /// <param name="palette">A palette snapshot from <see cref="Screen.Palette"/>.</param>
    /// <returns>The resolved 24-bit RGB.</returns>
    public int ResolveBackground(in PaletteSnapshot palette) =>
        palette.ResolveBackground(this.BackgroundColor);
}
