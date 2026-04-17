// <copyright file="BellDispatcher.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System;

/// <summary>
/// Pure routing logic that maps a <see cref="BellAction"/> onto zero or more
/// calls on an <see cref="IBellOutputs"/>. Lives in its own class so the
/// dispatch table is trivially unit-testable.
/// </summary>
internal static class BellDispatcher
{
    /// <summary>
    /// Invoke the matching <paramref name="outputs"/> methods for
    /// <paramref name="action"/>.
    /// </summary>
    /// <param name="action">The configured bell reaction.</param>
    /// <param name="outputs">The platform bell outputs.</param>
    public static void Dispatch(BellAction action, IBellOutputs outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);

        switch (action)
        {
            case BellAction.None:
                return;

            case BellAction.Visual:
                outputs.Visual();
                return;

            case BellAction.Audio:
                outputs.Audio();
                return;

            case BellAction.Notification:
                outputs.Notify();
                return;

            case BellAction.VisualAndAudio:
                outputs.Visual();
                outputs.Audio();
                return;

            case BellAction.All:
                outputs.Visual();
                outputs.Audio();
                outputs.Notify();
                return;

            default:
                return;
        }
    }
}
