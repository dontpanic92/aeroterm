// <copyright file="TextEditorDocument.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;

/// <summary>
/// Plain-text document loaded by the Workbench editor.
/// </summary>
/// <param name="Path">Absolute file path.</param>
/// <param name="Text">Decoded UTF-8 text.</param>
/// <param name="LastWriteTimeUtc">Last write time observed when the document was loaded or saved.</param>
/// <param name="Length">File length in bytes.</param>
internal sealed record TextEditorDocument(
    string Path,
    string Text,
    DateTime LastWriteTimeUtc,
    long Length);
