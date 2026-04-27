// <copyright file="NSRect.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

using System.Runtime.InteropServices;

/// <summary>
/// AppKit <c>NSRect</c> (origin + size, all <c>CGFloat</c>/double on
/// 64-bit). Layout matches the C struct so it round-trips through
/// <c>objc_msgSend</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NSRect
{
    /// <summary>Gets or sets the X coordinate of the origin.</summary>
    public double X { get; set; }

    /// <summary>Gets or sets the Y coordinate of the origin.</summary>
    public double Y { get; set; }

    /// <summary>Gets or sets the width of the rectangle.</summary>
    public double Width { get; set; }

    /// <summary>Gets or sets the height of the rectangle.</summary>
    public double Height { get; set; }
}
