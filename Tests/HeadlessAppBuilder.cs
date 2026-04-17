// <copyright file="HeadlessAppBuilder.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Tests;

using AeroTerm;
using Avalonia;
using Avalonia.Headless;

/// <summary>
/// Avalonia application entry point for the headless test harness.
/// <see cref="Avalonia.Headless.AvaloniaTestApplicationAttribute"/> (declared
/// at the assembly level in <c>AssemblyInfo.cs</c>) points at this type, and
/// the NUnit integration from <c>Avalonia.Headless.NUnit</c> discovers
/// <see cref="BuildAvaloniaApp"/> to spin up the real <see cref="App"/>
/// subclass on top of the in-process headless platform.
/// <para>
/// Headless drawing is enabled (<see cref="AvaloniaHeadlessPlatformOptions.UseHeadlessDrawing"/>
/// <c>= true</c>) — tests only ever exercise layout / input routing, not
/// actual pixel rendering, which keeps the harness runnable on CI hosts
/// without a display server.
/// </para>
/// </summary>
internal static class HeadlessAppBuilder
{
    /// <summary>
    /// Builds the Avalonia <see cref="AppBuilder"/> used by the headless
    /// test session. Called by <c>Avalonia.Headless</c> via reflection.
    /// </summary>
    /// <returns>A configured <see cref="AppBuilder"/> running the real <see cref="App"/> on the headless platform.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true,
            });
    }
}
