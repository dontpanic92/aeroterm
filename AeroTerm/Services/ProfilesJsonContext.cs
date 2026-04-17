// <copyright file="ProfilesJsonContext.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Text.Json.Serialization;

/// <summary>
/// Source-generated JSON metadata for the <c>profiles.json</c> file
/// format. Required because the main app builds with
/// <c>PublishAot=true</c>.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProfilesFile))]
internal sealed partial class ProfilesJsonContext : JsonSerializerContext
{
}
