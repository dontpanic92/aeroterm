// <copyright file="AvaloniaNativeMessageBoxAdapter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.NativeMessageBoxes;

using System;
using System.Threading.Tasks;
using AeroTerm.Theme.Controls;
using Avalonia.Controls;

/// <summary>
/// Avalonia-backed message-box adapter used on Windows, Linux, and unsupported
/// native message-box paths.
/// </summary>
internal sealed class AvaloniaNativeMessageBoxAdapter : INativeMessageBoxPlatformAdapter
{
    /// <inheritdoc/>
    public Task<NativeMessageBoxResult> ShowAsync(Window owner, NativeMessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(options);

        var window = CreateWindow(options);
        return window.ShowMessageBoxAsync(owner);
    }

    /// <summary>
    /// Creates an Avalonia message-box window for tests and fallback display.
    /// </summary>
    /// <param name="options">The message-box options.</param>
    /// <returns>The configured window.</returns>
    internal static AvaloniaMessageBoxWindow CreateWindow(NativeMessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new AvaloniaMessageBoxWindow(options);
    }
}
