// <copyright file="PaletteRowViewModel.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Dialogs;

using System;
using System.Collections.Generic;
using AeroTerm.Services;
using Avalonia.Controls.Documents;
using Avalonia.Media;

/// <summary>
/// Row-level view model for the command palette. Holds the backing
/// command plus the matched character positions so the view can
/// materialize bold / normal <see cref="Run"/> spans on demand.
/// </summary>
internal sealed class PaletteRowViewModel
{
    private readonly int[] matchedPositions;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaletteRowViewModel"/> class.
    /// </summary>
    /// <param name="command">The backing command.</param>
    /// <param name="matchedPositions">Zero-based indices in
    /// <see cref="PaletteCommand.Title"/> that matched the query. Empty
    /// for the default (no-query) listing.</param>
    public PaletteRowViewModel(PaletteCommand command, int[] matchedPositions)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(matchedPositions);
        this.Command = command;
        this.matchedPositions = matchedPositions;
    }

    /// <summary>Gets the backing command.</summary>
    public PaletteCommand Command { get; }

    /// <summary>Gets the subtitle, or <see langword="null"/> when absent.</summary>
    public string? Subtitle => this.Command.Subtitle;

    /// <summary>Gets the category tag, or <see langword="null"/> when absent.</summary>
    public string? Category => this.Command.Category;

    /// <summary>Gets a value indicating whether a subtitle should render.</summary>
    public bool HasSubtitle => !string.IsNullOrEmpty(this.Command.Subtitle);

    /// <summary>Gets a value indicating whether a category label should render.</summary>
    public bool HasCategory => !string.IsNullOrEmpty(this.Command.Category);

    /// <summary>
    /// Builds a fresh set of <see cref="Run"/> inlines for the title.
    /// Runs can only belong to one parent, so each view invocation must
    /// get its own instances.
    /// </summary>
    /// <returns>The inline runs, ready to append to a
    /// <c>TextBlock.Inlines</c> collection.</returns>
    public IReadOnlyList<Run> CreateTitleInlines()
    {
        var runs = new List<Run>();
        string title = this.Command.Title;
        if (this.matchedPositions.Length == 0)
        {
            runs.Add(new Run(title));
            return runs;
        }

        var bolded = new HashSet<int>(this.matchedPositions);
        int i = 0;
        while (i < title.Length)
        {
            bool bold = bolded.Contains(i);
            int start = i;
            while (i < title.Length && bolded.Contains(i) == bold)
            {
                i++;
            }

            var run = new Run(title.Substring(start, i - start));
            if (bold)
            {
                run.FontWeight = FontWeight.Bold;
            }

            runs.Add(run);
        }

        return runs;
    }
}
