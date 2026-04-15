// <copyright file="TerminalControl.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using System.Text;
using AeroTerm.Pty;
using AeroTerm.Utilities;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

/// <summary>
/// Custom Avalonia control that hosts a terminal emulator with PTY backend,
/// VT parsing, and SkiaSharp rendering.
/// </summary>
public class TerminalControl : Control, IDisposable
{
    private readonly TerminalBuffer buffer;
    private readonly VtParser parser;
    private readonly TerminalInputHandler inputHandler;
    private readonly EditorTextInputMethodClient imeClient;
    private readonly FontFallbackChain fontChain = new FontFallbackChain();
    private readonly LigatureTextShaper ligatureTextShaper = new();
    private readonly TerminalRenderer renderer;
    private readonly CursorStateManager cursorState;
    private readonly TerminalDrawOperation drawOperation;

    private TextLayoutParameters textParam;
    private IPtyConnection? ptyConnection;
    private Thread? readerThread;
    private ModeInfo currentModeInfo;
    private int lastReportedBg = -1;
    private volatile bool isDisposed;
    private int redrawQueued;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalControl"/> class.
    /// </summary>
    /// <param name="cols">Initial column count.</param>
    /// <param name="rows">Initial row count.</param>
    public TerminalControl(int cols = 80, int rows = 24)
    {
        this.buffer = new TerminalBuffer(cols, rows);
        this.parser = new VtParser(
            this.buffer,
            this.OnTitleChanged,
            this.OnWriteBack,
            this.OnClipboardRead,
            this.OnClipboardWrite);
        this.inputHandler = new TerminalInputHandler(this.WriteToPty);

        this.ClipToBounds = true;
        this.Focusable = true;

        this.imeClient = new EditorTextInputMethodClient(this);
        this.renderer = new TerminalRenderer(this.fontChain, this.ligatureTextShaper, this.imeClient);
        this.currentModeInfo = new ModeInfo(CursorShape.Block, 100, CursorBlinking.BlinkOn);
        this.cursorState = new CursorStateManager(this.InvalidateVisual, () => this.currentModeInfo);
        this.drawOperation = new TerminalDrawOperation(this, default);

        this.AddHandler(
            InputElement.TextInputMethodClientRequestedEvent,
            (_, e) =>
            {
                e.Client = this.imeClient;
            });

        this.RebuildFontChain(FontPriorityList.GetDefaultPlatformFonts().ToList());
        this.textParam = new TextLayoutParameters(this.fontChain.PrimaryFontName, 11);
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
            var c = (uint)(this.Bounds.Height / this.textParam.LineHeight);
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
    /// Gets the currently resolved pointer cursor type for tests.
    /// </summary>
    internal StandardCursorType? ResolvedPointerCursorType => this.cursorState.ResolvedPointerCursorType;

    /// <summary>
    /// Starts a child process connected via a PTY.
    /// </summary>
    /// <param name="app">Absolute path to the executable.</param>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="env">Environment variables for the child process.</param>
    /// <param name="cwd">Working directory for the child process.</param>
    public void StartProcess(string app, string[] args, IDictionary<string, string> env, string cwd)
    {
        if (this.ptyConnection is not null)
        {
            throw new InvalidOperationException("A process is already running.");
        }

        int cols = (int)this.DesiredColCount;
        int rows = (int)this.DesiredRowCount;
        this.ptyConnection = PtyConnectionFactory.Create(app, args, env, cwd, rows, cols);
        this.ptyConnection.ProcessExited += this.OnProcessExited;

        this.readerThread = new Thread(this.ReaderThreadProc)
        {
            Name = "TerminalControl.Reader",
            IsBackground = true,
        };
        this.readerThread.Start();
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

        // Nudge the PTY with a same-size resize to trigger SIGWINCH,
        // which causes the shell/application to redraw with new colors.
        // Palette-indexed colors in existing cells are baked as RGB at
        // write time, so only a full shell repaint can fix them.
        if (this.ptyConnection is not null)
        {
            int cols = (int)this.DesiredColCount;
            int rows = (int)this.DesiredRowCount;
            this.ptyConnection.Resize(cols, rows);
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

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        this.drawOperation.Bounds = new Rect(0, 0, this.Bounds.Width, this.Bounds.Height);
        context.Custom(this.drawOperation);
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
            this.inputHandler.HandleTextInput(e.Text);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (this.inputHandler.HandleKeyDown(e))
        {
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        this.Focus();
        var (row, col) = this.PixelToGridPosition(e.GetCurrentPoint(this).Position);
        e.Handled = this.inputHandler.HandlePointerPressed(e, row + 1, col + 1);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var (row, col) = this.PixelToGridPosition(e.GetCurrentPoint(this).Position);
        e.Handled = this.inputHandler.HandlePointerMoved(e, row + 1, col + 1);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var (row, col) = this.PixelToGridPosition(e.GetCurrentPoint(this).Position);
        e.Handled = this.inputHandler.HandlePointerReleased(e, row + 1, col + 1);
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var (row, col) = this.PixelToGridPosition(e.GetCurrentPoint(this).Position);
        e.Handled = this.inputHandler.HandlePointerWheel(e, row + 1, col + 1);
    }

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (this.buffer.FocusEventsEnabled)
        {
            this.WriteToPty(Encoding.ASCII.GetBytes("\x1B[I"));
        }

        this.cursorState.UpdateCursorBlink(this.currentModeInfo, resetCursorBlink: true);
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (this.buffer.FocusEventsEnabled)
        {
            this.WriteToPty(Encoding.ASCII.GetBytes("\x1B[O"));
        }

        this.inputHandler.ClearPressedButton();
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
                if (this.ptyConnection is not null)
                {
                    this.ptyConnection.ProcessExited -= this.OnProcessExited;
                    this.ptyConnection.Kill();
                    this.ptyConnection.Dispose();
                    this.ptyConnection = null;
                }

                this.readerThread = null;

                Dispatcher.UIThread.Post(this.DisposeCachedResources, DispatcherPriority.Background);
            }
        }
    }

    private (int Row, int Col) PixelToGridPosition(Point pixel)
    {
        int row = (int)(pixel.Y / this.textParam.LineHeight);
        int col = (int)(pixel.X / this.textParam.CharWidth);

        int maxRow = (int)this.DesiredRowCount - 1;
        int maxCol = (int)this.DesiredColCount - 1;

        row = Math.Clamp(row, 0, maxRow);
        col = Math.Clamp(col, 0, maxCol);

        return (row, col);
    }

    private void ReaderThreadProc()
    {
        byte[] buf = new byte[4096];
        try
        {
            while (!this.isDisposed && this.ptyConnection is not null)
            {
                int read = this.ptyConnection.ReaderStream.Read(buf, 0, buf.Length);
                if (read <= 0)
                {
                    break;
                }

                this.parser.Process(buf.AsSpan(0, read));

                // Sync terminal state to input handler.
                this.inputHandler.ApplicationCursorKeys = this.buffer.ApplicationCursorKeys;
                this.inputHandler.BracketedPasteEnabled = this.buffer.BracketedPasteEnabled;
                this.inputHandler.MouseTrackingMode = this.buffer.MouseTrackingMode;

                // Queue a redraw on the UI thread, coalescing rapid updates.
                if (Interlocked.Exchange(ref this.redrawQueued, 1) == 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!this.isDisposed)
                        {
                            this.UpdateModeInfoFromBuffer();
                            this.ApplyTerminalUiState();
                            this.InvalidateVisual();
                        }

                        Interlocked.Exchange(ref this.redrawQueued, 0);
                    });
                }
            }
        }
        catch (IOException)
        {
            // PTY stream closed.
        }
        catch (ObjectDisposedException)
        {
            // PTY disposed during shutdown.
        }
    }

    private void WriteToPty(byte[] data)
    {
        if (this.ptyConnection is not null && !this.isDisposed)
        {
            try
            {
                this.ptyConnection.WriterStream.Write(data, 0, data.Length);
                this.ptyConnection.WriterStream.Flush();
            }
            catch (IOException)
            {
                // PTY stream closed.
            }
            catch (ObjectDisposedException)
            {
                // PTY disposed.
            }
        }
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
        this.ptyConnection?.Resize(cols, rows);
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

        Interlocked.Exchange(ref this.redrawQueued, 0);

        // Update IME cursor position.
        var cursorPos = screen.CursorPosition;
        var tp = this.textParam;
        Dispatcher.UIThread.Post(() =>
        {
            if (!this.isDisposed)
            {
                float x = cursorPos.Col * tp.CharWidth;
                float y = cursorPos.Row * tp.LineHeight;
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

        this.renderer.Render(
            canvas,
            screen,
            this.textParam,
            this.currentModeInfo,
            this.EnableLigature,
            this.BackgroundAlpha,
            drawCursor);
    }

    private void RebuildFontChain(List<string> fontNames)
    {
        this.ligatureTextShaper.ClearCache();
        this.renderer.DiscardBackbuffer();
        this.fontChain.Rebuild(fontNames);
    }

    private void OnTitleChanged(string title)
    {
        Dispatcher.UIThread.Post(() => this.TitleChanged?.Invoke(title));
    }

    private void OnWriteBack(byte[] data)
    {
        this.WriteToPty(data);
    }

    private string OnClipboardRead()
    {
        // Clipboard access must happen on the UI thread. We block the
        // parser thread briefly to fetch the value synchronously.
        string? result = null;
        if (Dispatcher.UIThread.CheckAccess())
        {
            result = this.ReadClipboardSync();
        }
        else
        {
            var tcs = new TaskCompletionSource<string?>();
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    var text = clipboard is not null ? await clipboard.TryGetTextAsync() : null;
                    tcs.SetResult(text);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            try
            {
                result = tcs.Task.GetAwaiter().GetResult();
            }
            catch
            {
                result = string.Empty;
            }
        }

        return result ?? string.Empty;
    }

    private string? ReadClipboardSync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return null;
        }

        return clipboard.TryGetTextAsync().GetAwaiter().GetResult();
    }

    private void OnClipboardWrite(string text)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(text);
            }
        });
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => this.ProcessExited?.Invoke());
    }

    private void DisposeCachedResources()
    {
        this.Cursor = null;
        this.cursorState.Dispose();
        this.renderer.Dispose();
        this.ligatureTextShaper.Dispose();
        this.fontChain.Dispose();
    }

    /// <summary>
    /// Custom draw operation for rendering the terminal grid with SkiaSharp.
    /// Reused across frames to avoid per-frame allocation.
    /// </summary>
    private sealed class TerminalDrawOperation : ICustomDrawOperation
    {
        private readonly TerminalControl control;

        public TerminalDrawOperation(TerminalControl control, Rect bounds)
        {
            this.control = control;
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
            this.control.RenderWithSkia(canvas);
        }
    }
}
