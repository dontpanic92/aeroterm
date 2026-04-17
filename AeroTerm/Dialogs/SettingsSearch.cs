// <copyright file="SettingsSearch.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Dialogs;

using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

/// <summary>
/// Attached properties driving the Settings dialog search filter.
/// Rows in a settings page advertise their searchable text via
/// <see cref="LabelProperty"/>. A container (typically each page root)
/// binds <see cref="QueryProperty"/> to the current search query; when
/// the query changes, every descendant carrying a label whose text does
/// not contain the query is hidden.
/// </summary>
public static class SettingsSearch
{
    /// <summary>
    /// Identifies the <c>SettingsSearch.Label</c> attached property.
    /// The label supplies the searchable text for a single settings row.
    /// Rows with an empty or unset label are never hidden.
    /// </summary>
    public static readonly AttachedProperty<string?> LabelProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "Label",
            typeof(SettingsSearch));

    /// <summary>
    /// Identifies the <c>SettingsSearch.Query</c> attached property.
    /// Setting this on a container applies the filter to every
    /// descendant that has a <see cref="LabelProperty"/> value.
    /// </summary>
    public static readonly AttachedProperty<string?> QueryProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>(
            "Query",
            typeof(SettingsSearch));

    static SettingsSearch()
    {
        QueryProperty.Changed.AddClassHandler<Control>((container, _) =>
            ApplyFilter(container));

        // Re-apply when the container (or any new descendant) is attached,
        // so that pages swapped into a ContentControl pick up the current
        // query on first display.
        LabelProperty.Changed.AddClassHandler<Control>((row, _) =>
        {
            row.AttachedToLogicalTree -= OnRowAttached;
            row.AttachedToLogicalTree += OnRowAttached;
            ApplyToRow(row);
        });
    }

    /// <summary>Gets the value of <see cref="LabelProperty"/>.</summary>
    /// <param name="control">The control carrying the label.</param>
    /// <returns>The label, or <see langword="null"/> when unset.</returns>
    public static string? GetLabel(Control control) => control.GetValue(LabelProperty);

    /// <summary>Sets the value of <see cref="LabelProperty"/>.</summary>
    /// <param name="control">The control to annotate.</param>
    /// <param name="value">The searchable label.</param>
    public static void SetLabel(Control control, string? value) => control.SetValue(LabelProperty, value);

    /// <summary>Gets the value of <see cref="QueryProperty"/>.</summary>
    /// <param name="control">The container whose descendants are filtered.</param>
    /// <returns>The current query, or <see langword="null"/> when unset.</returns>
    public static string? GetQuery(Control control) => control.GetValue(QueryProperty);

    /// <summary>Sets the value of <see cref="QueryProperty"/>.</summary>
    /// <param name="control">The container whose descendants will be filtered.</param>
    /// <param name="value">The new query string.</param>
    public static void SetQuery(Control control, string? value) => control.SetValue(QueryProperty, value);

    /// <summary>
    /// Returns whether a row with the given label matches the query.
    /// Matching is case-insensitive substring, query and label are both
    /// trimmed, and an empty query matches everything.
    /// </summary>
    /// <param name="label">The row label (may be <see langword="null"/> or empty).</param>
    /// <param name="query">The search query (may be <see langword="null"/> or whitespace).</param>
    /// <returns><see langword="true"/> when the row should be visible.</returns>
    public static bool Matches(string? label, string? query)
    {
        var trimmedQuery = query?.Trim();
        if (string.IsNullOrEmpty(trimmedQuery))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return true;
        }

        return label!.Contains(trimmedQuery!, System.StringComparison.OrdinalIgnoreCase);
    }

    private static void OnRowAttached(object? sender, LogicalTreeAttachmentEventArgs e)
    {
        if (sender is Control row)
        {
            ApplyToRow(row);
        }
    }

    private static void ApplyToRow(Control row)
    {
        var query = FindQuery(row);
        var label = GetLabel(row);
        row.IsVisible = Matches(label, query);
    }

    private static void ApplyFilter(Control container)
    {
        var query = GetQuery(container);
        foreach (var descendant in container.GetVisualDescendants())
        {
            if (descendant is Control control && control.IsSet(LabelProperty))
            {
                control.IsVisible = Matches(GetLabel(control), query);
            }
        }
    }

    private static string? FindQuery(Control row)
    {
        Control? current = row;
        while (current is not null)
        {
            if (current.IsSet(QueryProperty))
            {
                return GetQuery(current);
            }

            current = current.Parent as Control;
        }

        return null;
    }
}
