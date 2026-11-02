using System.Diagnostics;
using System.Text;

namespace VoiceInputBuddy.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Thread-safe local diagnostic file logger for Voice Input Buddy.
/// Records system operations, connections, button mappings, and errors for troubleshooting.
/// Strictly enforces privacy: NEVER logs audio waveforms, user speech transcripts, or private text.
/// </summary>
public static class AppLogger
{
    private static readonly object _lock = new();
    private static string _logDirectory = string.Empty;
    private static string _logFilePath = string.Empty;
    private static long _maxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private static int _maxArchiveCount = 3;
    private static bool _isEnabled = true;
    private static bool _isInitialized = false;

    public static string LogDirectory
    {
        get
        {
            EnsureInitialized();
            return _logDirectory;
        }
    }

    public static string CurrentLogFilePath
    {
        get
        {
            EnsureInitialized();
            return _logFilePath;
        }
    }

    public static bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public static void Initialize(string? customLogDirectory = null, long? maxSizeBytes = null, int? maxArchives = null)
    {
        lock (_lock)
        {
            if (maxSizeBytes.HasValue && maxSizeBytes.Value > 1024)
            {
                _maxFileSizeBytes = maxSizeBytes.Value;
            }

            if (maxArchives.HasValue && maxArchives.Value >= 0)
            {
                _maxArchiveCount = maxArchives.Value;
            }

            if (!string.IsNullOrEmpty(customLogDirectory))
            {
                _logDirectory = customLogDirectory;
            }
            else
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _logDirectory = Path.Combine(appData, "VoiceInputBuddy", "logs");
            }

            try
            {
                Directory.CreateDirectory(_logDirectory);
                _logFilePath = Path.Combine(_logDirectory, "app.log");
                _isInitialized = true;

                // Write session start header if file is empty or new
                if (!File.Exists(_logFilePath) || new FileInfo(_logFilePath).Length == 0)
                {
                    string header = $"=== Voice Input Buddy Diagnostic Log Started at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC (PID: {Environment.ProcessId}) ===" + Environment.NewLine;
                    File.AppendAllText(_logFilePath, header, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[AppLogger] Failed to initialize log directory '{_logDirectory}': {ex.Message}");
            }
        }
    }

    public static void ResetForTesting()
    {
        lock (_lock)
        {
            _isInitialized = false;
            _logDirectory = string.Empty;
            _logFilePath = string.Empty;
            _maxFileSizeBytes = 5 * 1024 * 1024;
            _maxArchiveCount = 3;
            _isEnabled = true;
        }
    }

    private static void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            Initialize();
        }
    }

    public static void Debug(string message, string? component = null)
    {
        WriteEntry(LogLevel.Debug, message, component, null);
    }

    public static void Info(string message, string? component = null)
    {
        WriteEntry(LogLevel.Info, message, component, null);
    }

    public static void Warn(string message, string? component = null)
    {
        WriteEntry(LogLevel.Warn, message, component, null);
    }

    public static void Error(string message, Exception? ex = null, string? component = null)
    {
        WriteEntry(LogLevel.Error, message, component, ex);
    }

    public static void Log(LogLevel level, string message, string? component = null, Exception? ex = null)
    {
        WriteEntry(level, message, component, ex);
    }

    private static void WriteEntry(LogLevel level, string message, string? component, Exception? ex)
    {
        if (!_isEnabled) return;
        EnsureInitialized();

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string threadId = Environment.CurrentManagedThreadId.ToString("D2");
        string levelStr = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warn => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "INFO "
        };

        string compStr = string.IsNullOrEmpty(component) ? "" : $"[{component}] ";
        string logLine = $"[{timestamp}] [{levelStr}] [T{threadId}] {compStr}{message}";

        if (ex != null)
        {
            logLine += Environment.NewLine + $"Exception: {ex.GetType().FullName}: {ex.Message}" +
                       Environment.NewLine + ex.StackTrace;
        }

        lock (_lock)
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    var fileInfo = new FileInfo(_logFilePath);
                    if (fileInfo.Length >= _maxFileSizeBytes)
                    {
                        RotateLogs();
                    }
                }

                File.AppendAllText(_logFilePath, logLine + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ioEx)
            {
                Trace.WriteLine($"[AppLogger] Write error: {ioEx.Message}");
            }
        }

        Trace.WriteLine(logLine);
    }

    private static void RotateLogs()
    {
        try
        {
            if (_maxArchiveCount > 0)
            {
                string oldestArchive = Path.Combine(_logDirectory, $"app.{_maxArchiveCount}.log");
                if (File.Exists(oldestArchive))
                {
                    File.Delete(oldestArchive);
                }

                for (int i = _maxArchiveCount - 1; i >= 1; i--)
                {
                    string source = Path.Combine(_logDirectory, $"app.{i}.log");
                    string target = Path.Combine(_logDirectory, $"app.{i + 1}.log");
                    if (File.Exists(source))
                    {
                        File.Move(source, target, overwrite: true);
                    }
                }

                if (File.Exists(_logFilePath))
                {
                    string firstArchive = Path.Combine(_logDirectory, "app.1.log");
                    File.Move(_logFilePath, firstArchive, overwrite: true);
                }
            }
            else
            {
                if (File.Exists(_logFilePath))
                {
                    File.Delete(_logFilePath);
                }
            }

            string header = $"=== Voice Input Buddy Diagnostic Log Rotated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC ===\n";
            File.AppendAllText(_logFilePath, header, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[AppLogger] Failed to rotate logs: {ex.Message}");
        }
    }

    public static void Flush()
    {
        // File.AppendAllText immediately flushes to disk under lock
    }

    public static void OpenLogFolder()
    {
        EnsureInitialized();
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_logDirectory}\"",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", $"\"{_logDirectory}\"");
            }
        }
        catch (Exception ex)
        {
            Error("Failed to launch file explorer for logs folder", ex, "AppLogger");
        }
    }
}
