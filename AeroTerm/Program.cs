// <copyright file="Program.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm;

using System.Runtime.InteropServices;
using AeroTerm.Diagnostics;
using Avalonia;
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
            .LogToTrace();
    }
}
