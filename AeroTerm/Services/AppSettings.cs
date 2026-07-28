// <copyright file="AppSettings.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using AeroTerm.Diagnostics;
using AeroTerm.WindowEffects;
using Microsoft.Extensions.Logging;

/// <summary>
/// Application settings with JSON persistence.
/// </summary>
public sealed class AppSettings : INotifyPropertyChanged, IWindowEffectsSettings, IWindowGeometrySettings
{
    /// <summary>
    /// Base backoff (milliseconds) between settings file I/O retries. Each
    /// attempt waits this value multiplied by the attempt number.
    /// </summary>
    private const int SettingsIoRetryDelayMs = 40;

    private static readonly string DefaultSettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AeroTerm");

    private static readonly object DefaultInstanceLock = new();

    private static string? settingsDirectoryOverride;

    private static Lazy<AppSettings> defaultInstance = CreateDefaultInstance();

    private readonly HashSet<string> modifiedProperties = new(StringComparer.Ordinal);

    private bool enableBlurBehind = true;
    private BlurType blurType = BlurType.Acrylic;
    private MaterialTone materialTone = MaterialTone.Light;
    private double backgroundTintOpacity = 0.85;
    private double backgroundMaterialOpacity = 0.75;
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
    private bool autoCheckForUpdates = true;
    private DateTime? lastUpdateCheckUtc;
    private string? skippedVersion;
    private int settingsWindowWidth = 680;
    private int settingsWindowHeight = 480;
    private BellAction bellAction = BellAction.Visual;
    private int scrollbackLines = 10_000;
    private bool confirmOnClose = true;
    private bool quakeModeEnabled;
    private string quakeHotkey = DefaultQuakeHotkey();
    private bool middleClickPastes = true;
    private bool enableShellIntegration = true;
    private bool enableWorkbench;
    private TabBarOrientation tabBarOrientation = TabBarOrientation.Horizontal;
    private bool useFullScreenNotchArea;

    private bool loadedWithTransientError;
    private bool settingsFileExistedAtLoad;
    private string? persistedRevision;

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
    /// Gets or sets the tonal variant (light or dark) of the platform
    /// blur / acrylic / mica / vibrancy backdrop. Independent of
    /// Avalonia's <c>RequestedThemeVariant</c>; ignored when blur is
    /// disabled, when <see cref="WindowEffects.BlurType.Transparent"/>
    /// is selected, or on Linux. Defaults to
    /// <see cref="WindowEffects.MaterialTone.Light"/> to match the
    /// pre-existing implicit behavior.
    /// </summary>
    public MaterialTone MaterialTone
    {
        get => this.materialTone;
        set => this.SetField(ref this.materialTone, value);
    }

    /// <summary>
    /// Gets or sets the opacity of the tint color layered over the
    /// platform blur backdrop (0.0–1.0). Combined multiplicatively with
    /// <see cref="BackgroundMaterialOpacity"/> to produce the effective
    /// alpha of the window background brush. Mirrors
    /// <c>ExperimentalAcrylicMaterial.TintOpacity</c> in Avalonia.
    /// </summary>
    public double BackgroundTintOpacity
    {
        get => this.backgroundTintOpacity;
        set => this.SetField(ref this.backgroundTintOpacity, value);
    }

    /// <summary>
    /// Gets or sets the opacity of the overall material layer (0.0–1.0).
    /// Lower values let more of the platform backdrop show through.
    /// Combined multiplicatively with <see cref="BackgroundTintOpacity"/>
    /// to produce the effective alpha. Mirrors
    /// <c>ExperimentalAcrylicMaterial.MaterialOpacity</c> in Avalonia.
    /// </summary>
    public double BackgroundMaterialOpacity
    {
        get => this.backgroundMaterialOpacity;
        set => this.SetField(ref this.backgroundMaterialOpacity, value);
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
        set
        {
            if (value is not null && this.fallbackFonts.SequenceEqual(value))
            {
                return;
            }

            this.fallbackFonts = value ?? new List<string>();
            this.modifiedProperties.Add(nameof(this.FallbackFonts));
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.FallbackFonts)));
        }
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
    /// Gets or sets a value indicating whether the application should
    /// automatically check for updates at startup.
    /// </summary>
    public bool AutoCheckForUpdates
    {
        get => this.autoCheckForUpdates;
        set => this.SetField(ref this.autoCheckForUpdates, value);
    }

    /// <summary>
    /// Gets or sets the UTC timestamp of the last successful update check.
    /// </summary>
    public DateTime? LastUpdateCheckUtc
    {
        get => this.lastUpdateCheckUtc;
        set => this.SetField(ref this.lastUpdateCheckUtc, value);
    }

    /// <summary>
    /// Gets or sets the version string the user chose to skip. When set,
    /// the update notification is suppressed for this exact version.
    /// </summary>
    public string? SkippedVersion
    {
        get => this.skippedVersion;
        set => this.SetField(ref this.skippedVersion, value);
    }

    /// <summary>
    /// Gets or sets the remembered width of the Settings dialog.
    /// </summary>
    public int SettingsWindowWidth
    {
        get => this.settingsWindowWidth;
        set => this.SetField(ref this.settingsWindowWidth, value);
    }

    /// <summary>
    /// Gets or sets the remembered height of the Settings dialog.
    /// </summary>
    public int SettingsWindowHeight
    {
        get => this.settingsWindowHeight;
        set => this.SetField(ref this.settingsWindowHeight, value);
    }

    /// <summary>
    /// Gets or sets how the app reacts to the terminal BEL character.
    /// </summary>
    public BellAction BellAction
    {
        get => this.bellAction;
        set => this.SetField(ref this.bellAction, value);
    }

    /// <summary>
    /// Gets or sets the number of lines retained in the terminal's
    /// scrollback ring. Clamped to the range <c>[0, 1_000_000]</c> on load
    /// and set. A value of <c>0</c> disables scrollback entirely.
    /// </summary>
    public int ScrollbackLines
    {
        get => this.scrollbackLines;
        set => this.SetField(ref this.scrollbackLines, Math.Clamp(value, 0, 1_000_000));
    }

    /// <summary>
    /// Gets or sets a value indicating whether the application should prompt
    /// the user before closing a window that still contains more than one
    /// open tab. Single-tab windows always close immediately. Defaults to
    /// <c>true</c> (safer).
    /// </summary>
    public bool ConfirmOnClose
    {
        get => this.confirmOnClose;
        set => this.SetField(ref this.confirmOnClose, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the global Quake-mode
    /// hotkey is registered. Defaults to <c>false</c> because the feature
    /// grabs a system-wide key combination and should be opt-in.
    /// </summary>
    public bool QuakeModeEnabled
    {
        get => this.quakeModeEnabled;
        set => this.SetField(ref this.quakeModeEnabled, value);
    }

    /// <summary>
    /// Gets or sets the chord (in <see cref="KeyChordParser"/> syntax)
    /// used as the Quake-mode global hotkey. Default is <c>Ctrl+Oem3</c>
    /// on Windows / Linux and <c>Cmd+Oem3</c> on macOS — the classic
    /// Quake tilde key.
    /// </summary>
    public string QuakeHotkey
    {
        get => this.quakeHotkey;
        set => this.SetField(ref this.quakeHotkey, value ?? DefaultQuakeHotkey());
    }

    /// <summary>
    /// Gets or sets a value indicating whether a middle mouse-button click
    /// inside the terminal pastes text. On Linux/X11 the source is the
    /// PRIMARY selection (last mouse-selected text) with a fallback to
    /// the regular clipboard; on macOS and Windows the regular clipboard
    /// is always used. Defaults to <c>true</c> to match traditional
    /// xterm / GNOME Terminal behaviour.
    /// </summary>
    public bool MiddleClickPastes
    {
        get => this.middleClickPastes;
        set => this.SetField(ref this.middleClickPastes, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether AeroTerm injects its
    /// OSC 133 shell-integration scripts into recognised child shells
    /// (zsh, bash, fish, PowerShell). When <see langword="true"/> the
    /// terminal can identify prompt / user-input / command-output
    /// regions and enable features such as Cmd+Backspace / Ctrl+Shift+
    /// Backspace input deletion. Defaults to <see langword="true"/>.
    /// </summary>
    public bool EnableShellIntegration
    {
        get => this.enableShellIntegration;
        set => this.SetField(ref this.enableShellIntegration, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the experimental Workbench
    /// Git pane is enabled. Defaults to <see langword="false"/>.
    /// </summary>
    public bool EnableWorkbench
    {
        get => this.enableWorkbench;
        set => this.SetField(ref this.enableWorkbench, value);
    }

    /// <summary>
    /// Gets or sets the orientation of the tab strip. Defaults to
    /// <see cref="TabBarOrientation.Horizontal"/> (a classic top-docked
    /// tab bar). Setting <see cref="TabBarOrientation.Vertical"/> swaps
    /// the strip for a narrow left-edge rail with tabs stacked vertically.
    /// </summary>
    public TabBarOrientation TabBarOrientation
    {
        get => this.tabBarOrientation;
        set => this.SetField(ref this.tabBarOrientation, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether macOS native full screen
    /// should extend into the camera-housing ("notch") band at the top of
    /// the display, placing the tab strip beside the housing instead of
    /// below it. Defaults to <c>false</c>, which reproduces the standard
    /// macOS behaviour of a black band across the housing. Has no effect on
    /// other platforms or on displays without a camera housing.
    /// </summary>
    public bool UseFullScreenNotchArea
    {
        get => this.useFullScreenNotchArea;
        set => this.SetField(ref this.useFullScreenNotchArea, value);
    }

    /// <summary>
    /// Save settings to disk.
    /// </summary>
    /// <param name="reason">The application action that requested persistence.</param>
    /// <param name="caller">The member that requested persistence.</param>
    /// <returns><c>true</c> if the settings were saved successfully; otherwise, <c>false</c>.</returns>
    public bool Save(string reason = "unspecified", [CallerMemberName] string caller = "")
    {
        var settingsPath = GetSettingsPath();
        var tempPath = $"{settingsPath}.tmp-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var log = AppLogger.For<AppSettings>();
        string changedProperties = this.modifiedProperties.Count == 0
            ? "(none)"
            : string.Join(", ", this.modifiedProperties.Order());

        log.LogInformation(
            "Settings save requested — PID={ProcessId}, Reason={Reason}, Caller={Caller}, Path={Path}, ChangedProperties={ChangedProperties}, FileExistedAtLoad={FileExistedAtLoad}, LoadedRevision={LoadedRevision}.",
            Environment.ProcessId,
            reason,
            caller,
            settingsPath,
            changedProperties,
            this.settingsFileExistedAtLoad,
            this.persistedRevision ?? "(none)");

        // Safety net for concurrent instances / failed loads: if this
        // instance fell back to defaults because the on-disk file could not
        // be read for a *transient* reason (e.g. another instance was
        // momentarily replacing it), do NOT persist — otherwise we would
        // clobber the user's real config with defaults. The next successful
        // Reload() clears the flag.
        if (this.loadedWithTransientError)
        {
            log.LogWarning(
                "Skipping settings save — PID={ProcessId}, Reason={Reason}; the last load failed transiently, so config at {Path} will not be overwritten.",
                Environment.ProcessId,
                reason,
                settingsPath);
            return false;
        }

        try
        {
            Directory.CreateDirectory(GetSettingsDirectory());
            using var saveLock = AcquireSaveLock(settingsPath + ".lock");
            string? persistedJson = File.Exists(settingsPath)
                ? ReadAllTextWithRetry(settingsPath)
                : null;

            if (this.HasPersistedFileChanged(persistedJson))
            {
                this.LastPersistenceError =
                    "Settings were changed by another AeroTerm instance after this instance started. "
                    + "The stale settings were not saved; reload or restart AeroTerm before saving again.";
                log.LogWarning(
                    "Skipping stale settings save — PID={ProcessId}, Reason={Reason}; {Path} changed after this instance loaded it.",
                    Environment.ProcessId,
                    reason,
                    settingsPath);
                return false;
            }

            var json = this.BuildJsonForSave(persistedJson);

            // Atomic write: serialise to a sibling temp file, fsync, then
            // rename over the target. This prevents a half-written
            // settings.json after a process kill / power loss / Velopack
            // auto-restart, which would otherwise be silently replaced by
            // defaults on the next launch.
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            // Retry the rename-over: a concurrent instance may briefly hold
            // settings.json open for reading, causing a transient sharing
            // violation. Last-writer-wins is intentional.
            MoveWithRetry(tempPath, settingsPath);
            this.LastPersistenceError = string.Empty;
            this.settingsFileExistedAtLoad = true;
            this.persistedRevision = ComputeRevision(json);
            this.AcceptChanges();
            log.LogInformation(
                "Settings save completed — PID={ProcessId}, Reason={Reason}, Caller={Caller}, Path={Path}, Revision={Revision}.",
                Environment.ProcessId,
                reason,
                caller,
                settingsPath,
                this.persistedRevision);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            this.LastPersistenceError = ex.Message;
            log.LogError(
                ex,
                "Settings save failed — PID={ProcessId}, Reason={Reason}, Caller={Caller}, Path={Path}.",
                Environment.ProcessId,
                reason,
                caller,
                settingsPath);
            TryDeleteFile(tempPath);
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
        this.MaterialTone = fresh.MaterialTone;
        this.BackgroundTintOpacity = fresh.BackgroundTintOpacity;
        this.BackgroundMaterialOpacity = fresh.BackgroundMaterialOpacity;
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
        this.AutoCheckForUpdates = fresh.AutoCheckForUpdates;
        this.LastUpdateCheckUtc = fresh.LastUpdateCheckUtc;
        this.SkippedVersion = fresh.SkippedVersion;
        this.SettingsWindowWidth = fresh.SettingsWindowWidth;
        this.SettingsWindowHeight = fresh.SettingsWindowHeight;
        this.BellAction = fresh.BellAction;
        this.ScrollbackLines = fresh.ScrollbackLines;
        this.ConfirmOnClose = fresh.ConfirmOnClose;
        this.QuakeModeEnabled = fresh.QuakeModeEnabled;
        this.QuakeHotkey = fresh.QuakeHotkey;
        this.MiddleClickPastes = fresh.MiddleClickPastes;
        this.EnableShellIntegration = fresh.EnableShellIntegration;
        this.EnableWorkbench = fresh.EnableWorkbench;
        this.TabBarOrientation = fresh.TabBarOrientation;
        this.UseFullScreenNotchArea = fresh.UseFullScreenNotchArea;
        this.LastPersistenceError = fresh.LastPersistenceError;
        this.loadedWithTransientError = fresh.loadedWithTransientError;
        this.settingsFileExistedAtLoad = fresh.settingsFileExistedAtLoad;
        this.persistedRevision = fresh.persistedRevision;
        this.AcceptChanges();
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

    /// <summary>
    /// Rewrites legacy settings JSON in place so that older keys whose
    /// schemas have changed deserialize correctly under the current
    /// source-generated contract. Currently handles the
    /// <c>BackgroundOpacity</c> → <c>BackgroundTintOpacity</c> +
    /// <c>BackgroundMaterialOpacity</c> split: an upgrader's existing
    /// effective alpha is preserved by seeding tint = legacy and
    /// material = 1.0 (so tint * material = legacy).
    /// </summary>
    /// <param name="json">The raw settings JSON text.</param>
    /// <returns>The migrated JSON text, or <paramref name="json"/> unchanged when no migration applies.</returns>
    internal static string MigrateLegacyJson(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject node)
            {
                return json;
            }

            if (node.TryGetPropertyValue("BackgroundOpacity", out var legacy)
                && legacy is JsonValue legacyValue
                && legacyValue.TryGetValue<double>(out var legacyOpacity))
            {
                if (!node.ContainsKey("BackgroundTintOpacity"))
                {
                    node["BackgroundTintOpacity"] = legacyOpacity;
                }

                if (!node.ContainsKey("BackgroundMaterialOpacity"))
                {
                    node["BackgroundMaterialOpacity"] = 1.0;
                }

                node.Remove("BackgroundOpacity");
                return node.ToJsonString();
            }
        }
        catch (JsonException)
        {
            // Fall through and let the source-gen deserializer surface the error.
        }

        return json;
    }

    private static AppSettings Load()
    {
        string settingsPath = GetSettingsPath();
        var log = AppLogger.For<AppSettings>();
        try
        {
            if (File.Exists(settingsPath))
            {
                var persistedJson = ReadAllTextWithRetry(settingsPath);
                var json = MigrateLegacyJson(persistedJson);

                // Schema-mismatch guard: if the file contains a non-empty
                // JSON object whose keys do not intersect *at all* with the
                // current AppSettings property set (e.g., after a refactor
                // that renamed every property, or a corrupt overwrite by
                // an unrelated tool), source-gen's Deserialize would
                // happily return an all-defaults instance with no
                // exception. The next clean shutdown would then save those
                // defaults back, silently destroying the user's data.
                // Detect that case explicitly and quarantine the file.
                if (TryGetUnrecognisedSchema(json, out var unrecognised))
                {
                    log.LogWarning(
                        "Settings file at {Path} has no recognised properties; quarantining and using defaults. Unknown keys: {Keys}",
                        settingsPath,
                        unrecognised);
                    var quarantinePath = QuarantineCorruptFile(settingsPath, "no recognised properties");
                    return new AppSettings
                    {
                        LastPersistenceError = BuildCorruptError(
                            settingsPath,
                            $"the file has no recognised settings (found: {unrecognised}).",
                            quarantinePath),
                    };
                }

                AppSettings? settings;
                try
                {
                    settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings) as AppSettings;
                }
                catch (JsonException ex)
                {
                    // Genuine corruption (malformed JSON). Quarantine so the
                    // bad file can't be re-saved over, and surface a detailed
                    // reason to the user.
                    log.LogWarning(ex, "Failed to parse settings from {Path}; using default settings.", settingsPath);
                    var quarantinePath = QuarantineCorruptFile(settingsPath, ex.Message);
                    return new AppSettings
                    {
                        LastPersistenceError = BuildCorruptError(settingsPath, ex.Message, quarantinePath),
                    };
                }

                if (settings is null)
                {
                    const string Reason = "the file did not contain a settings object.";
                    log.LogWarning(
                        "Failed to load settings from {Path}: {Message}; using default settings.",
                        settingsPath,
                        Reason);
                    var quarantinePath = QuarantineCorruptFile(settingsPath, Reason);
                    return new AppSettings
                    {
                        LastPersistenceError = BuildCorruptError(settingsPath, Reason, quarantinePath),
                    };
                }

                settings.settingsFileExistedAtLoad = true;
                settings.persistedRevision = ComputeRevision(persistedJson);
                settings.AcceptChanges();
                log.LogInformation("Loaded settings from {Path} ({Bytes} bytes).", settingsPath, json.Length);
                return settings;
            }

            log.LogInformation("No settings file at {Path}; using built-in defaults.", settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Transient / environmental failure (file locked by another
            // instance, AV scanner, permission glitch). Do NOT quarantine and
            // do NOT let this default-laden instance overwrite the real config
            // on shutdown — that is exactly how concurrent instances used to
            // destroy each other's settings.
            log.LogWarning(ex, "Could not read settings from {Path}; using defaults for this session without overwriting the file.", settingsPath);
            return new AppSettings
            {
                LastPersistenceError = BuildTransientError(settingsPath, ex.Message),
                loadedWithTransientError = true,
            };
        }

        return new AppSettings();
    }

    /// <summary>
    /// Builds a detailed, user-facing message describing why a corrupt or
    /// unreadable settings file was replaced with defaults, including the
    /// quarantine location when the bad file was preserved.
    /// </summary>
    private static string BuildCorruptError(string settingsPath, string reason, string? quarantinePath)
    {
        var message = $"AeroTerm could not load your settings from \"{settingsPath}\" because {reason} Default settings are being used for this session.";
        if (!string.IsNullOrEmpty(quarantinePath))
        {
            message += $" The unreadable file was preserved at \"{quarantinePath}\".";
        }

        return message;
    }

    /// <summary>
    /// Builds a detailed, user-facing message for a transient read failure.
    /// The on-disk file is left untouched and will not be overwritten this
    /// session.
    /// </summary>
    private static string BuildTransientError(string settingsPath, string detail)
    {
        return $"AeroTerm could not read your settings from \"{settingsPath}\" (it may be in use by another AeroTerm instance). Detail: {detail}. Default settings are being used for this session and your saved file will not be overwritten.";
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="json"/> parses to
    /// a non-empty JSON object whose top-level keys do not match any
    /// property known to <see cref="AppSettingsJsonContext"/>. The
    /// out-parameter then contains a comma-separated list of the
    /// unrecognised keys for diagnostic logging.
    /// </summary>
    private static bool TryGetUnrecognisedSchema(string json, out string unrecognisedKeys)
    {
        unrecognisedKeys = string.Empty;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject node || node.Count == 0)
            {
                return false;
            }

            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in AppSettingsJsonContext.Default.AppSettings.Properties)
            {
                known.Add(p.Name);
            }

            var unknown = new List<string>();
            bool anyKnown = false;
            foreach (var kvp in node)
            {
                if (known.Contains(kvp.Key))
                {
                    anyKnown = true;
                }
                else
                {
                    unknown.Add(kvp.Key);
                }
            }

            if (!anyKnown)
            {
                unrecognisedKeys = string.Join(", ", unknown);
                return true;
            }
        }
        catch (JsonException)
        {
            // Caller's Deserialize will surface the parse failure.
        }

        return false;
    }

    /// <summary>
    /// Renames a settings file that failed to load so it cannot be
    /// silently overwritten with defaults on the next save. Best effort:
    /// any failure here is logged and swallowed so startup still proceeds.
    /// </summary>
    /// <param name="settingsPath">The path of the unreadable settings file.</param>
    /// <param name="reason">A human-readable reason for the quarantine.</param>
    /// <returns>The path the file was moved to, or <see langword="null"/> if it could not be quarantined.</returns>
    private static string? QuarantineCorruptFile(string settingsPath, string reason)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture);
            var quarantinePath = settingsPath + ".bad-" + stamp;
            File.Move(settingsPath, quarantinePath, overwrite: true);
            AppLogger.For<AppSettings>().LogWarning(
                "Quarantined unreadable settings file to {Quarantine} ({Reason}).",
                quarantinePath,
                reason);
            return quarantinePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLogger.For<AppSettings>().LogWarning(ex, "Failed to quarantine corrupt settings file {Path}.", settingsPath);
            return null;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLogger.For<AppSettings>().LogDebug(ex, "Failed to remove temp file {Path}.", path);
        }
    }

    private static FileStream AcquireSaveLock(string path)
    {
        const int MaxAttempts = 10;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(SettingsIoRetryDelayMs * attempt);
            }
        }
    }

    private static string ComputeRevision(string json)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>
    /// Reads a file's full text, retrying a bounded number of times on
    /// transient sharing violations. A concurrently running instance may be
    /// replacing the file (atomic temp-file + rename), which briefly makes it
    /// unreadable; a short backoff lets that window pass. The file is opened
    /// with <see cref="FileShare.ReadWrite"/> so a concurrent writer does not
    /// have to fail just because we hold a read handle.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The file contents as text.</returns>
    private static string ReadAllTextWithRetry(string path)
    {
        const int MaxAttempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < MaxAttempts && File.Exists(path))
            {
                Thread.Sleep(SettingsIoRetryDelayMs * attempt);
            }
        }
    }

    /// <summary>
    /// Renames <paramref name="source"/> over <paramref name="destination"/>,
    /// retrying a bounded number of times on transient sharing violations
    /// caused by a concurrently running instance momentarily reading the
    /// destination. Last-writer-wins is intentional.
    /// </summary>
    /// <param name="source">The temp file to promote.</param>
    /// <param name="destination">The target settings path.</param>
    private static void MoveWithRetry(string source, string destination)
    {
        const int MaxAttempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                Thread.Sleep(SettingsIoRetryDelayMs * attempt);
            }
        }
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

    private static string DefaultQuakeHotkey()
    {
        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
            ? "Cmd+Oem3"
            : "Ctrl+Oem3";
    }

    private void AcceptChanges()
    {
        this.modifiedProperties.Clear();
    }

    private string BuildJsonForSave(string? persistedJson)
    {
        var serialized = JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings);
        if (!this.settingsFileExistedAtLoad || persistedJson is null)
        {
            return serialized;
        }

        if (JsonNode.Parse(MigrateLegacyJson(persistedJson)) is not JsonObject persisted
            || JsonNode.Parse(serialized) is not JsonObject current)
        {
            return serialized;
        }

        foreach (var propertyName in this.modifiedProperties)
        {
            if (current.TryGetPropertyValue(propertyName, out var value))
            {
                persisted[propertyName] = value?.DeepClone();
            }
        }

        return persisted.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private bool HasPersistedFileChanged(string? persistedJson)
    {
        bool exists = persistedJson is not null;
        if (exists != this.settingsFileExistedAtLoad)
        {
            return true;
        }

        if (!exists)
        {
            return false;
        }

        var currentRevision = ComputeRevision(persistedJson!);
        return !string.Equals(currentRevision, this.persistedRevision, StringComparison.Ordinal);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            if (propertyName is not null)
            {
                this.modifiedProperties.Add(propertyName);
            }

            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
