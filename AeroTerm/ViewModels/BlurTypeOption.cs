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
internal sealed record BlurTypeOption(BlurType? Value, string Label);
