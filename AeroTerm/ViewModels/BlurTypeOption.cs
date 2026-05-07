// <copyright file="BlurTypeOption.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.ViewModels;

using AeroTerm.WindowEffects;

/// <summary>
/// One entry in the window-transparency-effect dropdown on the appearance
/// settings page. Pairs a <see cref="BlurType"/> value with a user-facing
/// display label.
/// </summary>
/// <param name="Value">The underlying blur effect, or <c>null</c> when transparency is disabled (the "None" entry).</param>
/// <param name="Label">The human-readable label shown in the dropdown.</param>
internal sealed record BlurTypeOption(BlurType? Value, string Label)
{
    /// <summary>
    /// Returns the human-readable label. This override ensures the dropdown
    /// renders the label correctly even when reflection metadata for
    /// <see cref="Label"/> has been trimmed away under <c>PublishAot</c>,
    /// which would otherwise cause the default record <c>ToString</c>
    /// (e.g. <c>BlurTypeOption { Value = ..., Label = ... }</c>) to surface.
    /// </summary>
    /// <returns>The display label for this option.</returns>
    public override string ToString() => this.Label;
}
