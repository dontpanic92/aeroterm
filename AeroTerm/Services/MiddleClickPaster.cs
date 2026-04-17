// <copyright file="MiddleClickPaster.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;
using System.Threading.Tasks;

/// <summary>
/// Shared middle-click paste dispatch logic used by <see cref="AeroTerm.Controls.TerminalControl"/>.
/// Factored out so it can be unit-tested without an Avalonia top-level.
/// </summary>
internal static class MiddleClickPaster
{
    /// <summary>
    /// Attempts to satisfy a middle-click paste gesture. When
    /// <paramref name="middleClickPastes"/> is <c>false</c> no work is done.
    /// Otherwise the PRIMARY selection is tried first; if PRIMARY is
    /// unavailable or empty, <paramref name="clipboardFallback"/> is used.
    /// </summary>
    /// <param name="middleClickPastes">
    /// User preference from <see cref="AppSettings.MiddleClickPastes"/>.
    /// </param>
    /// <param name="primary">The PRIMARY selection service.</param>
    /// <param name="clipboardFallback">
    /// Callback that reads the regular clipboard; invoked only when PRIMARY
    /// is empty or unavailable.
    /// </param>
    /// <param name="onText">
    /// Invoked with the resolved text when a paste should be dispatched.
    /// </param>
    /// <returns>
    /// <c>true</c> when <paramref name="onText"/> was invoked with
    /// non-empty text; <c>false</c> otherwise.
    /// </returns>
    public static async Task<bool> TryPasteAsync(
        bool middleClickPastes,
        IPrimarySelectionService primary,
        Func<Task<string?>> clipboardFallback,
        Action<string> onText)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(clipboardFallback);
        ArgumentNullException.ThrowIfNull(onText);

        if (!middleClickPastes)
        {
            return false;
        }

        string? text = await primary.ReadAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(text))
        {
            text = await clipboardFallback().ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        onText(text);
        return true;
    }

    /// <summary>
    /// Publishes <paramref name="text"/> to PRIMARY when a backend is
    /// available. Non-Linux / Wayland / missing-helper environments short
    /// circuit without touching PRIMARY.
    /// </summary>
    /// <param name="primary">The PRIMARY selection service.</param>
    /// <param name="text">Text to publish.</param>
    /// <returns>
    /// <c>true</c> when the write was dispatched to the backend; <c>false</c>
    /// when PRIMARY is unavailable or the text was null/empty.
    /// </returns>
    public static async Task<bool> TryWritePrimaryAsync(IPrimarySelectionService primary, string text)
    {
        ArgumentNullException.ThrowIfNull(primary);

        if (!primary.IsAvailable || string.IsNullOrEmpty(text))
        {
            return false;
        }

        await primary.WriteAsync(text).ConfigureAwait(false);
        return true;
    }
}
