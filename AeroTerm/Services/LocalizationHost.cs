// <copyright file="LocalizationHost.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Globalization;
using System.Resources;
using AeroTerm.Resources;

/// <summary>
/// Thin wrapper around <see cref="ResourceManager"/> that centralises all
/// UI-string lookups. Exposes a single <see cref="Culture"/> override so
/// unit tests can pin a specific locale without mutating the process-wide
/// <see cref="CultureInfo.CurrentUICulture"/>, and guarantees a
/// deterministic fallback to the English baseline when a satellite
/// resource is missing a key.
/// </summary>
public static class LocalizationHost
{
    private static CultureInfo? cultureOverride;

    /// <summary>
    /// Gets or sets the culture used for resource lookups. When
    /// <see langword="null"/> (the default), lookups track
    /// <see cref="CultureInfo.CurrentUICulture"/>. Setting this property
    /// is primarily intended for tests; production callers should prefer
    /// mutating <see cref="CultureInfo.CurrentUICulture"/> so the rest of
    /// the framework (formatting, Avalonia, etc.) stays consistent.
    /// </summary>
    public static CultureInfo? Culture
    {
        get => cultureOverride;
        set => cultureOverride = value;
    }

    /// <summary>
    /// Gets the effective culture used for the next lookup — either the
    /// explicit <see cref="Culture"/> override or
    /// <see cref="CultureInfo.CurrentUICulture"/> when no override is set.
    /// </summary>
    public static CultureInfo EffectiveCulture => cultureOverride ?? CultureInfo.CurrentUICulture;

    /// <summary>
    /// Looks up <paramref name="key"/> in the AeroTerm string table for
    /// the <see cref="EffectiveCulture"/>. If the key is missing in the
    /// requested culture's satellite assembly, <see cref="ResourceManager"/>
    /// automatically walks the parent-culture chain and falls back to the
    /// English baseline. If the key is missing everywhere, the key name
    /// itself is returned wrapped in <c>[brackets]</c> so a developer can
    /// spot the omission at a glance without the UI crashing.
    /// </summary>
    /// <param name="key">The resource key (see <see cref="Strings"/>).</param>
    /// <returns>The localised string, or <c>[key]</c> if unresolved.</returns>
    public static string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var resolved = Strings.ResourceManager.GetString(key, EffectiveCulture);
        return resolved ?? $"[{key}]";
    }

    /// <summary>
    /// Overload of <see cref="GetString(string)"/> that takes an explicit
    /// culture — used primarily by tests that need to probe a culture
    /// without mutating shared state.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="culture">The culture to look up in.</param>
    /// <returns>The localised string, or <c>[key]</c> if unresolved.</returns>
    public static string GetString(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(culture);
        var resolved = Strings.ResourceManager.GetString(key, culture);
        return resolved ?? $"[{key}]";
    }
}
