// <copyright file="TerminalPtyBridge.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using AeroTerm.Pty;
using Avalonia.Threading;

/// <summary>
/// Owns the PTY lifecycle for a <see cref="TerminalControl"/>: connection
/// creation, reader thread, writer fan-out, and same-size resize
/// signaling. The reader body delegates per-chunk work to
/// <see cref="IPtyReaderHost"/> callbacks so all buffer/parser/selection
/// interactions stay on the control side.
/// </summary>
internal sealed class TerminalPtyBridge : IDisposable
{
    private readonly IPtyConnectionFactory ptyFactory;
    private readonly IPtyReaderHost host;

    private IPtyConnection? ptyConnection;
    private Thread? readerThread;
    private int lastPtyCols;
    private int lastPtyRows;
    private int redrawQueued;
    private volatile bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalPtyBridge"/> class.
    /// </summary>
    /// <param name="ptyFactory">Factory that produces the PTY connection.</param>
    /// <param name="host">Callback surface the reader thread invokes.</param>
    public TerminalPtyBridge(IPtyConnectionFactory ptyFactory, IPtyReaderHost host)
    {
        this.ptyFactory = ptyFactory ?? throw new ArgumentNullException(nameof(ptyFactory));
        this.host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// Occurs on the UI thread when the child process exits.
    /// </summary>
    public event Action? ProcessExited;

    /// <summary>
    /// Gets the OS-level process identifier of the running child shell,
    /// or <see langword="null"/> if no process has been started.
    /// </summary>
    public int? ChildPid => this.ptyConnection?.Pid;

    /// <summary>
    /// Gets a value indicating whether a PTY connection is currently active.
    /// </summary>
    public bool IsRunning => this.ptyConnection is not null;

    /// <summary>
    /// Starts a child process connected to a freshly-created PTY.
    /// </summary>
    /// <param name="app">Absolute path to the executable.</param>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="env">Environment variables for the child process.</param>
    /// <param name="cwd">Working directory for the child process.</param>
    /// <param name="cols">Initial column count.</param>
    /// <param name="rows">Initial row count.</param>
    public void StartProcess(string app, string[] args, IDictionary<string, string> env, string cwd, int cols, int rows)
    {
        if (this.ptyConnection is not null)
        {
            throw new InvalidOperationException("A process is already running.");
        }

        this.lastPtyCols = cols;
        this.lastPtyRows = rows;
        this.ptyConnection = this.ptyFactory.Create(app, args, env, cwd, rows, cols);
        this.ptyConnection.ProcessExited += this.OnProcessExited;

        this.readerThread = new Thread(this.ReaderThreadProc)
        {
            Name = "TerminalControl.Reader",
            IsBackground = true,
        };
        this.readerThread.Start();
    }

    /// <summary>
    /// Writes a byte buffer to the PTY. Swallows expected shutdown-time
    /// IO exceptions.
    /// </summary>
    /// <param name="data">The bytes to write.</param>
    public void WriteToPty(byte[] data)
    {
        if (this.ptyConnection is null || this.isDisposed)
        {
            return;
        }

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
        catch (UnauthorizedAccessException)
        {
            // PTY file descriptor closed during shutdown (macOS/Linux).
        }
    }

    /// <summary>
    /// Forwards a resize request to the PTY iff the size actually changed
    /// since the last report. Returns the cached last-reported dimensions.
    /// </summary>
    /// <param name="cols">Requested column count.</param>
    /// <param name="rows">Requested row count.</param>
    public void Resize(int cols, int rows)
    {
        if (cols == this.lastPtyCols && rows == this.lastPtyRows)
        {
            return;
        }

        this.lastPtyCols = cols;
        this.lastPtyRows = rows;
        this.ptyConnection?.Resize(cols, rows);
    }

    /// <summary>
    /// Forces a resize to the given size (used by
    /// <see cref="TerminalControl.ApplyColorScheme"/> to trigger SIGWINCH
    /// without changing dimensions).
    /// </summary>
    /// <param name="cols">Column count.</param>
    /// <param name="rows">Row count.</param>
    public void ForceResize(int cols, int rows)
    {
        this.ptyConnection?.Resize(cols, rows);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;

        if (this.ptyConnection is not null)
        {
            this.ptyConnection.ProcessExited -= this.OnProcessExited;
            this.ptyConnection.Kill();
            this.ptyConnection.Dispose();
            this.ptyConnection = null;
        }

        this.readerThread = null;
    }

    /// <summary>
    /// Clears the redraw-coalescing latch from the render path so the next
    /// reader-thread post enqueues a fresh UI-thread tick.
    /// </summary>
    public void ResetRedrawLatch()
    {
        Interlocked.Exchange(ref this.redrawQueued, 0);
    }

    /// <summary>
    /// Schedules a coalesced redraw on the UI thread. Safe to call from any
    /// thread; concurrent callers collapse onto a single posted tick via the
    /// same latch used by the reader loop.
    /// </summary>
    public void RequestRedraw()
    {
        if (this.isDisposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref this.redrawQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!this.isDisposed)
                {
                    this.host.OnRedrawTick();
                }

                Interlocked.Exchange(ref this.redrawQueued, 0);
            });
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => this.ProcessExited?.Invoke());
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

                int scrollbackDelta = this.host.ProcessAndReportScrollbackDelta(buf.AsSpan(0, read));
                this.host.OnAfterParse(scrollbackDelta);

                // DECSET 2026 — synchronized output. While the emitter has
                // the mode enabled we skip the redraw post entirely; the
                // SynchronizedUpdateCoordinator in the host handles the
                // eventual flush (either when the emitter sends CSI ? 2026 l
                // or via its 150 ms watchdog).
                if (this.host.SynchronizedOutput)
                {
                    continue;
                }

                this.RequestRedraw();
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
        catch (UnauthorizedAccessException)
        {
            // PTY file descriptor closed during shutdown (macOS/Linux).
        }
    }
}
