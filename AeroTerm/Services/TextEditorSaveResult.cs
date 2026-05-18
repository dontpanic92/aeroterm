// <copyright file="TextEditorSaveResult.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

/// <summary>
/// Result of saving a text document.
/// </summary>
/// <param name="Document">Updated document metadata, or <see langword="null"/> when saving failed.</param>
/// <param name="ErrorMessage">User-visible error message, or <see langword="null"/> when saving succeeded.</param>
internal sealed record TextEditorSaveResult(
    TextEditorDocument? Document,
    string? ErrorMessage);
