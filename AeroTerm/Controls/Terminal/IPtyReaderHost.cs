// <copyright file="IPtyReaderHost.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

/// <summary>
/// Callback surface the PTY reader thread uses to hand per-chunk work
/// back to the hosting <see cref="TerminalControl"/>. Called on the
/// reader thread unless otherwise noted.
/// </summary>
internal interface IPtyReaderHost
{
    /// <summary>
    /// Gets a value indicating whether DECSET 2026 synchronized-output
    /// mode is currently enabled on the buffer.
    /// </summary>
    bool SynchronizedOutput { get; }

    /// <summary>
    /// Feeds a chunk of PTY bytes through the parser into the buffer and
    /// returns the net scrollback growth in rows (can be zero when the
    /// ring was already full).
    /// </summary>
    /// <param name="bytes">The bytes just read from the PTY.</param>
    /// <returns>Scrollback-row delta added by this chunk.</returns>
    int ProcessAndReportScrollbackDelta(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Called immediately after <see cref="ProcessAndReportScrollbackDelta"/>
    /// on the reader thread with the computed scrollback delta; used to
    /// adjust the viewport and clear the selection if necessary.
    /// </summary>
    /// <param name="scrollbackDelta">Net rows added to scrollback.</param>
    void OnAfterParse(int scrollbackDelta);

    /// <summary>
    /// Invoked on the UI thread at most once per coalesced redraw batch.
    /// Implementers should sync terminal state and invalidate the visual.
    /// </summary>
    void OnRedrawTick();
}
