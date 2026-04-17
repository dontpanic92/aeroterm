// <copyright file="PaletteMruJsonContext.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Source-generated JSON metadata for the palette MRU file format (a
/// plain list of command ids).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class PaletteMruJsonContext : JsonSerializerContext
{
}
