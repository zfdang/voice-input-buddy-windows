// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Win32;
using VoiceInputBuddy.Core.Diagnostics;

namespace VoiceInputBuddy.Platform.Windows.Services;

public static class StartupService
{
    private const string AppName = "VoiceInputBuddy";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsLaunchAtStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var val = key?.GetValue(AppName) as string;
            if (string.IsNullOrEmpty(val)) return false;
            string? currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe)) return true;
            return val.Contains(currentExe, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to query Run registry key: {ex.Message}", "StartupService");
            return false;
        }
    }

    public static bool SetLaunchAtStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return false;

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\" --minimized");
                    AppLogger.Info($"Registered startup run key for '{exePath}'", "StartupService");
                    return true;
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
                AppLogger.Info("Removed startup run key", "StartupService");
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to update startup run key: {ex.Message}", ex, "StartupService");
            return false;
        }

        return false;
    }
}
