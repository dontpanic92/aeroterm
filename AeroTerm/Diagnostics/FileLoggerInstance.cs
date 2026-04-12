// <copyright file="FileLoggerInstance.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Diagnostics;

using Microsoft.Extensions.Logging;

/// <summary>
/// A category-scoped <see cref="ILogger"/> that delegates to a shared
/// <see cref="FileLogger"/> and tags every message with its category name.
/// </summary>
internal sealed class FileLoggerInstance : ILogger
{
    private readonly FileLogger fileLogger;
    private readonly string categoryName;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLoggerInstance"/> class.
    /// </summary>
    /// <param name="fileLogger">The shared file logger.</param>
    /// <param name="categoryName">The category name for this logger instance.</param>
    public FileLoggerInstance(FileLogger fileLogger, string categoryName)
    {
        this.fileLogger = fileLogger;
        this.categoryName = categoryName;
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!this.IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);
        this.fileLogger.Write(logLevel, this.categoryName, message, exception);
    }
}
