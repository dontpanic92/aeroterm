// <copyright file="TextEditorService.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.IO;
using System.Text;

/// <summary>
/// Loads and saves UTF-8 text files for the Workbench editor.
/// </summary>
internal sealed class TextEditorService
{
    /// <summary>
    /// Maximum size, in bytes, that the lightweight editor opens.
    /// </summary>
    internal const long MaxEditableBytes = 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Opens a UTF-8 text file.
    /// </summary>
    /// <param name="path">Path to open.</param>
    /// <returns>The opened document or a user-visible error.</returns>
    internal TextEditorOpenResult Open(string path)
    {
        if (!File.Exists(path))
        {
            return new TextEditorOpenResult(null, "The selected file no longer exists.");
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxEditableBytes)
            {
                return new TextEditorOpenResult(
                    null,
                    $"The file is larger than the {MaxEditableBytes / 1024 / 1024} MiB editor limit.");
            }

            var bytes = File.ReadAllBytes(path);
            if (this.ContainsBinaryData(bytes))
            {
                return new TextEditorOpenResult(null, "Binary files cannot be opened in the lightweight editor.");
            }

            var text = StrictUtf8.GetString(bytes);
            return new TextEditorOpenResult(
                new TextEditorDocument(path, text, info.LastWriteTimeUtc, info.Length),
                null);
        }
        catch (DecoderFallbackException ex)
        {
            return new TextEditorOpenResult(null, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new TextEditorOpenResult(null, ex.Message);
        }
        catch (IOException ex)
        {
            return new TextEditorOpenResult(null, ex.Message);
        }
    }

    /// <summary>
    /// Saves a UTF-8 text file.
    /// </summary>
    /// <param name="document">Document metadata from the last load or save.</param>
    /// <param name="text">New document text.</param>
    /// <param name="overwriteExternalChanges">Whether to overwrite a file modified outside the editor.</param>
    /// <returns>The updated document metadata or a user-visible error.</returns>
    internal TextEditorSaveResult Save(
        TextEditorDocument document,
        string text,
        bool overwriteExternalChanges = false)
    {
        try
        {
            if (File.Exists(document.Path))
            {
                var current = new FileInfo(document.Path);
                if (!overwriteExternalChanges && current.LastWriteTimeUtc != document.LastWriteTimeUtc)
                {
                    return new TextEditorSaveResult(
                        null,
                        "The file changed on disk. Reload it before saving, or save again after reopening.");
                }
            }

            File.WriteAllText(document.Path, text, StrictUtf8);
            var updated = new FileInfo(document.Path);
            return new TextEditorSaveResult(
                new TextEditorDocument(document.Path, text, updated.LastWriteTimeUtc, updated.Length),
                null);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new TextEditorSaveResult(null, ex.Message);
        }
        catch (IOException ex)
        {
            return new TextEditorSaveResult(null, ex.Message);
        }
    }

    private bool ContainsBinaryData(byte[] bytes)
    {
        foreach (var value in bytes)
        {
            if (value == 0)
            {
                return true;
            }
        }

        return false;
    }
}
