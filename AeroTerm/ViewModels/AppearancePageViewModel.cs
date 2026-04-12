// <copyright file="AppearancePageViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroTerm.Models;
using AeroTerm.Services;
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
        this.backgroundOpacity = settings.BackgroundOpacity;

        this.ColorSchemes = ColorSchemePresets.All;
        this.selectedColorScheme = ColorSchemePresets.FindByName(settings.ColorSchemeName)
            ?? ColorSchemePresets.Default;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public override string DisplayName => "Appearance";

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
