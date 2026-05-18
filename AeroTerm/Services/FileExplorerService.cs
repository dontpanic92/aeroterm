// <copyright file="FileExplorerService.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.IO;
using System.Linq;

/// <summary>
/// Enumerates filesystem entries for the Workbench file explorer.
/// </summary>
internal sealed class FileExplorerService
{
    /// <summary>
    /// Enumerates a directory without recursively walking children.
    /// </summary>
    /// <param name="directoryPath">Directory to enumerate.</param>
    /// <param name="showHidden">Whether hidden entries should be included.</param>
    /// <returns>The directory listing, including any user-visible error.</returns>
    internal FileExplorerListing EnumerateDirectory(string? directoryPath, bool showHidden)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return new FileExplorerListing(
                string.Empty,
                Array.Empty<FileExplorerEntry>(),
                "No terminal working directory is available yet.");
        }

        if (!Directory.Exists(directoryPath))
        {
            return new FileExplorerListing(
                directoryPath,
                Array.Empty<FileExplorerEntry>(),
                "The terminal working directory no longer exists.");
        }

        try
        {
            var entries = Directory.EnumerateFileSystemEntries(directoryPath)
                .Select(this.CreateEntry)
                .Where(entry => showHidden || !entry.IsHidden)
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            return new FileExplorerListing(directoryPath, entries, null);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new FileExplorerListing(directoryPath, Array.Empty<FileExplorerEntry>(), ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return new FileExplorerListing(directoryPath, Array.Empty<FileExplorerEntry>(), ex.Message);
        }
        catch (IOException ex)
        {
            return new FileExplorerListing(directoryPath, Array.Empty<FileExplorerEntry>(), ex.Message);
        }
    }

    private FileExplorerEntry CreateEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = attributes.HasFlag(FileAttributes.Directory);
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
        {
            name = path;
        }

        if (isDirectory)
        {
            var directory = new DirectoryInfo(path);
            return new FileExplorerEntry(
                name,
                path,
                FileExplorerEntryKind.Directory,
                this.IsHidden(name, attributes),
                null,
                directory.LastWriteTimeUtc);
        }

        var file = new FileInfo(path);
        return new FileExplorerEntry(
            name,
            path,
            FileExplorerEntryKind.File,
            this.IsHidden(name, attributes),
            file.Length,
            file.LastWriteTimeUtc);
    }

    private bool IsHidden(string name, FileAttributes attributes)
    {
        return attributes.HasFlag(FileAttributes.Hidden) || name.StartsWith(".", StringComparison.Ordinal);
    }
}
