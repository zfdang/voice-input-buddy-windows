// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using VoiceInputBuddy.App.ViewModels;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Platform.Windows.Bluetooth;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class OnboardingConnectionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Unknown")]
    public void ToolConfiguration_InvalidShortcut_DoesNotAdvanceOrOverwriteSavedShortcut(string shortcut)
    {
        using var manager = new WinRtBluetoothManager();
        var config = CreateConfig();
        using var vm = new OnboardingViewModel(config, manager)
        {
            CurrentStep = 6,
            ShortcutText = shortcut
        };

        vm.NextStep();

        Assert.Equal(6, vm.CurrentStep);
        Assert.Equal("Ctrl+Win", config.Current.InputTool.Shortcut);
        Assert.Contains("快捷键", vm.ValidationError);
        vm.ApplyPreset("WeChatInput");
        vm.NextStep();
        Assert.Equal(7, vm.CurrentStep);
        Assert.Empty(vm.ValidationError);
    }

    [Fact]
    public void ReopeningWizard_WithReadyService_RestoresDeviceAndPassesConnectionStep()
    {
        using var manager = new WinRtBluetoothManager();
        typeof(WinRtBluetoothManager).GetProperty(nameof(WinRtBluetoothManager.IsReady))!
            .SetValue(manager, true);
        var config = CreateConfig();
        var vm = new OnboardingViewModel(config, manager) { CurrentStep = 3 };

        Assert.Same(config.Current.Devices[0], vm.SelectedDevice);
        Assert.Single(vm.DiscoveredDevices);
        Assert.True(vm.IsDeviceConnected);
        vm.NextStep();
        Assert.Equal(4, vm.CurrentStep);
        Assert.Empty(vm.ValidationError);
    }

    [Fact]
    public void SavedConnectedFlag_WithoutReadyService_DoesNotPassConnectionStep()
    {
        using var manager = new WinRtBluetoothManager();
        var vm = new OnboardingViewModel(CreateConfig(), manager) { CurrentStep = 3 };

        Assert.False(vm.IsDeviceConnected);
        vm.NextStep();
        Assert.Equal(3, vm.CurrentStep);
        Assert.Contains("语音服务", vm.ValidationError);
    }

    [Fact]
    public void Disconnect_ClearsServiceReadiness()
    {
        using var manager = new WinRtBluetoothManager();
        typeof(WinRtBluetoothManager).GetProperty(nameof(WinRtBluetoothManager.IsReady))!
            .SetValue(manager, true);
        manager.Disconnect();
        Assert.False(manager.IsReady);
    }

    private static ConfigService CreateConfig()
    {
        var config = new ConfigService(Path.Combine(Path.GetTempPath(), $"vib-test-{Guid.NewGuid():N}.json"));
        config.Current.SelectedDeviceId = "1234";
        config.Current.Devices.Add(new RemoteDevice
        {
            Id = "1234", BluetoothAddress = 0x1234, Name = "Test remote", IsConnected = true
        });
        return config;
    }

    [Fact]
    public void ButtonTest_RequiresFreshPressDuringStepFour()
    {
        using var manager = new WinRtBluetoothManager();
        var vm = new OnboardingViewModel(CreateConfig(), manager);
        vm.NotifyButtonPressed(RemoteButton.Up);
        Assert.False(vm.HasButtonTested);
        vm.CurrentStep = 4;
        vm.NextStep();
        Assert.Equal(4, vm.CurrentStep);
        vm.NotifyButtonPressed(RemoteButton.Ok);
        vm.NextStep();
        Assert.Equal(5, vm.CurrentStep);
        vm.PreviousStep();
        Assert.False(vm.HasButtonTested);
    }
}
