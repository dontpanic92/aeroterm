// <copyright file="TerminalClipboardBridge.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls.Terminal;

using System.Threading.Tasks;
using AeroTerm.Controls;
using AeroTerm.Diagnostics;
using AeroTerm.Pty;
using AeroTerm.Services;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

/// <summary>
/// Centralises every clipboard / PRIMARY-selection interaction the
/// <see cref="TerminalControl"/> needs: Ctrl/Cmd+C copy of the current
/// selection, Ctrl/Cmd+V paste, middle-click PRIMARY paste, and the
/// synchronous OSC 52 read/write callbacks handed to the VT parser.
/// </summary>
internal sealed class TerminalClipboardBridge
{
    private readonly Func<TopLevel?> getTopLevel;
    private readonly Action<string> handlePaste;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalClipboardBridge"/> class.
    /// </summary>
    /// <param name="getTopLevel">Callback that returns the owning control's
    /// <see cref="TopLevel"/>, or <see langword="null"/> when the control is not
    /// yet attached. Used to locate the Avalonia clipboard.</param>
    /// <param name="handlePaste">Callback that forwards pasted text through
    /// the terminal input handler (applying bracketed-paste framing and
    /// PTY encoding).</param>
    public TerminalClipboardBridge(Func<TopLevel?> getTopLevel, Action<string> handlePaste)
    {
        this.getTopLevel = getTopLevel ?? throw new ArgumentNullException(nameof(getTopLevel));
        this.handlePaste = handlePaste ?? throw new ArgumentNullException(nameof(handlePaste));
    }

    /// <summary>
    /// Gets or sets a value indicating whether middle-button clicks should
    /// paste (PRIMARY on Linux/X11 with clipboard fallback; regular clipboard
    /// on macOS/Windows).
    /// </summary>
    public bool MiddleClickPastes { get; set; } = true;

    /// <summary>
    /// Gets or sets the PRIMARY-selection backend used for middle-click
    /// paste and selection publication. Tests substitute a fake; production
    /// uses <see cref="DefaultPrimarySelectionService.Instance"/>.
    /// </summary>
    public IPrimarySelectionService PrimarySelectionService { get; set; }
        = DefaultPrimarySelectionService.Instance;

    /// <summary>
    /// Copies the current selection (if any, non-empty) to the OS clipboard
    /// asynchronously on the UI thread.
    /// </summary>
    /// <param name="selection">The active terminal selection.</param>
    /// <param name="buffer">The terminal buffer whose screen cells back the selection.</param>
    public void CopySelectionToClipboard(TerminalSelection selection, TerminalBuffer buffer)
    {
        var screen = buffer.GetScreen();
        if (screen is null || selection.IsEmpty)
        {
            return;
        }

        string text = selection.CopyText(new BufferRowSource(buffer, screen));
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            var clipboard = this.getTopLevel()?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(text);
            }
        });
    }

    /// <summary>
    /// Publishes the current selection to the X11 PRIMARY selection when
    /// available. Fire-and-forget; failures are logged but never surfaced.
    /// No-op on non-X11 platforms.
    /// </summary>
    /// <param name="selection">The active terminal selection.</param>
    /// <param name="buffer">The terminal buffer whose screen cells back the selection.</param>
    public void PublishSelectionToPrimary(TerminalSelection selection, TerminalBuffer buffer)
    {
        var screen = buffer.GetScreen();
        if (screen is null || selection.IsEmpty)
        {
            return;
        }

        string text = selection.CopyText(new BufferRowSource(buffer, screen));
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var service = this.PrimarySelectionService;

        _ = Task.Run(async () =>
        {
            try
            {
                await MiddleClickPaster.TryWritePrimaryAsync(service, text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.For<TerminalClipboardBridge>().LogDebug(ex, "PRIMARY selection write failed.");
            }
        });
    }

    /// <summary>
    /// Reads text from the OS clipboard on the UI thread and feeds it
    /// through the terminal's paste path. No-op if the clipboard is
    /// unavailable or empty.
    /// </summary>
    public void PasteFromClipboard()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var clipboard = this.getTopLevel()?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            try
            {
                string? text = await clipboard.TryGetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    this.handlePaste(text);
                }
            }
            catch (InvalidOperationException)
            {
                // Clipboard unavailable on some platforms in some hosting modes.
            }
        });
    }

    /// <summary>
    /// Middle-click paste: prefers PRIMARY when available (Linux/X11) and
    /// falls back to the regular clipboard. No-op when
    /// <see cref="MiddleClickPastes"/> is disabled.
    /// </summary>
    public void PasteMiddleClick()
    {
        var service = this.PrimarySelectionService;
        var topLevel = this.getTopLevel();
        var clipboard = topLevel?.Clipboard;
        bool enabled = this.MiddleClickPastes;

        Func<Task<string?>> clipboardFallback = async () =>
        {
            if (clipboard is null)
            {
                return null;
            }

            try
            {
                return await clipboard.TryGetTextAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        };

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await MiddleClickPaster.TryPasteAsync(
                    enabled,
                    service,
                    clipboardFallback,
                    text => this.handlePaste(text)).ConfigureAwait(true);
            }
            catch (InvalidOperationException)
            {
                // Clipboard/PRIMARY transiently unavailable — ignore.
            }
        });
    }

    /// <summary>
    /// Synchronous clipboard read for the OSC 52 parser callback. Blocks
    /// the calling thread (typically the PTY reader) briefly while the
    /// UI thread fetches the value. Returns an empty string on failure.
    /// </summary>
    /// <returns>The clipboard text, or empty string.</returns>
    public string ReadClipboardForParser()
    {
        string? result;
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
                    var clipboard = this.getTopLevel()?.Clipboard;
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

    /// <summary>
    /// Asynchronous clipboard write for the OSC 52 parser callback.
    /// Posted to the UI thread; failures are swallowed.
    /// </summary>
    /// <param name="text">The text to copy to the clipboard.</param>
    public void WriteClipboardFromParser(string text)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var clipboard = this.getTopLevel()?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(text);
            }
        });
    }

    private string? ReadClipboardSync()
    {
        var clipboard = this.getTopLevel()?.Clipboard;
        if (clipboard is null)
        {
            return null;
        }

        return clipboard.TryGetTextAsync().GetAwaiter().GetResult();
    }
}
