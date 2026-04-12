// <copyright file="FileLogger.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Diagnostics;

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

/// <summary>
/// Thread-safe file logger that writes structured log lines and echoes
/// them to <see cref="System.Diagnostics.Trace"/> for debugger visibility.
/// Performs simple size-based rotation.
/// </summary>
internal sealed class FileLogger : ILogger, IDisposable
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private readonly string logFilePath;
    private readonly string oldLogFilePath;
    private readonly object syncRoot = new();
    private StreamWriter? writer;
    private long currentSize;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileLogger"/> class.
    /// Creates the log directory and opens the log file for appending.
    /// </summary>
    /// <param name="logDirectory">
    /// Directory where log files are written. Created if it does not exist.
    /// </param>
    public FileLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        this.logFilePath = Path.Combine(logDirectory, "aeroterm.log");
        this.oldLogFilePath = Path.Combine(logDirectory, "aeroterm.old.log");
        this.LogDirectory = logDirectory;
        this.OpenWriter();
    }

    /// <summary>
    /// Gets the directory where log files are stored.
    /// </summary>
    public string LogDirectory { get; }

    /// <summary>
    /// Gets the full path to the current log file.
    /// </summary>
    public string LogFilePath => this.logFilePath;

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
        string line = FormatLine(logLevel, string.Empty, message, exception);

        Trace.WriteLine(line);

        lock (this.syncRoot)
        {
            if (this.disposed)
            {
                return;
            }

            this.WriteLocked(line);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (this.syncRoot)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.writer?.Flush();
            this.writer?.Dispose();
            this.writer = null;
        }
    }

    /// <summary>
    /// Writes a log entry with an explicit category name. Used by
    /// <see cref="FileLoggerInstance"/> to include the logger category.
    /// </summary>
    /// <param name="logLevel">Severity of the message.</param>
    /// <param name="category">The logger category name.</param>
    /// <param name="message">Human-readable description.</param>
    /// <param name="exception">Optional associated exception.</param>
    internal void Write(LogLevel logLevel, string category, string message, Exception? exception)
    {
        string line = FormatLine(logLevel, category, message, exception);

        Trace.WriteLine(line);

        lock (this.syncRoot)
        {
            if (this.disposed)
            {
                return;
            }

            this.WriteLocked(line);
        }
    }

    private static string FormatLine(LogLevel level, string category, string message, Exception? exception)
    {
        string tag = level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };

        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder(256);
        sb.Append(timestamp).Append(" [").Append(tag).Append(']');

        if (!string.IsNullOrEmpty(category))
        {
            sb.Append(" [").Append(category).Append(']');
        }

        sb.Append(' ').Append(message);

        if (exception is not null)
        {
            sb.AppendLine();
            sb.Append(exception);
        }

        return sb.ToString();
    }

    private void OpenWriter()
    {
        try
        {
            var stream = new FileStream(
                this.logFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);
            this.currentSize = stream.Length;
            this.writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        }
        catch (IOException)
        {
            // If the file cannot be opened, logging degrades to Trace only.
            this.writer = null;
            this.currentSize = 0;
        }
    }

    private void WriteLocked(string line)
    {
        if (this.writer is null)
        {
            return;
        }

        this.writer.WriteLine(line);
        this.currentSize += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;

        if (this.currentSize >= MaxFileSizeBytes)
        {
            this.Rotate();
        }
    }

    private void Rotate()
    {
        try
        {
            this.writer?.Flush();
            this.writer?.Dispose();
            this.writer = null;

            if (File.Exists(this.oldLogFilePath))
            {
                File.Delete(this.oldLogFilePath);
            }

            File.Move(this.logFilePath, this.oldLogFilePath);
        }
        catch (IOException)
        {
            // Best-effort rotation; if it fails we just keep writing.
        }

        this.OpenWriter();
    }
}
