// <copyright file="IWindowEffectsSettings.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

using System.ComponentModel;

/// <summary>
/// Defines the settings required by the window effects service.
/// </summary>
public interface IWindowEffectsSettings : INotifyPropertyChanged
{
    /// <summary>
    /// Gets a value indicating whether blur-behind transparency is enabled.
    /// </summary>
    bool EnableBlurBehind { get; }

    /// <summary>
    /// Gets the blur effect type to apply.
    /// </summary>
    BlurType BlurType { get; }

    /// <summary>
    /// Gets the opacity of the tint color layered over the platform blur
    /// backdrop (0.0–1.0). Combined multiplicatively with
    /// <see cref="BackgroundMaterialOpacity"/> to produce the effective
    /// alpha of the window background brush. Mirrors
    /// <c>ExperimentalAcrylicMaterial.TintOpacity</c> in Avalonia.
    /// </summary>
    double BackgroundTintOpacity { get; }

    /// <summary>
    /// Gets the opacity of the overall material layer (0.0–1.0). Lower
    /// values let more of the platform backdrop show through. Combined
    /// multiplicatively with <see cref="BackgroundTintOpacity"/> to
    /// produce the effective alpha. Mirrors
    /// <c>ExperimentalAcrylicMaterial.MaterialOpacity</c> in Avalonia.
    /// </summary>
    double BackgroundMaterialOpacity { get; }

    /// <summary>
    /// Gets the tonal variant (light or dark) used for the platform
    /// blur / acrylic / mica / vibrancy backdrop. Applied independently
    /// of Avalonia's theme variant; ignored when blur is disabled, when
    /// <see cref="BlurType.Transparent"/> is selected, or on Linux.
    /// </summary>
    MaterialTone MaterialTone { get; }
}
