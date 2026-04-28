// <copyright file="TerminalControl.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Runtime.InteropServices;
using System.Text;
using AeroTerm.Controls.Terminal;
using AeroTerm.Pty;
using AeroTerm.Services;
using AeroTerm.Utilities;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;

/// <summary>
/// Custom Avalonia control that hosts a terminal emulator with PTY backend,
/// VT parsing, and SkiaSharp rendering. The control is a thin coordinator
/// that composes focused peer helpers under
/// <see cref="AeroTerm.Controls.Terminal"/>:
/// <see cref="TerminalInputHandler"/> for keyboard/text input,
/// <see cref="TerminalPointerHandler"/> for pointer &amp; selection dispatch,
/// <see cref="TerminalClipboardBridge"/> for copy/paste and PRIMARY
/// selection, <see cref="TerminalPtyBridge"/> for PTY lifecycle and the
/// reader thread, and <see cref="TerminalVisualHost"/> for the render
/// pipeline.
/// </summary>
public class TerminalControl : Control, IDisposable
{
    private const float TitleBarInsetBlurSigma = 8f;

    private readonly TerminalBuffer buffer;
    private readonly VtParser parser;
    private readonly TerminalInputHandler inputHandler;
    private readonly EditorTextInputMethodClient imeClient;
    private readonly FontFallbackChain fontChain = new FontFallbackChain();
    private readonly LigatureTextShaper ligatureTextShaper = new();
    private readonly TerminalRenderer renderer;
    private readonly CursorStateManager cursorState;
    private readonly TerminalSelection selection = new();
    private readonly SearchOverlay searchOverlay = new();
    private readonly DispatcherTimer searchRecomputeTimer;
    private readonly PromptMarksRegistry promptMarks = new();

    private readonly TerminalClipboardBridge clipboard;
    private readonly TerminalPtyBridge ptyBridge;
    private readonly SynchronizedUpdateCoordinator syncUpdateCoordinator;
    private readonly TerminalPointerHandler pointerHandler;
    private readonly TerminalVisualHost visualHost;

    private TextLayoutParameters textParam;
    private ModeInfo currentModeInfo;
    private int lastReportedBg = -1;
    private volatile bool isDisposed;
    private SKColor selectionColor = new SKColor(0x39, 0x66, 0xCC, 0x70);
    private int viewportOffset;
    private float topInset;
    private bool searchOverlayOpen;
    private bool searchUsingAltBufferSnapshot;
    private IReadOnlyList<SearchMatch> searchMatches = Array.Empty<SearchMatch>();
    private int activeMatchIndex = -1;
    private int searchSnapshotScrollbackCount;
    private int searchSnapshotRows;
    private int lastEvictedDelta;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalControl"/> class.
    /// </summary>
    /// <param name="cols">Initial column count.</param>
    /// <param name="rows">Initial row count.</param>
    public TerminalControl(int cols = 80, int rows = 24)
        : this(DefaultPtyConnectionFactory.Instance, cols, rows)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalControl"/> class.
    /// </summary>
    /// <param name="ptyFactory">Factory that produces the PTY connection once
    /// <see cref="StartProcess"/> is called. Tests can pass a fake.</param>
    /// <param name="cols">Initial column count.</param>
    /// <param name="rows">Initial row count.</param>
    public TerminalControl(IPtyConnectionFactory ptyFactory, int cols = 80, int rows = 24)
    {
        ArgumentNullException.ThrowIfNull(ptyFactory);

        this.buffer = new TerminalBuffer(cols, rows);

        this.clipboard = new TerminalClipboardBridge(
            () => TopLevel.GetTopLevel(this),
            text => this.inputHandler!.HandlePaste(text));

        this.parser = new VtParser(
            this.buffer,
            this.OnTitleChanged,
            data => this.ptyBridge!.WriteToPty(data),
            () => this.clipboard.ReadClipboardForParser(),
            text => this.clipboard.WriteClipboardFromParser(text));
        this.parser.BellRaised += (_, _) => this.BellRaised?.Invoke();
        this.parser.PromptMarkRaised += this.OnPromptMarkRaised;

        this.ptyBridge = new TerminalPtyBridge(ptyFactory, new ReaderHost(this));
        this.ptyBridge.ProcessExited += () => this.ProcessExited?.Invoke();

        // DECSET 2026 — synchronized update. The coordinator owns the 150 ms
        // watchdog and the "flush one redraw on exit" post so the reader
        // thread can stay oblivious to both.
        this.syncUpdateCoordinator = new SynchronizedUpdateCoordinator(
            this.buffer,
            () => this.ptyBridge.RequestRedraw());

        this.inputHandler = new TerminalInputHandler(this.ptyBridge.WriteToPty);

        this.ClipToBounds = true;
        this.Focusable = true;

        this.imeClient = new EditorTextInputMethodClient(this);
        this.renderer = new TerminalRenderer(this.fontChain, this.ligatureTextShaper, this.imeClient);
        this.currentModeInfo = new ModeInfo(CursorShape.Block, 100, CursorBlinking.BlinkOn);
        this.cursorState = new CursorStateManager(this.InvalidateVisual, () => this.currentModeInfo);

        this.pointerHandler = new TerminalPointerHandler(this);
        this.visualHost = new TerminalVisualHost(this.RenderWithSkia);

        this.AddHandler(
            InputElement.TextInputMethodClientRequestedEvent,
            (_, e) =>
            {
                e.Client = this.imeClient;
            });

        this.RebuildFontChain(FontPriorityList.GetDefaultPlatformFonts().ToList());
        this.textParam = new TextLayoutParameters(this.fontChain.PrimaryFontName, 11);

        // Scrollback-search overlay. Hosted by the caller (see
        // <see cref="SearchOverlayVisual"/>) — TerminalControl is a
        // plain Control, not a Panel, so it can't hold visual children
        // of its own. The overlay is owned here because its lifetime,
        // state, and wiring are terminal-specific.
        this.searchOverlay.QueryChanged += this.OnSearchQueryOrOptionsChanged;
        this.searchOverlay.NavigateRequested += this.OnSearchNavigateRequested;
        this.searchOverlay.CloseRequested += (_, _) => this.HideSearchOverlay();

        // 50 ms debounce: streaming PTY output shouldn't thrash the
        // matcher, but interactive edits should feel immediate.
        this.searchRecomputeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        this.searchRecomputeTimer.Tick += this.OnSearchRecomputeTick;
    }

    /// <summary>
    /// Occurs when the terminal title changes (OSC 0/2).
    /// </summary>
    public event Action<string>? TitleChanged;

    /// <summary>
    /// Occurs when the detected background color changes.
    /// </summary>
    public event Action<int>? BackgroundColorChanged;

    /// <summary>
    /// Occurs when the child process has exited.
    /// </summary>
    public event Action? ProcessExited;

    /// <summary>
    /// Occurs when the terminal receives a BEL (0x07) control character.
    /// Raised on the PTY reader thread; handlers must marshal to the UI
    /// thread themselves if they touch UI state.
    /// </summary>
    public event Action? BellRaised;

    /// <summary>
    /// Raised whenever <see cref="TopInset"/> changes. The argument is the
    /// new inset in pixels. Hosts that overlay sibling visuals on top of the
    /// terminal (e.g., the search overlay) subscribe to this so they can
    /// shift their layout out from under the floating title bar.
    /// </summary>
    internal event EventHandler<float>? TopInsetChanged;

    /// <summary>
    /// Gets or sets a value indicating whether font ligature is enabled.
    /// </summary>
    public bool EnableLigature { get; set; }

    /// <summary>
    /// Gets a value indicating whether an IME composition is in progress.
    /// </summary>
    public bool IsComposing => this.imeClient.IsComposing;

    /// <summary>
    /// Gets or sets the alpha channel used for the default background.
    /// </summary>
    public byte BackgroundAlpha { get; set; } = byte.MaxValue;

    /// <summary>
    /// Gets or sets the scrollback capacity (lines retained when content
    /// scrolls off the top of the primary buffer). Forwards to
    /// <see cref="TerminalBuffer.ScrollbackLimit"/> with the same clamping
    /// semantics. Setting a smaller value drops the oldest lines; 0 disables
    /// scrollback entirely.
    /// </summary>
    public int ScrollbackLimit
    {
        get => this.buffer.ScrollbackLimit;
        set
        {
            this.buffer.ScrollbackLimit = value;
            if (this.viewportOffset > this.buffer.ScrollbackCount)
            {
                this.viewportOffset = this.buffer.ScrollbackCount;
                this.InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets the visual (a <see cref="SearchOverlay"/>) that should be
    /// laid out on top of the terminal — top-right anchored — by the
    /// hosting container. Because <see cref="TerminalControl"/> derives
    /// from <see cref="Control"/> (not <see cref="Panel"/>) it cannot
    /// carry its own children; the caller is responsible for placing
    /// this visual as a sibling of the terminal within a
    /// <see cref="Grid"/> or similar panel.
    /// </summary>
    public Control SearchOverlayVisual => this.searchOverlay;

    /// <summary>
    /// Gets the current primary font family name.
    /// </summary>
    public string FontName => this.textParam.FontName;

    /// <summary>
    /// Gets the current font size in device-independent pixels (Skia units).
    /// </summary>
    public double FontSize => this.textParam.SkiaFontSize;

    /// <summary>
    /// Gets the current character cell width in pixels.
    /// </summary>
    public double CharWidth => this.textParam.CharWidth;

    /// <summary>
    /// Gets the current line height in pixels.
    /// </summary>
    public double LineHeight => this.textParam.LineHeight;

    /// <summary>
    /// Gets the desired row count based on current bounds and font metrics.
    /// </summary>
    public uint DesiredRowCount
    {
        get
        {
            var c = (uint)((this.Bounds.Height - this.topInset) / this.textParam.LineHeight);
            return c == 0 ? 1 : c;
        }
    }

    /// <summary>
    /// Gets the desired column count based on current bounds and font metrics.
    /// </summary>
    public uint DesiredColCount
    {
        get
        {
            var c = (uint)(this.Bounds.Width / this.textParam.CharWidth);
            return c == 0 ? 1 : c;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether a middle-button click inside
    /// the terminal should paste text. On Linux/X11 the source is the
    /// PRIMARY selection (with a fallback to the regular clipboard); on
    /// macOS and Windows the regular clipboard is always used.
    /// </summary>
    public bool MiddleClickPastes
    {
        get => this.clipboard.MiddleClickPastes;
        set => this.clipboard.MiddleClickPastes = value;
    }

    /// <summary>
    /// Gets the currently resolved pointer cursor type for tests.
    /// </summary>
    internal StandardCursorType? ResolvedPointerCursorType => this.cursorState.ResolvedPointerCursorType;

    /// <summary>
    /// Gets or sets the PRIMARY-selection backend. Defaults to
    /// <see cref="DefaultPrimarySelectionService.Instance"/>; tests can
    /// substitute a fake.
    /// </summary>
    internal IPrimarySelectionService PrimarySelectionService
    {
        get => this.clipboard.PrimarySelectionService;
        set => this.clipboard.PrimarySelectionService = value ?? DefaultPrimarySelectionService.Instance;
    }

    /// <summary>
    /// Gets the OS-level process identifier of the running child shell,
    /// or <c>null</c> if no process has been started (or the underlying
    /// PTY has since been released).
    /// </summary>
    internal int? ChildPid => this.ptyBridge.ChildPid;

    /// <summary>
    /// Gets the underlying terminal buffer. Used by peer helpers living in
    /// <see cref="AeroTerm.Controls.Terminal"/>.
    /// </summary>
    internal TerminalBuffer Buffer => this.buffer;

    /// <summary>
    /// Gets the active selection model. Used by peer helpers.
    /// </summary>
    internal TerminalSelection Selection => this.selection;

    /// <summary>
    /// Gets the keyboard/text input handler shared between
    /// <see cref="OnKeyDown"/> and the pointer handler.
    /// </summary>
    internal TerminalInputHandler InputHandler => this.inputHandler;

    /// <summary>
    /// Gets the clipboard bridge. Used by the pointer handler for middle-
    /// click paste and PRIMARY selection publication.
    /// </summary>
    internal TerminalClipboardBridge Clipboard => this.clipboard;

    /// <summary>
    /// Gets the cursor state manager, exposed to the pointer handler so it
    /// can restore the default cursor when the hand-hover clears.
    /// </summary>
    internal CursorStateManager CursorState => this.cursorState;

    /// <summary>
    /// Gets the current cached mode info, exposed to the pointer handler
    /// for cursor resolution.
    /// </summary>
    internal ModeInfo CurrentModeInfo => this.currentModeInfo;

    /// <summary>
    /// Gets or sets the viewport's scrollback offset in rows. Zero means
    /// the viewport is anchored to the live grid.
    /// </summary>
    internal int ViewportOffset
    {
        get => this.viewportOffset;
        set => this.viewportOffset = value;
    }

    /// <summary>
    /// Gets or sets the vertical inset in pixels reserved at the top of
    /// the control for a floating title-bar blur overlay. Grid rendering
    /// begins at this offset; the area above it shows a blurred preview
    /// of scrollback when the viewport is scrolled.
    /// </summary>
    internal float TopInset
    {
        get => this.topInset;

        set
        {
            if (this.topInset == value)
            {
                return;
            }

            this.topInset = value;
            this.TryResize();
            this.InvalidateVisual();
            this.TopInsetChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Gets the prompt-mark registry populated by OSC 133 / OSC 633 sequences.
    /// Exposed for tests and for the palette "jump" commands.
    /// </summary>
    internal PromptMarksRegistry PromptMarks => this.promptMarks;

    /// <summary>
    /// Scrolls the viewport to the nearest prior navigable prompt mark
    /// (<see cref="PromptMarkKind.OutputStart"/> or
    /// <see cref="PromptMarkKind.CommandStart"/>). No-op when no such mark
    /// exists above the current viewport anchor.
    /// </summary>
    /// <returns><see langword="true"/> when the viewport moved.</returns>
    public bool JumpToPreviousCommand()
    {
        return this.JumpToMark(previous: true);
    }

    /// <summary>
    /// Scrolls the viewport to the nearest later navigable prompt mark.
    /// Counterpart to <see cref="JumpToPreviousCommand"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the viewport moved.</returns>
    public bool JumpToNextCommand()
    {
        return this.JumpToMark(previous: false);
    }

    /// <summary>
    /// Starts a child process connected via a PTY.
    /// </summary>
    /// <param name="app">Absolute path to the executable.</param>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="env">Environment variables for the child process.</param>
    /// <param name="cwd">Working directory for the child process.</param>
    public void StartProcess(string app, string[] args, IDictionary<string, string> env, string cwd)
    {
        int cols = (int)this.DesiredColCount;
        int rows = (int)this.DesiredRowCount;
        this.ptyBridge.StartProcess(app, args, env, cwd, cols, rows);
    }

    /// <summary>
    /// Applies a color scheme to the terminal by updating the ANSI palette
    /// and triggering a redraw.
    /// </summary>
    /// <param name="scheme">The color scheme to apply.</param>
    public void ApplyColorScheme(Models.ColorScheme scheme)
    {
        this.buffer.SetAnsiPalette(scheme.Palette);
        this.buffer.RecolorDefaults(scheme.Foreground, scheme.Background);

        // Update the selection overlay color. If the scheme doesn't specify
        // one explicitly, derive a muted translucent tint from the foreground
        // so selections stay visible on any background.
        if (scheme.Selection is int sel)
        {
            byte r = (byte)((sel >> 16) & 0xFF);
            byte g = (byte)((sel >> 8) & 0xFF);
            byte b = (byte)(sel & 0xFF);
            this.selectionColor = new SKColor(r, g, b, 0x80);
        }
        else
        {
            byte fr = (byte)((scheme.Foreground >> 16) & 0xFF);
            byte fg = (byte)((scheme.Foreground >> 8) & 0xFF);
            byte fb = (byte)(scheme.Foreground & 0xFF);
            this.selectionColor = new SKColor(fr, fg, fb, 0x50);
        }

        // Nudge the PTY with a same-size resize to trigger SIGWINCH,
        // which causes the shell/application to redraw with new colors.
        // Palette-indexed colors in existing cells are baked as RGB at
        // write time, so only a full shell repaint can fix them.
        if (this.ptyBridge.IsRunning)
        {
            int cols = (int)this.DesiredColCount;
            int rows = (int)this.DesiredRowCount;
            this.ptyBridge.ForceResize(cols, rows);
        }

        this.BackgroundColorChanged?.Invoke(scheme.Background);
        this.InvalidateVisual();
    }

    /// <summary>
    /// Sets the font priority list and rebuilds the font chain.
    /// </summary>
    /// <param name="fonts">Ordered list of font family names.</param>
    public void SetFontPriorityList(List<string> fonts)
    {
        this.RebuildFontChain(fonts);
        if (this.fontChain.PrimaryFontName.Length > 0)
        {
            this.textParam = new TextLayoutParameters(this.fontChain.PrimaryFontName, this.textParam.PointSize);
            this.TryResize();
        }

        this.InvalidateVisual();
    }

    /// <summary>
    /// Sets the font size in points and rebuilds the text layout.
    /// </summary>
    /// <param name="pointSize">The font size in points.</param>
    public void SetFontSize(double pointSize)
    {
        if (pointSize <= 0 || this.fontChain.PrimaryFontName.Length == 0)
        {
            return;
        }

        this.textParam = new TextLayoutParameters(this.fontChain.PrimaryFontName, (float)pointSize);
        this.TryResize();
        this.InvalidateVisual();
    }

    /// <summary>
    /// Atomically updates the font priority list and font size, performing
    /// a single resize and invalidation. Use this instead of calling
    /// <see cref="SetFontPriorityList"/> and <see cref="SetFontSize"/>
    /// separately to avoid an intermediate resize with stale metrics.
    /// </summary>
    /// <param name="fonts">Ordered list of font family names.</param>
    /// <param name="pointSize">The font size in points.</param>
    public void ApplyFontChange(List<string> fonts, double pointSize)
    {
        this.RebuildFontChain(fonts);
        if (this.fontChain.PrimaryFontName.Length > 0 && pointSize > 0)
        {
            this.textParam = new TextLayoutParameters(this.fontChain.PrimaryFontName, (float)pointSize);
            this.TryResize();
        }

        this.InvalidateVisual();
    }

    /// <summary>
    /// Opens the scrollback-search overlay and focuses its text box.
    /// Clears any active text selection so the two overlays don't fight
    /// for the same visual channel. Idempotent if already open.
    /// </summary>
    public void ShowSearchOverlay()
    {
        this.searchOverlayOpen = true;
        if (!this.selection.IsEmpty)
        {
            this.selection.Clear();
        }

        this.searchOverlay.Open();
        this.RecomputeSearchMatchesNow();
        this.InvalidateVisual();
    }

    /// <summary>
    /// Closes the search overlay, clears the current match set, and
    /// returns focus to the terminal. Query text and toggles are
    /// preserved for the next open.
    /// </summary>
    public void HideSearchOverlay()
    {
        if (!this.searchOverlayOpen)
        {
            return;
        }

        this.searchOverlayOpen = false;
        this.searchRecomputeTimer.Stop();
        this.searchOverlay.Close();
        this.searchMatches = Array.Empty<SearchMatch>();
        this.activeMatchIndex = -1;
        this.searchOverlay.SetStatus(0, 0);
        this.Focus();
        this.InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        this.visualHost.Render(context, new Rect(0, 0, this.Bounds.Width, this.Bounds.Height));
    }

    /// <summary>
    /// Dispose the control.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Renders the control directly to a supplied Skia canvas for tests.
    /// </summary>
    /// <param name="canvas">The target Skia canvas.</param>
    internal void RenderForTesting(SKCanvas canvas)
    {
        this.RenderWithSkia(canvas);
    }

    /// <summary>
    /// Sets the IME preedit text for renderer tests.
    /// </summary>
    /// <param name="preeditText">The preedit text, or <c>null</c> to clear it.</param>
    /// <param name="cursorPos">The cursor position within the preedit text.</param>
    internal void SetPreeditTextForTesting(string? preeditText, int? cursorPos)
    {
        this.imeClient.SetPreeditText(preeditText, cursorPos);
    }

    /// <summary>
    /// Sets the current cursor blink visibility state for renderer tests.
    /// </summary>
    /// <param name="visible">A value indicating whether the cursor should be rendered as visible.</param>
    internal void SetCursorBlinkVisibleForTesting(bool visible)
    {
        this.cursorState.SetCursorBlinkVisible(visible);
    }

    /// <summary>
    /// Gets the resolved primary font name from the font chain for tests.
    /// </summary>
    /// <returns>The primary font family name, or empty if no font is resolved.</returns>
    internal string GetPrimaryFontNameForTesting()
    {
        return this.fontChain.PrimaryFontName;
    }

    /// <summary>
    /// Converts a pointer pixel position to grid (row, column) coordinates,
    /// clamped to the visible grid.
    /// </summary>
    /// <param name="pixel">The pointer position in control-local pixels.</param>
    /// <returns>A <c>(row, col)</c> tuple in visible-grid space.</returns>
    internal (int Row, int Col) PixelToGridPosition(Point pixel)
    {
        int row = (int)((pixel.Y - this.topInset) / this.textParam.LineHeight);
        int col = (int)(pixel.X / this.textParam.CharWidth);

        int maxRow = (int)this.DesiredRowCount - 1;
        int maxCol = (int)this.DesiredColCount - 1;

        row = Math.Clamp(row, 0, maxRow);
        col = Math.Clamp(col, 0, maxCol);

        return (row, col);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        return availableSize;
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            this.TryResize();
        }
    }

    /// <inheritdoc />
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Text is not null)
        {
            this.SnapToBottom();
            if (!this.selection.IsEmpty)
            {
                this.selection.Clear();
                this.InvalidateVisual();
            }

            this.inputHandler.HandleTextInput(e.Text);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        this.pointerHandler.UpdateHyperlinkModifier(e.KeyModifiers);

        // Copy/paste hotkeys take priority over the PTY input path so they
        // work regardless of what the shell does with Ctrl+C / Ctrl+V.
        if (e.Key == Key.C && IsCopyModifier(e.KeyModifiers))
        {
            if (!this.selection.IsEmpty)
            {
                this.clipboard.CopySelectionToClipboard(this.selection, this.buffer);
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.V && IsPasteModifier(e.KeyModifiers))
        {
            this.clipboard.PasteFromClipboard();
            e.Handled = true;
            return;
        }

        // Cmd+F / Ctrl+F: open the scrollback search overlay. Intercepted
        // before the input handler so it never reaches the shell.
        if (e.Key == Key.F && IsSearchModifier(e.KeyModifiers))
        {
            this.ShowSearchOverlay();
            e.Handled = true;
            return;
        }

        if (this.inputHandler.HandleKeyDown(e))
        {
            // A keystroke went to the shell: snap the viewport back to the
            // live grid (matching xterm) and invalidate the selection whose
            // coordinates are about to become stale.
            this.SnapToBottom();
            if (!this.selection.IsEmpty)
            {
                this.selection.Clear();
                this.InvalidateVisual();
            }

            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        this.pointerHandler.UpdateHyperlinkModifier(e.KeyModifiers);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        this.pointerHandler.HandlePointerPressed(e);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        this.pointerHandler.HandlePointerMoved(e);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        this.pointerHandler.HandlePointerReleased(e);
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        this.pointerHandler.HandlePointerWheel(e);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        this.pointerHandler.HandlePointerExited();
    }

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (this.buffer.FocusEventsEnabled)
        {
            this.ptyBridge.WriteToPty(Encoding.ASCII.GetBytes("\x1B[I"));
        }

        this.cursorState.UpdateCursorBlink(this.currentModeInfo, resetCursorBlink: true);
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (this.buffer.FocusEventsEnabled)
        {
            this.ptyBridge.WriteToPty(Encoding.ASCII.GetBytes("\x1B[O"));
        }

        this.inputHandler.ClearPressedButton();
        this.pointerHandler.OnFocusLost();
    }

    /// <summary>
    /// Dispose managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose, false from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.isDisposed)
        {
            this.isDisposed = true;

            if (disposing)
            {
                this.searchRecomputeTimer.Stop();
                this.syncUpdateCoordinator.Dispose();
                this.ptyBridge.Dispose();
                Dispatcher.UIThread.Post(this.DisposeCachedResources, DispatcherPriority.Background);
            }
        }
    }

    private static bool IsCopyModifier(KeyModifiers modifiers)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return modifiers.HasFlag(KeyModifiers.Meta)
                && !modifiers.HasFlag(KeyModifiers.Shift)
                && !modifiers.HasFlag(KeyModifiers.Control)
                && !modifiers.HasFlag(KeyModifiers.Alt);
        }

        return modifiers.HasFlag(KeyModifiers.Control) && modifiers.HasFlag(KeyModifiers.Shift);
    }

    private static bool IsPasteModifier(KeyModifiers modifiers) => IsCopyModifier(modifiers);

    private static bool IsSearchModifier(KeyModifiers modifiers)
    {
        // Same modifier used for hyperlink activation: Cmd on macOS,
        // Ctrl on Windows/Linux, with no extras.
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

    private void TryResize()
    {
        int cols = (int)this.DesiredColCount;
        int rows = (int)this.DesiredRowCount;
        if (cols < 1 || rows < 1)
        {
            return;
        }

        this.buffer.Resize(cols, rows);
        this.ptyBridge.Resize(cols, rows);

        // After resize the scrollback ring may have absorbed live rows, the
        // live grid height may have shrunk, and per-row column counts may
        // have changed. Clear any active selection rather than try to track
        // these shifts precisely.
        if (this.selection.Mode != TerminalSelectionMode.None)
        {
            this.selection.Clear();
        }
    }

    private void UpdateModeInfoFromBuffer()
    {
        var shape = this.buffer.RequestedCursorShape ?? CursorShape.Block;
        var blinking = this.buffer.RequestedCursorBlinking;
        var pointerMode = (PointerMode)this.buffer.PointerMode;
        this.currentModeInfo = new ModeInfo(
            shape,
            100,
            blinking,
            this.buffer.PointerShape,
            this.buffer.CursorVisible,
            cursorStyleEnabled: true,
            pointerMode);
    }

    private void ApplyTerminalUiState()
    {
        bool mouseEnabled = this.buffer.MouseTrackingMode != MouseTrackingMode.None;
        if (!mouseEnabled)
        {
            this.inputHandler.ClearPressedButton();
        }

        this.Cursor = this.cursorState.UpdatePointerCursor(this.currentModeInfo, mouseEnabled);
        this.cursorState.UpdateCursorBlink(this.currentModeInfo, resetCursorBlink: true);
    }

    private void SnapToBottom()
    {
        if (this.viewportOffset != 0)
        {
            this.viewportOffset = 0;
            this.InvalidateVisual();
        }
    }

    private void OnSearchQueryOrOptionsChanged(object? sender, System.EventArgs e)
    {
        if (!this.searchOverlayOpen)
        {
            return;
        }

        // Recompute immediately on user edit; only PTY-stream updates go
        // through the debounce timer.
        this.searchRecomputeTimer.Stop();
        this.RecomputeSearchMatchesNow();
        this.InvalidateVisual();
    }

    private void OnSearchNavigateRequested(object? sender, bool forward)
    {
        if (!this.searchOverlayOpen || this.searchMatches.Count == 0)
        {
            return;
        }

        int n = this.searchMatches.Count;
        int idx = this.activeMatchIndex < 0 ? (forward ? 0 : n - 1) : (forward ? (this.activeMatchIndex + 1) % n : (this.activeMatchIndex - 1 + n) % n);
        this.activeMatchIndex = idx;
        this.ScrollToMatch(this.searchMatches[idx]);
        this.searchOverlay.SetStatus(idx + 1, n);
        this.InvalidateVisual();
    }

    private void OnSearchRecomputeTick(object? sender, System.EventArgs e)
    {
        this.searchRecomputeTimer.Stop();
        if (!this.searchOverlayOpen)
        {
            return;
        }

        this.RecomputeSearchMatchesNow();
        this.InvalidateVisual();
    }

    /// <summary>
    /// Called from the reader thread (via UI-thread Post) when new output
    /// arrives. Schedules a debounced recompute — streaming output won't
    /// thrash the matcher but a single batch of edits settles within
    /// ~50&#160;ms.
    /// </summary>
    private void ScheduleSearchRecompute()
    {
        if (!this.searchOverlayOpen)
        {
            return;
        }

        if (!this.searchRecomputeTimer.IsEnabled)
        {
            this.searchRecomputeTimer.Start();
        }
    }

    private void RecomputeSearchMatchesNow()
    {
        string query = this.searchOverlay.Query;
        var options = this.searchOverlay.CurrentOptions;

        var snapshot = this.buffer.CreateSnapshot();
        this.searchSnapshotScrollbackCount = snapshot.IsUsingAltBuffer ? 0 : snapshot.ScrollbackCount;
        this.searchSnapshotRows = snapshot.Rows;
        this.searchUsingAltBufferSnapshot = snapshot.IsUsingAltBuffer;

        // Alt-buffer mode: search the live alt screen only, never
        // scrollback (per spec). Also force viewport to the bottom so
        // the alt grid is always on screen.
        if (snapshot.IsUsingAltBuffer && this.viewportOffset != 0)
        {
            this.viewportOffset = 0;
        }

        // Stash the currently-active match's (row, startCol) so we can
        // attempt to keep it selected after the recompute.
        SearchMatch? stickyAnchor = (this.activeMatchIndex >= 0 && this.activeMatchIndex < this.searchMatches.Count)
            ? this.searchMatches[this.activeMatchIndex]
            : null;

        var corpus = new List<Cell[]>();
        if (!snapshot.IsUsingAltBuffer)
        {
            for (int i = 0; i < snapshot.ScrollbackRows.Length; i++)
            {
                corpus.Add(snapshot.ScrollbackRows[i]);
            }
        }

        var live = snapshot.LiveScreen.Cells;
        int liveRows = live.GetLength(0);
        int liveCols = live.GetLength(1);
        for (int r = 0; r < liveRows; r++)
        {
            var rowArr = new Cell[liveCols];
            for (int c = 0; c < liveCols; c++)
            {
                rowArr[c] = live[r, c];
            }

            corpus.Add(rowArr);
        }

        this.searchMatches = ScrollbackSearch.FindMatches(corpus, snapshot.Cols, query, options);

        if (this.searchMatches.Count == 0)
        {
            this.activeMatchIndex = -1;
        }
        else if (stickyAnchor is SearchMatch anchor)
        {
            // Re-select the sticky match if it's still in the list.
            int newIdx = -1;
            for (int i = 0; i < this.searchMatches.Count; i++)
            {
                if (this.searchMatches[i].AbsoluteRow == anchor.AbsoluteRow
                    && this.searchMatches[i].StartCol == anchor.StartCol)
                {
                    newIdx = i;
                    break;
                }
            }

            this.activeMatchIndex = newIdx >= 0 ? newIdx : 0;
            if (newIdx < 0)
            {
                this.ScrollToMatch(this.searchMatches[0]);
            }
        }
        else
        {
            this.activeMatchIndex = 0;
            this.ScrollToMatch(this.searchMatches[0]);
        }

        int display = this.activeMatchIndex < 0 ? 0 : this.activeMatchIndex + 1;
        this.searchOverlay.SetStatus(display, this.searchMatches.Count);
    }

    /// <summary>
    /// Scrolls the viewport by the minimum amount needed to make
    /// <paramref name="match"/>'s row visible (matches top of viewport if
    /// above, bottom if below, no-op if already visible). Clamped to
    /// <c>[0, ScrollbackCount]</c>.
    /// </summary>
    private void ScrollToMatch(SearchMatch match)
    {
        if (this.searchUsingAltBufferSnapshot)
        {
            this.viewportOffset = 0;
            return;
        }

        int s = this.searchSnapshotScrollbackCount;
        int r = this.searchSnapshotRows;
        if (r <= 0)
        {
            return;
        }

        int m = match.AbsoluteRow;
        int topAbs = s - this.viewportOffset;
        int bottomAbs = topAbs + r - 1;

        int newOffset = this.viewportOffset;
        if (m < topAbs)
        {
            newOffset = s - m;
        }
        else if (m > bottomAbs)
        {
            newOffset = Math.Max(0, s - m + r - 1);
        }

        int clamped = Math.Clamp(newOffset, 0, this.buffer.ScrollbackCount);
        if (clamped != this.viewportOffset)
        {
            this.viewportOffset = clamped;
        }
    }

    private void RenderWithSkia(SKCanvas canvas)
    {
        if (this.isDisposed)
        {
            return;
        }

        var screen = this.buffer.GetScreen();
        if (screen is null)
        {
            return;
        }

        this.ptyBridge.ResetRedrawLatch();

        // Update IME cursor position.
        var cursorPos = screen.CursorPosition;
        var tp = this.textParam;
        float currentTopInset = this.topInset;
        Dispatcher.UIThread.Post(() =>
        {
            if (!this.isDisposed)
            {
                float x = cursorPos.Col * tp.CharWidth;
                float y = (cursorPos.Row * tp.LineHeight) + currentTopInset;
                this.imeClient.UpdateCursorRectangle(new Rect(x, y, tp.CharWidth, tp.LineHeight));
            }
        });

        // Fire background color change event when detected bg changes.
        int screenBg = screen.BackgroundColor;
        if (screenBg != this.lastReportedBg)
        {
            this.lastReportedBg = screenBg;
            Dispatcher.UIThread.Post(() => this.BackgroundColorChanged?.Invoke(screenBg));
        }

        bool drawCursor = this.cursorState.ShouldDrawCursor(this.currentModeInfo);
        TerminalSelection? selectionForRender = this.selection;
        HyperlinkRun? hyperlinkForRender = this.pointerHandler.CurrentHyperlinkRun;
        Pty.Screen renderScreen = screen;

        // Project absolute-row selection coordinates into screen rows.
        // selectionRowOffset is the absolute-row index that maps to screen
        // row 0; equivalently, ScrollbackCount - viewportOffset.
        int scrollbackCountForRender = this.buffer.ScrollbackCount;
        int selectionRowOffset = scrollbackCountForRender - this.viewportOffset;

        if (this.viewportOffset > 0)
        {
            renderScreen = TerminalVisualHost.ComposeScrollbackScreen(screen, this.buffer, this.viewportOffset);

            // Hide the cursor and any hyperlink overlay while viewing history.
            // Selection now operates in absolute-row coords so it remains
            // valid (and visible) across the scrollback boundary.
            drawCursor = false;
            hyperlinkForRender = null;
        }

        IReadOnlyList<VisibleMatch>? visibleMatches = this.searchOverlayOpen
            ? TerminalVisualHost.ProjectVisibleMatches(
                this.searchMatches,
                this.activeMatchIndex,
                renderScreen,
                this.searchSnapshotScrollbackCount,
                this.viewportOffset,
                this.searchUsingAltBufferSnapshot)
            : null;

        this.renderer.Render(
            canvas,
            renderScreen,
            this.textParam,
            this.currentModeInfo,
            this.EnableLigature,
            this.BackgroundAlpha,
            drawCursor,
            this.topInset,
            selectionForRender,
            this.selectionColor,
            selectionRowOffset,
            hyperlinkForRender,
            visibleMatches);

        // Render the title-bar inset overlay. When scrolled, show a blurred
        // preview of scrollback rows; otherwise just a tinted background so
        // the floating title bar is visually distinct.
        if (this.topInset > 0)
        {
            this.RenderBlurredInset(canvas, screen);
        }
    }

    private void RebuildFontChain(List<string> fontNames)
    {
        this.ligatureTextShaper.ClearCache();
        this.renderer.DiscardBackbuffer();
        this.fontChain.Rebuild(fontNames);
    }

    /// <summary>
    /// Renders the floating title-bar overlay in the top inset area.
    /// When the viewport is scrolled, draws a blurred preview of
    /// scrollback rows; otherwise renders a tinted backdrop.
    /// </summary>
    private void RenderBlurredInset(SKCanvas canvas, Pty.Screen screen)
    {
        float insetHeight = this.topInset;
        float canvasWidth = (float)this.Bounds.Width;
        var insetRect = new SKRect(0, 0, canvasWidth, insetHeight);

        // Show blurred ghost rows whenever scrollback has content —
        // not only when the user has manually scrolled back.
        int cols = screen.Cells.GetLength(1);
        int insetRowCount = (int)Math.Ceiling(insetHeight / this.textParam.LineHeight);
        int scrollbackCount = this.buffer.ScrollbackCount;
        int viewportTop = scrollbackCount - this.viewportOffset;
        int ghostStart = Math.Max(0, viewportTop - insetRowCount);
        int actualGhostRows = viewportTop - ghostStart;

        if (actualGhostRows > 0)
        {
            // Draw only ghost-row text into a blurred layer. The inset
            // background is already the same as the main terminal canvas, so
            // painting or blurring cell backgrounds would create a visible
            // titlebar-only shadow/tint.
            using var blurFilter = SKImageFilter.CreateBlur(TitleBarInsetBlurSigma, TitleBarInsetBlurSigma);
            using var layerPaint = new SKPaint { ImageFilter = blurFilter };

            canvas.Save();
            canvas.ClipRect(insetRect);
            canvas.SaveLayer(layerPaint);

            using var fgPaint = new SKPaint { IsAntialias = true };
            using var ghostFont = new SKFont { Size = this.textParam.SkiaFontSize, Subpixel = true };
            if (this.fontChain.PrimaryTypeface is { } primaryTf)
            {
                ghostFont.Typeface = primaryTf;
            }

            // Align the bottom ghost row with the top of the main grid.
            float baseY = insetHeight - (actualGhostRows * this.textParam.LineHeight);

            for (int gi = 0; gi < actualGhostRows; gi++)
            {
                int sbIndex = ghostStart + gi;
                if (sbIndex < 0 || sbIndex >= scrollbackCount)
                {
                    continue;
                }

                var sbRow = this.buffer.GetScrollbackLine(sbIndex);
                float rowY = baseY + (gi * this.textParam.LineHeight);
                int copyCols = Math.Min(cols, sbRow.Length);

                for (int j = 0; j < copyCols; j++)
                {
                    ref readonly var cell = ref sbRow[j];

                    float x = j * this.textParam.CharWidth;
                    string? ch = cell.Character;
                    if (!string.IsNullOrEmpty(ch) && ch != " ")
                    {
                        fgPaint.Color = TerminalRenderer.GetSkColor(cell.ForegroundColor);
                        float baselineY = rowY + (this.textParam.LineHeight * 0.8f);
                        canvas.DrawText(ch, x, baselineY, ghostFont, fgPaint);
                    }
                }
            }

            canvas.Restore(); // SaveLayer — blur is applied.
            canvas.Restore(); // ClipRect.
        }
    }

    private void OnTitleChanged(string title)
    {
        Dispatcher.UIThread.Post(() => this.TitleChanged?.Invoke(title));
    }

    private int ReaderProcessAndReportScrollbackDelta(ReadOnlySpan<byte> bytes)
    {
        int before = this.buffer.ScrollbackCount;
        long evictedBefore = this.buffer.ScrollbackEvictedTotal;
        this.parser.Process(bytes);
        int after = this.buffer.ScrollbackCount;
        this.lastEvictedDelta = (int)Math.Min(int.MaxValue, this.buffer.ScrollbackEvictedTotal - evictedBefore);
        return after - before;
    }

    private void OnPromptMarkRaised(object? sender, PromptMarkEventArgs e)
    {
        // Capture the mark at the live cursor position. Using
        // ScrollbackCount+CursorRow as the stable "absolute row" stays
        // valid as long as the scrollback ring hasn't saturated; once it
        // saturates, stale marks can drift — pruned opportunistically via
        // PromptMarksRegistry.PruneBelow when we detect eviction.
        int absRow = this.buffer.ScrollbackCount + this.buffer.CursorRow;
        int col = this.buffer.CursorCol;
        var mark = new PromptMark(e.Kind, absRow, col, e.ExitCode, e.CurrentDirectory);
        this.promptMarks.Add(mark);
    }

    private bool JumpToMark(bool previous)
    {
        int scrollbackCount = this.buffer.ScrollbackCount;
        int topAbs = scrollbackCount - this.viewportOffset;
        PromptMark? target = previous
            ? this.promptMarks.FindPrevious(topAbs)
            : this.promptMarks.FindNext(topAbs);

        if (target is null)
        {
            return false;
        }

        int newOffset;
        if (target.AbsoluteRow >= scrollbackCount)
        {
            // Mark still on the live grid: anchor viewport to live.
            newOffset = 0;
        }
        else
        {
            newOffset = scrollbackCount - target.AbsoluteRow;
        }

        int clamped = Math.Clamp(newOffset, 0, scrollbackCount);
        if (clamped == this.viewportOffset)
        {
            return false;
        }

        this.viewportOffset = clamped;
        Dispatcher.UIThread.Post(this.InvalidateVisual);
        return true;
    }

    private void ReaderOnAfterParse(int scrollbackDelta)
    {
        // Anchor the viewport in history while the user is scrolled up:
        // new lines captured into scrollback shift the visible window
        // away from the most recent output by the same amount. When the
        // ring was already full the delta is zero even though older lines
        // were evicted — on-screen content still advances then, but the
        // old code accepted this loss so we preserve the behavior.
        if (this.viewportOffset > 0 && scrollbackDelta > 0)
        {
            int newOffset = Math.Min(this.viewportOffset + scrollbackDelta, this.buffer.ScrollbackLimit);
            newOffset = Math.Min(newOffset, this.buffer.ScrollbackCount);
            this.viewportOffset = newOffset;
        }

        // Selection is anchored in absolute-row coords. New PTY output that
        // simply pushes lines into scrollback does not invalidate it; only
        // ring eviction (lines falling off the oldest end) does. Shift
        // selection rows down by the eviction count and clamp to the new
        // valid range. This runs on the reader thread but TerminalSelection
        // mutators are not thread-safe, so we marshal to the UI thread.
        int evictedDelta = this.lastEvictedDelta;
        this.lastEvictedDelta = 0;
        if (this.selection.Mode != TerminalSelectionMode.None && evictedDelta > 0)
        {
            int evictedSnapshot = evictedDelta;
            Dispatcher.UIThread.Post(() =>
            {
                if (this.selection.Mode == TerminalSelectionMode.None)
                {
                    return;
                }

                this.selection.Shift(-evictedSnapshot);
                int maxRow = this.buffer.ScrollbackCount + (int)this.DesiredRowCount - 1;
                this.selection.ClampRows(0, maxRow);
                this.InvalidateVisual();
            });
        }

        // Sync terminal state to input handler.
        this.inputHandler.ApplicationCursorKeys = this.buffer.ApplicationCursorKeys;
        this.inputHandler.BracketedPasteEnabled = this.buffer.BracketedPasteEnabled;
        this.inputHandler.MouseTrackingMode = this.buffer.MouseTrackingMode;
    }

    private void ReaderOnRedrawTick()
    {
        this.UpdateModeInfoFromBuffer();
        this.ApplyTerminalUiState();
        this.ScheduleSearchRecompute();
        this.InvalidateVisual();
    }

    private void DisposeCachedResources()
    {
        this.Cursor = null;
        this.pointerHandler.DisposeResources();
        this.cursorState.Dispose();
        this.renderer.Dispose();
        this.ligatureTextShaper.Dispose();
        this.fontChain.Dispose();
    }

    private sealed class ReaderHost : IPtyReaderHost
    {
        private readonly TerminalControl owner;

        public ReaderHost(TerminalControl owner)
        {
            this.owner = owner;
        }

        public bool SynchronizedOutput => this.owner.buffer.SynchronizedOutput;

        public int ProcessAndReportScrollbackDelta(ReadOnlySpan<byte> bytes)
            => this.owner.ReaderProcessAndReportScrollbackDelta(bytes);

        public void OnAfterParse(int scrollbackDelta) => this.owner.ReaderOnAfterParse(scrollbackDelta);

        public void OnRedrawTick() => this.owner.ReaderOnRedrawTick();
    }
}
