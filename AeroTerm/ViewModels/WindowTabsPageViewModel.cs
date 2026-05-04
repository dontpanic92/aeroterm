// <copyright file="WindowTabsPageViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroTerm.Dialogs;
using AeroTerm.Services;
using Avalonia;
using Avalonia.Media;

/// <summary>
/// View model for window, tab, and Quake-mode settings.
/// </summary>
internal sealed class WindowTabsPageViewModel : SettingsPageViewModel, INotifyPropertyChanged
{
    private readonly AppSettings settings;
    private bool confirmOnClose;
    private TabBarOrientation tabBarOrientation;
    private bool quakeModeEnabled;
    private string quakeHotkey = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowTabsPageViewModel"/> class.
    /// </summary>
    /// <param name="settings">Application settings.</param>
    public WindowTabsPageViewModel(AppSettings settings)
    {
        this.settings = settings;
        this.confirmOnClose = settings.ConfirmOnClose;
        this.tabBarOrientation = settings.TabBarOrientation;
        this.quakeModeEnabled = settings.QuakeModeEnabled;
        this.quakeHotkey = settings.QuakeHotkey;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public override string DisplayName => "Window & Tabs";

    /// <inheritdoc/>
    public override IReadOnlyList<string> SearchableLabels { get; } = new[]
    {
        SettingsSearchLabels.TabBarOrientation,
        SettingsSearchLabels.ConfirmOnClose,
        SettingsSearchLabels.QuakeMode,
        SettingsSearchLabels.QuakeHotkey,
    };

    /// <summary>
    /// Gets or sets a value indicating whether the application should prompt
    /// before closing a window that still contains more than one open tab.
    /// </summary>
    public bool ConfirmOnClose
    {
        get => this.confirmOnClose;
        set
        {
            if (this.SetField(ref this.confirmOnClose, value))
            {
                this.settings.ConfirmOnClose = value;
            }
        }
    }

    /// <summary>
    /// Gets the fixed list of tab-bar orientation choices surfaced in the UI.
    /// </summary>
    public IReadOnlyList<TabBarOrientation> TabBarOrientations { get; } = new[]
    {
        TabBarOrientation.Horizontal,
        TabBarOrientation.Vertical,
    };

    /// <summary>
    /// Gets or sets the tab-bar orientation.
    /// </summary>
    public TabBarOrientation TabBarOrientation
    {
        get => this.tabBarOrientation;
        set
        {
            if (this.SetField(ref this.tabBarOrientation, value))
            {
                this.settings.TabBarOrientation = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the Quake-mode global hotkey is active.
    /// </summary>
    public bool QuakeModeEnabled
    {
        get => this.quakeModeEnabled;
        set
        {
            if (this.SetField(ref this.quakeModeEnabled, value))
            {
                this.settings.QuakeModeEnabled = value;
                this.OnPropertyChanged(nameof(this.QuakeHotkeyStatus));
                this.OnPropertyChanged(nameof(this.QuakeHotkeyStatusBrush));
            }
        }
    }

    /// <summary>
    /// Gets or sets the chord string bound to the Quake hotkey.
    /// </summary>
    public string QuakeHotkey
    {
        get => this.quakeHotkey;
        set
        {
            string normalized = value ?? string.Empty;
            if (this.SetField(ref this.quakeHotkey, normalized))
            {
                if (KeyChordParser.TryParse(normalized, out _))
                {
                    this.settings.QuakeHotkey = normalized;
                }

                this.OnPropertyChanged(nameof(this.QuakeHotkeyStatus));
                this.OnPropertyChanged(nameof(this.QuakeHotkeyStatusBrush));
            }
        }
    }

    /// <summary>
    /// Gets a short status string describing whether the current
    /// <see cref="QuakeHotkey"/> string is a valid chord.
    /// </summary>
    public string QuakeHotkeyStatus =>
        KeyChordParser.TryParse(this.quakeHotkey, out _) ? "✓ valid" : "✗ invalid chord";

    /// <summary>
    /// Gets the brush used to render <see cref="QuakeHotkeyStatus"/>.
    /// </summary>
    public IBrush QuakeHotkeyStatusBrush =>
        KeyChordParser.TryParse(this.quakeHotkey, out _)
            ? ResolveApplicationThemeBrush("SuccessFillBrush", Color.FromRgb(0x2E, 0xA0, 0x43))
            : ResolveApplicationThemeBrush("DangerFillBrush", Color.FromRgb(0xC0, 0x39, 0x2B));

    /// <summary>
    /// Gets a platform-specific warning displayed when Quake mode cannot be used.
    /// </summary>
    public string QuakePlatformWarning
    {
        get
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                return "Quake mode is not yet supported on Linux — the global hotkey backend for X11/Wayland has not shipped.";
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="QuakePlatformWarning"/> is non-empty.
    /// </summary>
    public bool HasQuakePlatformWarning => !string.IsNullOrEmpty(this.QuakePlatformWarning);

    private static IBrush ResolveApplicationThemeBrush(string key, Color fallback)
    {
        if (Application.Current is { } app && app.TryGetResource(key, null, out var value))
        {
            if (value is IBrush brush)
            {
                return brush;
            }

            if (value is Color color)
            {
                return new SolidColorBrush(color);
            }
        }

        return new SolidColorBrush(fallback);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        this.OnPropertyChanged(propertyName);
        return true;
    }
}
