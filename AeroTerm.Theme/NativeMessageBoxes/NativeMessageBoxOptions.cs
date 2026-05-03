// <copyright file="NativeMessageBoxOptions.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.NativeMessageBoxes;

using System;
using AeroTerm.Theme.Controls;

/// <summary>
/// Immutable configuration for a native message box.
/// </summary>
internal sealed class NativeMessageBoxOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeMessageBoxOptions"/> class.
    /// </summary>
    /// <param name="buttons">The button layout.</param>
    /// <param name="title">The message-box title.</param>
    /// <param name="message">The message body.</param>
    /// <param name="primaryButtonText">The primary button text.</param>
    /// <param name="secondaryButtonText">The optional secondary button text.</param>
    /// <param name="cancelResult">The result used for cancellation/window close.</param>
    private NativeMessageBoxOptions(
        NativeMessageBoxButtons buttons,
        string title,
        string message,
        string primaryButtonText,
        string? secondaryButtonText,
        NativeMessageBoxResult cancelResult)
    {
        this.Buttons = buttons;
        this.Title = ValidateText(title, nameof(title));
        this.Message = ValidateText(message, nameof(message));
        this.PrimaryButtonText = ValidateText(primaryButtonText, nameof(primaryButtonText));
        this.SecondaryButtonText = secondaryButtonText is null
            ? null
            : ValidateText(secondaryButtonText, nameof(secondaryButtonText));
        this.CancelResult = cancelResult;
    }

    /// <summary>
    /// Gets the button layout.
    /// </summary>
    internal NativeMessageBoxButtons Buttons { get; }

    /// <summary>
    /// Gets the message-box title.
    /// </summary>
    internal string Title { get; }

    /// <summary>
    /// Gets the message body.
    /// </summary>
    internal string Message { get; }

    /// <summary>
    /// Gets the primary button text.
    /// </summary>
    internal string PrimaryButtonText { get; }

    /// <summary>
    /// Gets the secondary button text.
    /// </summary>
    internal string? SecondaryButtonText { get; }

    /// <summary>
    /// Gets the result used for cancellation/window close.
    /// </summary>
    internal NativeMessageBoxResult CancelResult { get; }

    /// <summary>
    /// Creates OK-only message-box options.
    /// </summary>
    /// <param name="title">The message-box title.</param>
    /// <param name="message">The message body.</param>
    /// <param name="okText">Optional OK button text.</param>
    /// <returns>The configured options.</returns>
    internal static NativeMessageBoxOptions CreateOk(string title, string message, string? okText)
    {
        return new NativeMessageBoxOptions(
            NativeMessageBoxButtons.Ok,
            title,
            message,
            string.IsNullOrWhiteSpace(okText) ? "OK" : okText,
            secondaryButtonText: null,
            NativeMessageBoxResult.Ok);
    }

    /// <summary>
    /// Creates Yes/No message-box options.
    /// </summary>
    /// <param name="title">The message-box title.</param>
    /// <param name="message">The message body.</param>
    /// <param name="yesText">Optional Yes button text.</param>
    /// <param name="noText">Optional No button text.</param>
    /// <returns>The configured options.</returns>
    internal static NativeMessageBoxOptions CreateYesNo(
        string title,
        string message,
        string? yesText,
        string? noText)
    {
        return new NativeMessageBoxOptions(
            NativeMessageBoxButtons.YesNo,
            title,
            message,
            string.IsNullOrWhiteSpace(yesText) ? "Yes" : yesText,
            string.IsNullOrWhiteSpace(noText) ? "No" : noText,
            NativeMessageBoxResult.No);
    }

    private static string ValidateText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
