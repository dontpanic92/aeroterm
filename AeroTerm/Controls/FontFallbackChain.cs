// <copyright file="FontFallbackChain.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using SkiaSharp;

/// <summary>
/// Manages an ordered list of typefaces and provides per-glyph font lookup
/// with caching. The chain is built from a list of font family names.
/// The OS font matcher is used as a last resort.
/// </summary>
public sealed class FontFallbackChain : IDisposable
{
    private readonly List<SKTypeface> chain = new List<SKTypeface>();
    private readonly Dictionary<TypefaceKey, SKTypeface> styledCache = new Dictionary<TypefaceKey, SKTypeface>();
    private readonly Dictionary<GlyphKey, SKTypeface> glyphCache = new Dictionary<GlyphKey, SKTypeface>();
    private readonly object syncLock = new object();
    private bool isDisposed;

    /// <summary>
    /// Gets the primary typeface (first entry in the chain), used for
    /// calculating <c>CharWidth</c> and <c>LineHeight</c>.
    /// </summary>
    public SKTypeface? PrimaryTypeface
    {
        get
        {
            lock (this.syncLock)
            {
                return this.chain.Count > 0 ? this.chain[0] : null;
            }
        }
    }

    /// <summary>
    /// Gets the family name of the primary typeface.
    /// </summary>
    public string PrimaryFontName => this.PrimaryTypeface?.FamilyName ?? string.Empty;

    /// <summary>
    /// Rebuilds the chain from an ordered list of font family names.
    /// Only fonts that are actually available on the system are included.
    /// </summary>
    /// <param name="fontNames">The ordered font family names.</param>
    public void Rebuild(IReadOnlyList<string> fontNames)
    {
        // Build the new chain outside the lock so that font loading
        // does not block the render thread.
        var newChain = new List<SKTypeface>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in fontNames)
        {
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
            {
                var typeface = CreateValidatedTypeface(name);
                if (typeface is not null)
                {
                    newChain.Add(typeface);
                }
            }
        }

        List<SKTypeface> oldChain;
        Dictionary<TypefaceKey, SKTypeface> oldStyled;
        Dictionary<GlyphKey, SKTypeface> oldGlyph;

        lock (this.syncLock)
        {
            oldChain = new List<SKTypeface>(this.chain);
            oldStyled = new Dictionary<TypefaceKey, SKTypeface>(this.styledCache);
            oldGlyph = new Dictionary<GlyphKey, SKTypeface>(this.glyphCache);

            this.chain.Clear();
            this.chain.AddRange(newChain);
            this.styledCache.Clear();
            this.glyphCache.Clear();
        }

        // Dispose old typefaces outside the lock.
        DisposeTypefaces(oldChain, oldStyled, oldGlyph);
    }

    /// <summary>
    /// Returns the typeface that should be used to render the given code point,
    /// searching the chain in priority order with styled-variant support.
    /// Results are cached per (weight, slant, codePoint) combination.
    /// </summary>
    /// <param name="codePoint">The Unicode code point to look up.</param>
    /// <param name="text">The string representation of the code point (for <c>ContainsGlyphs</c>).</param>
    /// <param name="weight">The desired font weight (normal or bold).</param>
    /// <param name="slant">The desired font slant (upright or italic).</param>
    /// <returns>The best-matching typeface, or the primary typeface if nothing matches.</returns>
    public SKTypeface GetTypefaceForGlyph(int codePoint, string text, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        lock (this.syncLock)
        {
            var key = new GlyphKey(weight, slant, codePoint);
            if (this.glyphCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            // Search through the chain for a typeface that contains the glyph.
            foreach (var baseTypeface in this.chain)
            {
                var styled = this.GetStyledVariantLocked(baseTypeface, weight, slant);
                if (styled.ContainsGlyphs(text))
                {
                    this.glyphCache[key] = styled;
                    return styled;
                }
            }

            // Last resort: ask the OS font matcher.
            var osMatch = SKFontManager.Default.MatchCharacter(codePoint);
            if (osMatch is not null)
            {
                this.glyphCache[key] = osMatch;
                return osMatch;
            }

            // Nothing found; return primary styled variant so rendering doesn't crash.
            var fallback = this.GetStyledTypefaceLocked(weight, slant);
            this.glyphCache[key] = fallback;
            return fallback;
        }
    }

    /// <summary>
    /// Returns a styled (bold/italic) variant of the primary typeface.
    /// </summary>
    /// <param name="weight">The desired font weight.</param>
    /// <param name="slant">The desired font slant.</param>
    /// <returns>The styled typeface.</returns>
    public SKTypeface GetStyledTypeface(SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        lock (this.syncLock)
        {
            return this.GetStyledTypefaceLocked(weight, slant);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!this.isDisposed)
        {
            lock (this.syncLock)
            {
                DisposeTypefaces(this.chain, this.styledCache, this.glyphCache);
                this.chain.Clear();
                this.styledCache.Clear();
                this.glyphCache.Clear();
            }

            this.isDisposed = true;
        }
    }

    private static void DisposeTypefaces(
        List<SKTypeface> chain,
        Dictionary<TypefaceKey, SKTypeface> styledCache,
        Dictionary<GlyphKey, SKTypeface> glyphCache)
    {
        var toDispose = new HashSet<SKTypeface>();

        foreach (var tf in chain)
        {
            toDispose.Add(tf);
        }

        foreach (var tf in styledCache.Values)
        {
            toDispose.Add(tf);
        }

        foreach (var tf in glyphCache.Values)
        {
            toDispose.Add(tf);
        }

        foreach (var tf in toDispose)
        {
            // SKTypeface.Default must not be disposed.
            if (tf != SKTypeface.Default)
            {
                tf.Dispose();
            }
        }
    }

    private static SKTypeface? CreateValidatedTypeface(string fontName)
    {
        var typeface = SKTypeface.FromFamilyName(fontName);
        if (typeface is not null
            && string.Equals(typeface.FamilyName, fontName, StringComparison.OrdinalIgnoreCase))
        {
            return typeface;
        }

        // SkiaSharp silently substituted another font; reject it.
        typeface?.Dispose();
        return null;
    }

    private SKTypeface GetStyledTypefaceLocked(SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        var primary = this.chain.Count > 0 ? this.chain[0] : null;
        if (primary is null)
        {
            return SKTypeface.Default;
        }

        return this.GetStyledVariantLocked(primary, weight, slant);
    }

    private SKTypeface GetStyledVariantLocked(SKTypeface baseTypeface, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        var key = new TypefaceKey(baseTypeface.FamilyName, weight, slant);
        if (this.styledCache.TryGetValue(key, out var styled))
        {
            return styled;
        }

        // For the normal/upright case, return the base typeface directly.
        if (weight == SKFontStyleWeight.Normal && slant == SKFontStyleSlant.Upright)
        {
            this.styledCache[key] = baseTypeface;
            return baseTypeface;
        }

        var variant = SKTypeface.FromFamilyName(
            baseTypeface.FamilyName, weight, SKFontStyleWidth.Normal, slant);
        styled = variant ?? baseTypeface;
        this.styledCache[key] = styled;
        return styled;
    }

    /// <summary>
    /// Cache key for styled font variants (bold/italic).
    /// </summary>
    private readonly struct TypefaceKey : IEquatable<TypefaceKey>
    {
        public TypefaceKey(string fontName, SKFontStyleWeight weight, SKFontStyleSlant slant)
        {
            this.FontName = fontName;
            this.Weight = weight;
            this.Slant = slant;
        }

        public string FontName { get; }

        public SKFontStyleWeight Weight { get; }

        public SKFontStyleSlant Slant { get; }

        public bool Equals(TypefaceKey other)
        {
            return string.Equals(this.FontName, other.FontName, StringComparison.Ordinal)
                && this.Weight == other.Weight
                && this.Slant == other.Slant;
        }

        public override bool Equals(object? obj)
        {
            return obj is TypefaceKey other && this.Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(this.FontName, this.Weight, this.Slant);
        }
    }

    /// <summary>
    /// Cache key for per-glyph typeface lookups.
    /// </summary>
    private readonly struct GlyphKey : IEquatable<GlyphKey>
    {
        public GlyphKey(SKFontStyleWeight weight, SKFontStyleSlant slant, int codePoint)
        {
            this.Weight = weight;
            this.Slant = slant;
            this.CodePoint = codePoint;
        }

        public SKFontStyleWeight Weight { get; }

        public SKFontStyleSlant Slant { get; }

        public int CodePoint { get; }

        public bool Equals(GlyphKey other)
        {
            return this.Weight == other.Weight
                && this.Slant == other.Slant
                && this.CodePoint == other.CodePoint;
        }

        public override bool Equals(object? obj)
        {
            return obj is GlyphKey other && this.Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(this.Weight, this.Slant, this.CodePoint);
        }
    }
}
