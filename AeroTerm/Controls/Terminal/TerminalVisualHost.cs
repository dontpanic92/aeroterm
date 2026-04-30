// <copyright file="TerminalVisualHost.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using AeroTerm.Pty;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

/// <summary>
/// Bridges Avalonia's <see cref="DrawingContext"/> custom-draw
/// mechanism to a SkiaSharp-drawing callback. Reused across frames to
/// avoid per-frame allocation of the custom draw operation.
/// </summary>
internal sealed class TerminalVisualHost
{
    private readonly Action<SKCanvas> renderCallback;
    private readonly TerminalDrawOperation drawOperation;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalVisualHost"/> class.
    /// </summary>
    /// <param name="renderCallback">Callback invoked with the leased Skia
    /// canvas for each frame.</param>
    public TerminalVisualHost(Action<SKCanvas> renderCallback)
    {
        this.renderCallback = renderCallback ?? throw new ArgumentNullException(nameof(renderCallback));
        this.drawOperation = new TerminalDrawOperation(this.renderCallback, default);
    }

    /// <summary>
    /// Splices a live screen with scrollback rows to produce a composite
    /// screen for viewport-scrolled rendering. The topmost
    /// <paramref name="viewportOffset"/> rows (capped to screen height)
    /// come from scrollback; the remainder is live content.
    /// </summary>
    /// <param name="live">The live screen.</param>
    /// <param name="buffer">The buffer providing scrollback access.</param>
    /// <param name="viewportOffset">How many rows up in scrollback the
    /// viewport is scrolled.</param>
    /// <returns>A freshly-allocated composite screen.</returns>
    public static Screen ComposeScrollbackScreen(Screen live, TerminalBuffer buffer, int viewportOffset)
    {
        int rows = live.Cells.GetLength(0);
        int cols = live.Cells.GetLength(1);
        int historyRows = Math.Min(viewportOffset, rows);
        int scrollbackCount = buffer.ScrollbackCount;

        // Oldest scrollback row shown at the top of the viewport.
        int scrollbackStart = Math.Max(0, scrollbackCount - viewportOffset);

        var composed = new Cell[rows, cols];
        var defaultStyle = new CellStyle(ColorRef.DefaultFg, ColorRef.DefaultBg, 0, false, false, false, false, false);

        for (int i = 0; i < historyRows; i++)
        {
            int sbIndex = scrollbackStart + i;
            if (sbIndex < 0 || sbIndex >= scrollbackCount)
            {
                // Out-of-range (e.g. scrollback shrank mid-render) — fill blank.
                for (int j = 0; j < cols; j++)
                {
                    composed[i, j].Clear(ColorRef.DefaultFg, ColorRef.DefaultBg, 0);
                }

                continue;
            }

            var sbRow = buffer.GetScrollbackLine(sbIndex);
            int copyCols = Math.Min(cols, sbRow.Length);
            for (int j = 0; j < copyCols; j++)
            {
                composed[i, j] = sbRow[j];
            }

            for (int j = copyCols; j < cols; j++)
            {
                composed[i, j].Set(" ", defaultStyle);
            }
        }

        int liveRowsToShow = rows - historyRows;
        for (int i = 0; i < liveRowsToShow; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                composed[historyRows + i, j] = live.Cells[i, j];
            }
        }

        return new Screen
        {
            Cells = composed,
            CursorPosition = live.CursorPosition,
            ForegroundColor = live.ForegroundColor,
            BackgroundColor = live.BackgroundColor,
            Palette = live.Palette,
            AllDirty = true,
            DirtyRows = null,
        };
    }

    /// <summary>
    /// Projects an absolute-row match list into screen-row coordinates
    /// for the currently composed frame. Returns <see langword="null"/>
    /// when no matches are on-screen. In alt-buffer mode absolute rows
    /// are just the live grid (no scrollback); otherwise scrollback rows
    /// occupy absolute rows <c>[0, scrollbackCount)</c>.
    /// </summary>
    /// <param name="matches">Absolute-row match list from the search engine.</param>
    /// <param name="activeMatchIndex">Index of the currently-highlighted match, or <c>-1</c>.</param>
    /// <param name="renderScreen">The screen that will actually be rendered.</param>
    /// <param name="scrollbackCount">Snapshot scrollback count the matches were computed against.</param>
    /// <param name="viewportOffset">Current viewport offset.</param>
    /// <param name="altBuffer">Whether the matches were computed against the alt buffer.</param>
    /// <returns>Visible matches in screen-row coordinates, or <see langword="null"/>.</returns>
    public static IReadOnlyList<VisibleMatch>? ProjectVisibleMatches(
        IReadOnlyList<SearchMatch> matches,
        int activeMatchIndex,
        Screen renderScreen,
        int scrollbackCount,
        int viewportOffset,
        bool altBuffer)
    {
        if (matches.Count == 0)
        {
            return null;
        }

        int screenRows = renderScreen.Cells.GetLength(0);
        int screenCols = renderScreen.Cells.GetLength(1);

        // Absolute row of the top-most visible row.
        int topAbsolute = altBuffer ? 0 : scrollbackCount - viewportOffset;
        int bottomAbsolute = topAbsolute + screenRows - 1;

        var projected = new List<VisibleMatch>();
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            if (m.AbsoluteRow < topAbsolute || m.AbsoluteRow > bottomAbsolute)
            {
                continue;
            }

            int screenRow = m.AbsoluteRow - topAbsolute;
            int startCol = Math.Min(m.StartCol, screenCols - 1);
            if (startCol < 0)
            {
                continue;
            }

            int cellLength = m.CellLength;
            if (startCol + cellLength > screenCols)
            {
                cellLength = screenCols - startCol;
            }

            if (cellLength <= 0)
            {
                continue;
            }

            projected.Add(new VisibleMatch(screenRow, startCol, cellLength, i == activeMatchIndex));
        }

        return projected.Count == 0 ? null : projected;
    }

    /// <summary>
    /// Issues the custom draw operation against the supplied Avalonia
    /// drawing context.
    /// </summary>
    /// <param name="context">The Avalonia drawing context.</param>
    /// <param name="bounds">The control's bounds for the operation.</param>
    public void Render(DrawingContext context, Rect bounds)
    {
        this.drawOperation.Bounds = bounds;
        context.Custom(this.drawOperation);
    }

    private sealed class TerminalDrawOperation : ICustomDrawOperation
    {
        private readonly Action<SKCanvas> renderCallback;

        public TerminalDrawOperation(Action<SKCanvas> renderCallback, Rect bounds)
        {
            this.renderCallback = renderCallback;
            this.Bounds = bounds;
        }

        public Rect Bounds { get; set; }

        public void Dispose()
        {
        }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => this.Bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            this.renderCallback(canvas);
        }
    }
}
