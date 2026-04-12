// <copyright file="FileLoggerFactory.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Diagnostics;

using Microsoft.Extensions.Logging;

/// <summary>
/// A minimal <see cref="ILoggerFactory"/> backed by a single
/// <see cref="FileLoggerProvider"/>. Avoids a dependency on
/// <c>Microsoft.Extensions.Logging</c> (the concrete package) by
/// implementing the factory directly.
/// </summary>
internal sealed class FileLoggerFactory : ILoggerFactory
{
    private readonly FileLoggerProvider provider;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLoggerFactory"/> class.
    /// </summary>
    /// <param name="provider">The provider that creates logger instances.</param>
    public FileLoggerFactory(FileLoggerProvider provider)
    {
        this.provider = provider;
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        return this.provider.CreateLogger(categoryName);
    }

    /// <inheritdoc/>
    public void AddProvider(ILoggerProvider provider)
    {
        // Only the initial FileLoggerProvider is supported.
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.provider.Dispose();
        }
    }
}
