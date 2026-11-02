// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceInputBuddy.Core.Diagnostics;
using VoiceInputBuddy.Core.Models;

namespace VoiceInputBuddy.Core.Config;

public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _lock = new();
    private readonly string _configFilePath;
    private readonly string _backupFilePath;

    public AppConfig Current { get; private set; }

    public ConfigService(string? customPath = null)
    {
        if (!string.IsNullOrEmpty(customPath))
        {
            _configFilePath = customPath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "VoiceInputBuddy");
            Directory.CreateDirectory(dir);
            _configFilePath = Path.Combine(dir, "config.json");
        }

        _backupFilePath = _configFilePath + ".bak";
        Current = Load();
    }

    public AppConfig Load()
    {
        lock (_lock)
        {
            bool hasMain = File.Exists(_configFilePath);
            if (hasMain && TryLoadFile(_configFilePath, out var config))
            {
                Current = config;
                AppLogger.Info($"Configuration loaded from {_configFilePath}. Devices: {Current.Devices.Count}, Selected: {Current.SelectedDeviceId ?? "none"}", "ConfigService");
                return Current;
            }

            // If main file exists but failed to load, quarantine it immediately
            // so subsequent saves or backups do not overwrite valid backups with corrupted data
            if (hasMain)
            {
                string corruptPath = _configFilePath + $".corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}";
                try
                {
                    File.Move(_configFilePath, corruptPath, overwrite: true);
                    AppLogger.Warn($"Preserved corrupt config file as {corruptPath}", "ConfigService");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Could not move corrupt config file: {ex.Message}", "ConfigService");
                }
            }

            // Fallback to backup if main file was corrupt or missing
            if (File.Exists(_backupFilePath))
            {
                if (TryLoadFile(_backupFilePath, out var backupConfig))
                {
                    AppLogger.Warn($"Configuration restored from backup {_backupFilePath}", "ConfigService");
                    Current = backupConfig;
                    // Re-persist clean state to restore main config.json
                    Save();
                    return Current;
                }
                else
                {
                    string corruptBackup = _backupFilePath + $".corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}";
                    try
                    {
                        File.Move(_backupFilePath, corruptBackup, overwrite: true);
                        AppLogger.Warn($"Preserved corrupt backup file as {corruptBackup}", "ConfigService");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Could not move corrupt backup file: {ex.Message}", "ConfigService");
                    }
                }
            }

            Current = new AppConfig();
            AppLogger.Info($"Initialized default configuration for {_configFilePath}", "ConfigService");
            return Current;
        }
    }

    private static bool TryLoadFile(string path, out AppConfig config)
    {
        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                config = null!;
                return false;
            }

            var parsed = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (parsed != null)
            {
                parsed.InputTool ??= new();
                parsed.Devices ??= new();
                foreach (var dev in parsed.Devices)
                {
                    dev.Mappings ??= RemoteDevice.CreateDefaultMappings(dev.ModelType);
                }
                config = parsed;
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to parse config file at {path}", ex, "ConfigService");
        }

        config = null!;
        return false;
    }

    public void Save()
    {
        lock (_lock)
        {
            string tempFile = _configFilePath + $".{Guid.NewGuid():N}.tmp";
            bool cleanupNeeded = true;
            try
            {
                string? dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(Current, JsonOptions);
                File.WriteAllText(tempFile, json);

                if (File.Exists(_configFilePath))
                {
                    try
                    {
                        File.Replace(tempFile, _configFilePath, _backupFilePath, ignoreMetadataErrors: true);
                        cleanupNeeded = false;
                    }
                    catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
                    {
                        AppLogger.Warn($"File.Replace failed ({ex.Message}), falling back to direct copy", "ConfigService");
                        File.Copy(_configFilePath, _backupFilePath, overwrite: true);
                        File.Move(tempFile, _configFilePath, overwrite: true);
                        cleanupNeeded = false;
                    }
                }
                else
                {
                    File.Move(tempFile, _configFilePath, overwrite: true);
                    cleanupNeeded = false;
                    try
                    {
                        File.Copy(_configFilePath, _backupFilePath, overwrite: true);
                    }
                    catch { }
                }

                AppLogger.Info($"Configuration safely saved to {_configFilePath}", "ConfigService");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to save configuration to {_configFilePath}", ex, "ConfigService");
            }
            finally
            {
                if (cleanupNeeded && File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }
    }
}
