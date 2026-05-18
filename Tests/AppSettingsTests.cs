// <copyright file="AppSettingsTests.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using System.IO;
using System.Text.Json;
using AeroTerm.Diagnostics;
using AeroTerm.Services;
using AeroTerm.WindowEffects;
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

        var migrated = AppSettings.MigrateLegacyJson(LegacyJson);
        var loaded = JsonSerializer.Deserialize(migrated, AppSettingsJsonContext.Default.AppSettings);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.ConfirmOnClose, Is.True);
    }

    /// <summary>
    /// Brand-new <see cref="AppSettings"/> instances default the split
    /// background opacity values to Avalonia-style acrylic defaults
    /// (tint 0.85, material 0.75).
    /// </summary>
    [Test]
    public void BackgroundOpacity_DefaultsToSplitAvaloniaAcrylicValues()
    {
        var settings = new AppSettings();
        Assert.That(settings.BackgroundTintOpacity, Is.EqualTo(0.85));
        Assert.That(settings.BackgroundMaterialOpacity, Is.EqualTo(0.75));
    }

    /// <summary>
    /// Legacy settings files that only carry the old single
    /// <c>BackgroundOpacity</c> key migrate to the split tint/material
    /// model so that the perceived effective alpha is preserved
    /// (<c>tint * material == legacy</c>).
    /// </summary>
    [Test]
    public void BackgroundOpacity_LegacyJsonMigratesPreservingEffectiveAlpha()
    {
        const string LegacyJson = "{ \"BackgroundOpacity\": 0.6 }";

        var migrated = AppSettings.MigrateLegacyJson(LegacyJson);
        var loaded = JsonSerializer.Deserialize(migrated, AppSettingsJsonContext.Default.AppSettings);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.BackgroundTintOpacity, Is.EqualTo(0.6));
        Assert.That(loaded.BackgroundMaterialOpacity, Is.EqualTo(1.0));
        Assert.That(loaded.BackgroundTintOpacity * loaded.BackgroundMaterialOpacity, Is.EqualTo(0.6));
    }

    /// <summary>
    /// When new keys are explicitly present in the JSON they take
    /// precedence over any lingering legacy <c>BackgroundOpacity</c>
    /// value (which is dropped during migration).
    /// </summary>
    [Test]
    public void BackgroundOpacity_NewKeysOverrideLegacyDuringMigration()
    {
        const string MixedJson =
            "{ \"BackgroundOpacity\": 0.6, " +
            "\"BackgroundTintOpacity\": 0.4, " +
            "\"BackgroundMaterialOpacity\": 0.5 }";

        var migrated = AppSettings.MigrateLegacyJson(MixedJson);
        var loaded = JsonSerializer.Deserialize(migrated, AppSettingsJsonContext.Default.AppSettings);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.BackgroundTintOpacity, Is.EqualTo(0.4));
        Assert.That(loaded.BackgroundMaterialOpacity, Is.EqualTo(0.5));
    }

    /// <summary>
    /// Brand-new <see cref="AppSettings"/> instances default
    /// <see cref="AppSettings.TabBarOrientation"/> to
    /// <see cref="TabBarOrientation.Horizontal"/> (the preserved
    /// pre-session-28 behaviour).
    /// </summary>
    [Test]
    public void TabBarOrientation_DefaultsToHorizontal()
    {
        var settings = new AppSettings();
        Assert.That(settings.TabBarOrientation, Is.EqualTo(TabBarOrientation.Horizontal));
    }

    /// <summary>
    /// <see cref="AppSettings.TabBarOrientation"/> survives a save /
    /// reload round-trip through the source-generated JSON context for
    /// both variants.
    /// </summary>
    [Test]
    public void TabBarOrientation_RoundTripsThroughJson()
    {
        var ctx = AppSettingsJsonContext.Default.AppSettings;

        var a = new AppSettings { TabBarOrientation = TabBarOrientation.Vertical };
        string jsonA = JsonSerializer.Serialize(a, ctx);
        var loadedA = JsonSerializer.Deserialize(jsonA, ctx);
        Assert.That(loadedA, Is.Not.Null);
        Assert.That(loadedA!.TabBarOrientation, Is.EqualTo(TabBarOrientation.Vertical));

        var b = new AppSettings { TabBarOrientation = TabBarOrientation.Horizontal };
        string jsonB = JsonSerializer.Serialize(b, ctx);
        var loadedB = JsonSerializer.Deserialize(jsonB, ctx);
        Assert.That(loadedB, Is.Not.Null);
        Assert.That(loadedB!.TabBarOrientation, Is.EqualTo(TabBarOrientation.Horizontal));
    }

    /// <summary>
    /// Legacy settings files written before
    /// <see cref="AppSettings.TabBarOrientation"/> existed deserialize
    /// cleanly and default the property to
    /// <see cref="TabBarOrientation.Horizontal"/>.
    /// </summary>
    [Test]
    public void TabBarOrientation_MissingFromLegacyJson_DefaultsToHorizontal()
    {
        const string LegacyJson =
            "{\n" +
            "  \"EnableBlurBehind\": true,\n" +
            "  \"FontSize\": 11\n" +
            "}";

        var loaded = JsonSerializer.Deserialize(LegacyJson, AppSettingsJsonContext.Default.AppSettings);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.TabBarOrientation, Is.EqualTo(TabBarOrientation.Horizontal));
    }

    /// <summary>
    /// Brand-new <see cref="AppSettings"/> instances keep the experimental
    /// Workbench disabled unless the user opts in.
    /// </summary>
    [Test]
    public void EnableWorkbench_DefaultsToFalse()
    {
        var settings = new AppSettings();
        Assert.That(settings.EnableWorkbench, Is.False);
    }

    /// <summary>
    /// <see cref="AppSettings.EnableWorkbench"/> survives a save / reload
    /// round-trip through the source-generated JSON context for both values.
    /// </summary>
    [Test]
    public void EnableWorkbench_RoundTripsThroughJson()
    {
        var ctx = AppSettingsJsonContext.Default.AppSettings;

        var a = new AppSettings { EnableWorkbench = true };
        string jsonA = JsonSerializer.Serialize(a, ctx);
        var loadedA = JsonSerializer.Deserialize(jsonA, ctx);
        Assert.That(loadedA, Is.Not.Null);
        Assert.That(loadedA!.EnableWorkbench, Is.True);

        var b = new AppSettings { EnableWorkbench = false };
        string jsonB = JsonSerializer.Serialize(b, ctx);
        var loadedB = JsonSerializer.Deserialize(jsonB, ctx);
        Assert.That(loadedB, Is.Not.Null);
        Assert.That(loadedB!.EnableWorkbench, Is.False);
    }

    /// <summary>
    /// Legacy settings files written before the Workbench experiment existed
    /// deserialize cleanly and keep the experiment disabled by default.
    /// </summary>
    [Test]
    public void EnableWorkbench_MissingFromLegacyJson_DefaultsToFalse()
    {
        const string LegacyJson =
            "{\n" +
            "  \"EnableBlurBehind\": true,\n" +
            "  \"FontSize\": 11\n" +
            "}";

        var loaded = JsonSerializer.Deserialize(LegacyJson, AppSettingsJsonContext.Default.AppSettings);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.EnableWorkbench, Is.False);
    }

    /// <summary>
    /// <see cref="AppSettings.BellAction"/> survives a JSON round-trip for
    /// every enum value — including the newly-introduced
    /// <see cref="BellAction.VisualAndAudio"/>.
    /// </summary>
    /// <param name="value">The bell-action enum value under test.</param>
    [Test]
    public void BellAction_RoundTripsThroughJson(
        [Values(
            BellAction.None,
            BellAction.Visual,
            BellAction.Audio,
            BellAction.Notification,
            BellAction.VisualAndAudio,
            BellAction.All)] BellAction value)
    {
        var ctx = AppSettingsJsonContext.Default.AppSettings;

        var settings = new AppSettings { BellAction = value };
        string json = JsonSerializer.Serialize(settings, ctx);
        var loaded = JsonSerializer.Deserialize(json, ctx);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.BellAction, Is.EqualTo(value));
    }

    /// <summary>
    /// Legacy settings files written before <see cref="AppSettings.BellAction"/>
    /// existed deserialize cleanly and default to
    /// <see cref="BellAction.Visual"/> (the existing default).
    /// </summary>
    [Test]
    public void BellAction_MissingFromLegacyJson_DefaultsToVisual()
    {
        const string LegacyJson =
            "{\n" +
            "  \"EnableBlurBehind\": true,\n" +
            "  \"FontSize\": 11\n" +
            "}";

        var loaded = JsonSerializer.Deserialize(LegacyJson, AppSettingsJsonContext.Default.AppSettings);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.BellAction, Is.EqualTo(BellAction.Visual));
    }

    /// <summary>
    /// Brand-new <see cref="AppSettings"/> instances default
    /// <see cref="AppSettings.ScrollbackLines"/> to <c>10000</c>.
    /// </summary>
    [Test]
    public void ScrollbackLines_DefaultsToTenThousand()
    {
        var settings = new AppSettings();
        Assert.That(settings.ScrollbackLines, Is.EqualTo(10_000));
    }

    /// <summary>
    /// <see cref="AppSettings.ScrollbackLines"/> survives a JSON round-trip
    /// across a representative set of values (zero, default, and the new
    /// 1,000,000-line maximum).
    /// </summary>
    /// <param name="value">Scrollback line count under test.</param>
    [Test]
    public void ScrollbackLines_RoundTripsThroughJson(
        [Values(0, 100, 10_000, 500_000, 1_000_000)] int value)
    {
        var ctx = AppSettingsJsonContext.Default.AppSettings;

        var settings = new AppSettings { ScrollbackLines = value };
        string json = JsonSerializer.Serialize(settings, ctx);
        var loaded = JsonSerializer.Deserialize(json, ctx);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.ScrollbackLines, Is.EqualTo(value));
    }

    /// <summary>
    /// Values outside <c>[0, 1_000_000]</c> are clamped to the valid range
    /// rather than stored as-is.
    /// </summary>
    [Test]
    public void ScrollbackLines_OutOfRangeValuesAreClamped()
    {
        var settings = new AppSettings { ScrollbackLines = -50 };
        Assert.That(settings.ScrollbackLines, Is.EqualTo(0));

        settings.ScrollbackLines = 5_000_000;
        Assert.That(settings.ScrollbackLines, Is.EqualTo(1_000_000));
    }

    /// <summary>
    /// Setting <see cref="AppSettings.ScrollbackLines"/> to a new value
    /// raises <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>
    /// — this is the contract <see cref="TerminalSessionCoordinator"/>
    /// relies on to live-update the terminal buffer.
    /// </summary>
    [Test]
    public void ScrollbackLines_Set_RaisesPropertyChanged()
    {
        var settings = new AppSettings { ScrollbackLines = 1000 };
        string? last = null;
        settings.PropertyChanged += (_, e) => last = e.PropertyName;

        settings.ScrollbackLines = 2000;

        Assert.That(last, Is.EqualTo(nameof(AppSettings.ScrollbackLines)));
    }

    /// <summary>
    /// Brand-new <see cref="AppSettings"/> instances default
    /// <see cref="AppSettings.MaterialTone"/> to
    /// <see cref="MaterialTone.Light"/> to preserve pre-feature
    /// behavior (where the OS picked the light tonal variant via
    /// Avalonia's <c>RequestedThemeVariant="Light"</c>).
    /// </summary>
    [Test]
    public void MaterialTone_DefaultsToLight()
    {
        var settings = new AppSettings();
        Assert.That(settings.MaterialTone, Is.EqualTo(MaterialTone.Light));
    }

    /// <summary>
    /// <see cref="AppSettings.MaterialTone"/> survives a save / reload
    /// round-trip through the source-generated JSON context for both
    /// variants.
    /// </summary>
    [Test]
    public void MaterialTone_RoundTripsThroughJson()
    {
        var ctx = AppSettingsJsonContext.Default.AppSettings;

        var dark = new AppSettings { MaterialTone = MaterialTone.Dark };
        var loadedDark = JsonSerializer.Deserialize(JsonSerializer.Serialize(dark, ctx), ctx);
        Assert.That(loadedDark, Is.Not.Null);
        Assert.That(loadedDark!.MaterialTone, Is.EqualTo(MaterialTone.Dark));

        var light = new AppSettings { MaterialTone = MaterialTone.Light };
        var loadedLight = JsonSerializer.Deserialize(JsonSerializer.Serialize(light, ctx), ctx);
        Assert.That(loadedLight, Is.Not.Null);
        Assert.That(loadedLight!.MaterialTone, Is.EqualTo(MaterialTone.Light));
    }

    /// <summary>
    /// Malformed persisted settings are logged and fall back to a default
    /// instance instead of preventing startup.
    /// </summary>
    [Test]
    public void Load_MalformedJson_LogsAndFallsBackToDefaults()
    {
        string settingsDirectory = CreateTemporaryDirectory();
        string logDirectory = CreateTemporaryDirectory();
        string logPath = string.Empty;

        try
        {
            Directory.CreateDirectory(settingsDirectory);
            File.WriteAllText(Path.Combine(settingsDirectory, "settings.json"), "{");

            AppLogger.Initialize(new FileLogger(logDirectory));
            AppSettings.SetStorageDirectoryForTesting(settingsDirectory);

            var settings = AppSettings.Default;
            logPath = AppLogger.LogFilePath ?? string.Empty;

            Assert.That(settings.FontSize, Is.EqualTo(11));
            Assert.That(settings.ConfirmOnClose, Is.True);
            Assert.That(settings.LastPersistenceError, Is.Not.Empty);
        }
        finally
        {
            AppLogger.Shutdown();
            AppSettings.ResetForTesting();
        }

        string log = File.ReadAllText(logPath);
        Assert.That(log, Does.Contain("Failed to load settings from"));
        Assert.That(log, Does.Contain("using default settings"));
        Assert.That(log, Does.Contain("settings.json"));

        Directory.Delete(settingsDirectory, recursive: true);
        Directory.Delete(logDirectory, recursive: true);
    }

    /// <summary>
    /// A JSON <c>null</c> settings file is treated as an invalid load result,
    /// logged, and replaced by default settings.
    /// </summary>
    [Test]
    public void Load_JsonNull_LogsAndFallsBackToDefaults()
    {
        string settingsDirectory = CreateTemporaryDirectory();
        string logDirectory = CreateTemporaryDirectory();
        string logPath = string.Empty;

        try
        {
            Directory.CreateDirectory(settingsDirectory);
            File.WriteAllText(Path.Combine(settingsDirectory, "settings.json"), "null");

            AppLogger.Initialize(new FileLogger(logDirectory));
            AppSettings.SetStorageDirectoryForTesting(settingsDirectory);

            var settings = AppSettings.Default;
            logPath = AppLogger.LogFilePath ?? string.Empty;

            Assert.That(settings.FontSize, Is.EqualTo(11));
            Assert.That(settings.ConfirmOnClose, Is.True);
            Assert.That(settings.LastPersistenceError, Is.EqualTo("Settings file did not contain an object."));
        }
        finally
        {
            AppLogger.Shutdown();
            AppSettings.ResetForTesting();
        }

        string log = File.ReadAllText(logPath);
        Assert.That(log, Does.Contain("Failed to load settings from"));
        Assert.That(log, Does.Contain("using default settings"));
        Assert.That(log, Does.Contain("Settings file did not contain an object."));

        Directory.Delete(settingsDirectory, recursive: true);
        Directory.Delete(logDirectory, recursive: true);
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "AeroTermTests", Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }
}
