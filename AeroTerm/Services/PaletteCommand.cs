// <copyright file="PaletteCommand.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Threading.Tasks;

/// <summary>
/// A single invokable command exposed through the Command Palette.
/// </summary>
/// <param name="Id">Stable identifier. Used as the key for MRU ordering
/// across sessions — e.g. <c>tab.new</c>, <c>profile.activate.&lt;guid&gt;</c>,
/// <c>scheme.activate.Dracula</c>. Rename carefully: a changed id drops
/// its MRU history.</param>
/// <param name="Title">Primary label displayed in the palette row.</param>
/// <param name="Subtitle">Optional second line; <see langword="null"/>
/// if the command does not need one.</param>
/// <param name="Category">Optional category tag (e.g. "Tab", "Profile",
/// "Color scheme"). Rendered muted next to the title.</param>
/// <param name="Execute">Async command body. Awaited so commands that
/// open dialogs (e.g. Open Settings) can complete before the palette
/// considers itself finished.</param>
public sealed record PaletteCommand(
    string Id,
    string Title,
    string? Subtitle,
    string? Category,
    Func<ValueTask> Execute);
