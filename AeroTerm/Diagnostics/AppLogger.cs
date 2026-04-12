// <copyright file="AppLogger.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Application-wide logger accessor. Must be initialized once at startup
/// via <see cref="Initialize"/>. Falls back to <see cref="NullLoggerFactory"/>
/// when not initialized.
/// </summary>
internal static class AppLogger
{
    private static ILoggerFactory factory = NullLoggerFactory.Instance;
    private static FileLogger? fileLogger;

    /// <summary>
    /// Gets the <see cref="ILoggerFactory"/> that library services can use
    /// to create their own typed loggers.
    /// </summary>
    public static ILoggerFactory Factory => factory;

    /// <summary>
    /// Gets the log file path when a <see cref="FileLogger"/> is active,
    /// or <c>null</c> if logging was not configured.
    /// </summary>
    public static string? LogFilePath => fileLogger?.LogFilePath;

    /// <summary>
    /// Creates a logger whose category name is derived from
    /// <typeparamref name="T"/>'s full name.
    /// </summary>
    /// <typeparam name="T">The type whose name becomes the logger category.</typeparam>
    /// <returns>An <see cref="ILogger"/> for the specified type.</returns>
    public static ILogger For<T>()
    {
        return factory.CreateLogger<T>();
    }

    /// <summary>
    /// Creates a logger with an explicit category name.
    /// </summary>
    /// <param name="categoryName">The category for log messages.</param>
    /// <returns>An <see cref="ILogger"/> for the specified category.</returns>
    public static ILogger For(string categoryName)
    {
        return factory.CreateLogger(categoryName);
    }

    /// <summary>
    /// Sets the global logger to write through the given <see cref="FileLogger"/>.
    /// Should be called exactly once during application startup.
    /// </summary>
    /// <param name="logger">The file logger to use application-wide.</param>
    public static void Initialize(FileLogger logger)
    {
        fileLogger = logger;
        var provider = new FileLoggerProvider(logger);
        factory = new FileLoggerFactory(provider);
    }

    /// <summary>
    /// Disposes the current logger factory and resets the instance to
    /// <see cref="NullLoggerFactory"/>.
    /// </summary>
    public static void Shutdown()
    {
        (factory as IDisposable)?.Dispose();
        fileLogger?.Dispose();
        factory = NullLoggerFactory.Instance;
        fileLogger = null;
    }
}
