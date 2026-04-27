// <copyright file="Program.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm;

using System.Runtime.InteropServices;
using AeroTerm.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using Velopack;

/// <summary>
/// The main entry point for the AeroTerm terminal emulator.
/// </summary>
public static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must be the first call — it handles install/update hooks
        // and exits immediately when invoked by the updater process.
        VelopackApp.Build().Run();

        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AeroTerm",
            "logs");
        AppLogger.Initialize(new FileLogger(logDir));

        var log = AppLogger.For("Startup");
        log.LogInformation("AeroTerm starting — OS={OsDescription}, Runtime={FrameworkDescription}", RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            log.LogInformation("AeroTerm shutting down.");
            AppLogger.Shutdown();
        }
    }

    /// <summary>
    /// Builds the Avalonia application configuration.
    /// </summary>
    /// <returns>The configured AppBuilder.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = GetPlatformDefaultUiFont(),
            })
            .LogToTrace();
    }

    /// <summary>
    /// Returns a comma-separated font family fallback chain that prefers the
    /// host operating system's native UI font, so Avalonia controls visually
    /// blend in with the rest of the desktop.
    /// </summary>
    /// <returns>A font family name (or comma-separated fallback list).</returns>
    private static string GetPlatformDefaultUiFont()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Segoe UI Variable Text ships on Windows 11; Segoe UI is the
            // Windows 10 fallback. Inter is the last-resort embedded font.
            return "Segoe UI Variable Text, Segoe UI, Inter";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // .AppleSystemUIFont resolves to SF Pro on modern macOS.
            return ".AppleSystemUIFont, Helvetica Neue, Inter";
        }

        // Linux: common desktop UI fonts first, then embedded Inter as fallback.
        return "Ubuntu, Cantarell, Noto Sans, DejaVu Sans, Liberation Sans, Inter";
    }
}
