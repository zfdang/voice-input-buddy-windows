using System.Text.Json;
using VoiceInputBuddy.Core.Diagnostics;
using VoiceInputBuddy.Core.Models;

namespace VoiceInputBuddy.Core.Config;

public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _configFilePath;

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

        Current = Load();
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                {
                    Current = config;
                    AppLogger.Info($"Configuration loaded from {_configFilePath}. Devices: {Current.Devices.Count}, Selected: {Current.SelectedDeviceId ?? "none"}", "ConfigService");
                    return Current;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to load configuration from {_configFilePath}, using defaults", ex, "ConfigService");
        }

        Current = new AppConfig();
        AppLogger.Info($"Initialized default configuration for {_configFilePath}", "ConfigService");
        return Current;
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(_configFilePath, json);
            AppLogger.Info($"Configuration saved to {_configFilePath}", "ConfigService");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to save configuration to {_configFilePath}", ex, "ConfigService");
        }
    }
}
