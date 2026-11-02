using VoiceInputBuddy.Core.Diagnostics;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class AppLoggerTests : IDisposable
{
    private readonly string _tempLogDir;

    public AppLoggerTests()
    {
        _tempLogDir = Path.Combine(Path.GetTempPath(), "VIB_Tests_" + Guid.NewGuid().ToString("N"));
        AppLogger.ResetForTesting();
        AppLogger.Initialize(_tempLogDir);
    }

    public void Dispose()
    {
        AppLogger.ResetForTesting();
        try
        {
            if (Directory.Exists(_tempLogDir))
            {
                Directory.Delete(_tempLogDir, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup errors
        }
    }

    [Fact]
    public void AppLogger_WritesFormattedLogEntry_ToFile()
    {
        AppLogger.Info("Bluetooth connection established", "BluetoothManager");

        string logFile = AppLogger.CurrentLogFilePath;
        Assert.True(File.Exists(logFile));

        string content = File.ReadAllText(logFile);
        Assert.Contains("[INFO ]", content);
        Assert.Contains("[BluetoothManager]", content);
        Assert.Contains("Bluetooth connection established", content);
    }

    [Fact]
    public void AppLogger_WritesExceptionDetails_OnError()
    {
        var ex = new InvalidOperationException("GATT discovery failed");
        AppLogger.Error("Operation failed", ex, "BluetoothManager");

        string content = File.ReadAllText(AppLogger.CurrentLogFilePath);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("[BluetoothManager]", content);
        Assert.Contains("Operation failed", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("GATT discovery failed", content);
    }

    [Fact]
    public void AppLogger_RotatesLogs_WhenExceedingMaxSize()
    {
        // Re-initialize with small limit: 1200 bytes
        AppLogger.ResetForTesting();
        AppLogger.Initialize(_tempLogDir, maxSizeBytes: 1200, maxArchives: 2);

        // Write enough lines to exceed 1200 bytes
        for (int i = 0; i < 30; i++)
        {
            AppLogger.Info($"Repetitive test log message number {i:D3} for rotation testing", "TestComp");
        }

        string archive1 = Path.Combine(_tempLogDir, "app.1.log");
        Assert.True(File.Exists(archive1), "Expected app.1.log to exist after rotation");

        string current = File.ReadAllText(AppLogger.CurrentLogFilePath);
        Assert.Contains("=== Voice Input Buddy Diagnostic Log Rotated", current);
    }

    [Fact]
    public void AppLogger_Disabled_DoesNotAppend()
    {
        AppLogger.Info("First entry", "Comp");
        string before = File.ReadAllText(AppLogger.CurrentLogFilePath);

        AppLogger.IsEnabled = false;
        AppLogger.Info("Second entry should be ignored", "Comp");

        string after = File.ReadAllText(AppLogger.CurrentLogFilePath);
        Assert.Equal(before, after);
    }
}
