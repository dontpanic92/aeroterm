// <copyright file="MaterialToneConverter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using System.Globalization;
using AeroTerm.WindowEffects;
using Avalonia.Data.Converters;

/// <summary>
/// Converter that maps a <see cref="MaterialTone"/> enum value to a
/// boolean for radio button two-way binding. Each static instance
/// corresponds to one material tone value.
/// </summary>
internal sealed class MaterialToneConverter : IValueConverter
{
    /// <summary>
    /// Converter instance for the Light material tone.
    /// </summary>
    public static readonly MaterialToneConverter Light = new(MaterialTone.Light);

    /// <summary>
    /// Converter instance for the Dark material tone.
    /// </summary>
    public static readonly MaterialToneConverter Dark = new(MaterialTone.Dark);

    private readonly MaterialTone targetValue;

    private MaterialToneConverter(MaterialTone targetValue)
    {
        this.targetValue = targetValue;
    }

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is MaterialTone tone && tone == this.targetValue;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? this.targetValue : Avalonia.Data.BindingOperations.DoNothing;
    }
}
