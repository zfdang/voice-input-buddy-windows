// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using VoiceInputBuddy.App.Converters;
using VoiceInputBuddy.App.ViewModels;
using VoiceInputBuddy.Core.Bluetooth;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Models;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class ConvertersAndViewModelTests
{
    private sealed class DummyBluetoothManager : IBluetoothManager
    {
        public bool IsScanning { get; private set; }
        public bool IsConnected => false;
        public bool IsReady => false;
        public bool IsStreaming => false;
        public float CurrentGain { get; set; } = 1.0f;
        public BluetoothSessionState SessionState => BluetoothSessionState.Idle;
        public string ConnectedDeviceName => string.Empty;
        public Guid? ConnectedDeviceContainerId => null;

        public event Action<RemoteDevice>? DeviceDiscovered { add { } remove { } }
        public event Action<bool, string>? ConnectionStatusChanged { add { } remove { } }
        public event Action<float>? AudioLevelChanged { add { } remove { } }
        public event Action? VoiceButtonPressed { add { } remove { } }
        public event Action? VoiceSessionStarted { add { } remove { } }
        public event Action? VoiceSessionEnded { add { } remove { } }

        public void StartScanning() => IsScanning = true;
        public void StopScanning() => IsScanning = false;
        public Task DiscoverPairedRemotesAsync() => Task.CompletedTask;
        public void StartScan() => StartScanning();
        public void StopScan() => StopScanning();
        public Task<bool> ConnectAsync(ulong bluetoothAddress) => Task.FromResult(true);
        public void Disconnect() { }
        public Task<bool> StartVoiceSessionAsync() => Task.FromResult(true);
        public Task<bool> StopVoiceSessionAsync() => Task.FromResult(true);
        public void Dispose() { }
    }

    [Theory]
    [InlineData(1.0f, "+0 dB")]
    [InlineData(2.0f, "+6 dB")]
    [InlineData(4.0f, "+12 dB")]
    [InlineData(10.0f, "+20 dB")]
    [InlineData(0.5f, "-6 dB")]
    public void GainToDbTextConverter_FormatsDbCorrectly(float gain, string expected)
    {
        var converter = new GainToDbTextConverter();
        var result = converter.Convert(gain, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IntegrationsViewModel_ApplyPreset_UpdatesShortcutAndMode()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vib-int-{Guid.NewGuid():N}.json");
        var config = new ConfigService(path);
        var vm = new IntegrationsViewModel(config);

        // Apply WeChat
        vm.ApplyPreset("WeChatInput");
        Assert.Equal(TargetInputTool.WeChatInput, vm.SelectedTool);
        Assert.Equal("Ctrl+F5", vm.ShortcutText);
        Assert.Equal(VoiceTriggerMode.HoldToTalk, vm.TriggerMode);
        Assert.True(vm.IsHoldToTalk);
        Assert.False(vm.IsClickToToggle);
        Assert.Equal("微信输入法", vm.CurrentToolDisplayName);

        // Apply Windows Dictation
        vm.ApplyPreset("WindowsDictation");
        Assert.Equal(TargetInputTool.WindowsDictation, vm.SelectedTool);
        Assert.Equal("Win+H", vm.ShortcutText);
        Assert.Equal(VoiceTriggerMode.ClickToToggle, vm.TriggerMode);
        Assert.False(vm.IsHoldToTalk);
        Assert.True(vm.IsClickToToggle);
        Assert.Equal("Windows 语音听写 (Win+H)", vm.CurrentToolDisplayName);

        // Apply Doubao
        vm.ApplyPreset("DoubaoInput");
        Assert.Equal(TargetInputTool.DoubaoInput, vm.SelectedTool);
        Assert.Equal("F1", vm.ShortcutText);
        Assert.Equal(VoiceTriggerMode.HoldToTalk, vm.TriggerMode);

        // Save
        vm.Save();
        Assert.True(vm.SaveNoticeVisible);

        var reloaded = new ConfigService(path);
        Assert.Equal(TargetInputTool.DoubaoInput, reloaded.Current.InputTool.Tool);
        Assert.Equal("F1", reloaded.Current.InputTool.Shortcut);
    }

    [Fact]
    public void MappingViewModel_SelectButtonAndClear_OperatesCorrectly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vib-map-{Guid.NewGuid():N}.json");
        var config = new ConfigService(path);
        var dev = new RemoteDevice { Id = "test_dev", ModelType = RemoteModelType.XiaomiRC003 };
        config.Current.Devices.Add(dev);
        config.Current.SelectedDeviceId = dev.Id;

        var vm = new MappingViewModel(config);
        Assert.NotNull(vm.SelectedDevice);

        // Select Up button
        vm.SelectButton("Up");
        Assert.NotNull(vm.SelectedMapping);
        Assert.Equal(RemoteButton.Up, vm.SelectedMapping.Button);

        // Select Home button
        vm.SelectButton("Home");
        Assert.NotNull(vm.SelectedMapping);
        Assert.Equal(RemoteButton.Home, vm.SelectedMapping.Button);

        // Clear Home button
        vm.ClearSelectedMapping();
        Assert.Equal(ButtonActionType.None, vm.SelectedMapping.ClickAction);
        Assert.Empty(vm.SelectedMapping.ParameterText);

        // Reset to Defaults
        vm.ResetToDefaults();
        vm.SelectButton("Home");
        Assert.Equal(ButtonActionType.KeyboardShortcut, vm.SelectedMapping.ClickAction);
        Assert.Equal("Alt+Tab", vm.SelectedMapping.ParameterText);
    }

    [Fact]
    public void DevicesViewModel_RemoveDevice_RemovesFromConfigAndList()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vib-dev-{Guid.NewGuid():N}.json");
        var config = new ConfigService(path);
        var dev1 = new RemoteDevice { Id = "dev1", BluetoothAddress = 1, Name = "Remote 1" };
        var dev2 = new RemoteDevice { Id = "dev2", BluetoothAddress = 2, Name = "Remote 2" };
        config.Current.Devices.AddRange([dev1, dev2]);
        config.Current.SelectedDeviceId = dev1.Id;

        var dummyBt = new DummyBluetoothManager();
        var vm = new DevicesViewModel(config, dummyBt);

        Assert.Equal(2, vm.DiscoveredDevices.Count);

        vm.RemoveDevice(dev1);

        Assert.Single(vm.DiscoveredDevices);
        Assert.Equal("dev2", vm.DiscoveredDevices[0].Id);
        Assert.Single(config.Current.Devices);
        Assert.Null(config.Current.SelectedDeviceId);
    }
}
