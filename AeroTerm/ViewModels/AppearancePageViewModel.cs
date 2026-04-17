// <copyright file="AppearancePageViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroTerm.Dialogs;
using AeroTerm.Models;
using AeroTerm.Services;
using AeroTerm.Utilities;
using AeroTerm.WindowEffects;
using Avalonia.Media;

/// <summary>
/// View model for the Appearance settings page.
/// </summary>
internal sealed class AppearancePageViewModel : SettingsPageViewModel, INotifyPropertyChanged
{
    private readonly AppSettings settings;

    private bool enableLigature;
    private bool enableBlurBehind;
    private BlurType blurType;
    private double backgroundOpacity;
    private double fontSize;
    private int selectedFontIndex = -1;
    private ColorScheme selectedColorScheme;
    private BellAction bellAction;
    private int scrollbackLines;
    private bool confirmOnClose;
    private bool middleClickPastes;
    private TabBarOrientation tabBarOrientation;
    private bool quakeModeEnabled;
    private string quakeHotkey = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppearancePageViewModel"/> class.
    /// </summary>
    /// <param name="settings">The application settings.</param>
    public AppearancePageViewModel(AppSettings settings)
    {
        this.settings = settings;

        this.enableLigature = settings.EnableLigature;
        this.enableBlurBehind = settings.EnableBlurBehind;
        this.blurType = settings.BlurType;
        this.backgroundOpacity = settings.BackgroundOpacity;
        this.fontSize = settings.FontSize;

        foreach (var entry in settings.FallbackFonts)
        {
            this.FontItems.Add(CreateFontDisplayItem(entry));
        }

        this.ColorSchemes = ColorSchemePresets.All;
        this.selectedColorScheme = ColorSchemePresets.FindByName(settings.ColorSchemeName)
            ?? ColorSchemePresets.Default;

        this.bellAction = settings.BellAction;
        this.scrollbackLines = settings.ScrollbackLines;
        this.confirmOnClose = settings.ConfirmOnClose;
        this.middleClickPastes = settings.MiddleClickPastes;
        this.tabBarOrientation = settings.TabBarOrientation;
        this.quakeModeEnabled = settings.QuakeModeEnabled;
        this.quakeHotkey = settings.QuakeHotkey;

        this.FontItems.CollectionChanged += this.OnFontItemsChanged;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public override string DisplayName => "Appearance";

    /// <inheritdoc/>
    public override IReadOnlyList<string> SearchableLabels { get; } = new[]
    {
        "Window Transparency",
        "Blur",
        "Acrylic",
        "Mica",
        "Background Opacity",
        "Font Ligature",
        "Font Size",
        "Font Priority",
        "Color Scheme",
        "Bell",
        "Scrollback lines",
        "Ligature preview",
        "Confirm on close",
        "Middle-click paste",
        "Quake mode",
        "Quake hotkey",
    };

    /// <summary>
    /// Gets or sets a value indicating whether font ligature is enabled.
    /// </summary>
    public bool EnableLigature
    {
        get => this.enableLigature;
        set
        {
            if (this.SetField(ref this.enableLigature, value))
            {
                this.settings.EnableLigature = value;
                this.OnPropertyChanged(nameof(this.PreviewFontFeatures));
            }
        }
    }

    /// <summary>
    /// Gets or sets the font size in points.
    /// </summary>
    public double FontSize
    {
        get => this.fontSize;
        set
        {
            if (this.SetField(ref this.fontSize, value))
            {
                this.settings.FontSize = value;
            }
        }
    }

    /// <summary>
    /// Gets the preview string exercising common programming ligatures.
    /// </summary>
    public string LigaturePreviewLine1 => "!= == === !== -> => <=> :=  |> <| >>= <<= <= >=  /* */  // <!--  -->  ::  ...";

    /// <summary>
    /// Gets the preview string exercising general shaping (letters, digits, common ligatures).
    /// </summary>
    public string LigaturePreviewLine2 => "fi fl ffi  0 O o  l 1 I  abcABC 0123 :;,.";

    /// <summary>
    /// Gets the preview string exercising box-drawing clusters.
    /// </summary>
    public string LigaturePreviewLine3 => "─ │ ┌ ┐ └ ┘ ├ ┤ ┬ ┴ ┼   ═ ║ ╔ ╗ ╚ ╝";

    /// <summary>
    /// Gets the effective <see cref="Avalonia.Media.FontFamily"/> used by the
    /// ligature preview. Expands the user font list (including the
    /// <c>$SYSTEM_MONO</c> sentinel) into a comma-separated Avalonia font
    /// family chain so that the preview matches what the terminal resolves.
    /// </summary>
    public FontFamily PreviewFontFamily
    {
        get
        {
            var expanded = FontPriorityList.Expand(this.GetRawFontList());
            if (expanded.Count == 0)
            {
                return FontFamily.Default;
            }

            return new FontFamily(string.Join(",", expanded));
        }
    }

    /// <summary>
    /// Gets the font feature overrides applied to the ligature preview. Returns
    /// <see langword="null"/> when ligatures are enabled (so the font's default
    /// OpenType features apply) and a collection that disables <c>liga</c>,
    /// <c>clig</c>, and <c>calt</c> when ligatures are disabled.
    /// </summary>
    public FontFeatureCollection? PreviewFontFeatures
        => this.EnableLigature ? null : FontFeatureCollection.Parse("liga=0,clig=0,calt=0");

    /// <summary>
    /// Gets the font priority list items. Each item is either a plain
    /// <see cref="string"/> (user font) or a <see cref="FontPriorityItem"/>
    /// (sentinel such as <c>$SYSTEM_MONO</c>).
    /// </summary>
    public ObservableCollection<object> FontItems { get; } = new();

    /// <summary>
    /// Gets or sets the selected index in the font priority list.
    /// </summary>
    public int SelectedFontIndex
    {
        get => this.selectedFontIndex;
        set
        {
            if (this.SetField(ref this.selectedFontIndex, value))
            {
                this.OnPropertyChanged(nameof(this.CanRemoveFont));
                this.OnPropertyChanged(nameof(this.CanMoveUp));
                this.OnPropertyChanged(nameof(this.CanMoveDown));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the selected font can be removed.
    /// Sentinel items cannot be removed.
    /// </summary>
    public bool CanRemoveFont =>
        this.SelectedFontIndex >= 0 &&
        GetRawFontEntry(this.FontItems[this.SelectedFontIndex]) is string raw &&
        !FontPriorityList.IsSentinel(raw);

    /// <summary>
    /// Gets a value indicating whether the selected font can be moved up.
    /// </summary>
    public bool CanMoveUp => this.SelectedFontIndex > 0;

    /// <summary>
    /// Gets a value indicating whether the selected font can be moved down.
    /// </summary>
    public bool CanMoveDown => this.SelectedFontIndex >= 0 && this.SelectedFontIndex < this.FontItems.Count - 1;

    /// <summary>
    /// Gets or sets a value indicating whether blur behind is enabled.
    /// </summary>
    public bool EnableBlurBehind
    {
        get => this.enableBlurBehind;
        set
        {
            if (this.SetField(ref this.enableBlurBehind, value))
            {
                this.settings.EnableBlurBehind = value;
                this.OnPropertyChanged(nameof(this.IsTransparentEnabled));
                this.OnPropertyChanged(nameof(this.IsGaussianEnabled));
                this.OnPropertyChanged(nameof(this.IsAcrylicEnabled));
                this.OnPropertyChanged(nameof(this.IsMicaEnabled));
                this.OnPropertyChanged(nameof(this.IsOpacityEnabled));
            }
        }
    }

    /// <summary>
    /// Gets or sets the blur type.
    /// </summary>
    public BlurType BlurType
    {
        get => this.blurType;
        set
        {
            if (this.SetField(ref this.blurType, value))
            {
                this.settings.BlurType = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the background opacity.
    /// </summary>
    public double BackgroundOpacity
    {
        get => this.backgroundOpacity;
        set
        {
            if (this.SetField(ref this.backgroundOpacity, value))
            {
                this.settings.BackgroundOpacity = value;
                this.OnPropertyChanged(nameof(this.OpacityLabel));
            }
        }
    }

    /// <summary>
    /// Gets the opacity label text (e.g. "75%").
    /// </summary>
    public string OpacityLabel => ((int)(this.BackgroundOpacity * 100)).ToString() + "%";

    /// <summary>
    /// Gets the available color schemes.
    /// </summary>
    public IReadOnlyList<ColorScheme> ColorSchemes { get; }

    /// <summary>
    /// Gets or sets the selected color scheme.
    /// </summary>
    public ColorScheme SelectedColorScheme
    {
        get => this.selectedColorScheme;
        set
        {
            if (this.SetField(ref this.selectedColorScheme, value) && value is not null)
            {
                this.settings.ColorSchemeName = value.Name;
                this.OnPropertyChanged(nameof(this.PreviewSwatches));
            }
        }
    }

    /// <summary>
    /// Gets the first 6 palette colors of the selected scheme for preview.
    /// </summary>
    public IReadOnlyList<int> PreviewSwatches =>
        this.SelectedColorScheme.Palette.Take(6).ToArray();

    /// <summary>
    /// Gets a value indicating whether the Transparent radio option should be enabled.
    /// </summary>
    public bool IsTransparentEnabled => this.EnableBlurBehind && PlatformHelper.TransparentAvailable();

    /// <summary>
    /// Gets a value indicating whether the Gaussian radio option should be enabled.
    /// </summary>
    public bool IsGaussianEnabled => this.EnableBlurBehind && PlatformHelper.GaussianBlurAvailable();

    /// <summary>
    /// Gets a value indicating whether the Acrylic radio option should be enabled.
    /// </summary>
    public bool IsAcrylicEnabled => this.EnableBlurBehind && PlatformHelper.AcrylicBlurAvailable();

    /// <summary>
    /// Gets a value indicating whether the Mica radio option should be enabled.
    /// </summary>
    public bool IsMicaEnabled => this.EnableBlurBehind && PlatformHelper.MicaAvailable();

    /// <summary>
    /// Gets a value indicating whether the opacity slider should be enabled.
    /// </summary>
    public bool IsOpacityEnabled => this.EnableBlurBehind;

    /// <summary>
    /// Gets the available bell-action choices for data binding.
    /// </summary>
    public IReadOnlyList<BellAction> BellActions { get; } = new[]
    {
        BellAction.None,
        BellAction.Visual,
        BellAction.Audio,
        BellAction.Notification,
        BellAction.All,
    };

    /// <summary>
    /// Gets or sets how the app reacts to the terminal BEL character.
    /// </summary>
    public BellAction BellAction
    {
        get => this.bellAction;
        set
        {
            if (this.SetField(ref this.bellAction, value))
            {
                this.settings.BellAction = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the number of lines retained in the terminal's scrollback
    /// ring. Range is <c>0..100000</c>; <c>0</c> disables scrollback.
    /// </summary>
    public int ScrollbackLines
    {
        get => this.scrollbackLines;
        set
        {
            int clamped = Math.Clamp(value, 0, 100_000);
            if (this.SetField(ref this.scrollbackLines, clamped))
            {
                this.settings.ScrollbackLines = clamped;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the application should prompt
    /// the user before closing a window that still contains more than one
    /// open tab.
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
    /// Gets or sets a value indicating whether middle-click inside the
    /// terminal pastes text. On Linux/X11 the source is the PRIMARY
    /// selection, with a fallback to the regular clipboard; on macOS and
    /// Windows the regular clipboard is always used.
    /// </summary>
    public bool MiddleClickPastes
    {
        get => this.middleClickPastes;
        set
        {
            if (this.SetField(ref this.middleClickPastes, value))
            {
                this.settings.MiddleClickPastes = value;
            }
        }
    }

    /// <summary>
    /// Gets the fixed list of tab-bar orientation choices surfaced in the
    /// settings UI. Order determines the display order in the UI combo.
    /// </summary>
    public IReadOnlyList<TabBarOrientation> TabBarOrientations { get; } = new[]
    {
        TabBarOrientation.Horizontal,
        TabBarOrientation.Vertical,
    };

    /// <summary>
    /// Gets or sets the tab-bar orientation. Setting this writes straight
    /// through to <see cref="AppSettings.TabBarOrientation"/>, which
    /// raises a property change that <c>MainWindow</c> listens for to
    /// re-dock the tab strip live.
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
    /// Gets or sets a value indicating whether the Quake-mode global
    /// hotkey is active.
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
    /// Gets the brush used to render <see cref="QuakeHotkeyStatus"/>
    /// (green for valid, red for invalid).
    /// </summary>
    public IBrush QuakeHotkeyStatusBrush =>
        KeyChordParser.TryParse(this.quakeHotkey, out _)
            ? new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43))
            : new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

    /// <summary>
    /// Gets a platform-specific warning displayed when Quake-mode cannot
    /// be used on the current OS / session. Empty string when the feature
    /// is available.
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
    /// Gets a value indicating whether <see cref="QuakePlatformWarning"/>
    /// is non-empty.
    /// </summary>
    public bool HasQuakePlatformWarning => !string.IsNullOrEmpty(this.QuakePlatformWarning);

    /// <summary>
    /// Adds a font name to the priority list at the current selection index.
    /// Called by the view after the user picks a font from the font picker dialog.
    /// </summary>
    /// <param name="fontName">The font family name to add.</param>
    public void AddFont(string fontName)
    {
        int insertIndex = this.SelectedFontIndex >= 0
            ? this.SelectedFontIndex
            : this.FontItems.Count;
        this.FontItems.Insert(insertIndex, fontName);
        this.SelectedFontIndex = insertIndex;
        this.UpdateFontPriorityLive();
    }

    /// <summary>
    /// Removes the currently selected font from the priority list.
    /// </summary>
    public void RemoveFont()
    {
        if (this.SelectedFontIndex < 0)
        {
            return;
        }

        string? raw = GetRawFontEntry(this.FontItems[this.SelectedFontIndex]);
        if (raw is not null && FontPriorityList.IsSentinel(raw))
        {
            return;
        }

        this.FontItems.RemoveAt(this.SelectedFontIndex);
        this.UpdateFontPriorityLive();
    }

    /// <summary>
    /// Moves the currently selected font one position up in the priority list.
    /// </summary>
    public void MoveFontUp()
    {
        int index = this.SelectedFontIndex;
        if (index > 0)
        {
            var item = this.FontItems[index];
            this.FontItems.RemoveAt(index);
            this.FontItems.Insert(index - 1, item);
            this.SelectedFontIndex = index - 1;
            this.UpdateFontPriorityLive();
        }
    }

    /// <summary>
    /// Moves the currently selected font one position down in the priority list.
    /// </summary>
    public void MoveFontDown()
    {
        int index = this.SelectedFontIndex;
        if (index >= 0 && index < this.FontItems.Count - 1)
        {
            var item = this.FontItems[index];
            this.FontItems.RemoveAt(index);
            this.FontItems.Insert(index + 1, item);
            this.SelectedFontIndex = index + 1;
            this.UpdateFontPriorityLive();
        }
    }

    private static string? GetRawFontEntry(object? item)
    {
        if (item is FontPriorityItem sentinel)
        {
            return sentinel.Sentinel;
        }

        if (item is string fontName)
        {
            return fontName;
        }

        return null;
    }

    private static object CreateFontDisplayItem(string entry)
    {
        if (string.Equals(entry, FontPriorityList.SystemMonoSentinel, StringComparison.Ordinal))
        {
            string resolved = string.Join(", ", FontPriorityList.GetDefaultPlatformFonts());
            return new FontPriorityItem(FontPriorityList.SystemMonoSentinel, $"[System Monospace]  ({resolved})");
        }

        return entry;
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

    private List<string> GetRawFontList()
    {
        var fonts = new List<string>();
        foreach (var item in this.FontItems)
        {
            string? raw = GetRawFontEntry(item);
            if (raw is not null)
            {
                fonts.Add(raw);
            }
        }

        return fonts;
    }

    private void UpdateFontPriorityLive()
    {
        this.settings.FallbackFonts = this.GetRawFontList();
        this.OnPropertyChanged(nameof(this.PreviewFontFamily));
    }

    private void OnFontItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.OnPropertyChanged(nameof(this.PreviewFontFamily));
    }
}
