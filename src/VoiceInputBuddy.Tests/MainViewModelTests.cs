// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using VoiceInputBuddy.App.ViewModels;
using VoiceInputBuddy.Core.Bluetooth;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Models;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class MainViewModelTests
{
    private sealed class TestBluetoothManager : IBluetoothManager
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

    [Fact]
    public void MainViewModel_InitialState_LoadsConfigAndDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vib-mvm-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigService(path);
            var bt = new TestBluetoothManager();
            using var vm = new MainViewModel(config, bt);

            Assert.Equal("Devices", vm.ActivePageName);
            Assert.False(vm.IsConnected);
            Assert.Equal("未连接设备", vm.ConnectedDeviceName);
            Assert.False(vm.IsVoiceActive);
            Assert.Equal(0f, vm.CurrentAudioLevel);
            Assert.NotNull(vm.DevicesVm);
            Assert.NotNull(vm.MappingVm);
            Assert.NotNull(vm.IntegrationsVm);
            Assert.NotNull(vm.EnvironmentVm);
            Assert.NotNull(vm.SettingsVm);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MainViewModel_Navigate_UpdatesActivePageName()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vib-mvm-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigService(path);
            var bt = new TestBluetoothManager();
            using var vm = new MainViewModel(config, bt);

            vm.Navigate("Mapping");
            Assert.Equal("Mapping", vm.ActivePageName);

            vm.Navigate("Integrations");
            Assert.Equal("Integrations", vm.ActivePageName);

            vm.Navigate("Settings");
            Assert.Equal("Settings", vm.ActivePageName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MainViewModel_HandleDeviceRemoval_DoesNotThrow()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vib-mvm-{Guid.NewGuid():N}.json");
        try
        {
            var config = new ConfigService(path);
            var bt = new TestBluetoothManager();
            using var vm = new MainViewModel(config, bt);

            vm.HandleDeviceRemoval(new IntPtr(0x1234));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
