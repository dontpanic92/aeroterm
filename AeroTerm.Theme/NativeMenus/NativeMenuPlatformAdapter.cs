// <copyright file="NativeMenuPlatformAdapter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.NativeMenus;

using System.Runtime.InteropServices;

/// <summary>
/// Selects the native menu adapter for the current platform.
/// </summary>
internal static class NativeMenuPlatformAdapter
{
    private static readonly INativeMenuPlatformAdapter Instance = Create();

    /// <summary>
    /// Gets the platform adapter for the current process.
    /// </summary>
    public static INativeMenuPlatformAdapter Current => Instance;

    private static INativeMenuPlatformAdapter Create()
    {
        var avaloniaFallback = new AvaloniaNativeMenuAdapter();
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new MacOSNativeMenuAdapter(avaloniaFallback)
            : avaloniaFallback;
    }
}
