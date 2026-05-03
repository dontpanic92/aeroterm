// <copyright file="NativeMessageBox.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Controls;

using System.Threading.Tasks;
using AeroTerm.Theme.NativeMessageBoxes;
using Avalonia.Controls;

/// <summary>
/// Displays small message boxes using native AppKit alerts on macOS and an
/// AeroTerm-themed Avalonia fallback on Windows and Linux.
/// </summary>
public static class NativeMessageBox
{
    /// <summary>
    /// Displays an OK-only message box.
    /// </summary>
    /// <param name="owner">The owning window.</param>
    /// <param name="title">The message-box title.</param>
    /// <param name="message">The message body.</param>
    /// <param name="okText">Optional localized OK button text.</param>
    /// <returns>The selected result.</returns>
    public static Task<NativeMessageBoxResult> ShowOkAsync(
        Window owner,
        string title,
        string message,
        string? okText = null)
    {
        var options = NativeMessageBoxOptions.CreateOk(title, message, okText);
        return NativeMessageBoxPlatformAdapter.Current.ShowAsync(owner, options);
    }

    /// <summary>
    /// Displays a Yes/No message box.
    /// </summary>
    /// <param name="owner">The owning window.</param>
    /// <param name="title">The message-box title.</param>
    /// <param name="message">The message body.</param>
    /// <param name="yesText">Optional localized Yes button text.</param>
    /// <param name="noText">Optional localized No button text.</param>
    /// <returns>The selected result.</returns>
    public static Task<NativeMessageBoxResult> ShowYesNoAsync(
        Window owner,
        string title,
        string message,
        string? yesText = null,
        string? noText = null)
    {
        var options = NativeMessageBoxOptions.CreateYesNo(title, message, yesText, noText);
        return NativeMessageBoxPlatformAdapter.Current.ShowAsync(owner, options);
    }
}
