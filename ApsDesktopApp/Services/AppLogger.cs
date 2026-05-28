using System;
using System.Collections.Generic;
using System.IO;

namespace ApsDesktopApp.Services;

public enum LogLevel { Debug, Info, Warning, Error }

public record LogEntry(DateTime Timestamp, LogLevel Level, string Category, string Message);

// Singleton log service. WPF-free: raises EntryAdded for the UI to subscribe and
// dispatch. Writes to %APPDATA%\ApsDesktopApp\logs\app-YYYY-MM-DD.log.
// In-memory buffer is capped at MaxEntries to avoid unbounded growth.
public class AppLogger
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();
    private readonly string _logFile;
    private const int MaxEntries = 5000;

    // Raised on the calling thread (often a thread-pool thread). Subscribers must
    // dispatch to the UI thread themselves before touching WPF objects.
    public event EventHandler<LogEntry>? EntryAdded;

    public string LogFilePath => _logFile;

    // Minimum level written to the log file. Defaults to Debug so every message
    // is persisted. Raise this to Info or Warning to reduce file noise in production.
    public LogLevel FileLogLevel { get; set; } = LogLevel.Debug;

    // Log files older than this many days are deleted at startup.
    private const int RetainDays = 7;

    public AppLogger()
    {
        var logDir = Path.Combine(AppPaths.DataDirectory, "logs");
        Directory.CreateDirectory(logDir);
        _logFile = Path.Combine(logDir, $"app-{DateTime.Today:yyyy-MM-dd}.log");
        PurgeOldLogs(logDir);
    }

    // Deletes app-YYYY-MM-DD.log files whose date is older than RetainDays.
    // Parses dates from the filename so file-system timestamps can't mislead us.
    private static void PurgeOldLogs(string logDir)
    {
        try
        {
            var cutoff = DateTime.Today.AddDays(-RetainDays);
            foreach (var file in Directory.GetFiles(logDir, "app-????-??-??.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file); // "app-2026-05-01"
                var datePart = name.Length > 4 ? name[4..] : null; // "2026-05-01"
                if (datePart is not null
                    && DateTime.TryParseExact(datePart, "yyyy-MM-dd",
                        null, System.Globalization.DateTimeStyles.None, out var fileDate)
                    && fileDate < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch { /* never block startup over a housekeeping failure */ }
    }

    // Thread-safe snapshot for initial window population.
    public IReadOnlyList<LogEntry> GetSnapshot()
    {
        lock (_lock) return _entries.ToArray();
    }

    public void Log(LogLevel level, string category, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, category, message);
        lock (_lock)
        {
            if (_entries.Count >= MaxEntries)
                _entries.RemoveAt(0);
            _entries.Add(entry);
            AppendToFile(entry);
        }
        EntryAdded?.Invoke(this, entry);
    }

    public void Debug(string category, string message)   => Log(LogLevel.Debug,   category, message);
    public void Info(string category, string message)    => Log(LogLevel.Info,    category, message);
    public void Warn(string category, string message)    => Log(LogLevel.Warning, category, message);
    public void Error(string category, string message)   => Log(LogLevel.Error,   category, message);

    private void AppendToFile(LogEntry entry)
    {
        if (entry.Level < FileLogLevel) return;
        try
        {
            var tag = entry.Level switch
            {
                LogLevel.Debug   => "DBG",
                LogLevel.Info    => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error   => "ERR",
                _                => "???"
            };
            var line = $"[{entry.Timestamp:HH:mm:ss.fff}] [{tag}] [{entry.Category,-20}] {entry.Message}";
            File.AppendAllText(_logFile, line + Environment.NewLine);
        }
        catch { /* never let logging crash the app */ }
    }
}
