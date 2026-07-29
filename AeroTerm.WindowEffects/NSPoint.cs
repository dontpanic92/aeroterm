// <copyright file="NSPoint.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

using System.Runtime.InteropServices;

/// <summary>
/// AppKit <c>NSPoint</c> (all <c>CGFloat</c>/double on 64-bit). Layout
/// matches the C struct so it round-trips through <c>objc_msgSend</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NSPoint
{
    /// <summary>Gets or sets the X coordinate.</summary>
    public double X { get; set; }

    /// <summary>Gets or sets the Y coordinate.</summary>
    public double Y { get; set; }
}
