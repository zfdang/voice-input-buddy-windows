// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Models;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "VIB_ConfigTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void Load_InitializesDefault_WhenFileDoesNotExist()
    {
        string path = Path.Combine(_tempDir, "config.json");
        var service = new ConfigService(path);

        Assert.NotNull(service.Current);
        Assert.False(service.Current.HasCompletedOnboarding);
        Assert.Empty(service.Current.Devices);
    }

    [Fact]
    public void SaveAndLoad_PersistsAllProperties()
    {
        string path = Path.Combine(_tempDir, "config.json");
        var service = new ConfigService(path);

        service.Current.HasCompletedOnboarding = true;
        service.Current.SelectedDeviceId = "DEV_001";
        service.Current.Devices.Add(new RemoteDevice
        {
            Id = "DEV_001",
            Name = "Living Room Remote",
            BluetoothAddress = 0xAABBCCDDEEFF,
            ModelType = RemoteModelType.XiaomiARN9
        });
        service.Current.InputTool.Tool = TargetInputTool.WeChatInput;
        service.Current.InputTool.Shortcut = "Ctrl + Win";

        service.Save();

        var reloaded = new ConfigService(path);
        Assert.True(reloaded.Current.HasCompletedOnboarding);
        Assert.Equal("DEV_001", reloaded.Current.SelectedDeviceId);
        Assert.Single(reloaded.Current.Devices);
        Assert.Equal(RemoteModelType.XiaomiARN9, reloaded.Current.Devices[0].ModelType);
        Assert.Equal(TargetInputTool.WeChatInput, reloaded.Current.InputTool.Tool);
        Assert.Equal("Ctrl + Win", reloaded.Current.InputTool.Shortcut);
    }

    [Fact]
    public void Load_RestoresFromBackup_WhenMainConfigIsCorrupted()
    {
        string path = Path.Combine(_tempDir, "config.json");
        var service = new ConfigService(path);

        service.Current.HasCompletedOnboarding = true;
        service.Current.SelectedDeviceId = "ORIGINAL_VALID_ID";
        service.Save(); // Generates config.json and backup

        // Corrupt main config.json
        File.WriteAllText(path, "{ corrupt_json: [ incomplete");

        var recoveredService = new ConfigService(path);
        Assert.True(recoveredService.Current.HasCompletedOnboarding);
        Assert.Equal("ORIGINAL_VALID_ID", recoveredService.Current.SelectedDeviceId);
    }

    [Fact]
    public void Load_PreservesCorruptFileAsTimestampedBackup_WhenNoValidBackupExists()
    {
        string path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, "THIS IS INVALID NOT JSON");

        var service = new ConfigService(path);

        // Fallback to default
        Assert.NotNull(service.Current);
        Assert.False(service.Current.HasCompletedOnboarding);

        // The corrupt file must have been preserved for diagnostics
        var corruptFiles = Directory.GetFiles(_tempDir, "config.json.corrupt.*");
        Assert.Single(corruptFiles);
        Assert.Equal("THIS IS INVALID NOT JSON", File.ReadAllText(corruptFiles[0]));
    }
}
