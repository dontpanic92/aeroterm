// <copyright file="NativeMessageBoxPlatformAdapter.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.NativeMessageBoxes;

using System.Runtime.InteropServices;

/// <summary>
/// Selects the native message-box adapter for the current platform.
/// </summary>
internal static class NativeMessageBoxPlatformAdapter
{
    private static readonly INativeMessageBoxPlatformAdapter Instance = Create();

    /// <summary>
    /// Gets the platform adapter for the current process.
    /// </summary>
    internal static INativeMessageBoxPlatformAdapter Current => Instance;

    private static INativeMessageBoxPlatformAdapter Create()
    {
        var avaloniaFallback = new AvaloniaNativeMessageBoxAdapter();
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new MacOSNativeMessageBoxAdapter(avaloniaFallback)
            : avaloniaFallback;
    }
}
