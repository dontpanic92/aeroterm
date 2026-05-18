// <copyright file="FileExplorerListing.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Collections.Generic;

/// <summary>
/// Result of enumerating a directory for the Workbench file explorer.
/// </summary>
/// <param name="DirectoryPath">Directory that was enumerated.</param>
/// <param name="Entries">Entries that could be read.</param>
/// <param name="ErrorMessage">User-visible error message, or <see langword="null"/> when enumeration succeeded.</param>
internal sealed record FileExplorerListing(
    string DirectoryPath,
    IReadOnlyList<FileExplorerEntry> Entries,
    string? ErrorMessage);
