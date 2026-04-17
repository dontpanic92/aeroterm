// <copyright file="PromptMarksRegistry.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

using System.Collections.Generic;

/// <summary>
/// In-memory store of <see cref="PromptMark"/>s captured while parsing
/// OSC 133 / OSC 633 sequences. Not persisted beyond the session.
/// </summary>
/// <remarks>
/// Marks are keyed by <see cref="PromptMark.AbsoluteRow"/> (<c>ScrollbackCount
/// + CursorRow</c> at capture). This mapping stays stable while the
/// scrollback ring has not yet reached its limit; once the ring saturates
/// and begins evicting the oldest rows, previously-captured marks may
/// drift relative to the currently-visible scrollback. Full preservation
/// across scrollback-eviction and buffer reflow is a deferred enhancement
/// (see roadmap §2.7).
/// </remarks>
public sealed class PromptMarksRegistry
{
    private readonly List<PromptMark> marks = new();

    /// <summary>
    /// Gets a snapshot of all tracked marks in insertion order.
    /// </summary>
    public IReadOnlyList<PromptMark> Marks => this.marks;

    /// <summary>
    /// Appends <paramref name="mark"/> to the registry.
    /// </summary>
    /// <param name="mark">The mark to add.</param>
    public void Add(PromptMark mark)
    {
        this.marks.Add(mark);
    }

    /// <summary>Clears every stored mark.</summary>
    public void Clear() => this.marks.Clear();

    /// <summary>
    /// Returns the mark with the greatest <see cref="PromptMark.AbsoluteRow"/>
    /// strictly less than <paramref name="currentAbsoluteRow"/> whose kind
    /// is considered "navigable" (i.e. <see cref="PromptMarkKind.OutputStart"/>
    /// or <see cref="PromptMarkKind.CommandStart"/>). Returns
    /// <see langword="null"/> when none exists.
    /// </summary>
    /// <param name="currentAbsoluteRow">Viewport anchor in absolute rows.</param>
    /// <returns>The previous navigable mark, or <see langword="null"/>.</returns>
    public PromptMark? FindPrevious(int currentAbsoluteRow)
    {
        PromptMark? best = null;
        for (int i = 0; i < this.marks.Count; i++)
        {
            var m = this.marks[i];
            if (!IsNavigable(m.Kind))
            {
                continue;
            }

            if (m.AbsoluteRow >= currentAbsoluteRow)
            {
                continue;
            }

            if (best is null || m.AbsoluteRow > best.AbsoluteRow)
            {
                best = m;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the mark with the smallest <see cref="PromptMark.AbsoluteRow"/>
    /// strictly greater than <paramref name="currentAbsoluteRow"/> whose kind
    /// is considered navigable. Returns <see langword="null"/> when none
    /// exists.
    /// </summary>
    /// <param name="currentAbsoluteRow">Viewport anchor in absolute rows.</param>
    /// <returns>The next navigable mark, or <see langword="null"/>.</returns>
    public PromptMark? FindNext(int currentAbsoluteRow)
    {
        PromptMark? best = null;
        for (int i = 0; i < this.marks.Count; i++)
        {
            var m = this.marks[i];
            if (!IsNavigable(m.Kind))
            {
                continue;
            }

            if (m.AbsoluteRow <= currentAbsoluteRow)
            {
                continue;
            }

            if (best is null || m.AbsoluteRow < best.AbsoluteRow)
            {
                best = m;
            }
        }

        return best;
    }

    /// <summary>
    /// Removes any mark whose <see cref="PromptMark.AbsoluteRow"/> is less
    /// than <paramref name="minAbsoluteRow"/>. Intended for pruning entries
    /// that have scrolled out of the retained scrollback window.
    /// </summary>
    /// <param name="minAbsoluteRow">Inclusive lower bound to retain.</param>
    public void PruneBelow(int minAbsoluteRow)
    {
        this.marks.RemoveAll(m => m.AbsoluteRow < minAbsoluteRow);
    }

    private static bool IsNavigable(PromptMarkKind kind) =>
        kind == PromptMarkKind.OutputStart || kind == PromptMarkKind.CommandStart;
}
