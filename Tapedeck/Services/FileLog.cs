using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Tapedeck.Services;

/// <summary>
/// Writes all log entries of the app into one text file, <c>log.txt</c> in the app data folder, one line per entry:
/// <c>2026-11-07 14:03:12.345 INF [DownloadService] Downloaded …</c>. Exceptions follow indented.
/// The file stays readable while the app runs. It is trimmed at start when it grew too large, so it never needs
/// cleaning up and still reaches back a few weeks.
/// </summary>
public sealed class FileLog : ILoggerProvider
{
    public const string FILE_NAME = "log.txt";

    // trimmed to the newest part at start, so the file stays easy to open and to attach to a bug report
    private const long MAX_BYTES = 2 * 1024 * 1024;
    private const long KEEP_BYTES = 1024 * 1024;

    private readonly object gate = new();
    private StreamWriter? writer;

    /// <summary>The log of this run, opened in <c>Program.Main</c> before anything else.</summary>
    public static FileLog? Current { get; private set; }

    private FileLog(string path)
    {
        this.Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        Trim(path);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        this.writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    /// <summary>Absolute path of the log file.</summary>
    public string Path { get; }

    /// <summary>Opens the log in the app data folder; a log that cannot be opened is left out without failing the app.</summary>
    public static FileLog? Start(string dataDir)
    {
        try
        {
            Current = new FileLog(System.IO.Path.Combine(dataDir, FILE_NAME));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Current = null;
        }
        return Current;
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, ShortName(categoryName));

    public void Dispose()
    {
        lock (this.gate)
        {
            this.writer?.Dispose();
            this.writer = null;
        }
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
            .Append(' ').Append(LevelName(level))
            .Append(" [").Append(category).Append("] ")
            .Append(message);
        if (exception is not null)
        {
            foreach (var part in exception.ToString().Split('\n'))
                line.Append('\n').Append("    ").Append(part.TrimEnd('\r'));
        }

        lock (this.gate)
        {
            try
            {
                this.writer?.WriteLine(line.ToString());
            }
            catch (IOException)
            {
                // a full disk must not take the app down because of its log
            }
        }
    }

    /// <summary>Keeps only the newest part of a file that grew too large, starting at a whole line.</summary>
    private static void Trim(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= MAX_BYTES)
            return;

        var tail = new byte[KEEP_BYTES];
        using (var stream = file.OpenRead())
        {
            stream.Seek(-KEEP_BYTES, SeekOrigin.End);
            stream.ReadExactly(tail);
        }
        var start = Array.IndexOf(tail, (byte)'\n') + 1;
        File.WriteAllBytes(path, tail[start..]);
    }

    private static string ShortName(string category) =>
        category.LastIndexOf('.') is var dot and >= 0 ? category[(dot + 1)..] : category;

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "   ",
    };

    private sealed class Logger(FileLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!this.IsEnabled(logLevel))
                return;
            log.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
