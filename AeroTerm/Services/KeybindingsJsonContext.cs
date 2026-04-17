// <copyright file="KeybindingsJsonContext.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Text.Json.Serialization;

/// <summary>
/// Source-generated JSON metadata for the keybindings file format.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(KeybindingsFile))]
internal sealed partial class KeybindingsJsonContext : JsonSerializerContext
{
}
