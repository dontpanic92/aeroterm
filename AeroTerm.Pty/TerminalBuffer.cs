// <copyright file="TerminalBuffer.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Pty;

using System.Collections.Generic;

/// <summary>
/// Maintains the terminal cell grid state that the VT parser modifies.
/// </summary>
public class TerminalBuffer
{
    /// <summary>
    /// Default maximum number of lines retained in the scrollback ring.
    /// </summary>
    public const int DefaultScrollbackLimit = 10_000;

    /// <summary>
    /// Maximum value accepted for <see cref="ScrollbackLimit"/>.
    /// </summary>
    /// <remarks>
    /// At ~80 bytes per <see cref="Cell"/> and an 80-column screen this cap
    /// bounds worst-case scrollback memory at roughly 6.1 GiB per buffer;
    /// realistic terminals with modest column counts stay well under a GiB.
    /// </remarks>
    public const int MaxScrollbackLimit = 1_000_000;

    private static readonly int[] DefaultPalette = CreateDefaultPalette();

    private readonly object screenLock = new();
    private readonly Screen screen = new() { Cells = new Cell[0, 0] };

    private Cell[,] cells;
    private Cell[,]? altCells;
    private bool[]? altRowWrapped;
    private bool usingAltBuffer;

    private int cursorRow;
    private int cursorCol;
    private int savedCursorRow;
    private int savedCursorCol;
    private int savedFg = -1;
    private int savedBg = -1;
    private int savedSpecial;
    private bool savedBold;
    private bool savedItalic;
    private bool savedUnderline;
    private bool savedUndercurl;
    private bool savedDoubleUnderline;
    private bool savedReverse;
    private bool savedDim;
    private bool savedStrikethrough;
    private bool savedHidden;
    private bool savedBlink;
    private bool savedOverline;
    private bool savedAutoWrap = true;
    private int savedGlCharset;
    private bool savedG0IsLineDrawing;
    private bool savedG1IsLineDrawing;

    private int scrollTop;
    private int scrollBottom;

    private int currentFg = -1;
    private int currentBg = -1;
    private int currentSpecial;
    private bool bold;
    private bool italic;
    private bool underline;
    private bool undercurl;
    private bool doubleUnderline;
    private bool reverse;
    private bool dim;
    private bool strikethrough;
    private bool hidden;
    private bool blink;
    private bool overline;

    private bool[] dirtyRows;
    private bool allDirty;

    private int defaultFg = 0x000000;
    private int defaultBg = 0xFFFFFF;
    private int detectedBg = 0xFFFFFF;

    // Character set state: G0–G3 designations, and which is active (GL).
    private bool g0IsLineDrawing;
    private bool g1IsLineDrawing;
    private bool g2IsLineDrawing;
    private bool g3IsLineDrawing;
    private int glCharset; // 0=G0 (default), 1=G1, etc.
    private int singleShiftCharset = -1; // -1 = none, 2 = SS2 (G2), 3 = SS3 (G3)
    private bool[] tabStops;
    private int[] palette = (int[])DefaultPalette.Clone();
    private int cursorColor = -1;

    // OSC 8 hyperlink state currently being stamped onto new cells.
    private string? currentHyperlinkUri;
    private string? currentHyperlinkId;

    private Dictionary<int, int> bgHistogram = new(16);
    private bool bgHistogramValid;

    // Scrollback ring: bounded circular buffer of rows evicted from the top
    // of the primary screen during full-region scroll-ups. Lines are stored
    // as jagged rows preserving their original column count (no reflow on
    // resize).
    private Cell[]?[] scrollbackRing = new Cell[]?[DefaultScrollbackLimit];

    // Parallel to scrollbackRing: whether the evicted row was marked as
    // line-wrapped (i.e. its logical line continues into the next row).
    // Indexed the same way as scrollbackRing (slot = (head + i) % limit).
    private bool[] scrollbackWrappedRing = new bool[DefaultScrollbackLimit];
    private int scrollbackHead;
    private int scrollbackCount;
    private int scrollbackLimit = DefaultScrollbackLimit;

    // Monotonic counter of rows overwritten by the scrollback ring. Used by
    // consumers (e.g. selection) that anchor to absolute-row positions and
    // need to detect/compensate for ring eviction. Only incremented on the
    // ring-overwrite path; not reset by ClearScrollback (callers that care
    // about clearing should observe the buffer state directly).
    private long scrollbackEvictedTotal;

    // Per-row "line-wrapped" marker on the primary (or alt) grid. When
    // rowWrapped[r] is true, row r+1 is understood as a continuation of
    // row r (the cursor auto-wrapped at the right margin while writing
    // row r). Reflow concatenates such rows into a single logical line.
    private bool[] rowWrapped;

    private bool synchronizedOutput;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalBuffer"/> class.
    /// </summary>
    /// <param name="cols">Initial column count.</param>
    /// <param name="rows">Initial row count.</param>
    public TerminalBuffer(int cols, int rows)
    {
        this.Rows = rows;
        this.Cols = cols;
        this.cells = new Cell[rows, cols];
        this.dirtyRows = new bool[rows];
        this.rowWrapped = new bool[rows];
        this.tabStops = CreateDefaultTabStops(cols);
        this.scrollBottom = rows - 1;
        this.ClearRegion(0, 0, rows - 1, cols - 1);
        this.allDirty = true;
    }

    /// <summary>
    /// Raised whenever <see cref="SynchronizedOutput"/> transitions. The boolean
    /// payload is the new value. Raised on the thread that mutated the property
    /// (typically the PTY reader thread while the VT parser is running).
    /// </summary>
    public event EventHandler<bool>? SynchronizedOutputChanged;

    /// <summary>
    /// Gets the row count.
    /// </summary>
    public int Rows { get; private set; }

    /// <summary>
    /// Gets the column count.
    /// </summary>
    public int Cols { get; private set; }

    /// <summary>
    /// Gets the current cursor row.
    /// </summary>
    public int CursorRow => this.cursorRow;

    /// <summary>
    /// Gets the current cursor column.
    /// </summary>
    public int CursorCol => this.cursorCol;

    /// <summary>
    /// Gets or sets a value indicating whether the cursor is visible.
    /// </summary>
    public bool CursorVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether SGR mouse mode is enabled.
    /// </summary>
    public bool SgrMouseEnabled { get; set; }

    /// <summary>
    /// Gets or sets the current mouse tracking mode.
    /// </summary>
    public MouseTrackingMode MouseTrackingMode { get; set; }

    /// <summary>
    /// Gets or sets the mouse pointer shape name requested via OSC 22.
    /// Null means no shape was requested (use default).
    /// </summary>
    public string? PointerShape { get; set; }

    /// <summary>
    /// Gets or sets the text cursor shape requested via DECSCUSR.
    /// Null means no shape was requested (use default).
    /// </summary>
    public CursorShape? RequestedCursorShape { get; set; }

    /// <summary>
    /// Gets or sets the cursor blinking policy requested via DECSCUSR.
    /// </summary>
    public CursorBlinking RequestedCursorBlinking { get; set; } = CursorBlinking.BlinkOff;

    /// <summary>
    /// Gets or sets the pointer auto-hide mode set via XTSMPOINTER (CSI > Ps p).
    /// 0 = never hide, 1 = hide when tracking not enabled (default), 2 = always hide, 3 = always hide even on leave.
    /// </summary>
    public int PointerMode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether application cursor keys mode (DECCKM) is enabled.
    /// When enabled, arrow keys send SS3 sequences (ESC O A) instead of CSI sequences (ESC [ A).
    /// </summary>
    public bool ApplicationCursorKeys { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether screen-level reverse video (DECSCNM) is enabled.
    /// </summary>
    public bool ReverseVideo { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether origin mode (DECOM) is enabled.
    /// When enabled, cursor positioning is relative to the scroll region.
    /// </summary>
    public bool OriginMode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether auto-wrap mode (DECAWM) is enabled.
    /// When enabled (default), writing past the last column wraps to the next line.
    /// </summary>
    public bool AutoWrap { get; set; } = true;

    /// <summary>
    /// Gets a value indicating whether the cursor is in the pending-wrap state.
    /// This is set when a character is written to the last column with auto-wrap
    /// enabled; the actual wrap occurs on the next printable character.
    /// </summary>
    public bool PendingWrap { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether bracketed paste mode is enabled.
    /// When enabled, pasted text should be wrapped in ESC[200~ ... ESC[201~.
    /// </summary>
    public bool BracketedPasteEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether synchronized output mode (DECSET 2026) is enabled.
    /// When enabled, screen updates should be batched until the mode is reset. Setting this
    /// property raises <see cref="SynchronizedOutputChanged"/> on transitions.
    /// </summary>
    public bool SynchronizedOutput
    {
        get => this.synchronizedOutput;
        set
        {
            if (this.synchronizedOutput == value)
            {
                return;
            }

            this.synchronizedOutput = value;
            this.SynchronizedOutputChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether focus event reporting is enabled.
    /// When enabled, the terminal should send ESC[I on focus-in and ESC[O on focus-out.
    /// </summary>
    public bool FocusEventsEnabled { get; set; }

    /// <summary>
    /// Gets the default foreground color.
    /// </summary>
    public int DefaultForeground => this.defaultFg;

    /// <summary>
    /// Gets the default background color.
    /// </summary>
    public int DefaultBackground => this.defaultBg;

    /// <summary>
    /// Gets the current cursor color used for OSC 12 queries.
    /// </summary>
    public int CursorColor => this.cursorColor >= 0 ? this.cursorColor : this.defaultFg;

    /// <summary>
    /// Gets or sets the maximum number of lines retained in the primary-buffer
    /// scrollback ring. Clamped to the range <c>[0, <see cref="MaxScrollbackLimit"/>]</c>.
    /// A value of 0 disables scrollback capture and clears any existing entries.
    /// Shrinking drops the oldest lines and preserves the most recent ones.
    /// </summary>
    public int ScrollbackLimit
    {
        get
        {
            lock (this.screenLock)
            {
                return this.scrollbackLimit;
            }
        }

        set
        {
            int clamped = Math.Clamp(value, 0, MaxScrollbackLimit);
            lock (this.screenLock)
            {
                if (clamped == this.scrollbackLimit)
                {
                    return;
                }

                if (clamped == 0)
                {
                    this.scrollbackEvictedTotal += this.scrollbackCount;
                    this.scrollbackRing = Array.Empty<Cell[]?>();
                    this.scrollbackWrappedRing = Array.Empty<bool>();
                    this.scrollbackHead = 0;
                    this.scrollbackCount = 0;
                    this.scrollbackLimit = 0;
                    return;
                }

                // Reallocate, preserving the newest lines (drop oldest if shrinking).
                int keep = Math.Min(this.scrollbackCount, clamped);
                int skip = this.scrollbackCount - keep;
                this.scrollbackEvictedTotal += skip;
                var newRing = new Cell[]?[clamped];
                var newWrapped = new bool[clamped];
                for (int i = 0; i < keep; i++)
                {
                    int src = (this.scrollbackHead + skip + i) % Math.Max(this.scrollbackLimit, 1);
                    newRing[i] = this.scrollbackRing[src];
                    newWrapped[i] = src < this.scrollbackWrappedRing.Length && this.scrollbackWrappedRing[src];
                }

                this.scrollbackRing = newRing;
                this.scrollbackWrappedRing = newWrapped;
                this.scrollbackHead = 0;
                this.scrollbackCount = keep;
                this.scrollbackLimit = clamped;
            }
        }
    }

    /// <summary>
    /// Gets the number of lines currently retained in the scrollback ring.
    /// </summary>
    public int ScrollbackCount
    {
        get
        {
            lock (this.screenLock)
            {
                return this.scrollbackCount;
            }
        }
    }

    /// <summary>
    /// Gets the monotonic count of scrollback rows that have been
    /// overwritten (evicted from the ring) since this buffer was created.
    /// Combined with <see cref="ScrollbackCount"/> this lets callers detect
    /// eviction between observations: if the value increased by N, the
    /// oldest N scrollback indices have been replaced and any absolute-row
    /// reference into them is now invalid.
    /// </summary>
    public long ScrollbackEvictedTotal
    {
        get
        {
            lock (this.screenLock)
            {
                return this.scrollbackEvictedTotal;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the alternate screen buffer is
    /// currently active. The alternate buffer never contributes to
    /// scrollback.
    /// </summary>
    public bool IsUsingAltBuffer
    {
        get
        {
            lock (this.screenLock)
            {
                return this.usingAltBuffer;
            }
        }
    }

    /// <summary>
    /// Resize the terminal buffer.
    /// </summary>
    /// <param name="cols">New column count.</param>
    /// <param name="rows">New row count.</param>
    /// <remarks>
    /// On the primary screen the content is re-flowed: logical lines
    /// (sequences of rows whose <c>rowWrapped</c> flag joins them) are
    /// concatenated, re-chunked into <paramref name="cols"/>-wide
    /// physical rows, and partitioned back into scrollback and the live
    /// grid. The cursor's logical position is preserved where possible.
    /// On the alternate screen reflow is explicitly skipped — full-screen
    /// apps (vim / less / htop) redraw their own content on SIGWINCH and
    /// don't benefit from engine-side reflow, which could also destroy
    /// their layout. Any buffer-side selection state is cleared.
    /// </remarks>
    public void Resize(int cols, int rows)
    {
        lock (this.screenLock)
        {
            if (cols == this.Cols && rows == this.Rows)
            {
                return;
            }

            if (this.usingAltBuffer || cols <= 0 || rows <= 0)
            {
                this.ResizeTruncatePad(cols, rows);
                return;
            }

            this.ResizeReflowPrimary(cols, rows);
        }
    }

    /// <summary>
    /// Write a single code point at the cursor position and advance the cursor.
    /// For grapheme clusters containing combining marks, ZWJ emoji sequences,
    /// variation selectors, skin-tone modifiers or regional-indicator pairs,
    /// prefer <see cref="PutCluster(string, int)"/> so the whole cluster is
    /// preserved as one cell.
    /// </summary>
    /// <param name="codePoint">The Unicode code point to write.</param>
    public void PutChar(int codePoint)
    {
        // Apply DEC Special Graphics character set translation.
        codePoint = this.TranslateCharset(codePoint);
        int width = UnicodeWidth.IsWideCharacter(codePoint) ? 2 : 1;
        this.PutCluster(char.ConvertFromUtf32(codePoint), width);
    }

    /// <summary>
    /// Write a grapheme cluster at the cursor position and advance the cursor
    /// by <paramref name="width"/> cells. The entire cluster string is stored
    /// in the main cell; when <paramref name="width"/> is 2, the following
    /// cell is marked as a wide-character continuation (null character).
    /// </summary>
    /// <param name="cluster">The cluster text to write. Must be non-empty.</param>
    /// <param name="width">Display width in terminal cells, 1 or 2.</param>
    public void PutCluster(string cluster, int width)
    {
        if (string.IsNullOrEmpty(cluster))
        {
            return;
        }

        if (width < 1)
        {
            width = 1;
        }
        else if (width > 2)
        {
            width = 2;
        }

        lock (this.screenLock)
        {
            bool wide = width == 2;

            // Resolve pending wrap: if the previous character filled the
            // last column with auto-wrap on, actually wrap now.
            if (this.PendingWrap)
            {
                this.PendingWrap = false;
                this.MarkRowWrapped(this.cursorRow);
                this.cursorCol = 0;
                this.LineFeedInternal();
            }

            // Wide char needs 2 columns; if it won't fit, wrap to next line.
            if (wide && this.cursorCol >= this.Cols - 1)
            {
                if (this.AutoWrap)
                {
                    this.MarkRowWrapped(this.cursorRow);
                    this.cursorCol = 0;
                    this.LineFeedInternal();
                }
                else
                {
                    this.cursorCol = this.Cols - 2;
                }
            }
            else if (this.cursorCol >= this.Cols)
            {
                // Should not normally happen with pending-wrap, but guard anyway.
                this.cursorCol = this.Cols - 1;
            }

            // If we're overwriting the second half of an existing wide char,
            // clear the orphaned first half.
            if (this.cursorCol > 0 && this.cells[this.cursorRow, this.cursorCol].Character is null)
            {
                this.cells[this.cursorRow, this.cursorCol - 1].Set(
                    " ",
                    new CellStyle(
                        this.ResolveFg(),
                        this.ResolveBg(),
                        this.currentSpecial,
                        false,
                        false,
                        false,
                        false,
                        false));
            }

            // If we're overwriting the first half of an existing wide char,
            // clear the orphaned continuation cell.
            if (this.cursorCol < this.Cols - 1
                && this.cells[this.cursorRow, this.cursorCol + 1].Character is null)
            {
                this.cells[this.cursorRow, this.cursorCol + 1].Set(
                    " ",
                    new CellStyle(
                        this.ResolveFg(),
                        this.ResolveBg(),
                        this.currentSpecial,
                        false,
                        false,
                        false,
                        false,
                        false));
            }

            this.cells[this.cursorRow, this.cursorCol].Set(
                cluster,
                new CellStyle(
                    this.ResolveFg(),
                    this.ResolveBg(),
                    this.currentSpecial,
                    this.reverse,
                    this.italic,
                    this.bold,
                    this.underline,
                    this.undercurl));
            this.ApplyExtendedAttrs(ref this.cells[this.cursorRow, this.cursorCol]);
            this.MarkDirty(this.cursorRow);

            if (wide && this.cursorCol + 1 < this.Cols)
            {
                this.cursorCol++;
                this.cells[this.cursorRow, this.cursorCol].Set(
                    null,
                    new CellStyle(
                        this.ResolveFg(),
                        this.ResolveBg(),
                        this.currentSpecial,
                        this.reverse,
                        this.italic,
                        this.bold,
                        this.underline,
                        this.undercurl));
            }

            // Advance cursor; set pending wrap if at end of line.
            if (this.cursorCol < this.Cols - 1)
            {
                this.cursorCol++;
            }
            else if (this.AutoWrap)
            {
                this.PendingWrap = true;
            }
        }
    }

    /// <summary>
    /// Set cursor position (0-based).
    /// </summary>
    /// <param name="row">Row index.</param>
    /// <param name="col">Column index.</param>
    public void SetCursorPosition(int row, int col)
    {
        lock (this.screenLock)
        {
            if (this.OriginMode)
            {
                row += this.scrollTop;
                row = Math.Clamp(row, this.scrollTop, this.scrollBottom);
            }
            else
            {
                row = Math.Clamp(row, 0, this.Rows - 1);
            }

            this.cursorRow = row;
            this.cursorCol = Math.Clamp(col, 0, this.Cols - 1);
            this.PendingWrap = false;
        }
    }

    /// <summary>
    /// Move cursor up by n rows.
    /// </summary>
    /// <param name="n">Number of rows to move.</param>
    public void MoveCursorUp(int n)
    {
        lock (this.screenLock)
        {
            this.cursorRow = Math.Max(this.scrollTop, this.cursorRow - n);
            this.PendingWrap = false;
        }
    }

    /// <summary>
    /// Move cursor down by n rows.
    /// </summary>
    /// <param name="n">Number of rows to move.</param>
    public void MoveCursorDown(int n)
    {
        lock (this.screenLock)
        {
            this.cursorRow = Math.Min(this.scrollBottom, this.cursorRow + n);
            this.PendingWrap = false;
        }
    }

    /// <summary>
    /// Move cursor forward (right) by n columns.
    /// </summary>
    /// <param name="n">Number of columns to move.</param>
    public void MoveCursorForward(int n)
    {
        lock (this.screenLock)
        {
            this.cursorCol = Math.Min(this.Cols - 1, this.cursorCol + n);
            this.PendingWrap = false;
        }
    }

    /// <summary>
    /// Move cursor back (left) by n columns.
    /// </summary>
    /// <param name="n">Number of columns to move.</param>
    public void MoveCursorBack(int n)
    {
        lock (this.screenLock)
        {
            this.cursorCol = Math.Max(0, this.cursorCol - n);
            this.PendingWrap = false;
        }
    }

    /// <summary>
    /// Perform a line feed: move cursor down, scrolling if needed.
    /// </summary>
    public void LineFeed()
    {
        lock (this.screenLock)
        {
            // Explicit LF means the current row is NOT a wrap
            // continuation of the prior line — clear the wrap flag that
            // some earlier cursor-wrap may have set on this row.
            this.ClearRowWrapped(this.cursorRow);
            this.LineFeedInternal();
        }
    }

    /// <summary>
    /// Perform a reverse index: move cursor up, scrolling down if at top.
    /// </summary>
    public void ReverseIndex()
    {
        lock (this.screenLock)
        {
            if (this.cursorRow == this.scrollTop)
            {
                this.ScrollDownInternal(1);
            }
            else if (this.cursorRow > 0)
            {
                this.cursorRow--;
            }
        }
    }

    /// <summary>
    /// Perform a carriage return: move cursor to column 0.
    /// </summary>
    public void CarriageReturn()
    {
        lock (this.screenLock)
        {
            this.cursorCol = 0;
            this.PendingWrap = false;
        }
    }

    /// <summary>
    /// Advance the cursor to the next configured tab stop.
    /// </summary>
    public void AdvanceToNextTabStop()
    {
        lock (this.screenLock)
        {
            int next = this.cursorCol + 1;
            while (next < this.Cols && !this.tabStops[next])
            {
                next++;
            }

            this.cursorCol = Math.Min(next, this.Cols - 1);
            this.PendingWrap = false;
        }
    }

    /// <summary>
    /// Set a tab stop at the current cursor column.
    /// </summary>
    public void SetTabStopAtCursor()
    {
        lock (this.screenLock)
        {
            if (this.cursorCol >= 0 && this.cursorCol < this.tabStops.Length)
            {
                this.tabStops[this.cursorCol] = true;
            }
        }
    }

    /// <summary>
    /// Clear the tab stop at the current cursor column.
    /// </summary>
    public void ClearTabStopAtCursor()
    {
        lock (this.screenLock)
        {
            if (this.cursorCol >= 0 && this.cursorCol < this.tabStops.Length)
            {
                this.tabStops[this.cursorCol] = false;
            }
        }
    }

    /// <summary>
    /// Clear all configured tab stops.
    /// </summary>
    public void ClearAllTabStops()
    {
        lock (this.screenLock)
        {
            Array.Clear(this.tabStops, 0, this.tabStops.Length);
        }
    }

    /// <summary>
    /// Move the cursor backward to the previous tab stop.
    /// </summary>
    public void BackTab()
    {
        lock (this.screenLock)
        {
            int prev = this.cursorCol - 1;
            while (prev > 0 && !this.tabStops[prev])
            {
                prev--;
            }

            this.cursorCol = Math.Max(prev, 0);
            this.PendingWrap = false;
        }
    }

    /// <summary>
    /// Erase in display.
    /// </summary>
    /// <param name="mode">0=below, 1=above, 2=all, 3=scrollback.</param>
    public void EraseInDisplay(int mode)
    {
        lock (this.screenLock)
        {
            switch (mode)
            {
                case 0:
                    this.ClearRegion(this.cursorRow, this.cursorCol, this.cursorRow, this.Cols - 1);
                    this.ClearRegion(this.cursorRow + 1, 0, this.Rows - 1, this.Cols - 1);
                    break;
                case 1:
                    this.ClearRegion(0, 0, this.cursorRow - 1, this.Cols - 1);
                    this.ClearRegion(this.cursorRow, 0, this.cursorRow, this.cursorCol);
                    break;
                case 2:
                case 3:
                    this.ClearRegion(0, 0, this.Rows - 1, this.Cols - 1);
                    this.allDirty = true;
                    break;
            }
        }
    }

    /// <summary>
    /// Erase in line.
    /// </summary>
    /// <param name="mode">0=to right, 1=to left, 2=entire line.</param>
    public void EraseInLine(int mode)
    {
        lock (this.screenLock)
        {
            switch (mode)
            {
                case 0:
                    this.ClearRegion(this.cursorRow, this.cursorCol, this.cursorRow, this.Cols - 1);
                    break;
                case 1:
                    this.ClearRegion(this.cursorRow, 0, this.cursorRow, this.cursorCol);
                    break;
                case 2:
                    this.ClearRegion(this.cursorRow, 0, this.cursorRow, this.Cols - 1);
                    break;
            }
        }
    }

    /// <summary>
    /// Erase n characters at cursor position.
    /// </summary>
    /// <param name="n">Number of characters to erase.</param>
    public void EraseCharacters(int n)
    {
        lock (this.screenLock)
        {
            int end = Math.Min(this.cursorCol + n - 1, this.Cols - 1);
            this.ClearRegion(this.cursorRow, this.cursorCol, this.cursorRow, end);
        }
    }

    /// <summary>
    /// Insert n blank characters at cursor, shifting existing chars right.
    /// </summary>
    /// <param name="n">Number of characters to insert.</param>
    public void InsertCharacters(int n)
    {
        lock (this.screenLock)
        {
            for (int j = this.Cols - 1; j >= this.cursorCol + n; j--)
            {
                this.cells[this.cursorRow, j] = this.cells[this.cursorRow, j - n];
            }

            this.ClearRegion(this.cursorRow, this.cursorCol, this.cursorRow, Math.Min(this.cursorCol + n - 1, this.Cols - 1));
        }
    }

    /// <summary>
    /// Delete n characters at cursor, shifting remaining chars left.
    /// </summary>
    /// <param name="n">Number of characters to delete.</param>
    public void DeleteCharacters(int n)
    {
        lock (this.screenLock)
        {
            for (int j = this.cursorCol; j < this.Cols - n; j++)
            {
                this.cells[this.cursorRow, j] = this.cells[this.cursorRow, j + n];
            }

            this.ClearRegion(this.cursorRow, this.Cols - n, this.cursorRow, this.Cols - 1);
        }
    }

    /// <summary>
    /// Insert n blank lines at cursor row, shifting existing lines down.
    /// </summary>
    /// <param name="n">Number of lines to insert.</param>
    public void InsertLines(int n)
    {
        lock (this.screenLock)
        {
            if (this.cursorRow < this.scrollTop || this.cursorRow > this.scrollBottom)
            {
                return;
            }

            for (int i = this.scrollBottom; i >= this.cursorRow + n; i--)
            {
                for (int j = 0; j < this.Cols; j++)
                {
                    this.cells[i, j] = this.cells[i - n, j];
                }

                this.MarkDirty(i);
            }

            for (int i = this.cursorRow; i < Math.Min(this.cursorRow + n, this.scrollBottom + 1); i++)
            {
                this.ClearRow(i);
            }
        }
    }

    /// <summary>
    /// Delete n lines at cursor row, shifting lines below up.
    /// </summary>
    /// <param name="n">Number of lines to delete.</param>
    public void DeleteLines(int n)
    {
        lock (this.screenLock)
        {
            if (this.cursorRow < this.scrollTop || this.cursorRow > this.scrollBottom)
            {
                return;
            }

            for (int i = this.cursorRow; i <= this.scrollBottom - n; i++)
            {
                for (int j = 0; j < this.Cols; j++)
                {
                    this.cells[i, j] = this.cells[i + n, j];
                }

                this.MarkDirty(i);
            }

            for (int i = Math.Max(this.cursorRow, this.scrollBottom - n + 1); i <= this.scrollBottom; i++)
            {
                this.ClearRow(i);
            }
        }
    }

    /// <summary>
    /// Returns a defensive copy of a stored scrollback line. Index <c>0</c>
    /// is the oldest retained line; <c><see cref="ScrollbackCount"/> - 1</c>
    /// is the most recently evicted line. Rows preserve their capture-time
    /// column count and are not reflowed when the live grid is resized.
    /// </summary>
    /// <param name="index">Zero-based line index (0 = oldest).</param>
    /// <returns>A copy of the requested scrollback row.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/>
    /// is negative or greater than or equal to <see cref="ScrollbackCount"/>.</exception>
    public Cell[] GetScrollbackLine(int index)
    {
        lock (this.screenLock)
        {
            if (index < 0 || index >= this.scrollbackCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int slot = (this.scrollbackHead + index) % this.scrollbackLimit;
            var source = this.scrollbackRing[slot] ?? Array.Empty<Cell>();
            var copy = new Cell[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }

    /// <summary>
    /// Drops all scrollback entries. Capacity (<see cref="ScrollbackLimit"/>) is preserved.
    /// </summary>
    public void ClearScrollback()
    {
        lock (this.screenLock)
        {
            this.scrollbackEvictedTotal += this.scrollbackCount;
            Array.Clear(this.scrollbackRing, 0, this.scrollbackRing.Length);
            if (this.scrollbackWrappedRing.Length > 0)
            {
                Array.Clear(this.scrollbackWrappedRing, 0, this.scrollbackWrappedRing.Length);
            }

            this.scrollbackHead = 0;
            this.scrollbackCount = 0;
        }
    }

    /// <summary>
    /// Captures a consistent point-in-time copy of the buffer's scrollback
    /// ring and live screen under a single acquisition of the internal
    /// lock. All returned cell arrays are defensive copies. Intended as
    /// the only safe entry point for background consumers such as the
    /// scrollback search layer — assembling a corpus from separate calls
    /// to <see cref="ScrollbackCount"/> / <see cref="GetScrollbackLine(int)"/>
    /// / <see cref="GetScreen"/> cannot guarantee atomicity w.r.t. the
    /// reader thread mutating the buffer.
    /// </summary>
    /// <returns>An immutable <see cref="BufferSnapshot"/>.</returns>
    public BufferSnapshot CreateSnapshot()
    {
        lock (this.screenLock)
        {
            var scrollbackRows = new Cell[this.scrollbackCount][];
            for (int i = 0; i < this.scrollbackCount; i++)
            {
                int slot = (this.scrollbackHead + i) % Math.Max(this.scrollbackLimit, 1);
                var source = this.scrollbackRing[slot] ?? Array.Empty<Cell>();
                var copy = new Cell[source.Length];
                Array.Copy(source, copy, source.Length);
                scrollbackRows[i] = copy;
            }

            int rows = this.Rows;
            int cols = this.Cols;
            Cell[,] liveCells;
            (int Row, int Col) cursorPos;
            int detectedBgLocal = this.detectedBg;
            if (this.cells is null)
            {
                liveCells = new Cell[rows, cols];
                cursorPos = (0, 0);
            }
            else
            {
                liveCells = (Cell[,])this.cells.Clone();
                cursorPos = (this.cursorRow, this.cursorCol);
            }

            var liveScreen = new Screen
            {
                Cells = liveCells,
                CursorPosition = cursorPos,
                BackgroundColor = detectedBgLocal,
                ForegroundColor = ColorUtility.DeriveReadableForeground(detectedBgLocal),
                AllDirty = true,
                DirtyRows = null,
            };

            return new BufferSnapshot
            {
                IsUsingAltBuffer = this.usingAltBuffer,
                ScrollbackCount = this.scrollbackCount,
                ScrollbackRows = scrollbackRows,
                LiveScreen = liveScreen,
                Rows = rows,
                Cols = cols,
            };
        }
    }

    /// <summary>
    /// Scroll up n lines within the scroll region.
    /// </summary>
    /// <param name="n">Number of lines to scroll.</param>
    public void ScrollUp(int n)
    {
        lock (this.screenLock)
        {
            this.ScrollUpInternal(n);
        }
    }

    /// <summary>
    /// Scroll down n lines within the scroll region.
    /// </summary>
    /// <param name="n">Number of lines to scroll.</param>
    public void ScrollDown(int n)
    {
        lock (this.screenLock)
        {
            this.ScrollDownInternal(n);
        }
    }

    /// <summary>
    /// Set the scrolling region (0-based, inclusive).
    /// </summary>
    /// <param name="top">Top row of scroll region.</param>
    /// <param name="bottom">Bottom row of scroll region.</param>
    public void SetScrollRegion(int top, int bottom)
    {
        lock (this.screenLock)
        {
            this.scrollTop = Math.Clamp(top, 0, this.Rows - 1);
            this.scrollBottom = Math.Clamp(bottom, 0, this.Rows - 1);
            this.cursorRow = 0;
            this.cursorCol = 0;
        }
    }

    /// <summary>
    /// Save cursor position and attributes (DECSC).
    /// </summary>
    public void SaveCursor()
    {
        lock (this.screenLock)
        {
            this.savedCursorRow = this.cursorRow;
            this.savedCursorCol = this.cursorCol;
            this.savedFg = this.currentFg;
            this.savedBg = this.currentBg;
            this.savedSpecial = this.currentSpecial;
            this.savedBold = this.bold;
            this.savedItalic = this.italic;
            this.savedUnderline = this.underline;
            this.savedUndercurl = this.undercurl;
            this.savedDoubleUnderline = this.doubleUnderline;
            this.savedReverse = this.reverse;
            this.savedDim = this.dim;
            this.savedStrikethrough = this.strikethrough;
            this.savedHidden = this.hidden;
            this.savedBlink = this.blink;
            this.savedOverline = this.overline;
            this.savedAutoWrap = this.AutoWrap;
            this.savedGlCharset = this.glCharset;
            this.savedG0IsLineDrawing = this.g0IsLineDrawing;
            this.savedG1IsLineDrawing = this.g1IsLineDrawing;
        }
    }

    /// <summary>
    /// Restore cursor position and attributes (DECRC).
    /// </summary>
    public void RestoreCursor()
    {
        lock (this.screenLock)
        {
            this.cursorRow = Math.Clamp(this.savedCursorRow, 0, this.Rows - 1);
            this.cursorCol = Math.Clamp(this.savedCursorCol, 0, this.Cols - 1);
            this.currentFg = this.savedFg;
            this.currentBg = this.savedBg;
            this.currentSpecial = this.savedSpecial;
            this.bold = this.savedBold;
            this.italic = this.savedItalic;
            this.underline = this.savedUnderline;
            this.undercurl = this.savedUndercurl;
            this.doubleUnderline = this.savedDoubleUnderline;
            this.reverse = this.savedReverse;
            this.dim = this.savedDim;
            this.strikethrough = this.savedStrikethrough;
            this.hidden = this.savedHidden;
            this.blink = this.savedBlink;
            this.overline = this.savedOverline;
            this.AutoWrap = this.savedAutoWrap;
            this.glCharset = this.savedGlCharset;
            this.g0IsLineDrawing = this.savedG0IsLineDrawing;
            this.g1IsLineDrawing = this.savedG1IsLineDrawing;
        }
    }

    /// <summary>
    /// Switch to alternate screen buffer.
    /// </summary>
    public void SwitchToAlternateBuffer()
    {
        lock (this.screenLock)
        {
            if (!this.usingAltBuffer)
            {
                this.altCells = this.cells;
                this.altRowWrapped = this.rowWrapped;
                this.cells = new Cell[this.Rows, this.Cols];
                this.rowWrapped = new bool[this.Rows];
                this.ClearRegion(0, 0, this.Rows - 1, this.Cols - 1);
                this.usingAltBuffer = true;
                this.allDirty = true;
                this.bgHistogramValid = false;
            }
        }
    }

    /// <summary>
    /// Switch back to main screen buffer.
    /// </summary>
    public void SwitchToMainBuffer()
    {
        lock (this.screenLock)
        {
            if (this.usingAltBuffer && this.altCells is not null)
            {
                // The main buffer may have been saved at a different size
                // if the terminal was resized while the alternate buffer
                // was active.  Adjust to current dimensions.
                if (this.altCells.GetLength(0) != this.Rows
                    || this.altCells.GetLength(1) != this.Cols)
                {
                    var resized = new Cell[this.Rows, this.Cols];
                    int copyRows = Math.Min(this.Rows, this.altCells.GetLength(0));
                    int copyCols = Math.Min(this.Cols, this.altCells.GetLength(1));
                    for (int i = 0; i < copyRows; i++)
                    {
                        for (int j = 0; j < copyCols; j++)
                        {
                            resized[i, j] = this.altCells[i, j];
                        }
                    }

                    for (int i = 0; i < this.Rows; i++)
                    {
                        int startCol = i < copyRows ? copyCols : 0;
                        for (int j = startCol; j < this.Cols; j++)
                        {
                            resized[i, j].Clear(this.defaultFg, this.detectedBg, 0);
                        }
                    }

                    this.altCells = resized;
                }

                this.cells = this.altCells;
                this.rowWrapped = this.altRowWrapped ?? new bool[this.Rows];
                if (this.rowWrapped.Length != this.Rows)
                {
                    var resizedWrap = new bool[this.Rows];
                    Array.Copy(this.rowWrapped, resizedWrap, Math.Min(this.rowWrapped.Length, this.Rows));
                    this.rowWrapped = resizedWrap;
                }

                this.altCells = null!;
                this.altRowWrapped = null;
                this.usingAltBuffer = false;
                this.allDirty = true;
                this.bgHistogramValid = false;
            }
        }
    }

    /// <summary>
    /// Full terminal reset.
    /// </summary>
    public void Reset()
    {
        lock (this.screenLock)
        {
            this.ResetAttributes();
            this.cursorRow = 0;
            this.cursorCol = 0;
            this.scrollTop = 0;
            this.scrollBottom = this.Rows - 1;
            this.usingAltBuffer = false;
            this.altCells = null!;
            this.PendingWrap = false;
            this.AutoWrap = true;
            this.ApplicationCursorKeys = false;
            this.ReverseVideo = false;
            this.OriginMode = false;
            this.BracketedPasteEnabled = false;
            this.SynchronizedOutput = false;
            this.FocusEventsEnabled = false;
            this.SgrMouseEnabled = false;
            this.MouseTrackingMode = MouseTrackingMode.None;
            this.g0IsLineDrawing = false;
            this.g1IsLineDrawing = false;
            this.g2IsLineDrawing = false;
            this.g3IsLineDrawing = false;
            this.glCharset = 0;
            this.singleShiftCharset = -1;
            this.tabStops = CreateDefaultTabStops(this.Cols);
            if (this.rowWrapped is not null)
            {
                Array.Clear(this.rowWrapped, 0, this.rowWrapped.Length);
            }

            this.ResetPaletteColors();
            this.ResetTerminalDefaultForeground();
            this.ResetTerminalDefaultBackground();
            this.ResetTerminalCursorColor();
            this.ClearRegion(0, 0, this.Rows - 1, this.Cols - 1);
            this.allDirty = true;
            this.bgHistogramValid = false;
        }
    }

    /// <summary>
    /// Reset all text attributes to defaults.
    /// </summary>
    public void ResetAttributes()
    {
        this.currentFg = -1;
        this.currentBg = -1;
        this.currentSpecial = 0;
        this.bold = false;
        this.italic = false;
        this.underline = false;
        this.undercurl = false;
        this.doubleUnderline = false;
        this.reverse = false;
        this.dim = false;
        this.strikethrough = false;
        this.hidden = false;
        this.blink = false;
        this.overline = false;
    }

    /// <summary>
    /// Set bold attribute.
    /// </summary>
    /// <param name="on">True to enable bold.</param>
    public void SetBold(bool on) => this.bold = on;

    /// <summary>
    /// Set italic attribute.
    /// </summary>
    /// <param name="on">True to enable italic.</param>
    public void SetItalic(bool on) => this.italic = on;

    /// <summary>
    /// Set underline attribute.
    /// </summary>
    /// <param name="on">True to enable underline.</param>
    public void SetUnderline(bool on) => this.underline = on;

    /// <summary>
    /// Set undercurl attribute.
    /// </summary>
    /// <param name="on">True to enable undercurl.</param>
    public void SetUndercurl(bool on) => this.undercurl = on;

    /// <summary>
    /// Set double-underline attribute. Distinct from
    /// <see cref="SetUnderline(bool)"/>; enabling one does not affect the
    /// other but renderers typically show only the "stronger" style when
    /// both are set.
    /// </summary>
    /// <param name="on">True to enable double underline.</param>
    public void SetDoubleUnderline(bool on) => this.doubleUnderline = on;

    /// <summary>
    /// Set reverse attribute.
    /// </summary>
    /// <param name="on">True to enable reverse video.</param>
    public void SetReverse(bool on) => this.reverse = on;

    /// <summary>
    /// Set dim (faint) attribute.
    /// </summary>
    /// <param name="on">True to enable dim.</param>
    public void SetDim(bool on) => this.dim = on;

    /// <summary>
    /// Set strikethrough attribute.
    /// </summary>
    /// <param name="on">True to enable strikethrough.</param>
    public void SetStrikethrough(bool on) => this.strikethrough = on;

    /// <summary>
    /// Set hidden (concealed) attribute.
    /// </summary>
    /// <param name="on">True to enable hidden.</param>
    public void SetHidden(bool on) => this.hidden = on;

    /// <summary>
    /// Set blink attribute.
    /// </summary>
    /// <param name="on">True to enable blink.</param>
    public void SetBlink(bool on) => this.blink = on;

    /// <summary>
    /// Set overline attribute.
    /// </summary>
    /// <param name="on">True to enable overline.</param>
    public void SetOverline(bool on) => this.overline = on;

    /// <summary>
    /// Set the current OSC 8 hyperlink state. Subsequent characters written
    /// via <see cref="PutChar(int)"/> are stamped with this URI (and optional
    /// id). Pass <see langword="null"/> for both to end the hyperlink.
    /// </summary>
    /// <param name="uri">Hyperlink URI, or <see langword="null"/> to clear.</param>
    /// <param name="id">Optional hyperlink id; allows non-contiguous runs to be
    /// recognized as the same logical link.</param>
    public void SetHyperlink(string? uri, string? id)
    {
        this.currentHyperlinkUri = string.IsNullOrEmpty(uri) ? null : uri;
        this.currentHyperlinkId = this.currentHyperlinkUri is null ? null : id;
    }

    /// <summary>
    /// Set foreground color (RGB format).
    /// </summary>
    /// <param name="color">Color value in RGB format.</param>
    public void SetForegroundColor(int color) => this.currentFg = color;

    /// <summary>
    /// Set background color (RGB format).
    /// </summary>
    /// <param name="color">Color value in RGB format.</param>
    public void SetBackgroundColor(int color) => this.currentBg = color;

    /// <summary>
    /// Reset foreground color to default.
    /// </summary>
    public void SetDefaultForeground() => this.currentFg = -1;

    /// <summary>
    /// Reset background color to default.
    /// </summary>
    public void SetDefaultBackground() => this.currentBg = -1;

    /// <summary>
    /// Set special (underline/undercurl) color (RGB format).
    /// </summary>
    /// <param name="color">Color value in RGB format.</param>
    public void SetSpecialColor(int color) => this.currentSpecial = color;

    /// <summary>
    /// Reset special color to default (0).
    /// </summary>
    public void SetDefaultSpecial() => this.currentSpecial = 0;

    /// <summary>
    /// Designate a character set for G0–G3.
    /// </summary>
    /// <param name="gSet">0 for G0, 1 for G1, 2 for G2, 3 for G3.</param>
    /// <param name="isLineDrawing">True for DEC Special Graphics, false for ASCII.</param>
    public void DesignateCharset(int gSet, bool isLineDrawing)
    {
        switch (gSet)
        {
            case 0:
                this.g0IsLineDrawing = isLineDrawing;
                break;
            case 1:
                this.g1IsLineDrawing = isLineDrawing;
                break;
            case 2:
                this.g2IsLineDrawing = isLineDrawing;
                break;
            case 3:
                this.g3IsLineDrawing = isLineDrawing;
                break;
        }
    }

    /// <summary>
    /// Shift-In (SI): select G0 as the active character set.
    /// </summary>
    public void ShiftIn() => this.glCharset = 0;

    /// <summary>
    /// Shift-Out (SO): select G1 as the active character set.
    /// </summary>
    public void ShiftOut() => this.glCharset = 1;

    /// <summary>
    /// Single Shift: use the specified G-set for exactly one character.
    /// </summary>
    /// <param name="gSet">2 for SS2 (G2), 3 for SS3 (G3).</param>
    public void SingleShift(int gSet)
    {
        this.singleShiftCharset = gSet;
    }

    /// <summary>
    /// Fill the entire screen with 'E' characters (DECALN alignment test).
    /// </summary>
    public void FillWithE()
    {
        lock (this.screenLock)
        {
            this.ResetAttributes();
            for (int i = 0; i < this.Rows; i++)
            {
                for (int j = 0; j < this.Cols; j++)
                {
                    this.cells[i, j].Set(
                        "E",
                        new CellStyle(
                            this.defaultFg,
                            this.defaultBg,
                            0,
                            false,
                            false,
                            false,
                            false,
                            false));
                }

                this.MarkDirty(i);
            }

            this.cursorRow = 0;
            this.cursorCol = 0;
        }
    }

    /// <summary>
    /// Set the terminal default foreground color (e.g. from OSC 10).
    /// </summary>
    /// <param name="color">Color value in RGB format.</param>
    public void SetTerminalDefaultForeground(int color)
    {
        this.defaultFg = color;
    }

    /// <summary>
    /// Reset the terminal default foreground color to the built-in default.
    /// </summary>
    public void ResetTerminalDefaultForeground()
    {
        this.defaultFg = 0x000000;
    }

    /// <summary>
    /// Set the terminal default background color (e.g. from OSC 11).
    /// This affects how SGR 49 (default background) resolves.
    /// </summary>
    /// <param name="color">Color value in RGB format.</param>
    public void SetTerminalDefaultBackground(int color)
    {
        this.defaultBg = color;
    }

    /// <summary>
    /// Reset the terminal default background color to the built-in default.
    /// </summary>
    public void ResetTerminalDefaultBackground()
    {
        this.defaultBg = 0xFFFFFF;
    }

    /// <summary>
    /// Set the terminal cursor color (e.g. from OSC 12).
    /// </summary>
    /// <param name="color">Color value in RGB format.</param>
    public void SetTerminalCursorColor(int color)
    {
        this.cursorColor = color;
    }

    /// <summary>
    /// Reset the terminal cursor color to its default.
    /// </summary>
    public void ResetTerminalCursorColor()
    {
        this.cursorColor = -1;
    }

    /// <summary>
    /// Get the current palette color for a 256-color index.
    /// </summary>
    /// <param name="index">The palette index.</param>
    /// <returns>The RGB color value.</returns>
    public int GetPaletteColor(int index)
    {
        return this.palette[Math.Clamp(index, 0, this.palette.Length - 1)];
    }

    /// <summary>
    /// Set a palette color for a 256-color index.
    /// </summary>
    /// <param name="index">The palette index.</param>
    /// <param name="color">The RGB color value.</param>
    public void SetPaletteColor(int index, int color)
    {
        if (index >= 0 && index < this.palette.Length)
        {
            this.palette[index] = color;
        }
    }

    /// <summary>
    /// Reset a palette color to the built-in default.
    /// </summary>
    /// <param name="index">The palette index.</param>
    public void ResetPaletteColor(int index)
    {
        if (index >= 0 && index < this.palette.Length)
        {
            this.palette[index] = DefaultPalette[index];
        }
    }

    /// <summary>
    /// Reset all palette colors to their built-in defaults.
    /// </summary>
    public void ResetPaletteColors()
    {
        Array.Copy(DefaultPalette, this.palette, DefaultPalette.Length);
    }

    /// <summary>
    /// Overrides the first 16 ANSI palette colors with the given values.
    /// </summary>
    /// <param name="colors">An array of at least 16 RGB color values.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="colors"/>
    /// is <see langword="null"/>.</exception>
    public void SetAnsiPalette(int[] colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Length < 16)
        {
            return;
        }

        Array.Copy(colors, this.palette, 16);
        this.allDirty = true;
    }

    /// <summary>
    /// Replaces the default foreground and background colors on all existing
    /// cells, so that a color scheme change takes immediate visual effect.
    /// </summary>
    /// <param name="newFg">The new default foreground color.</param>
    /// <param name="newBg">The new default background color.</param>
    public void RecolorDefaults(int newFg, int newBg)
    {
        int oldFg = this.defaultFg;
        int oldBg = this.defaultBg;
        this.defaultFg = newFg;
        this.defaultBg = newBg;
        this.detectedBg = newBg;

        if (this.cells is not null && (oldFg != newFg || oldBg != newBg))
        {
            for (int i = 0; i < this.Rows; i++)
            {
                for (int j = 0; j < this.Cols; j++)
                {
                    RemapCellDefaultColor(ref this.cells[i, j], oldFg, newFg, oldBg, newBg);
                }
            }

            if (this.altCells is not null)
            {
                int altRows = this.altCells.GetLength(0);
                int altCols = this.altCells.GetLength(1);
                for (int i = 0; i < altRows; i++)
                {
                    for (int j = 0; j < altCols; j++)
                    {
                        RemapCellDefaultColor(ref this.altCells[i, j], oldFg, newFg, oldBg, newBg);
                    }
                }
            }

            this.allDirty = true;
            this.bgHistogramValid = false;
        }
    }

    /// <summary>
    /// Set the initial detected background color hint (e.g. from saved settings).
    /// Used as the starting value for <see cref="Screen.BackgroundColor"/> before
    /// screen analysis detects the actual predominant color.
    /// </summary>
    /// <param name="color">Color value in RGB format.</param>
    public void SetDetectedBackground(int color)
    {
        this.detectedBg = color;
    }

    /// <summary>
    /// Get the current screen state for rendering.
    /// </summary>
    /// <returns>A screen snapshot.</returns>
    public Screen? GetScreen()
    {
        lock (this.screenLock)
        {
            return this.GetScreenNoLock();
        }
    }

    /// <summary>
    /// Gets the current screen without acquiring the lock. The caller must
    /// hold an outer lock that serialises access to this buffer.
    /// </summary>
    /// <returns>The current screen state, or null if not yet initialized.</returns>
    internal Screen? GetScreenNoLock()
    {
        if (this.cells is null)
        {
            return null;
        }

        bool sizeChanged = this.screen.Cells is null
            || this.screen.Cells.GetLength(0) != this.Rows
            || this.screen.Cells.GetLength(1) != this.Cols;

        // Update bg histogram incrementally BEFORE copying cells, so
        // screen.Cells still holds the previous frame for diffing.
        this.UpdateBackgroundDetection(sizeChanged);

        if (sizeChanged)
        {
            this.screen.Cells = (Cell[,])this.cells.Clone();
        }
        else
        {
            var screenCells = this.screen.Cells!;
            if (this.allDirty)
            {
                for (int i = 0; i < this.Rows; i++)
                {
                    for (int j = 0; j < this.Cols; j++)
                    {
                        screenCells[i, j] = this.cells[i, j];
                    }
                }
            }
            else if (this.dirtyRows is not null)
            {
                for (int i = 0; i < this.dirtyRows.Length; i++)
                {
                    if (this.dirtyRows[i])
                    {
                        for (int j = 0; j < this.Cols; j++)
                        {
                            screenCells[i, j] = this.cells[i, j];
                        }
                    }
                }
            }
        }

        // Propagate dirty metadata to the screen before clearing.
        this.screen.AllDirty = sizeChanged || this.allDirty;
        if (this.dirtyRows is not null && !this.screen.AllDirty)
        {
            if (this.screen.DirtyRows is null || this.screen.DirtyRows.Length != this.Rows)
            {
                this.screen.DirtyRows = new bool[this.Rows];
            }

            Array.Copy(this.dirtyRows, this.screen.DirtyRows, this.Rows);
        }
        else
        {
            this.screen.DirtyRows = null;
        }

        this.allDirty = false;
        if (this.dirtyRows is not null)
        {
            Array.Clear(this.dirtyRows, 0, this.dirtyRows.Length);
        }

        this.screen.CursorPosition = (this.cursorRow, this.cursorCol);
        this.screen.BackgroundColor = this.detectedBg;
        this.screen.ForegroundColor = ColorUtility.DeriveReadableForeground(this.detectedBg);

        return this.screen;
    }

    /// <summary>
    /// Returns whether the given live-grid row is currently marked as
    /// line-wrapped (its logical line continues into the next row).
    /// Exposed for test assertions.
    /// </summary>
    /// <param name="row">Zero-based row index.</param>
    /// <returns>True when the row has a wrap flag set.</returns>
    internal bool IsRowWrapped(int row)
    {
        lock (this.screenLock)
        {
            if (row < 0 || row >= this.rowWrapped.Length)
            {
                return false;
            }

            return this.rowWrapped[row];
        }
    }

    /// <summary>
    /// Returns whether the scrollback row at <paramref name="index"/>
    /// (0 = oldest) carries a line-wrap flag.
    /// </summary>
    /// <param name="index">Zero-based scrollback index.</param>
    /// <returns>True when the scrollback row is wrapped.</returns>
    internal bool IsScrollbackRowWrapped(int index)
    {
        lock (this.screenLock)
        {
            if (index < 0 || index >= this.scrollbackCount || this.scrollbackLimit <= 0)
            {
                return false;
            }

            int slot = (this.scrollbackHead + index) % this.scrollbackLimit;
            return this.scrollbackWrappedRing[slot];
        }
    }

    private static int[] CreateDefaultPalette()
    {
        var palette = new int[256];

        // Standard colors (0-7)
        int[] standard =
        {
            0x000000, 0xCC0000, 0x00CC00, 0xCCCC00,
            0x0000CC, 0xCC00CC, 0x00CCCC, 0xCCCCCC,
        };
        Array.Copy(standard, palette, 8);

        // Bright colors (8-15)
        int[] bright =
        {
            0x555555, 0xFF5555, 0x55FF55, 0xFFFF55,
            0x5555FF, 0xFF55FF, 0x55FFFF, 0xFFFFFF,
        };
        Array.Copy(bright, 0, palette, 8, 8);

        // 6x6x6 color cube (16-231)
        int[] cubeValues = { 0, 95, 135, 175, 215, 255 };
        for (int r = 0; r < 6; r++)
        {
            for (int g = 0; g < 6; g++)
            {
                for (int b = 0; b < 6; b++)
                {
                    palette[16 + (r * 36) + (g * 6) + b] =
                        (cubeValues[r] << 16) | (cubeValues[g] << 8) | cubeValues[b];
                }
            }
        }

        // Grayscale ramp (232-255)
        for (int i = 0; i < 24; i++)
        {
            int level = Math.Clamp(8 + (10 * i), 0, 255);
            palette[232 + i] = (level << 16) | (level << 8) | level;
        }

        return palette;
    }

    private static void RemapCellDefaultColor(ref Cell cell, int oldFg, int newFg, int oldBg, int newBg)
    {
        bool changed = false;
        int fg = cell.ForegroundColor;
        int bg = cell.BackgroundColor;

        if (fg == oldFg)
        {
            fg = newFg;
            changed = true;
        }

        if (bg == oldBg)
        {
            bg = newBg;
            changed = true;
        }

        if (changed)
        {
            cell.Set(
                cell.Character,
                new CellStyle(fg, bg, cell.SpecialColor, cell.Reverse, cell.Italic, cell.Bold, cell.Underline, cell.Undercurl));
        }
    }

    private static bool[] CreateDefaultTabStops(int cols)
    {
        var stops = new bool[cols];
        for (int i = 0; i < cols; i += 8)
        {
            stops[i] = true;
        }

        return stops;
    }

    private static bool IsDefaultBlank(Cell cell, int defaultFg, int detectedBg, int defaultBg)
    {
        _ = defaultFg;
        if (cell.Character is null)
        {
            // Wide continuation — not "blank" for reflow purposes.
            return false;
        }

        if (cell.Character != " " && cell.Character != string.Empty)
        {
            return false;
        }

        if (cell.BackgroundColor != detectedBg && cell.BackgroundColor != defaultBg)
        {
            return false;
        }

        if (cell.Reverse || cell.Underline || cell.Undercurl || cell.DoubleUnderline
            || cell.Strikethrough || cell.Overline || cell.HyperlinkUri is not null)
        {
            return false;
        }

        return true;
    }

    private static Cell DefaultBlankCell(int defaultFg, int detectedBg, int specialColor)
    {
        var c = default(Cell);
        c.Clear(defaultFg, detectedBg, specialColor);
        return c;
    }

    private static Cell[] BlankRow(int cols, int defaultFg, int detectedBg, int specialColor)
    {
        var row = new Cell[cols];
        for (int j = 0; j < cols; j++)
        {
            row[j].Clear(defaultFg, detectedBg, specialColor);
        }

        return row;
    }

    private static int CellWidth(Cell cell)
    {
        if (cell.Character is null)
        {
            return 0;
        }

        if (cell.Character.Length == 0)
        {
            return 0;
        }

        // Grapheme clusters may span multiple code points (emoji ZWJ
        // sequences, regional-indicator flags, VS16-presented emoji, etc.).
        // Decode every rune and defer to GraphemeCluster for width.
        if (cell.Character.Length == 1 || (char.IsHighSurrogate(cell.Character[0]) && cell.Character.Length == 2))
        {
            int codePoint = char.ConvertToUtf32(cell.Character, 0);
            return UnicodeWidth.IsWideCharacter(codePoint) ? 2 : 1;
        }

        var runes = new List<int>(cell.Character.Length);
        for (int i = 0; i < cell.Character.Length;)
        {
            int cp = char.ConvertToUtf32(cell.Character, i);
            runes.Add(cp);
            i += char.IsHighSurrogate(cell.Character[i]) ? 2 : 1;
        }

        return GraphemeCluster.ComputeWidth(runes);
    }

    private int ResolveFg() => this.currentFg == -1 ? this.defaultFg : this.currentFg;

    private int ResolveBg() => this.currentBg == -1 ? this.defaultBg : this.currentBg;

    private void ApplyExtendedAttrs(ref Cell cell)
    {
        cell.Dim = this.dim;
        cell.Strikethrough = this.strikethrough;
        cell.Hidden = this.hidden;
        cell.Blink = this.blink;
        cell.Overline = this.overline;
        cell.DoubleUnderline = this.doubleUnderline;
        cell.HyperlinkUri = this.currentHyperlinkUri;
        cell.HyperlinkId = this.currentHyperlinkId;
    }

    /// <summary>
    /// Updates the background color histogram incrementally and resolves
    /// the predominant background color. Called before the cell copy in
    /// <see cref="GetScreen"/> so that <c>screen.Cells</c> still holds
    /// the previous frame for diffing.
    /// </summary>
    /// <param name="sizeChanged">Whether the grid dimensions changed since the last call.</param>
    private void UpdateBackgroundDetection(bool sizeChanged)
    {
        int totalCells = this.Rows * this.Cols;
        if (totalCells == 0)
        {
            return;
        }

        bool needFullRecount = sizeChanged || !this.bgHistogramValid;

        if (needFullRecount)
        {
            this.RebuildHistogramFromCells();
            this.bgHistogramValid = true;
        }
        else if (this.allDirty)
        {
            // All rows changed — differential update if old snapshot exists.
            var oldCells = this.screen.Cells;
            bool canDiff = oldCells is not null
                && oldCells.GetLength(0) == this.Rows
                && oldCells.GetLength(1) == this.Cols;

            if (canDiff)
            {
                this.DiffRows(oldCells!, 0, this.Rows);
            }
            else
            {
                this.RebuildHistogramFromCells();
            }
        }
        else if (this.dirtyRows is not null && this.screen.Cells is not null)
        {
            // Incremental: only update dirty rows.
            var oldCells = this.screen.Cells;
            for (int i = 0; i < this.dirtyRows.Length; i++)
            {
                if (this.dirtyRows[i])
                {
                    this.DiffRows(oldCells, i, i + 1);
                }
            }
        }

        // Find the peak bg color in the histogram.
        int bestColor = this.detectedBg;
        int bestCount = 0;
        foreach (var kvp in this.bgHistogram)
        {
            if (kvp.Value > bestCount)
            {
                bestCount = kvp.Value;
                bestColor = kvp.Key;
            }
        }

        if (bestCount > totalCells / 2)
        {
            this.detectedBg = bestColor;
        }
    }

    private void RebuildHistogramFromCells()
    {
        this.bgHistogram.Clear();
        for (int i = 0; i < this.Rows; i++)
        {
            for (int j = 0; j < this.Cols; j++)
            {
                int bg = this.cells[i, j].BackgroundColor;
                this.bgHistogram[bg] = this.bgHistogram.GetValueOrDefault(bg) + 1;
            }
        }
    }

    /// <summary>
    /// Subtracts old bg counts and adds new bg counts for rows in [rowStart, rowEnd).
    /// </summary>
    private void DiffRows(Cell[,] oldCells, int rowStart, int rowEnd)
    {
        for (int i = rowStart; i < rowEnd; i++)
        {
            for (int j = 0; j < this.Cols; j++)
            {
                int oldBg = oldCells[i, j].BackgroundColor;
                if (this.bgHistogram.TryGetValue(oldBg, out int oldCount))
                {
                    if (oldCount <= 1)
                    {
                        this.bgHistogram.Remove(oldBg);
                    }
                    else
                    {
                        this.bgHistogram[oldBg] = oldCount - 1;
                    }
                }

                int newBg = this.cells[i, j].BackgroundColor;
                this.bgHistogram[newBg] = this.bgHistogram.GetValueOrDefault(newBg) + 1;
            }
        }
    }

    private void MarkDirty(int row)
    {
        if (this.dirtyRows is not null && row >= 0 && row < this.dirtyRows.Length)
        {
            this.dirtyRows[row] = true;
        }
    }

    private void ClearRow(int row)
    {
        int fg = this.currentFg == -1 ? this.defaultFg : this.currentFg;
        int bg = this.currentBg == -1 ? this.detectedBg : this.currentBg;
        for (int j = 0; j < this.Cols; j++)
        {
            this.cells[row, j].Clear(fg, bg, this.currentSpecial);
        }

        this.ClearRowWrapped(row);
        this.MarkDirty(row);
    }

    private void ClearRegion(int rowStart, int colStart, int rowEnd, int colEnd)
    {
        rowStart = Math.Max(0, rowStart);
        colStart = Math.Max(0, colStart);
        rowEnd = Math.Min(this.Rows - 1, rowEnd);
        colEnd = Math.Min(this.Cols - 1, colEnd);

        int fg = this.currentFg == -1 ? this.defaultFg : this.currentFg;
        int bg = this.currentBg == -1 ? this.detectedBg : this.currentBg;
        for (int i = rowStart; i <= rowEnd; i++)
        {
            int jStart = i == rowStart ? colStart : 0;
            int jEnd = i == rowEnd ? colEnd : this.Cols - 1;
            for (int j = jStart; j <= jEnd; j++)
            {
                this.cells[i, j].Clear(fg, bg, this.currentSpecial);
            }

            if (jStart == 0 && jEnd == this.Cols - 1)
            {
                this.ClearRowWrapped(i);
            }

            this.MarkDirty(i);
        }
    }

    private void LineFeedInternal()
    {
        if (this.cursorRow == this.scrollBottom)
        {
            this.ScrollUpInternal(1);
        }
        else if (this.cursorRow < this.Rows - 1)
        {
            this.cursorRow++;
        }
    }

    private int TranslateCharset(int codePoint)
    {
        bool lineDrawing;
        if (this.singleShiftCharset >= 0)
        {
            lineDrawing = this.singleShiftCharset switch
            {
                2 => this.g2IsLineDrawing,
                3 => this.g3IsLineDrawing,
                _ => false,
            };
            this.singleShiftCharset = -1;
        }
        else
        {
            lineDrawing = this.glCharset switch
            {
                1 => this.g1IsLineDrawing,
                2 => this.g2IsLineDrawing,
                3 => this.g3IsLineDrawing,
                _ => this.g0IsLineDrawing,
            };
        }

        if (!lineDrawing || codePoint < 0x60 || codePoint > 0x7E)
        {
            return codePoint;
        }

        return codePoint switch
        {
            0x60 => 0x25C6, // ◆
            0x61 => 0x2592, // ▒
            0x62 => 0x2409, // HT symbol
            0x63 => 0x240C, // FF symbol
            0x64 => 0x240D, // CR symbol
            0x65 => 0x240A, // LF symbol
            0x66 => 0x00B0, // °
            0x67 => 0x00B1, // ±
            0x68 => 0x2424, // NL symbol
            0x69 => 0x240B, // VT symbol
            0x6A => 0x2518, // ┘
            0x6B => 0x2510, // ┐
            0x6C => 0x250C, // ┌
            0x6D => 0x2514, // └
            0x6E => 0x253C, // ┼
            0x6F => 0x23BA, // ⎺ scan 1
            0x70 => 0x23BB, // ⎻ scan 3
            0x71 => 0x2500, // ─
            0x72 => 0x23BC, // ⎼ scan 7
            0x73 => 0x23BD, // ⎽ scan 9
            0x74 => 0x251C, // ├
            0x75 => 0x2524, // ┤
            0x76 => 0x2534, // ┴
            0x77 => 0x252C, // ┬
            0x78 => 0x2502, // │
            0x79 => 0x2264, // ≤
            0x7A => 0x2265, // ≥
            0x7B => 0x03C0, // π
            0x7C => 0x2260, // ≠
            0x7D => 0x00A3, // £
            0x7E => 0x00B7, // ·
            _ => codePoint,
        };
    }

    private void ScrollUpInternal(int n)
    {
        this.CaptureScrollbackOnScrollUp(n);

        for (int i = this.scrollTop; i <= this.scrollBottom - n; i++)
        {
            for (int j = 0; j < this.Cols; j++)
            {
                this.cells[i, j] = this.cells[i + n, j];
            }

            if (this.rowWrapped is not null && i + n < this.rowWrapped.Length)
            {
                this.rowWrapped[i] = this.rowWrapped[i + n];
            }

            this.MarkDirty(i);
        }

        for (int i = Math.Max(this.scrollTop, this.scrollBottom - n + 1); i <= this.scrollBottom; i++)
        {
            this.ClearRow(i);
        }
    }

    private void ScrollDownInternal(int n)
    {
        for (int i = this.scrollBottom; i >= this.scrollTop + n; i--)
        {
            for (int j = 0; j < this.Cols; j++)
            {
                this.cells[i, j] = this.cells[i - n, j];
            }

            if (this.rowWrapped is not null && i - n >= 0)
            {
                this.rowWrapped[i] = this.rowWrapped[i - n];
            }

            this.MarkDirty(i);
        }

        for (int i = this.scrollTop; i < Math.Min(this.scrollTop + n, this.scrollBottom + 1); i++)
        {
            this.ClearRow(i);
        }
    }

    private void CaptureScrollbackOnScrollUp(int n)
    {
        // Only capture when operating on the primary (main) buffer's full
        // scroll region. Lines evicted from a constrained DEC scroll region
        // (top > 0 or bottom < Rows-1) are pager/TUI internals and must not
        // enter scrollback. The alt buffer never contributes scrollback.
        if (this.usingAltBuffer
            || this.scrollbackLimit <= 0
            || this.scrollTop != 0
            || this.scrollBottom != this.Rows - 1
            || n <= 0)
        {
            return;
        }

        int captureCount = Math.Min(n, this.Rows);
        for (int i = 0; i < captureCount; i++)
        {
            var row = new Cell[this.Cols];
            for (int j = 0; j < this.Cols; j++)
            {
                row[j] = this.cells[i, j];
            }

            bool wrapped = this.rowWrapped is not null && i < this.rowWrapped.Length && this.rowWrapped[i];
            this.AppendScrollbackRow(row, wrapped);
        }
    }

    private void AppendScrollbackRow(Cell[] row, bool wrapped)
    {
        if (this.scrollbackLimit <= 0)
        {
            return;
        }

        if (this.scrollbackCount < this.scrollbackLimit)
        {
            int tail = (this.scrollbackHead + this.scrollbackCount) % this.scrollbackLimit;
            this.scrollbackRing[tail] = row;
            this.scrollbackWrappedRing[tail] = wrapped;
            this.scrollbackCount++;
        }
        else
        {
            // Ring is full: overwrite the oldest slot and advance head.
            this.scrollbackRing[this.scrollbackHead] = row;
            this.scrollbackWrappedRing[this.scrollbackHead] = wrapped;
            this.scrollbackHead = (this.scrollbackHead + 1) % this.scrollbackLimit;
            this.scrollbackEvictedTotal++;
        }
    }

    private void ResizeTruncatePad(int cols, int rows)
    {
        var newCells = new Cell[rows, cols];
        int copyRows = Math.Min(rows, this.Rows);
        int copyCols = Math.Min(cols, this.Cols);

        for (int i = 0; i < copyRows; i++)
        {
            for (int j = 0; j < copyCols; j++)
            {
                newCells[i, j] = this.cells[i, j];
            }
        }

        for (int i = 0; i < rows; i++)
        {
            int startCol = i < copyRows ? copyCols : 0;
            for (int j = startCol; j < cols; j++)
            {
                newCells[i, j].Clear(this.defaultFg, this.detectedBg, this.currentSpecial);
            }
        }

        var newRowWrapped = new bool[rows];
        Array.Copy(this.rowWrapped, newRowWrapped, Math.Min(this.rowWrapped.Length, rows));

        // A row that wrapped into a column past the new grid width is no
        // longer a meaningful wrap relation — clear the flag.
        if (cols < this.Cols)
        {
            for (int i = 0; i < rows; i++)
            {
                newRowWrapped[i] = false;
            }
        }

        this.cells = newCells;
        this.Rows = rows;
        this.Cols = cols;
        this.dirtyRows = new bool[rows];
        this.rowWrapped = newRowWrapped;
        var newTabStops = CreateDefaultTabStops(cols);
        Array.Copy(this.tabStops, newTabStops, Math.Min(this.tabStops.Length, newTabStops.Length));
        this.tabStops = newTabStops;
        this.scrollTop = 0;
        this.scrollBottom = rows - 1;
        this.allDirty = true;
        this.bgHistogramValid = false;

        if (this.cursorRow >= rows)
        {
            this.cursorRow = rows - 1;
        }

        if (this.cursorCol >= cols)
        {
            this.cursorCol = cols - 1;
        }
    }

    private void ResizeReflowPrimary(int newCols, int newRows)
    {
        // Step 1: collect every "physical" row (scrollback oldest-first,
        // then live grid top-to-bottom up to last-meaningful-row) with
        // its wrap flag and a marker for where the cursor sits.
        var physRows = new List<Cell[]>();
        var physWrapped = new List<bool>();
        int cursorPhysRow = -1;
        int cursorPhysCol = Math.Clamp(this.cursorCol, 0, Math.Max(0, this.Cols - 1));

        for (int i = 0; i < this.scrollbackCount; i++)
        {
            int slot = (this.scrollbackHead + i) % Math.Max(this.scrollbackLimit, 1);
            var src = this.scrollbackRing[slot] ?? Array.Empty<Cell>();
            var copy = new Cell[src.Length];
            Array.Copy(src, copy, src.Length);
            physRows.Add(copy);
            physWrapped.Add(this.scrollbackWrappedRing[slot]);
        }

        int lastLiveRow = this.FindLastMeaningfulRow();
        int liveRowCount = Math.Max(this.cursorRow, lastLiveRow) + 1;
        liveRowCount = Math.Min(liveRowCount, this.Rows);

        for (int r = 0; r < liveRowCount; r++)
        {
            var row = new Cell[this.Cols];
            for (int c = 0; c < this.Cols; c++)
            {
                row[c] = this.cells[r, c];
            }

            physRows.Add(row);
            physWrapped.Add(this.rowWrapped[r]);

            if (r == this.cursorRow)
            {
                cursorPhysRow = physRows.Count - 1;
            }
        }

        // If we never recorded the cursor (e.g. cursor sits on a blank
        // trailing row beyond liveRowCount), add stub rows so its line
        // index stays representable.
        while (cursorPhysRow < 0 && physRows.Count <= this.scrollbackCount + this.cursorRow)
        {
            physRows.Add(new Cell[this.Cols]);
            physWrapped.Add(false);
            if (physRows.Count - 1 == this.scrollbackCount + this.cursorRow)
            {
                cursorPhysRow = physRows.Count - 1;
            }
        }

        if (cursorPhysRow < 0)
        {
            cursorPhysRow = Math.Max(0, physRows.Count - 1);
        }

        // Step 2: group consecutive physical rows into logical lines via
        // wrap flags. Also locate which logical line the cursor lives in
        // and its column offset inside that line.
        var logicalLines = new List<List<Cell>>();
        var logicalLineEndsWrapped = new List<bool>();
        int cursorLogicalLine = 0;
        int cursorLogicalCol = 0;

        int idx = 0;
        while (idx < physRows.Count)
        {
            var line = new List<Cell>();
            int start = idx;
            while (true)
            {
                var row = physRows[idx];
                bool wrapped = physWrapped[idx];

                if (idx == cursorPhysRow)
                {
                    cursorLogicalLine = logicalLines.Count;
                    cursorLogicalCol = line.Count + cursorPhysCol;
                }

                line.AddRange(row);

                if (!wrapped || idx + 1 >= physRows.Count)
                {
                    idx++;
                    break;
                }

                idx++;
            }

            _ = start;
            logicalLines.Add(line);
            logicalLineEndsWrapped.Add(false);
        }

        // Step 3: trim trailing default-blank cells from each logical
        // line. For the cursor's line, keep at least through cursorLogicalCol.
        for (int li = 0; li < logicalLines.Count; li++)
        {
            var line = logicalLines[li];
            int keep = line.Count;
            while (keep > 0 && IsDefaultBlank(line[keep - 1], this.defaultFg, this.detectedBg, this.defaultBg))
            {
                keep--;
            }

            if (li == cursorLogicalLine)
            {
                keep = Math.Max(keep, Math.Min(line.Count, cursorLogicalCol));
            }

            if (keep < line.Count)
            {
                line.RemoveRange(keep, line.Count - keep);
            }
        }

        // Step 4: re-chunk each logical line into newCols-wide physical
        // rows, handling wide-glyph straddle.
        var outRows = new List<Cell[]>();
        var outWrapped = new List<bool>();
        int newCursorRow = 0;
        int newCursorCol = 0;
        bool cursorPlaced = false;

        for (int li = 0; li < logicalLines.Count; li++)
        {
            var line = logicalLines[li];
            int outRowStart = outRows.Count;

            if (line.Count == 0)
            {
                outRows.Add(BlankRow(newCols, this.defaultFg, this.detectedBg, this.currentSpecial));
                outWrapped.Add(false);
            }
            else
            {
                var current = new List<Cell>(newCols);
                int ci = 0;
                while (ci < line.Count)
                {
                    var cell = line[ci];
                    int width = CellWidth(cell);

                    // Continuation cells (null char) belong with their
                    // lead; they were emitted inline by PutChar. Skip
                    // them here — the lead's width==2 path handles them.
                    if (cell.Character is null)
                    {
                        ci++;
                        continue;
                    }

                    if (width == 2)
                    {
                        if (current.Count + 2 > newCols)
                        {
                            // Pad with a blank, wrap.
                            while (current.Count < newCols)
                            {
                                current.Add(DefaultBlankCell(this.defaultFg, this.detectedBg, this.currentSpecial));
                            }

                            outRows.Add(current.ToArray());
                            outWrapped.Add(true);
                            current = new List<Cell>(newCols);
                        }

                        current.Add(cell);

                        // Synthetic continuation cell.
                        var cont = cell;
                        cont.Set(null, new CellStyle(cell.ForegroundColor, cell.BackgroundColor, cell.SpecialColor, cell.Reverse, cell.Italic, cell.Bold, cell.Underline, cell.Undercurl));
                        current.Add(cont);
                    }
                    else
                    {
                        if (current.Count + 1 > newCols)
                        {
                            outRows.Add(current.ToArray());
                            outWrapped.Add(true);
                            current = new List<Cell>(newCols);
                        }

                        current.Add(cell);
                    }

                    ci++;
                }

                // Pad last chunk to newCols.
                while (current.Count < newCols)
                {
                    current.Add(DefaultBlankCell(this.defaultFg, this.detectedBg, this.currentSpecial));
                }

                outRows.Add(current.ToArray());
                outWrapped.Add(false);
            }

            // Place cursor if this is the cursor's logical line.
            if (!cursorPlaced && li == cursorLogicalLine)
            {
                int col = Math.Max(0, cursorLogicalCol);
                int rowOffset = newCols > 0 ? col / newCols : 0;
                int colOffset = newCols > 0 ? col % newCols : 0;
                int rowsForLine = outRows.Count - outRowStart;
                if (rowOffset >= rowsForLine)
                {
                    rowOffset = Math.Max(0, rowsForLine - 1);
                    colOffset = newCols > 0 ? Math.Min(colOffset, newCols - 1) : 0;
                }

                newCursorRow = outRowStart + rowOffset;
                newCursorCol = colOffset;
                cursorPlaced = true;
            }
        }

        if (!cursorPlaced)
        {
            newCursorRow = outRows.Count;
            newCursorCol = 0;
        }

        // Step 5: partition into scrollback + live grid.
        int totalOut = outRows.Count;
        int liveStart = Math.Max(0, totalOut - newRows);

        // Ensure cursor fits in the live grid window. If newCursorRow
        // sits above liveStart (content pushed cursor into scrollback),
        // bump liveStart up so the cursor stays on row 0+.
        if (newCursorRow < liveStart)
        {
            liveStart = newCursorRow;
        }

        // Rows [0 .. liveStart) become scrollback (FIFO).
        var newScrollbackRing = this.scrollbackLimit > 0
            ? new Cell[]?[this.scrollbackLimit]
            : Array.Empty<Cell[]?>();
        var newScrollbackWrapped = this.scrollbackLimit > 0
            ? new bool[this.scrollbackLimit]
            : Array.Empty<bool>();
        int newScrollCount = 0;

        if (this.scrollbackLimit > 0)
        {
            int first = Math.Max(0, liveStart - this.scrollbackLimit);
            for (int i = first; i < liveStart; i++)
            {
                newScrollbackRing[newScrollCount] = outRows[i];
                newScrollbackWrapped[newScrollCount] = outWrapped[i];
                newScrollCount++;
            }
        }

        // Live grid: take up to newRows rows starting at liveStart, pad
        // remainder with blanks.
        var newLive = new Cell[newRows, newCols];
        var newLiveWrapped = new bool[newRows];
        int filled = 0;
        for (int i = liveStart; i < totalOut && filled < newRows; i++, filled++)
        {
            var row = outRows[i];
            for (int c = 0; c < newCols; c++)
            {
                newLive[filled, c] = c < row.Length ? row[c] : DefaultBlankCell(this.defaultFg, this.detectedBg, this.currentSpecial);
            }

            newLiveWrapped[filled] = outWrapped[i];
        }

        for (int i = filled; i < newRows; i++)
        {
            for (int c = 0; c < newCols; c++)
            {
                newLive[i, c] = DefaultBlankCell(this.defaultFg, this.detectedBg, this.currentSpecial);
            }

            newLiveWrapped[i] = false;
        }

        // The live-grid's last row must never carry a wrap flag (nothing
        // follows within the live grid) — wrap state beyond the live
        // grid is not representable.
        if (newRows > 0)
        {
            newLiveWrapped[newRows - 1] = false;
        }

        // Step 6: commit.
        this.cells = newLive;
        this.rowWrapped = newLiveWrapped;
        this.scrollbackRing = newScrollbackRing;
        this.scrollbackWrappedRing = newScrollbackWrapped;
        this.scrollbackHead = 0;
        this.scrollbackCount = newScrollCount;
        this.Rows = newRows;
        this.Cols = newCols;
        this.dirtyRows = new bool[newRows];
        var newTabStops = CreateDefaultTabStops(newCols);
        Array.Copy(this.tabStops, newTabStops, Math.Min(this.tabStops.Length, newTabStops.Length));
        this.tabStops = newTabStops;
        this.scrollTop = 0;
        this.scrollBottom = newRows - 1;
        this.allDirty = true;
        this.bgHistogramValid = false;
        this.PendingWrap = false;

        int cursorLive = Math.Max(0, newCursorRow - liveStart);
        this.cursorRow = Math.Clamp(cursorLive, 0, Math.Max(0, newRows - 1));
        this.cursorCol = Math.Clamp(newCursorCol, 0, Math.Max(0, newCols - 1));
    }

    private int FindLastMeaningfulRow()
    {
        for (int r = this.Rows - 1; r >= 0; r--)
        {
            for (int c = 0; c < this.Cols; c++)
            {
                if (!IsDefaultBlank(this.cells[r, c], this.defaultFg, this.detectedBg, this.defaultBg))
                {
                    return r;
                }
            }

            if (this.rowWrapped[r])
            {
                return r;
            }
        }

        return -1;
    }

    private void MarkRowWrapped(int row)
    {
        if (this.rowWrapped is not null && row >= 0 && row < this.rowWrapped.Length)
        {
            this.rowWrapped[row] = true;
        }
    }

    private void ClearRowWrapped(int row)
    {
        if (this.rowWrapped is not null && row >= 0 && row < this.rowWrapped.Length)
        {
            this.rowWrapped[row] = false;
        }
    }
}
