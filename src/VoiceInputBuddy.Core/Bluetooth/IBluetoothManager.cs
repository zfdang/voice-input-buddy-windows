// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using VoiceInputBuddy.Core.Models;

namespace VoiceInputBuddy.Core.Bluetooth;

public enum BluetoothSessionState
{
    Idle = 0,
    Opening,
    Streaming,
    Draining
}

public interface IBluetoothManager : IDisposable
{
    bool IsScanning { get; }
    bool IsConnected { get; }
    bool IsReady { get; }
    bool IsStreaming { get; }
    BluetoothSessionState SessionState { get; }
    string ConnectedDeviceName { get; }
    Guid? ConnectedDeviceContainerId { get; }

    event Action<RemoteDevice>? DeviceDiscovered;
    event Action<bool, string>? ConnectionStatusChanged;
    event Action<float>? AudioLevelChanged;
    event Action? VoiceButtonPressed;
    event Action? VoiceSessionStarted;
    event Action? VoiceSessionEnded;

    void StartScanning();
    void StopScanning();
    Task DiscoverPairedRemotesAsync();
    void StartScan();
    void StopScan();
    Task<bool> ConnectAsync(ulong bluetoothAddress);
    void Disconnect();
    Task<bool> StartVoiceSessionAsync();
    Task<bool> StopVoiceSessionAsync();
}
