// <copyright file="NSEdgeInsets.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.WindowEffects;

using System.Runtime.InteropServices;

/// <summary>
/// AppKit <c>NSEdgeInsets</c> (all <c>CGFloat</c>/double on 64-bit).
/// Field order matches the C struct (<c>top, left, bottom, right</c>) so
/// it round-trips through <c>objc_msgSend</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NSEdgeInsets
{
    /// <summary>Gets or sets the top inset.</summary>
    public double Top { get; set; }

    /// <summary>Gets or sets the left inset.</summary>
    public double Left { get; set; }

    /// <summary>Gets or sets the bottom inset.</summary>
    public double Bottom { get; set; }

    /// <summary>Gets or sets the right inset.</summary>
    public double Right { get; set; }
}
