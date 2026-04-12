// <copyright file="AppSettings.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AeroTerm.Diagnostics;
using AeroTerm.WindowEffects;
using Microsoft.Extensions.Logging;

/// <summary>
/// Application settings with JSON persistence.
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged, IWindowEffectsSettings, IWindowGeometrySettings
{
    private static readonly string DefaultSettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AeroTerm");

    private static readonly object DefaultInstanceLock = new();

    private static string? settingsDirectoryOverride;

    private static Lazy<AppSettings> defaultInstance = CreateDefaultInstance();

    private bool enableBlurBehind = true;
    private BlurType blurType = BlurType.Acrylic;
    private double backgroundOpacity = 0.75;
    private int windowWidth = 800;
    private int windowHeight = 600;
    private bool isMaximized;
    private string fontFamily = string.Empty;
    private double fontSize = 11;
    private List<string> fallbackFonts = new List<string>();
    private bool enableLigature = true;
    private int backgroundColor = 0x1E1E1E;
    private int foregroundColor = 0xCCCCCC;
    private string colorSchemeName = "VS Code Dark+";

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the default settings instance. This exists primarily for XAML
    /// designer support and parameterless-constructor fallbacks. Runtime
    /// code should receive <see cref="AppSettings"/> through constructor
    /// injection rather than accessing this property directly.
    /// </summary>
    public static AppSettings Default
    {
        get
        {
            lock (DefaultInstanceLock)
            {
                return defaultInstance.Value;
            }
        }
    }

    /// <summary>
    /// Gets the last persistence error, if any.
    /// </summary>
    public string LastPersistenceError { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether blur behind is enabled.
    /// </summary>
    public bool EnableBlurBehind
    {
        get => this.enableBlurBehind;
        set => this.SetField(ref this.enableBlurBehind, value);
    }

    /// <summary>
    /// Gets or sets the blur type.
    /// </summary>
    public BlurType BlurType
    {
        get => this.blurType;
        set => this.SetField(ref this.blurType, value);
    }

    /// <summary>
    /// Gets or sets the background opacity.
    /// </summary>
    public double BackgroundOpacity
    {
        get => this.backgroundOpacity;
        set => this.SetField(ref this.backgroundOpacity, value);
    }

    /// <summary>
    /// Gets or sets the window width.
    /// </summary>
    public int WindowWidth
    {
        get => this.windowWidth;
        set => this.SetField(ref this.windowWidth, value);
    }

    /// <summary>
    /// Gets or sets the window height.
    /// </summary>
    public int WindowHeight
    {
        get => this.windowHeight;
        set => this.SetField(ref this.windowHeight, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the window is maximized.
    /// </summary>
    public bool IsMaximized
    {
        get => this.isMaximized;
        set => this.SetField(ref this.isMaximized, value);
    }

    /// <summary>
    /// Gets or sets the preferred font family name. An empty string means
    /// the application should fall back to its built-in default.
    /// </summary>
    public string FontFamily
    {
        get => this.fontFamily;
        set => this.SetField(ref this.fontFamily, value);
    }

    /// <summary>
    /// Gets or sets the font size in points.
    /// </summary>
    public double FontSize
    {
        get => this.fontSize;
        set => this.SetField(ref this.fontSize, value);
    }

    /// <summary>
    /// Gets or sets the user-configured fallback font list.
    /// These fonts are searched (in order) when the primary font
    /// lacks a glyph.
    /// </summary>
    public List<string> FallbackFonts
    {
        get => this.fallbackFonts;
        set => this.SetField(ref this.fallbackFonts, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether font ligature is enabled.
    /// </summary>
    public bool EnableLigature
    {
        get => this.enableLigature;
        set => this.SetField(ref this.enableLigature, value);
    }

    /// <summary>
    /// Gets or sets the terminal background color as a 24-bit RGB integer.
    /// </summary>
    public int BackgroundColor
    {
        get => this.backgroundColor;
        set => this.SetField(ref this.backgroundColor, value);
    }

    /// <summary>
    /// Gets or sets the terminal foreground color as a 24-bit RGB integer.
    /// </summary>
    public int ForegroundColor
    {
        get => this.foregroundColor;
        set => this.SetField(ref this.foregroundColor, value);
    }

    /// <summary>
    /// Gets or sets the name of the active color scheme.
    /// </summary>
    public string ColorSchemeName
    {
        get => this.colorSchemeName;
        set => this.SetField(ref this.colorSchemeName, value);
    }

    /// <summary>
    /// Save settings to disk.
    /// </summary>
    /// <returns><c>true</c> if the settings were saved successfully; otherwise, <c>false</c>.</returns>
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(GetSettingsDirectory());
            var json = JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(GetSettingsPath(), json);
            this.LastPersistenceError = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            this.LastPersistenceError = ex.Message;
            AppLogger.For<AppSettings>().LogError(ex, "Failed to save settings.");
            return false;
        }
    }

    /// <summary>
    /// Reload settings from disk, discarding in-memory changes.
    /// </summary>
    /// <returns><c>true</c> if the settings were reloaded successfully; otherwise, <c>false</c>.</returns>
    public bool Reload()
    {
        var fresh = Load();
        this.EnableBlurBehind = fresh.EnableBlurBehind;
        this.BlurType = fresh.BlurType;
        this.BackgroundOpacity = fresh.BackgroundOpacity;
        this.WindowWidth = fresh.WindowWidth;
        this.WindowHeight = fresh.WindowHeight;
        this.IsMaximized = fresh.IsMaximized;
        this.FontFamily = fresh.FontFamily;
        this.FontSize = fresh.FontSize;
        this.FallbackFonts = fresh.FallbackFonts;
        this.EnableLigature = fresh.EnableLigature;
        this.BackgroundColor = fresh.BackgroundColor;
        this.ForegroundColor = fresh.ForegroundColor;
        this.ColorSchemeName = fresh.ColorSchemeName;
        this.LastPersistenceError = fresh.LastPersistenceError;
        return string.IsNullOrEmpty(this.LastPersistenceError);
    }

    /// <summary>
    /// Clears the last recorded persistence error after it has been shown to the user.
    /// </summary>
    public void ClearLastPersistenceError()
    {
        this.LastPersistenceError = string.Empty;
    }

    /// <summary>
    /// Redirects settings persistence to a test-specific directory.
    /// </summary>
    /// <param name="settingsDirectory">The override directory, or <c>null</c> to use the default location.</param>
    internal static void SetStorageDirectoryForTesting(string? settingsDirectory)
    {
        lock (DefaultInstanceLock)
        {
            settingsDirectoryOverride = settingsDirectory;
            defaultInstance = CreateDefaultInstance();
        }
    }

    /// <summary>
    /// Clears any test overrides and recreates the default singleton.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (DefaultInstanceLock)
        {
            settingsDirectoryOverride = null;
            defaultInstance = CreateDefaultInstance();
        }
    }

    /// <summary>
    /// Gets the effective settings path used by the current test configuration.
    /// </summary>
    /// <returns>The effective settings file path.</returns>
    internal static string GetSettingsPathForTesting()
    {
        return GetSettingsPath();
    }

    private static AppSettings Load()
    {
        try
        {
            string settingsPath = GetSettingsPath();
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings) as AppSettings ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            AppLogger.For<AppSettings>().LogError(ex, "Failed to load settings.");
            return new AppSettings
            {
                LastPersistenceError = ex.Message,
            };
        }

        return new AppSettings();
    }

    private static Lazy<AppSettings> CreateDefaultInstance()
    {
        return new Lazy<AppSettings>(Load);
    }

    private static string GetSettingsDirectory()
    {
        return settingsDirectoryOverride ?? DefaultSettingsDirectory;
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(GetSettingsDirectory(), "settings.json");
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
