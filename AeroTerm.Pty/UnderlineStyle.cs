// <copyright file="UnderlineStyle.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

/// <summary>
/// Underline decoration variants supported by the VT SGR 4 / 4:n / 21 parameters.
/// </summary>
public enum UnderlineStyle
{
    /// <summary>No underline (SGR 24, SGR 4:0).</summary>
    None = 0,

    /// <summary>Single horizontal underline (SGR 4).</summary>
    Single = 1,

    /// <summary>Double horizontal underline (SGR 21, SGR 4:2).</summary>
    Double = 2,

    /// <summary>Curly / sine-wave underline, also known as undercurl (SGR 4:3).</summary>
    Curly = 3,

    /// <summary>Dotted underline (SGR 4:4). Rendering may fall back to single.</summary>
    Dotted = 4,

    /// <summary>Dashed underline (SGR 4:5). Rendering may fall back to single.</summary>
    Dashed = 5,
}
