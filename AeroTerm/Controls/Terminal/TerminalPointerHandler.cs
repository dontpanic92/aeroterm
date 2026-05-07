// <copyright file="TerminalPointerHandler.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using System.Runtime.InteropServices;
using AeroTerm.Controls;
using AeroTerm.Pty;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;

/// <summary>
/// Encapsulates all pointer-related state and event dispatch for
/// <see cref="TerminalControl"/>: hover tracking, hyperlink
/// modifier/hover state, drag-select lifecycle, middle-click paste
/// routing, and wheel-to-scrollback navigation. Instance state is
/// manipulated only from the UI thread.
/// </summary>
internal sealed class TerminalPointerHandler
{
    private const double ScrollbackRowsPerWheelDelta = 3.0;

    // Auto-scroll cadence while the pointer is past the viewport edge
    // during a drag-select. Small enough to feel responsive; coarse enough
    // not to eat the selection on a single twitchy touchpad gesture.
    private static readonly TimeSpan DragAutoScrollInterval = TimeSpan.FromMilliseconds(60);

    private readonly TerminalControl owner;
    private readonly WheelDeltaAccumulator scrollbackWheelAccumulator = new();

    private bool pointerDragSelecting;
    private int pointerRow = -1;
    private int pointerCol = -1;
    private bool pointerInside;
    private bool hyperlinkModifierDown;
    private Cursor? handCursor;
    private HyperlinkRun? currentHyperlinkRun;

    private DispatcherTimer? dragAutoScrollTimer;
    private Point lastDragPointerPosition;
    private int dragAutoScrollDirection;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalPointerHandler"/> class.
    /// </summary>
    /// <param name="owner">The terminal control that owns this handler.</param>
    public TerminalPointerHandler(TerminalControl owner)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// Gets the currently-hovered hyperlink run (when the hyperlink
    /// modifier key is held and the pointer is over a linkable cell), or
    /// <see langword="null"/> otherwise. Used by the render path to
    /// draw the hyperlink underline.
    /// </summary>
    public HyperlinkRun? CurrentHyperlinkRun => this.currentHyperlinkRun;

    /// <summary>
    /// Tests whether the supplied modifier state matches the
    /// platform-specific hyperlink activation chord (Cmd on macOS,
    /// Ctrl on Windows/Linux, with no extras).
    /// </summary>
    /// <param name="modifiers">The modifier state.</param>
    /// <returns><see langword="true"/> if the chord matches.</returns>
    public static bool IsHyperlinkModifier(KeyModifiers modifiers)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return modifiers.HasFlag(KeyModifiers.Meta)
                && !modifiers.HasFlag(KeyModifiers.Shift)
                && !modifiers.HasFlag(KeyModifiers.Control)
                && !modifiers.HasFlag(KeyModifiers.Alt);
        }

        return modifiers.HasFlag(KeyModifiers.Control)
            && !modifiers.HasFlag(KeyModifiers.Shift)
            && !modifiers.HasFlag(KeyModifiers.Alt)
            && !modifiers.HasFlag(KeyModifiers.Meta);
    }

    /// <summary>
    /// Updates the cached hyperlink-modifier state from a key event and
    /// refreshes the hover run when the state changes.
    /// </summary>
    /// <param name="modifiers">The latest modifier state.</param>
    public void UpdateHyperlinkModifier(KeyModifiers modifiers)
    {
        bool down = IsHyperlinkModifier(modifiers);
        if (down == this.hyperlinkModifierDown)
        {
            return;
        }

        this.hyperlinkModifierDown = down;
        this.RefreshHyperlinkHover();
    }

    /// <summary>
    /// Clears the cached hyperlink-modifier state (on focus loss) so a
    /// subsequent focus-gain with the modifier released doesn't leave
    /// the hand cursor latched.
    /// </summary>
    public void OnFocusLost()
    {
        if (this.hyperlinkModifierDown)
        {
            this.hyperlinkModifierDown = false;
            this.RefreshHyperlinkHover();
        }
    }

    /// <summary>
    /// Handles the <c>PointerPressed</c> event.
    /// </summary>
    /// <param name="e">The pointer event.</param>
    public void HandlePointerPressed(PointerPressedEventArgs e)
    {
        this.owner.Focus();

        var point = e.GetCurrentPoint(this.owner);
        bool isLeft = point.Properties.IsLeftButtonPressed;
        bool isMiddle = point.Properties.IsMiddleButtonPressed;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool mouseTrackingOff = this.owner.Buffer.MouseTrackingMode == MouseTrackingMode.None;

        this.UpdateHyperlinkModifier(e.KeyModifiers);

        // Middle-click pastes (PRIMARY on Linux/X11, regular clipboard elsewhere).
        // Only when mouse-tracking is off — apps that capture the mouse
        // (vim, tmux, htop…) get the raw button report.
        if (isMiddle && mouseTrackingOff && this.owner.Clipboard.MiddleClickPastes)
        {
            this.owner.Clipboard.PasteMiddleClick();
            e.Handled = true;
            return;
        }

        // Pointer rows translate to absolute rows once the viewport is
        // scrolled into scrollback. Selection now spans the full buffer;
        // hyperlink resolution remains visible-grid-only (live screen).
        int gridRows = (int)this.owner.DesiredRowCount;
        int historyRows = Math.Min(this.owner.ViewportOffset, gridRows);
        int scrollbackCount = this.owner.Buffer.ScrollbackCount;
        int absRowAtScreenZero = scrollbackCount - this.owner.ViewportOffset;

        // Modifier+left-click on a hyperlink opens it; short-circuit before
        // selection logic so the click doesn't begin a drag selection.
        if (isLeft && IsHyperlinkModifier(e.KeyModifiers))
        {
            var (hr, hc) = this.owner.PixelToGridPosition(point.Position);
            int hLive = hr - historyRows;
            if (hLive >= 0)
            {
                var hcells = this.owner.Buffer.GetScreen()?.Cells;
                var run = HyperlinkBehavior.GetRunAt(hcells, hLive, hc);
                if (run is { } activatedRun)
                {
                    HyperlinkBehavior.Activate(activatedRun.Uri);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (isLeft && (mouseTrackingOff || shift))
        {
            var (row, col) = this.owner.PixelToGridPosition(point.Position);
            int absRow = absRowAtScreenZero + row;

            var screen = this.owner.Buffer.GetScreen();
            if (screen is not null)
            {
                var rowSource = new BufferRowSource(this.owner.Buffer, screen);
                int clicks = Math.Max(1, e.ClickCount);
                if (clicks == 2)
                {
                    this.owner.Selection.BeginWord(absRow, col, rowSource);
                }
                else if (clicks >= 3)
                {
                    this.owner.Selection.BeginLine(absRow, rowSource);
                }
                else
                {
                    this.owner.Selection.BeginCharacter(absRow, col, rowSource);
                }

                this.pointerDragSelecting = true;
                e.Pointer.Capture(this.owner);
                this.owner.InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        var (r, c) = this.owner.PixelToGridPosition(point.Position);
        e.Handled = this.owner.InputHandler.HandlePointerPressed(e, r + 1, c + 1);
    }

    /// <summary>
    /// Handles the <c>PointerMoved</c> event.
    /// </summary>
    /// <param name="e">The pointer event.</param>
    public void HandlePointerMoved(PointerEventArgs e)
    {
        var (row, col) = this.owner.PixelToGridPosition(e.GetCurrentPoint(this.owner).Position);

        this.pointerInside = true;
        this.pointerRow = row;
        this.pointerCol = col;
        this.UpdateHyperlinkModifier(e.KeyModifiers);
        this.RefreshHyperlinkHover();

        if (this.pointerDragSelecting)
        {
            int scrollbackCount = this.owner.Buffer.ScrollbackCount;
            int absRowAtScreenZero = scrollbackCount - this.owner.ViewportOffset;
            int absRow = absRowAtScreenZero + row;
            var screen = this.owner.Buffer.GetScreen();
            if (screen is not null)
            {
                var rowSource = new BufferRowSource(this.owner.Buffer, screen);
                this.owner.Selection.ExtendTo(absRow, col, rowSource);
                this.owner.InvalidateVisual();
            }

            // Auto-scroll: if the pointer drifted above the top inset or
            // below the visible grid, schedule the auto-scroll loop. The
            // loop converts the pointer's clamped row back into an absRow
            // each tick so the selection extends correctly.
            this.UpdateDragAutoScroll(e.GetCurrentPoint(this.owner).Position);

            e.Handled = true;
            return;
        }

        e.Handled = this.owner.InputHandler.HandlePointerMoved(e, row + 1, col + 1);
    }

    /// <summary>
    /// Handles the <c>PointerReleased</c> event.
    /// </summary>
    /// <param name="e">The pointer event.</param>
    public void HandlePointerReleased(PointerReleasedEventArgs e)
    {
        if (this.pointerDragSelecting)
        {
            this.pointerDragSelecting = false;
            this.StopDragAutoScroll();
            e.Pointer.Capture(null);

            // A plain click with no drag clears the selection so the next
            // keystroke isn't preceded by a phantom range.
            if (this.owner.Selection.IsEmpty)
            {
                this.owner.Selection.Clear();
                this.owner.InvalidateVisual();
            }
            else
            {
                // A completed drag that actually selected text should be
                // published to the X11 PRIMARY selection so middle-click
                // paste works across applications. No-op on other platforms.
                this.owner.Clipboard.PublishSelectionToPrimary(this.owner.Selection, this.owner.Buffer);
            }

            e.Handled = true;
            return;
        }

        var (row, col) = this.owner.PixelToGridPosition(e.GetCurrentPoint(this.owner).Position);
        e.Handled = this.owner.InputHandler.HandlePointerReleased(e, row + 1, col + 1);
    }

    /// <summary>
    /// Handles the <c>PointerWheelChanged</c> event.
    /// Returns <see langword="true"/> to stop further processing.
    /// </summary>
    /// <param name="e">The wheel event.</param>
    public void HandlePointerWheel(PointerWheelEventArgs e)
    {
        var (row, col) = this.owner.PixelToGridPosition(e.GetCurrentPoint(this.owner).Position);
        if (this.owner.InputHandler.HandlePointerWheel(e, row + 1, col + 1))
        {
            this.scrollbackWheelAccumulator.Reset();
            e.Handled = true;
            return;
        }

        // Mouse tracking is off and the PTY did not consume the event —
        // use the wheel to navigate the scrollback viewport instead.
        // Disabled while the alt buffer is active (pagers manage their own
        // scrolling via arrow keys / page keys).
        if (this.owner.Buffer.IsUsingAltBuffer)
        {
            this.scrollbackWheelAccumulator.Reset();
            return;
        }

        int scrollbackCount = this.owner.Buffer.ScrollbackCount;
        if (scrollbackCount == 0 && this.owner.ViewportOffset == 0)
        {
            this.scrollbackWheelAccumulator.Reset();
            return;
        }

        double deltaY = e.Delta.Y;
        if (deltaY == 0 || !double.IsFinite(deltaY))
        {
            return;
        }

        int before = this.owner.ViewportOffset;
        if ((deltaY > 0 && before >= scrollbackCount) || (deltaY < 0 && before <= 0))
        {
            this.scrollbackWheelAccumulator.Reset();
            e.Handled = true;
            return;
        }

        // One wheel notch moves three lines. `deltaY` is positive when
        // scrolling up (toward older content), and the accumulator keeps
        // sub-line trackpad deltas from being rounded away.
        int lines = this.scrollbackWheelAccumulator.Add(deltaY, ScrollbackRowsPerWheelDelta);
        if (lines == 0)
        {
            e.Handled = true;
            return;
        }

        int target = Math.Clamp(before + lines, 0, scrollbackCount);
        if (target == before)
        {
            this.scrollbackWheelAccumulator.Reset();
            e.Handled = true;
            return;
        }

        this.owner.ViewportOffset = target;
        if (target == 0 || target == scrollbackCount)
        {
            this.scrollbackWheelAccumulator.Reset();
        }

        // Selection now uses absolute-row coordinates and remains valid
        // across scrollback navigation; nothing to clear here.
        this.owner.InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>
    /// Handles the <c>PointerExited</c> event.
    /// </summary>
    public void HandlePointerExited()
    {
        this.pointerInside = false;
        this.pointerRow = -1;
        this.pointerCol = -1;
        this.RefreshHyperlinkHover();
    }

    /// <summary>
    /// Disposes the cached hand cursor.
    /// </summary>
    public void DisposeResources()
    {
        this.handCursor?.Dispose();
        this.handCursor = null;
        this.StopDragAutoScroll();
    }

    /// <summary>
    /// Starts or stops the drag auto-scroll timer based on the pointer's
    /// position relative to the visible grid bounds. Called on every
    /// pointer-moved event during an active drag.
    /// </summary>
    /// <param name="position">Current pointer position in control-local pixels.</param>
    private void UpdateDragAutoScroll(Point position)
    {
        this.lastDragPointerPosition = position;

        double topEdge = this.owner.TopInset;
        double bottomEdge = this.owner.Bounds.Height;
        int direction = 0;
        if (position.Y < topEdge)
        {
            direction = +1;  // scroll up into older history
        }
        else if (position.Y > bottomEdge)
        {
            direction = -1;  // scroll down toward live grid
        }

        // Skip up-direction auto-scroll when there's no scrollback to show
        // and skip down-direction auto-scroll when already at the bottom.
        if (direction > 0 && this.owner.ViewportOffset >= this.owner.Buffer.ScrollbackCount)
        {
            direction = 0;
        }
        else if (direction < 0 && this.owner.ViewportOffset == 0)
        {
            direction = 0;
        }

        if (direction == 0)
        {
            this.StopDragAutoScroll();
            return;
        }

        this.dragAutoScrollDirection = direction;
        if (this.dragAutoScrollTimer is null)
        {
            this.dragAutoScrollTimer = new DispatcherTimer(DragAutoScrollInterval, DispatcherPriority.Input, this.OnDragAutoScrollTick);
            this.dragAutoScrollTimer.Start();
        }
    }

    private void StopDragAutoScroll()
    {
        if (this.dragAutoScrollTimer is null)
        {
            return;
        }

        this.dragAutoScrollTimer.Stop();
        this.dragAutoScrollTimer.Tick -= this.OnDragAutoScrollTick;
        this.dragAutoScrollTimer = null;
        this.dragAutoScrollDirection = 0;
    }

    private void OnDragAutoScrollTick(object? sender, EventArgs e)
    {
        if (!this.pointerDragSelecting || this.dragAutoScrollDirection == 0)
        {
            this.StopDragAutoScroll();
            return;
        }

        // Use the alt-buffer guard from the wheel handler: pagers manage
        // their own scrolling, and we shouldn't fight them.
        if (this.owner.Buffer.IsUsingAltBuffer)
        {
            this.StopDragAutoScroll();
            return;
        }

        int scrollbackCount = this.owner.Buffer.ScrollbackCount;
        int before = this.owner.ViewportOffset;
        int target = Math.Clamp(before + this.dragAutoScrollDirection, 0, scrollbackCount);
        if (target == before)
        {
            this.StopDragAutoScroll();
            return;
        }

        this.owner.ViewportOffset = target;

        // Re-extend the selection using the latest pointer position so the
        // active endpoint follows the newly-revealed row.
        var (row, col) = this.owner.PixelToGridPosition(this.lastDragPointerPosition);
        int absRowAtScreenZero = this.owner.Buffer.ScrollbackCount - this.owner.ViewportOffset;
        int absRow = absRowAtScreenZero + row;
        var screen = this.owner.Buffer.GetScreen();
        if (screen is not null)
        {
            var rowSource = new BufferRowSource(this.owner.Buffer, screen);
            this.owner.Selection.ExtendTo(absRow, col, rowSource);
        }

        this.owner.InvalidateVisual();
    }

    /// <summary>
    /// Refreshes the hyperlink hover run and swaps the cursor between the
    /// cached hand cursor and the cursor-state-manager's default.
    /// </summary>
    private void RefreshHyperlinkHover()
    {
        HyperlinkRun? newRun = null;
        if (this.pointerInside && this.hyperlinkModifierDown)
        {
            // When the viewport is scrolled into scrollback, the top rows
            // on-screen aren't part of the live grid. Translate the pointer
            // row back into live-grid coordinates; skip cleanly if the row
            // is scrollback-only (hyperlink resolution over history is not
            // supported yet).
            int rows = (int)this.owner.DesiredRowCount;
            int historyRows = Math.Min(this.owner.ViewportOffset, rows);
            int liveRow = this.pointerRow - historyRows;
            if (liveRow >= 0)
            {
                var cells = this.owner.Buffer.GetScreen()?.Cells;
                newRun = HyperlinkBehavior.GetRunAt(cells, liveRow, this.pointerCol);
            }
        }

        bool runChanged = !Nullable.Equals(this.currentHyperlinkRun, newRun);
        this.currentHyperlinkRun = newRun;

        if (newRun is not null)
        {
            this.handCursor ??= new Cursor(StandardCursorType.Hand);
            if (this.owner.Cursor != this.handCursor)
            {
                this.owner.Cursor = this.handCursor;
            }
        }
        else
        {
            // Restore whatever the cursor manager thinks is right for the
            // current terminal mode (usually Ibeam).
            bool mouseEnabled = this.owner.Buffer.MouseTrackingMode != MouseTrackingMode.None;
            var defaultCursor = this.owner.CursorState.UpdatePointerCursor(this.owner.CurrentModeInfo, mouseEnabled);
            if (!ReferenceEquals(this.owner.Cursor, defaultCursor))
            {
                this.owner.Cursor = defaultCursor;
            }
        }

        if (runChanged)
        {
            this.owner.InvalidateVisual();
        }
    }
}
