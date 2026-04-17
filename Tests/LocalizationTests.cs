// <copyright file="LocalizationTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.Globalization;
using AeroTerm.Resources;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests covering the <see cref="LocalizationHost"/> / <see cref="Strings"/>
/// pipeline: the English baseline loads, the Spanish stub satellite
/// assembly is picked up, and missing satellite keys transparently fall
/// back to the English baseline.
/// </summary>
[TestFixture]
public class LocalizationTests
{
    /// <summary>
    /// Clear the <see cref="LocalizationHost.Culture"/> override between
    /// tests so one test can't silently leak state into another.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        LocalizationHost.Culture = null;
    }

    /// <summary>
    /// Under culture <c>en</c> (invariant baseline) keys resolve to the
    /// hand-authored English values from <c>Strings.resx</c>.
    /// </summary>
    [Test]
    public void ResourceManager_ReturnsEnglishForEnglishCulture()
    {
        var en = CultureInfo.GetCultureInfo("en");

        Assert.That(LocalizationHost.GetString("SettingsTitle", en), Is.EqualTo("Settings"));
        Assert.That(LocalizationHost.GetString("ButtonCancel", en), Is.EqualTo("Cancel"));
        Assert.That(LocalizationHost.GetString("ButtonClose", en), Is.EqualTo("Close"));
        Assert.That(LocalizationHost.GetString("AppTitle", en), Is.EqualTo("AeroTerm"));
    }

    /// <summary>
    /// Under culture <c>es</c> the three stubbed keys resolve via the
    /// satellite assembly (<c>es/aeroterm.resources.dll</c>) to their
    /// Spanish translations, proving satellite-assembly generation works
    /// end-to-end.
    /// </summary>
    [Test]
    public void ResourceManager_ReturnsSpanishForSpanishCulture()
    {
        var es = CultureInfo.GetCultureInfo("es");

        Assert.That(LocalizationHost.GetString("SettingsTitle", es), Is.EqualTo("Configuración"));
        Assert.That(LocalizationHost.GetString("ButtonCancel", es), Is.EqualTo("Cancelar"));
        Assert.That(LocalizationHost.GetString("ButtonClose", es), Is.EqualTo("Cerrar"));
    }

    /// <summary>
    /// Keys that are not translated in the Spanish stub fall back to the
    /// English baseline via ResourceManager's culture-parent chain — so
    /// partial translations don't produce blank UI.
    /// </summary>
    [Test]
    public void ResourceManager_FallsBackToEnglishWhenSatelliteMissingKey()
    {
        var es = CultureInfo.GetCultureInfo("es");

        // These keys exist in Strings.resx but NOT in Strings.es.resx.
        Assert.That(LocalizationHost.GetString("AppTitle", es), Is.EqualTo("AeroTerm"));
        Assert.That(LocalizationHost.GetString("ButtonOk", es), Is.EqualTo("OK"));
        Assert.That(LocalizationHost.GetString("PaletteNewTab", es), Is.EqualTo("New Tab"));
    }

    /// <summary>
    /// The <see cref="LocalizationHost.Culture"/> override takes
    /// precedence over the ambient <see cref="CultureInfo.CurrentUICulture"/>
    /// so tests can pin a locale without globally mutating thread state.
    /// </summary>
    [Test]
    public void LocalizationHost_CultureOverride_IsPreferredOverCurrentUICulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            LocalizationHost.Culture = CultureInfo.GetCultureInfo("es");

            Assert.That(LocalizationHost.GetString("ButtonCancel"), Is.EqualTo("Cancelar"));
            Assert.That(LocalizationHost.EffectiveCulture.Name, Is.EqualTo("es"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>
    /// Missing keys return a <c>[bracketed]</c> placeholder so UI doesn't
    /// crash on a typo; this is an explicit contract of
    /// <see cref="LocalizationHost.GetString(string)"/>.
    /// </summary>
    [Test]
    public void GetString_UnknownKey_ReturnsBracketedPlaceholder()
    {
        var result = LocalizationHost.GetString("ThisKeyDoesNotExist", CultureInfo.InvariantCulture);
        Assert.That(result, Is.EqualTo("[ThisKeyDoesNotExist]"));
    }

    /// <summary>
    /// The strongly-typed <see cref="Strings"/> accessor routes through
    /// <see cref="LocalizationHost"/>, so it also honours the culture
    /// override — the one-stop entry point that AXAML uses is guaranteed
    /// to localise.
    /// </summary>
    [Test]
    public void StronglyTypedAccessor_HonoursCultureOverride()
    {
        LocalizationHost.Culture = CultureInfo.GetCultureInfo("es");
        Assert.That(Strings.SettingsTitle, Is.EqualTo("Configuración"));

        LocalizationHost.Culture = CultureInfo.GetCultureInfo("en");
        Assert.That(Strings.SettingsTitle, Is.EqualTo("Settings"));
    }
}
