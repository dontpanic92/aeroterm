// <copyright file="FileLoggerProvider.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Diagnostics;

using Microsoft.Extensions.Logging;

/// <summary>
/// An <see cref="ILoggerProvider"/> that creates category-scoped loggers
/// backed by a shared <see cref="FileLogger"/>.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLogger fileLogger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLoggerProvider"/> class.
    /// </summary>
    /// <param name="fileLogger">The shared file logger that all instances write to.</param>
    public FileLoggerProvider(FileLogger fileLogger)
    {
        this.fileLogger = fileLogger;
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        return new FileLoggerInstance(this.fileLogger, categoryName);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The FileLogger lifetime is managed by AppLogger; do not dispose here.
    }
}
