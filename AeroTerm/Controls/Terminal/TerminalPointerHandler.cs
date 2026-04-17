// <copyright file="TerminalPointerHandler.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using System.Runtime.InteropServices;
using AeroTerm.Pty;
using Avalonia;
using Avalonia.Input;

/// <summary>
/// Encapsulates all pointer-related state and event dispatch for
/// <see cref="TerminalControl"/>: hover tracking, hyperlink
/// modifier/hover state, drag-select lifecycle, middle-click paste
/// routing, and wheel-to-scrollback navigation. Instance state is
/// manipulated only from the UI thread.
/// </summary>
internal sealed class TerminalPointerHandler
{
    private readonly TerminalControl owner;

    private bool pointerDragSelecting;
    private int pointerRow = -1;
    private int pointerCol = -1;
    private bool pointerInside;
    private bool hyperlinkModifierDown;
    private Cursor? handCursor;
    private HyperlinkRun? currentHyperlinkRun;

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

        // Pointer rows translate to live-grid rows once the viewport is
        // scrolled into scrollback; selection and hyperlink resolution are
        // both visible-grid-only features and must skip scrollback-only rows.
        int gridRows = (int)this.owner.DesiredRowCount;
        int historyRows = Math.Min(this.owner.ViewportOffset, gridRows);

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
            int liveRow = row - historyRows;
            if (liveRow < 0)
            {
                // Click landed on a scrollback-only row; selection is a
                // visible-grid-only feature today, so do nothing.
                e.Handled = true;
                return;
            }

            var cells = this.owner.Buffer.GetScreen()?.Cells;
            if (cells is not null)
            {
                int clicks = Math.Max(1, e.ClickCount);
                if (clicks == 2)
                {
                    this.owner.Selection.BeginWord(liveRow, col, cells);
                }
                else if (clicks >= 3)
                {
                    this.owner.Selection.BeginLine(liveRow, cells);
                }
                else
                {
                    this.owner.Selection.BeginCharacter(liveRow, col, cells);
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
            int gridRows = (int)this.owner.DesiredRowCount;
            int historyRows = Math.Min(this.owner.ViewportOffset, gridRows);
            int liveRow = Math.Max(0, row - historyRows);
            var cells = this.owner.Buffer.GetScreen()?.Cells;
            if (cells is not null)
            {
                this.owner.Selection.ExtendTo(liveRow, col, cells);
                this.owner.InvalidateVisual();
            }

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
            e.Handled = true;
            return;
        }

        // Mouse tracking is off and the PTY did not consume the event —
        // use the wheel to navigate the scrollback viewport instead.
        // Disabled while the alt buffer is active (pagers manage their own
        // scrolling via arrow keys / page keys).
        if (this.owner.Buffer.IsUsingAltBuffer)
        {
            return;
        }

        int scrollbackCount = this.owner.Buffer.ScrollbackCount;
        if (scrollbackCount == 0 && this.owner.ViewportOffset == 0)
        {
            return;
        }

        // One wheel notch moves three lines. `e.Delta.Y` is positive when
        // scrolling up (toward older content).
        int lines = (int)Math.Round(e.Delta.Y * 3.0);
        if (lines == 0)
        {
            return;
        }

        int before = this.owner.ViewportOffset;
        int target = Math.Clamp(before + lines, 0, scrollbackCount);
        if (target == before)
        {
            e.Handled = true;
            return;
        }

        this.owner.ViewportOffset = target;

        // Entering scrollback invalidates the visible-grid-anchored selection.
        if (!this.owner.Selection.IsEmpty)
        {
            this.owner.Selection.Clear();
        }

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
