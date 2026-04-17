// <copyright file="AppSettingsTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.IO;
using System.Text.Json;
using AeroTerm.Services;
using NUnit.Framework;

/// <summary>
/// Tests that cover newly-introduced settings (see session 12) and their
/// backward-compatible defaults.
/// </summary>
[TestFixture]
public class AppSettingsTests
{
    /// <summary>
    /// Brand-new <see cref="AppSettings"/> instances default
    /// <see cref="AppSettings.ConfirmOnClose"/> to <c>true</c>.
    /// </summary>
    [Test]
    public void ConfirmOnClose_DefaultsToTrue()
    {
        var settings = new AppSettings();
        Assert.That(settings.ConfirmOnClose, Is.True);
    }

    /// <summary>
    /// <see cref="AppSettings.ConfirmOnClose"/> survives a save / reload
    /// round-trip through the source-generated JSON context (both the
    /// true and the explicitly-false case).
    /// </summary>
    [Test]
    public void ConfirmOnClose_RoundTripsThroughJson()
    {
        var ctx = AppSettingsJsonContext.Default.AppSettings;

        var a = new AppSettings { ConfirmOnClose = false };
        string jsonA = JsonSerializer.Serialize(a, ctx);
        var loadedA = JsonSerializer.Deserialize(jsonA, ctx);
        Assert.That(loadedA, Is.Not.Null);
        Assert.That(loadedA!.ConfirmOnClose, Is.False);

        var b = new AppSettings { ConfirmOnClose = true };
        string jsonB = JsonSerializer.Serialize(b, ctx);
        var loadedB = JsonSerializer.Deserialize(jsonB, ctx);
        Assert.That(loadedB, Is.Not.Null);
        Assert.That(loadedB!.ConfirmOnClose, Is.True);
    }

    /// <summary>
    /// Legacy settings files written before <see cref="AppSettings.ConfirmOnClose"/>
    /// existed deserialize cleanly and default the flag to <c>true</c>
    /// (the safer behavior) rather than <c>false</c>.
    /// </summary>
    [Test]
    public void ConfirmOnClose_MissingFromLegacyJson_DefaultsToTrue()
    {
        // Synthesize a legacy JSON blob that omits the new property entirely.
        const string LegacyJson =
            "{\n" +
            "  \"EnableBlurBehind\": true,\n" +
            "  \"BackgroundOpacity\": 0.75,\n" +
            "  \"WindowWidth\": 800,\n" +
            "  \"WindowHeight\": 600,\n" +
            "  \"FontFamily\": \"\",\n" +
            "  \"FontSize\": 11,\n" +
            "  \"ScrollbackLines\": 1000\n" +
            "}";

        var loaded = JsonSerializer.Deserialize(LegacyJson, AppSettingsJsonContext.Default.AppSettings);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.ConfirmOnClose, Is.True);
    }
}
