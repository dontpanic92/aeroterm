// <copyright file="AppearancePageViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroTerm.Dialogs;
using AeroTerm.Models;
using AeroTerm.Services;
using AeroTerm.Utilities;
using AeroTerm.WindowEffects;

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
    }
}
