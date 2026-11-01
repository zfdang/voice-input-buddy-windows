using System.Text.Json;
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
                    return Current;
                }
            }
        }
        catch
        {
            // Fallback to default
        }

        Current = new AppConfig();
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
        }
        catch
        {
            // Ignore or log error
        }
    }
}
