// <copyright file="AeroTermTheme.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme;

using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

/// <summary>
/// Top-level <see cref="Styles"/> entry point for the AeroTerm theme.
/// Aggregates token resource dictionaries (palettes, brushes, typography, metrics, motion)
/// and per-control <see cref="ControlTheme"/> definitions into a single
/// <see cref="Styles"/> bundle that can be added directly to
/// <c>Application.Styles</c>.
/// </summary>
/// <remarks>
/// <para>
/// Usage:
/// </para>
/// <code>
/// &lt;Application xmlns:theme="using:AeroTerm.Theme"&gt;
///   &lt;Application.Styles&gt;
///     &lt;theme:AeroTermTheme /&gt;
///   &lt;/Application.Styles&gt;
/// &lt;/Application&gt;
/// </code>
/// </remarks>
public sealed class AeroTermTheme : Styles
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AeroTermTheme"/> class.
    /// Loads the bundled XAML composition.
    /// </summary>
    public AeroTermTheme()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
