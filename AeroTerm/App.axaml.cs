// <copyright file="App.axaml.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AeroTerm.Controls;
using AeroTerm.Services;
using AeroTerm.WindowEffects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

/// <summary>
/// The Avalonia application.
/// </summary>
public class App : Application
{
    private static KeybindingStore? keybindingStore;
    private static KeybindingSet keybindings = KeybindingSet.Defaults;
    private static ProfileStore? profileStore;
    private static ProfileStoreData profiles = new(new List<Profile> { ProfileStore.CreateSynthesizedDefault() }, null);
    private static PaletteMruStore? paletteMru;
    private static QuakeModeService? quakeModeService;
    private static IGlobalHotkeySource? globalHotkeySource;

    /// <summary>
    /// Raised whenever <see cref="Keybindings"/> has been reloaded from
    /// the store. Consumers that cache the current set should refresh.
    /// </summary>
    public static event Action? KeybindingsChanged;

    /// <summary>
    /// Raised whenever <see cref="Profiles"/> has been reloaded from the
    /// profile store (or when profiles are saved through the Settings UI).
    /// Consumers that cache the profile list should refresh.
    /// </summary>
    public static event Action? ProfilesChanged;

    /// <summary>
    /// Gets the current application-wide keybinding set.
    /// </summary>
    public static KeybindingSet Keybindings => keybindings;

    /// <summary>
    /// Gets the process-wide keybinding store. Created lazily on first
    /// access so tests can swap it via <see cref="SetKeybindingStoreForTesting"/>.
    /// </summary>
    public static KeybindingStore KeybindingStore
    {
        get
        {
            keybindingStore ??= new KeybindingStore();
            return keybindingStore;
        }
    }

    /// <summary>
    /// Gets the process-wide profile store. Created lazily on first
    /// access so tests can swap it via <see cref="SetProfileStoreForTesting"/>.
    /// </summary>
    public static ProfileStore ProfileStore
    {
        get
        {
            if (profileStore is null)
            {
                profileStore = new ProfileStore();
                profileStore.ProfilesChanged += ReloadProfiles;
            }

            return profileStore;
        }
    }

    /// <summary>
    /// Gets the process-wide command-palette MRU store. Created lazily
    /// on first access so tests can swap it via
    /// <see cref="SetPaletteMruForTesting"/>.
    /// </summary>
    public static PaletteMruStore PaletteMru
    {
        get
        {
            paletteMru ??= new PaletteMruStore();
            return paletteMru;
        }
    }

    /// <summary>
    /// Gets the current application-wide profile snapshot.
    /// </summary>
    public static ProfileStoreData Profiles => profiles;

    /// <summary>
    /// Gets the process-wide Quake-mode service. <see langword="null"/>
    /// before <see cref="OnFrameworkInitializationCompleted"/> has run.
    /// </summary>
    public static QuakeModeService? QuakeMode => quakeModeService;

    /// <summary>
    /// Gets or sets a test-only seam. When set, <see cref="MainWindow"/>
    /// uses this factory to construct tab content instead of spawning a
    /// real PTY-backed <see cref="Controls.TabSession"/>. Leave <c>null</c>
    /// in production.
    /// </summary>
    internal static Func<AppSettings, ITabSessionContent>? TestTabContentFactory { get; set; }

    /// <summary>
    /// Gets or sets a test-only seam. When set, <see cref="MainWindow.OnClosing"/>'s
    /// confirm-close flow delegates to this function instead of showing the
    /// real modal <see cref="Dialogs.ConfirmCloseDialog"/>. The returned
    /// task should resolve to <c>true</c> when the window should proceed
    /// with closing, or <c>false</c> to keep it open.
    /// </summary>
    internal static Func<MainWindow, Task<bool>>? TestConfirmCloseHandler { get; set; }

    /// <summary>
    /// Reloads the keybindings from the current <see cref="KeybindingStore"/>
    /// and raises <see cref="KeybindingsChanged"/>.
    /// </summary>
    public static void ReloadKeybindings()
    {
        keybindings = KeybindingStore.Load();
        KeybindingsChanged?.Invoke();
    }

    /// <summary>
    /// Reloads the profile list from the current <see cref="ProfileStore"/>
    /// and raises <see cref="ProfilesChanged"/>.
    /// </summary>
    public static void ReloadProfiles()
    {
        profiles = ProfileStore.Load();
        ProfilesChanged?.Invoke();
    }

    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        // Load keybindings once at startup — persisted overrides (if any)
        // are merged on top of the platform defaults. Missing or malformed
        // keybindings.json falls back to defaults silently.
        ReloadKeybindings();

        // Load the profile list; missing/malformed profiles.json yields a
        // single synthesized "Default" profile so behaviour matches the
        // pre-profile build.
        ReloadProfiles();

        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                MacOSWindowMenu.SetNewWindowHandler(() => this.CreateNewWindow());
            }

            desktop.MainWindow = this.CreateNewWindow();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                this.SetupMacOSActivation();
            }

            this.InitializeQuakeMode(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Creates and shows a new terminal window.
    /// </summary>
    /// <returns>The newly created window.</returns>
    public MainWindow CreateNewWindow()
    {
        var window = new MainWindow(AppSettings.Default);
        window.Show();
        return window;
    }

    /// <summary>
    /// Replaces the process-wide keybinding store (tests only).
    /// </summary>
    /// <param name="store">The replacement store, or <see langword="null"/> to reset.</param>
    internal static void SetKeybindingStoreForTesting(KeybindingStore? store)
    {
        keybindingStore = store;
        keybindings = (store ?? new KeybindingStore()).Load();
        KeybindingsChanged?.Invoke();
    }

    /// <summary>
    /// Replaces the process-wide profile store (tests only).
    /// </summary>
    /// <param name="store">The replacement store, or <see langword="null"/> to reset.</param>
    internal static void SetProfileStoreForTesting(ProfileStore? store)
    {
        profileStore = store;
        if (store is not null)
        {
            store.ProfilesChanged -= ReloadProfiles;
            store.ProfilesChanged += ReloadProfiles;
        }

        profiles = (store ?? new ProfileStore()).Load();
        ProfilesChanged?.Invoke();
    }

    /// <summary>
    /// Replaces the process-wide palette MRU store (tests only).
    /// </summary>
    /// <param name="store">The replacement store, or <see langword="null"/> to reset.</param>
    internal static void SetPaletteMruForTesting(PaletteMruStore? store)
    {
        paletteMru = store;
    }

    /// <summary>
    /// Replaces the process-wide global-hotkey source (tests only). Must
    /// be set before <see cref="OnFrameworkInitializationCompleted"/>
    /// runs to take effect for the production Quake service.
    /// </summary>
    /// <param name="source">The replacement source, or <see langword="null"/> to reset.</param>
    internal static void SetGlobalHotkeySourceForTesting(IGlobalHotkeySource? source)
    {
        globalHotkeySource = source;
    }

    /// <summary>
    /// Handles the New Window menu item click from the macOS native app menu.
    /// </summary>
    private void OnNewWindowClicked(object? sender, EventArgs e)
    {
        this.CreateNewWindow();
    }

    /// <summary>
    /// Handles the Settings menu item click from the macOS native app menu.
    /// </summary>
    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var target = desktop.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsActive)
                         ?? desktop.Windows.OfType<MainWindow>().FirstOrDefault();
            target?.OpenSettings();
        }
    }

    /// <summary>
    /// On macOS, reopens a window when the app is activated with no windows
    /// (e.g. clicking the dock icon).
    /// </summary>
    private void SetupMacOSActivation()
    {
        if (this.ApplicationLifetime is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, args) =>
            {
                if (args.Kind == ActivationKind.Reopen
                    && this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
                    && !d.Windows.OfType<MainWindow>().Any())
                {
                    this.CreateNewWindow();
                }
            };
        }
    }

    /// <summary>
    /// Creates the Quake-mode service, registers the configured global
    /// hotkey (if any), subscribes to setting changes, and disposes the
    /// registration on desktop shutdown.
    /// </summary>
    private void InitializeQuakeMode(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var settings = AppSettings.Default;
        var source = globalHotkeySource ?? new DefaultGlobalHotkeySource();
        globalHotkeySource = source;

        quakeModeService = new QuakeModeService(
            settings,
            source,
            windowFactory: () => new Dialogs.QuakeWindow(settings),
            toggleAction: w => ((Dialogs.QuakeWindow)w).Toggle());

        quakeModeService.ApplySettings();

        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.QuakeModeEnabled)
                || e.PropertyName == nameof(AppSettings.QuakeHotkey))
            {
                quakeModeService?.ApplySettings();
            }
        };

        desktop.ShutdownRequested += (_, _) => quakeModeService?.Dispose();
    }
}
