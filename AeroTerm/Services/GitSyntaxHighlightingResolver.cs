// <copyright file="GitSyntaxHighlightingResolver.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.IO;
using AvaloniaEdit.Highlighting;

/// <summary>
/// Resolves Git diff syntax highlighting from file names and extensions.
/// </summary>
internal static class GitSyntaxHighlightingResolver
{
    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".markdown",
        ".mdown",
        ".mkd",
        ".mkdn",
    };

    private static readonly IReadOnlyDictionary<string, string> FileNameAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".babelrc"] = "Json",
            [".eslintrc"] = "Json",
            [".prettierrc"] = "Json",
            [".stylelintrc"] = "Json",
        };

    private static readonly IReadOnlyDictionary<string, string> ExtensionAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".axaml"] = "XML",
            [".cls"] = "TeX",
            [".cjs"] = "JavaScript",
            [".cshtml"] = "ASP/XHTML",
            [".csx"] = "C#",
            [".cts"] = "JavaScript",
            [".cxx"] = "C++",
            [".ddl"] = "TSQL",
            [".dml"] = "TSQL",
            [".fsproj"] = "XML",
            [".hh"] = "C++",
            [".hxx"] = "C++",
            [".inl"] = "C++",
            [".ipynb"] = "Json",
            [".ipp"] = "C++",
            [".json5"] = "Json",
            [".jsonc"] = "Json",
            [".jsonl"] = "Json",
            [".jsx"] = "JavaScript",
            [".less"] = "CSS",
            [".mjs"] = "JavaScript",
            [".mts"] = "JavaScript",
            [".phtml"] = "PHP",
            [".plist"] = "XML",
            [".props"] = "XML",
            [".pyi"] = "Python",
            [".razor"] = "ASP/XHTML",
            [".resx"] = "XML",
            [".ruleset"] = "XML",
            [".sass"] = "CSS",
            [".scss"] = "CSS",
            [".shtml"] = "HTML",
            [".slnx"] = "XML",
            [".sty"] = "TeX",
            [".svg"] = "XML",
            [".targets"] = "XML",
            [".ts"] = "JavaScript",
            [".tsx"] = "JavaScript",
            [".vcxproj"] = "XML",
            [".webmanifest"] = "Json",
            [".xlf"] = "XML",
        };

    /// <summary>
    /// Resolves a built-in highlighting definition for a repository-relative path.
    /// </summary>
    /// <param name="path">The repository-relative file path.</param>
    /// <returns>The compatible built-in definition, or <see langword="null"/> for plain text.</returns>
    internal static IHighlightingDefinition? Resolve(string path)
    {
        var extension = Path.GetExtension(path);
        if (MarkdownExtensions.Contains(extension))
        {
            return null;
        }

        var manager = HighlightingManager.Instance;
        var builtIn = manager.GetDefinitionByExtension(extension);
        if (builtIn is not null)
        {
            return builtIn;
        }

        var fileName = Path.GetFileName(path);
        if (FileNameAliases.TryGetValue(fileName, out var fileDefinitionName))
        {
            return manager.GetDefinition(fileDefinitionName);
        }

        return ExtensionAliases.TryGetValue(extension, out var extensionDefinitionName)
            ? manager.GetDefinition(extensionDefinitionName)
            : null;
    }
}
