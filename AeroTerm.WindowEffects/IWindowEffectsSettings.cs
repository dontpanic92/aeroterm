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
    /// Gets the background opacity level (0.0–1.0).
    /// </summary>
    double BackgroundOpacity { get; }
}
