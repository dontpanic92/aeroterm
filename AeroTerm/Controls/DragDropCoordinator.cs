// <copyright file="DragDropCoordinator.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Controls;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

/// <summary>
/// Static helper that locates drop targets for cross-window tab drags.
/// During a drag, the source <see cref="TabStrip"/> calls
/// <see cref="FindDropTarget"/> with the current screen position to determine
/// whether the pointer is hovering over another window's tab strip.
/// </summary>
internal static class DragDropCoordinator
{
    /// <summary>
    /// Searches all open <see cref="MainWindow"/> instances (excluding
    /// <paramref name="excludeWindow"/>) for a <see cref="TabStrip"/>
    /// whose screen bounds contain <paramref name="screenPos"/>.
    /// </summary>
    /// <param name="screenPos">Pointer position in screen pixels.</param>
    /// <param name="excludeWindow">The window originating the drag (skipped).</param>
    /// <returns>
    /// A <see cref="DropTarget"/> if a target strip was found; <c>null</c>
    /// otherwise.
    /// </returns>
    internal static DropTarget? FindDropTarget(PixelPoint screenPos, Window excludeWindow)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        foreach (var window in desktop.Windows)
        {
            if (ReferenceEquals(window, excludeWindow) || window is not MainWindow mainWindow)
            {
                continue;
            }

            var strip = mainWindow.Strip;
            if (strip is null)
            {
                continue;
            }

            int index = strip.GetDropIndexAtScreenPoint(screenPos);
            if (index >= 0)
            {
                return new DropTarget(strip, index);
            }
        }

        return null;
    }

    /// <summary>
    /// Result of a cross-window drop-target search.
    /// </summary>
    /// <param name="TargetStrip">The tab strip under the pointer.</param>
    /// <param name="InsertionIndex">The position within the target strip where the tab would be inserted.</param>
    internal readonly record struct DropTarget(TabStrip TargetStrip, int InsertionIndex);
}
