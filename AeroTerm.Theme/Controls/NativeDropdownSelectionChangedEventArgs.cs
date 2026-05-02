// <copyright file="NativeDropdownSelectionChangedEventArgs.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Theme.Controls;

using System;

/// <summary>
/// Provides data for <see cref="NativeDropdown.SelectionChanged"/>.
/// </summary>
public sealed class NativeDropdownSelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeDropdownSelectionChangedEventArgs"/> class.
    /// </summary>
    /// <param name="oldIndex">The previous selected index.</param>
    /// <param name="oldItem">The previous selected item.</param>
    /// <param name="newIndex">The new selected index.</param>
    /// <param name="newItem">The new selected item.</param>
    public NativeDropdownSelectionChangedEventArgs(
        int oldIndex,
        NativeDropdownItem? oldItem,
        int newIndex,
        NativeDropdownItem? newItem)
    {
        this.OldIndex = oldIndex;
        this.OldItem = oldItem;
        this.NewIndex = newIndex;
        this.NewItem = newItem;
    }

    /// <summary>
    /// Gets the previous selected index.
    /// </summary>
    public int OldIndex { get; }

    /// <summary>
    /// Gets the previous selected item.
    /// </summary>
    public NativeDropdownItem? OldItem { get; }

    /// <summary>
    /// Gets the new selected index.
    /// </summary>
    public int NewIndex { get; }

    /// <summary>
    /// Gets the new selected item.
    /// </summary>
    public NativeDropdownItem? NewItem { get; }
}
