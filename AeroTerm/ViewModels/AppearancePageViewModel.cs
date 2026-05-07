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
using System.Linq;
using System.Runtime.CompilerServices;
using AeroTerm.Dialogs;
using AeroTerm.Models;
using AeroTerm.Services;
using AeroTerm.Utilities;
using AeroTerm.WindowEffects;
using Avalonia.Media;

/// <summary>
/// View model for visual appearance settings.
/// </summary>
internal sealed class AppearancePageViewModel : SettingsPageViewModel, INotifyPropertyChanged
{
    private readonly AppSettings settings;

    private bool enableLigature;
    private bool enableBlurBehind;
    private BlurType blurType;
    private MaterialTone materialTone;
    private double backgroundTintOpacity;
    private double backgroundMaterialOpacity;
    private double fontSize;
    private int selectedFontIndex = -1;
    private ColorScheme selectedColorScheme;

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
        this.materialTone = settings.MaterialTone;
        this.backgroundTintOpacity = settings.BackgroundTintOpacity;
        this.backgroundMaterialOpacity = settings.BackgroundMaterialOpacity;
        this.fontSize = settings.FontSize;

        foreach (var entry in settings.FallbackFonts)
        {
            this.FontItems.Add(CreateFontDisplayItem(entry));
        }

        this.ColorSchemes = ColorSchemePresets.All;
        this.selectedColorScheme = ColorSchemePresets.FindByName(settings.ColorSchemeName)
            ?? ColorSchemePresets.Default;

        this.BlurTypes = BuildAvailableBlurTypes();
        if (!this.BlurTypes.Any(o => o.Value.HasValue && o.Value.Value == this.blurType))
        {
            var fallback = this.BlurTypes.FirstOrDefault(o => o.Value.HasValue);
            if (fallback is not null)
            {
                this.blurType = fallback.Value!.Value;
                this.settings.BlurType = this.blurType;
            }
        }

        this.FontItems.CollectionChanged += this.OnFontItemsChanged;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public override string DisplayName => "Appearance";

    /// <inheritdoc/>
    public override IReadOnlyList<string> SearchableLabels { get; } = new[]
    {
        SettingsSearchLabels.TransparencyEffect,
        SettingsSearchLabels.MaterialTone,
        SettingsSearchLabels.TintOpacity,
        SettingsSearchLabels.MaterialOpacity,
        SettingsSearchLabels.FontLigature,
        SettingsSearchLabels.FontSize,
        SettingsSearchLabels.FontPriority,
        SettingsSearchLabels.LigaturePreview,
        SettingsSearchLabels.ColorScheme,
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
    /// Gets the effective <see cref="FontFamily"/> used by the ligature preview.
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
    /// Gets the font feature overrides applied to the ligature preview.
    /// </summary>
    public FontFeatureCollection? PreviewFontFeatures
        => this.EnableLigature ? null : FontFeatureCollection.Parse("liga=0,clig=0,calt=0");

    /// <summary>
    /// Gets the font priority list items. Each item is either a plain
    /// <see cref="string"/> or a <see cref="FontPriorityItem"/>.
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
                this.OnPropertyChanged(nameof(this.SelectedBlurOption));
                this.OnPropertyChanged(nameof(this.IsOpacityEnabled));
                this.OnPropertyChanged(nameof(this.IsMaterialToneEnabled));
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
                this.OnPropertyChanged(nameof(this.SelectedBlurOption));
                this.OnPropertyChanged(nameof(this.IsMaterialToneEnabled));
            }
        }
    }

    /// <summary>
    /// Gets the platform-supported window-transparency effect options shown
    /// in the dropdown. Always includes a leading "None" entry; effects
    /// unavailable on the current OS are omitted.
    /// </summary>
    public IReadOnlyList<BlurTypeOption> BlurTypes { get; }

    /// <summary>
    /// Gets or sets the dropdown-selected effect option. The "None" entry
    /// disables transparency; any other entry enables it and selects the
    /// underlying <see cref="BlurType"/>.
    /// </summary>
    public BlurTypeOption? SelectedBlurOption
    {
        get
        {
            if (!this.enableBlurBehind)
            {
                return this.BlurTypes.FirstOrDefault(o => o.Value is null);
            }

            return this.BlurTypes.FirstOrDefault(o => o.Value == this.blurType)
                ?? this.BlurTypes.FirstOrDefault(o => o.Value is null);
        }

        set
        {
            if (value is null)
            {
                return;
            }

            if (value.Value is null)
            {
                this.EnableBlurBehind = false;
            }
            else
            {
                this.BlurType = value.Value.Value;
                this.EnableBlurBehind = true;
            }
        }
    }

    /// <summary>
    /// Gets or sets the tonal variant of the platform material backdrop.
    /// </summary>
    public MaterialTone MaterialTone
    {
        get => this.materialTone;
        set
        {
            if (this.SetField(ref this.materialTone, value))
            {
                this.settings.MaterialTone = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the opacity of the tint color layered over the backdrop.
    /// </summary>
    public double BackgroundTintOpacity
    {
        get => this.backgroundTintOpacity;
        set
        {
            if (this.SetField(ref this.backgroundTintOpacity, value))
            {
                this.settings.BackgroundTintOpacity = value;
                this.OnPropertyChanged(nameof(this.TintOpacityLabel));
            }
        }
    }

    /// <summary>
    /// Gets or sets the opacity of the overall material layer over the backdrop.
    /// </summary>
    public double BackgroundMaterialOpacity
    {
        get => this.backgroundMaterialOpacity;
        set
        {
            if (this.SetField(ref this.backgroundMaterialOpacity, value))
            {
                this.settings.BackgroundMaterialOpacity = value;
                this.OnPropertyChanged(nameof(this.MaterialOpacityLabel));
            }
        }
    }

    /// <summary>
    /// Gets the tint opacity label text (e.g. "85%").
    /// </summary>
    public string TintOpacityLabel => ((int)(this.BackgroundTintOpacity * 100)).ToString() + "%";

    /// <summary>
    /// Gets the material opacity label text (e.g. "75%").
    /// </summary>
    public string MaterialOpacityLabel => ((int)(this.BackgroundMaterialOpacity * 100)).ToString() + "%";

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
            if (value is null)
            {
                return;
            }

            if (this.SetField(ref this.selectedColorScheme, value))
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
    /// Gets a value indicating whether the opacity slider should be enabled.
    /// </summary>
    public bool IsOpacityEnabled => this.EnableBlurBehind;

    /// <summary>
    /// Gets a value indicating whether the Material Tone radio buttons should be enabled.
    /// </summary>
    public bool IsMaterialToneEnabled => this.EnableBlurBehind && this.BlurType != BlurType.Transparent;

    /// <summary>
    /// Adds a font name to the priority list at the current selection index.
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

    private static IReadOnlyList<BlurTypeOption> BuildAvailableBlurTypes()
    {
        var list = new List<BlurTypeOption>(6)
        {
            new(null, "None"),
        };

        if (PlatformHelper.TransparentAvailable())
        {
            list.Add(new BlurTypeOption(BlurType.Transparent, "Transparent"));
        }

        if (PlatformHelper.GaussianBlurAvailable())
        {
            list.Add(new BlurTypeOption(BlurType.Gaussian, "Blur (Gaussian)"));
        }

        if (PlatformHelper.AcrylicBlurAvailable())
        {
            list.Add(new BlurTypeOption(BlurType.Acrylic, "Acrylic Blur"));
        }

        if (PlatformHelper.MicaAvailable())
        {
            list.Add(new BlurTypeOption(BlurType.Mica, "Mica"));
        }

        if (PlatformHelper.LiquidGlassAvailable())
        {
            list.Add(new BlurTypeOption(BlurType.LiquidGlass, "Liquid Glass"));
        }

        return list;
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
