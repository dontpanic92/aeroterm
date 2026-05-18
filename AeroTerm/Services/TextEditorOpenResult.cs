// <copyright file="TextEditorOpenResult.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Result of opening a text document.
/// </summary>
/// <param name="Document">Loaded document, or <see langword="null"/> when opening failed.</param>
/// <param name="ErrorMessage">User-visible error message, or <see langword="null"/> when opening succeeded.</param>
internal sealed record TextEditorOpenResult(
    TextEditorDocument? Document,
    string? ErrorMessage);
