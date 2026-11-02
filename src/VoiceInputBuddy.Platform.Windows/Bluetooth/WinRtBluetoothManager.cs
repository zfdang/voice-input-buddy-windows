// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;
using VoiceInputBuddy.Core.Audio;
using VoiceInputBuddy.Core.Bluetooth;
using VoiceInputBuddy.Core.Diagnostics;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Core.Protocols;
using VoiceInputBuddy.Platform.Windows.Audio;

namespace VoiceInputBuddy.Platform.Windows.Bluetooth;

public sealed class WinRtBluetoothManager : IBluetoothManager
{
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private BluetoothLEDevice? _connectedDevice;
    private GattDeviceService? _gattService;
    private GattCharacteristic? _txCharacteristic;
    private GattCharacteristic? _audioCharacteristic;
    private GattCharacteristic? _controlCharacteristic;

    private readonly ImaAdpcmDecoder _decoder = new();
    private readonly WasapiAudioPlayer _audioPlayer = new();
    private readonly ATVVFrameAccumulator _frameAccumulator = new();
    private readonly object _audioLock = new();
    private short[] _decodedBuffer = new short[1024];

    private byte _currentSessionId = 0;
    private byte? _lastEndedSessionId;
    private long _lastEndedSessionTimeMs;
    private int _frameSize = 120;
    private BluetoothSessionState _sessionState = BluetoothSessionState.Idle;
    private bool _cancelOpenPending = false;
    private bool _voiceSessionStartedEmitted = false;
    private bool _audioStopReceived = false;
    private int _connectionGeneration = 0;
    private int _sessionGeneration = 0;
    private int _audioGeneration = 0;
    private int _activeOpeningOperationId = 0;
    private TaskCompletionSource<bool>? _capabilitiesTcs;
    private TaskCompletionSource<bool>? _audioStopTcs;
    private System.Threading.Timer? _keepaliveTimer;
    private readonly object _stateLock = new();

    public event Action<RemoteDevice>? DeviceDiscovered;
    public event Action<bool, string>? ConnectionStatusChanged;
    public event Action<float>? AudioLevelChanged;
    public event Action? VoiceSessionStarted;
    public event Action? VoiceSessionEnded;
    public event Action? VoiceButtonPressed;

    public bool IsScanning => _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started;
    public bool IsConnected => _connectedDevice != null && _connectedDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
    public bool IsReady { get; private set; }
    public bool IsStreaming => _sessionState == BluetoothSessionState.Streaming;
    public BluetoothSessionState SessionState => _sessionState;
    public string ConnectedDeviceName => _connectedDevice?.Name ?? string.Empty;
    public Guid? ConnectedDeviceContainerId { get; private set; }

    public void StartScan() => StartScanning();
    public void StopScan() => StopScanning();

    public WinRtBluetoothManager()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        _watcher.Received += OnAdvertisementReceived;
    }

    public void StartScanning()
    {
        try
        {
            AppLogger.Info("Starting Bluetooth LE advertisement scan...", "BluetoothManager");
            _watcher.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to start Bluetooth LE scan", ex, "BluetoothManager");
        }
    }

    public async Task DiscoverPairedRemotesAsync()
    {
        try
        {
            var devices = await global::Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
            foreach (var info in devices)
            {
                if (!XiaomiProtocol.IsCandidate(info.Name) && !G9N9NProtocol.IsCandidate(info.Name)) continue;
                using var device = await BluetoothLEDevice.FromIdAsync(info.Id);
                if (device == null) continue;
                AppLogger.Info($"Found paired remote: '{info.Name}' (0x{device.BluetoothAddress:X12}), OS status={device.ConnectionStatus}", "BluetoothManager");
                DeviceDiscovered?.Invoke(new RemoteDevice
                {
                    Id = device.BluetoothAddress.ToString("X"),
                    BluetoothAddress = device.BluetoothAddress,
                    Name = info.Name,
                    ModelType = XiaomiProtocol.DetectModelType(info.Name)
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to enumerate paired remotes", ex, "BluetoothManager");
        }
    }

    public void StopScanning()
    {
        try
        {
            AppLogger.Info("Stopping Bluetooth LE advertisement scan...", "BluetoothManager");
            _watcher.Stop();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to stop Bluetooth LE scan", ex, "BluetoothManager");
        }
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string name = args.Advertisement.LocalName;
        if (string.IsNullOrWhiteSpace(name)) return;

        if (XiaomiProtocol.IsCandidate(name) || G9N9NProtocol.IsCandidate(name))
        {
            AppLogger.Info($"Discovered candidate remote: '{name}' (Address: 0x{args.BluetoothAddress:X12}, RSSI: {args.RawSignalStrengthInDBm} dBm)", "BluetoothManager");
            var device = new RemoteDevice
            {
                Id = args.BluetoothAddress.ToString("X"),
                BluetoothAddress = args.BluetoothAddress,
                Name = name,
                ModelType = XiaomiProtocol.DetectModelType(name),
                IsConnected = false
            };
            DeviceDiscovered?.Invoke(device);
        }
    }

    public async Task<bool> ConnectAsync(ulong bluetoothAddress)
    {
        AppLogger.Info($"ConnectAsync initiated for BluetoothAddress: 0x{bluetoothAddress:X12}", "BluetoothManager");
        Disconnect();

        int targetConnGen;
        lock (_stateLock)
        {
            targetConnGen = ++_connectionGeneration;
            _activeOpeningOperationId++;
        }

        void CleanupAttempt(BluetoothLEDevice? localDev)
        {
            localDev?.Dispose();
            lock (_stateLock)
            {
                if (_gattService != null)
                {
                    try { _gattService.Dispose(); } catch { }
                    _gattService = null;
                }

                if (targetConnGen == _connectionGeneration)
                {
                    Disconnect();
                }
            }
        }

        try
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device?.Dispose();
                    return false;
                }

                if (device == null)
                {
                    AppLogger.Error($"BluetoothLEDevice.FromBluetoothAddressAsync returned null for 0x{bluetoothAddress:X12}", component: "BluetoothManager");
                    CleanupAttempt(null);
                    ConnectionStatusChanged?.Invoke(false, "无法获取蓝牙设备");
                    return false;
                }

                _connectedDevice = device;
                _connectedDevice.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;
            }

            AppLogger.Info($"Device object obtained: '{device.Name}' ({device.DeviceId}), status: {device.ConnectionStatus}", "BluetoothManager");

            // Discover Service
            var servicesResult = await device.GetGattServicesForUuidAsync(ATVVProtocol.ServiceUuid);
            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }

                if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                {
                    AppLogger.Error($"Failed to discover ATVV GATT service {ATVVProtocol.ServiceUuid}: Status={servicesResult.Status}, Count={servicesResult.Services.Count}", component: "BluetoothManager");
                    CleanupAttempt(device);
                    ConnectionStatusChanged?.Invoke(false, "未找到语音服务 (ATVV)");
                    return false;
                }
            }

            var service = servicesResult.Services[0];
            var charsResult = await service.GetCharacteristicsAsync();
            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }

                if (charsResult.Status != GattCommunicationStatus.Success)
                {
                    AppLogger.Error($"Failed to discover ATVV characteristics: Status={charsResult.Status}", component: "BluetoothManager");
                    CleanupAttempt(device);
                    ConnectionStatusChanged?.Invoke(false, "获取服务特征失败");
                    return false;
                }
            }

            GattCharacteristic? tx = null;
            GattCharacteristic? audio = null;
            GattCharacteristic? control = null;

            foreach (var ch in charsResult.Characteristics)
            {
                if (ch.Uuid == ATVVProtocol.TransmitUuid) tx = ch;
                else if (ch.Uuid == ATVVProtocol.AudioUuid) audio = ch;
                else if (ch.Uuid == ATVVProtocol.ControlUuid) control = ch;
            }

            if (tx == null || audio == null || control == null)
            {
                AppLogger.Error($"Missing required ATVV characteristics: TX={(tx != null)}, Audio={(audio != null)}, Control={(control != null)}", component: "BluetoothManager");
                lock (_stateLock)
                {
                    if (targetConnGen == _connectionGeneration)
                    {
                        CleanupAttempt(device);
                        ConnectionStatusChanged?.Invoke(false, "缺少必要的 ATVV 特征");
                    }
                    else
                    {
                        device.Dispose();
                    }
                }
                return false;
            }

            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    service.Dispose();
                    device.Dispose();
                    return false;
                }

                _gattService = service;
                _txCharacteristic = tx;
                _audioCharacteristic = audio;
                _controlCharacteristic = control;
                _decoder.LowNibbleFirst = device.Name.Contains("arn9", StringComparison.OrdinalIgnoreCase);
            }

            // Subscribe notifications
            var audioStatus = await audio.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }

                if (audioStatus != GattCommunicationStatus.Success)
                {
                    AppLogger.Error($"Failed to subscribe to Audio notifications: Status={audioStatus}", component: "BluetoothManager");
                    CleanupAttempt(device);
                    ConnectionStatusChanged?.Invoke(false, "订阅音频通知失败");
                    return false;
                }
            }
            audio.ValueChanged += OnAudioValueChanged;

            var controlStatus = await control.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }

                if (controlStatus != GattCommunicationStatus.Success)
                {
                    AppLogger.Error($"Failed to subscribe to Control notifications: Status={controlStatus}", component: "BluetoothManager");
                    CleanupAttempt(device);
                    ConnectionStatusChanged?.Invoke(false, "订阅控制通知失败");
                    return false;
                }
            }
            control.ValueChanged += OnControlValueChanged;

            // Negotiate capabilities before declaring ready
            _capabilitiesTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            byte[] getCaps = ATVVProtocol.BuildGetCapabilities();
            var buffer = CryptographicBuffer.CreateFromByteArray(getCaps);
            var writeStatus = await tx.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);
            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }

                if (writeStatus != GattCommunicationStatus.Success)
                {
                    AppLogger.Error($"Failed to write ATVV GetCapabilities command: Status={writeStatus}", component: "BluetoothManager");
                    CleanupAttempt(device);
                    ConnectionStatusChanged?.Invoke(false, "下发能力协商命令失败");
                    return false;
                }
            }

            AppLogger.Info("ATVV GetCapabilities sent, awaiting remote response...", "BluetoothManager");
            var completedTask = await Task.WhenAny(_capabilitiesTcs.Task, Task.Delay(5000));
            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }

                if (completedTask != _capabilitiesTcs.Task || !_capabilitiesTcs.Task.Result)
                {
                    AppLogger.Warn("ATVV capabilities negotiation timed out or failed 16kHz validation", "BluetoothManager");
                    CleanupAttempt(device);
                    ConnectionStatusChanged?.Invoke(false, "能力协商超时或不支持 16kHz 编码");
                    return false;
                }
            }

            // Retrieve ContainerId for device matching
            Guid? foundContainerId = null;
            try
            {
                var devInfo = await global::Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(
                    device.DeviceId,
                    new[] { "System.Devices.Aep.ContainerId" },
                    global::Windows.Devices.Enumeration.DeviceInformationKind.AssociationEndpoint);
                if (devInfo != null && devInfo.Properties.TryGetValue("System.Devices.Aep.ContainerId", out object? cObj) && cObj is Guid guidVal && guidVal != Guid.Empty)
                {
                    foundContainerId = guidVal;
                }
            }
            catch { }

            if (!foundContainerId.HasValue)
            {
                foundContainerId = Input.RawInputReceiver.GetDeviceContainerId(device.DeviceId);
            }

            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    // Stale connection attempt superseded while querying ContainerId
                    device.Dispose();
                    return false;
                }

                ConnectedDeviceContainerId = foundContainerId;
                IsReady = true;
            }

            AppLogger.Info($"Bluetooth remote connected and ready: '{device.Name}', FrameSize={_frameSize}, ContainerId={ConnectedDeviceContainerId}", "BluetoothManager");
            ConnectionStatusChanged?.Invoke(true, device.Name);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Exception during ConnectAsync for 0x{bluetoothAddress:X12}", ex, "BluetoothManager");
            lock (_stateLock)
            {
                if (targetConnGen == _connectionGeneration)
                {
                    CleanupAttempt(null);
                    ConnectionStatusChanged?.Invoke(false, "连接异常: " + ex.Message);
                }
            }
            return false;
        }
    }

    private void OnDeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        lock (_stateLock)
        {
            if (_connectedDevice != sender) return;
        }
        bool connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
        AppLogger.Info($"Device connection status changed: {sender.Name}, connected={connected}", "BluetoothManager");
        if (!connected)
        {
            Disconnect();
            ConnectionStatusChanged?.Invoke(false, sender.Name);
        }
    }

    public async Task<bool> StartVoiceSessionAsync()
    {
        int currentConnGen;
        int currentSessGen;
        int opId;
        lock (_stateLock)
        {
            if (_sessionState != BluetoothSessionState.Idle || _txCharacteristic == null)
            {
                AppLogger.Warn($"Cannot start voice session: State={_sessionState}, TxNull={(_txCharacteristic == null)}", "BluetoothManager");
                return false;
            }

            // Output must be ready before opening mic
            if (!_audioPlayer.Start())
            {
                AppLogger.Error("Audio player failed to start output device before opening mic", component: "BluetoothManager");
                return false;
            }

            _sessionState = BluetoothSessionState.Opening;
            _cancelOpenPending = false;
            _audioStopReceived = false;
            _lastEndedSessionId = null;
            currentConnGen = _connectionGeneration;
            currentSessGen = ++_sessionGeneration;
            opId = ++_activeOpeningOperationId;
        }

        AppLogger.Info($"Starting voice session: SessionGen={currentSessGen}, OpId={opId}. State -> Opening", "BluetoothManager");

        // Reset audio pipeline outside _stateLock to prevent AB-BA deadlock
        lock (_audioLock)
        {
            _audioGeneration++;
            _frameAccumulator.Reset();
            _decoder.Reset();
        }

        try
        {
            byte[] micOpen = ATVVProtocol.BuildMicrophoneOpen();
            var buffer = CryptographicBuffer.CreateFromByteArray(micOpen);
            var writeStatus = await _txCharacteristic.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);

            if (writeStatus != GattCommunicationStatus.Success)
            {
                AppLogger.Error($"Failed to send ATVV MicrophoneOpen packet: Status={writeStatus}", component: "BluetoothManager");
                lock (_stateLock)
                {
                    if (currentConnGen == _connectionGeneration && currentSessGen == _sessionGeneration)
                    {
                        _audioPlayer.Stop();
                        _sessionState = BluetoothSessionState.Idle;
                    }
                }
                return false;
            }

            lock (_stateLock)
            {
                if (currentConnGen != _connectionGeneration || currentSessGen != _sessionGeneration || opId != _activeOpeningOperationId)
                {
                    AppLogger.Warn("MicrophoneOpen completed but operation was cancelled or superseded", "BluetoothManager");
                    if (currentConnGen == _connectionGeneration && currentSessGen == _sessionGeneration)
                    {
                        _audioPlayer.Stop();
                        _sessionState = BluetoothSessionState.Idle;
                        _ = SendClosePacketAsync(_currentSessionId);
                    }
                    return false;
                }

                if (_sessionState != BluetoothSessionState.Opening || _cancelOpenPending)
                {
                    AppLogger.Info("Fast release detected during Opening, closing mic immediately", "BluetoothManager");
                    _ = SendClosePacketAsync(_currentSessionId);
                    _audioPlayer.Stop();
                    _sessionState = BluetoothSessionState.Idle;
                    _cancelOpenPending = false;
                    return false;
                }

                _sessionState = BluetoothSessionState.Streaming;
                _voiceSessionStartedEmitted = true;
                StartKeepaliveTimer();
            }

            AppLogger.Info("Microphone opened successfully. State -> Streaming", "BluetoothManager");
            VoiceSessionStarted?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Exception while opening microphone", ex, "BluetoothManager");
            lock (_stateLock)
            {
                if (currentConnGen == _connectionGeneration && currentSessGen == _sessionGeneration)
                {
                    _audioPlayer.Stop();
                    _sessionState = BluetoothSessionState.Idle;
                }
            }
            return false;
        }
    }

    public async Task<bool> StopVoiceSessionAsync()
    {
        byte sessionIdToClose;
        int currentConnGen;
        int currentSessGen;
        bool needsAwaitStop = false;
        TaskCompletionSource<bool>? stopTcs = null;

        lock (_stateLock)
        {
            if (_sessionState == BluetoothSessionState.Opening)
            {
                AppLogger.Info("StopVoiceSession requested during Opening, cancelling opening session immediately", "BluetoothManager");
                _sessionGeneration++;
                _cancelOpenPending = true;
                _activeOpeningOperationId++;
                _audioPlayer.Stop();
                _sessionState = BluetoothSessionState.Idle;
                _ = SendClosePacketAsync(_currentSessionId);
                return true;
            }

            if (_sessionState != BluetoothSessionState.Streaming)
            {
                AppLogger.Debug($"StopVoiceSession ignored, current state is {_sessionState}", "BluetoothManager");
                return false;
            }

            _sessionState = BluetoothSessionState.Draining;
            sessionIdToClose = _currentSessionId;
            _lastEndedSessionId = sessionIdToClose;
            _lastEndedSessionTimeMs = Environment.TickCount64;
            currentConnGen = _connectionGeneration;
            currentSessGen = _sessionGeneration;
            StopKeepaliveTimer();

            if (!_audioStopReceived)
            {
                _audioStopTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                stopTcs = _audioStopTcs;
                needsAwaitStop = true;
            }
        }

        AppLogger.Info($"StopVoiceSession initiated for SessionId={sessionIdToClose}. State -> Draining", "BluetoothManager");

        // Send 0x0D MicClose
        await SendClosePacketAsync(sessionIdToClose);

        // Await AUDIO_STOP (0x00) or bounded tail timeout (250ms)
        if (needsAwaitStop && stopTcs != null)
        {
            await Task.WhenAny(stopTcs.Task, Task.Delay(250)).ConfigureAwait(false);
        }

        // Drain audio player asynchronously OUTSIDE _stateLock to prevent blocking other threads
        await _audioPlayer.DrainAsync(50).ConfigureAwait(false);

        bool emitEnded = false;
        lock (_stateLock)
        {
            if (currentConnGen != _connectionGeneration || currentSessGen != _sessionGeneration)
            {
                AppLogger.Warn("StopVoiceSession completed but session was superseded", "BluetoothManager");
                return false;
            }

            _audioPlayer.Stop();
            _sessionState = BluetoothSessionState.Idle;
            if (_voiceSessionStartedEmitted)
            {
                emitEnded = true;
                _voiceSessionStartedEmitted = false;
            }
        }

        AppLogger.Info("Voice session closed and audio stopped. State -> Idle", "BluetoothManager");
        if (emitEnded)
        {
            VoiceSessionEnded?.Invoke();
        }
        return true;
    }

    private async Task SendClosePacketAsync(byte sessionId)
    {
        if (_txCharacteristic == null) return;
        try
        {
            byte[] micClose = ATVVProtocol.BuildMicrophoneClose(sessionId);
            var buffer = CryptographicBuffer.CreateFromByteArray(micClose);
            await _txCharacteristic.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);
        }
        catch { }
    }

    private void StartKeepaliveTimer()
    {
        StopKeepaliveTimer();
        // Bind keepalive callbacks to this session and do not overlap slow GATT writes.
        int connectionGeneration = _connectionGeneration;
        int sessionGeneration = _sessionGeneration;
        int writePending = 0;
        _keepaliveTimer = new System.Threading.Timer(async _ =>
        {
            if (Interlocked.Exchange(ref writePending, 1) != 0) return;
            try
            {
                GattCharacteristic tx;
                byte sessId;
                lock (_stateLock)
                {
                    if (connectionGeneration != _connectionGeneration || sessionGeneration != _sessionGeneration ||
                        _sessionState != BluetoothSessionState.Streaming || _txCharacteristic == null) return;
                    tx = _txCharacteristic;
                    sessId = _currentSessionId;
                }
                byte[] extend = ATVVProtocol.BuildMicrophoneExtend(sessId);
                var buffer = CryptographicBuffer.CreateFromByteArray(extend);
                var status = await tx.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);
                AppLogger.Debug($"MIC_EXTEND: SessionId={sessId}, Status={status}", "BluetoothManager");
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Keepalive extend failed: {ex.Message}", "BluetoothManager");
            }
            finally
            {
                Volatile.Write(ref writePending, 0);
            }
        }, null, 8000, 8000);
    }

    private void StopKeepaliveTimer()
    {
        _keepaliveTimer?.Dispose();
        _keepaliveTimer = null;
    }

    private void OnAudioValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        int currentAudioGen = Volatile.Read(ref _audioGeneration);
        int frameSize;
        lock (_stateLock)
        {
            if (sender != _audioCharacteristic) return;
            if (_sessionState != BluetoothSessionState.Streaming && _sessionState != BluetoothSessionState.Draining)
            {
                return;
            }
            frameSize = _frameSize;
        }

        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
        if (data.Length == 0) return;

        // Process audio in _audioLock ONLY (never nest _stateLock inside to prevent deadlock)
        lock (_audioLock)
        {
            if (currentAudioGen != Volatile.Read(ref _audioGeneration))
            {
                return;
            }

            var frames = _frameAccumulator.Append(data, frameSize);
            foreach (var frame in frames)
            {
                if (_frameAccumulator.TryConsumeSyncForNextFrame(out short predictor, out byte stepIndex))
                {
                    _decoder.Synchronize(predictor, stepIndex);
                }

                int requiredSamples = frame.Length * 2;
                if (_decodedBuffer.Length < requiredSamples)
                {
                    Array.Resize(ref _decodedBuffer, Math.Max(requiredSamples, _decodedBuffer.Length * 2));
                }

                int count = _decoder.Decode(frame, _decodedBuffer);
                var sampleSpan = _decodedBuffer.AsSpan(0, count);

                _audioPlayer.WriteSamples(sampleSpan);

                float level = AudioLevelMeter.CalculateRms(sampleSpan);
                AudioLevelChanged?.Invoke(level);
            }
        }
    }

    private void OnControlValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        int capturedConnGen;
        int capturedAudioGen;
        lock (_stateLock)
        {
            if (sender != _controlCharacteristic) return;
            capturedConnGen = _connectionGeneration;
            capturedAudioGen = _audioGeneration;
        }

        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
        if (data.Length == 0) return;

        byte opcode = data[0];
        switch (opcode)
        {
            case ATVVProtocol.OpcodeCapabilitiesResp: // 0x0B
                lock (_stateLock)
                {
                    if (capturedConnGen != _connectionGeneration) return;
                }
                var caps = ATVVProtocol.ParseCapabilities(data);
                AppLogger.Info($"ATVV response CapabilitiesResp (0x0B): Version=0x{caps?.Version:X4}, Codecs=0x{caps?.Codecs:X2}, FrameSize={caps?.FrameSize}", "BluetoothManager");
                if (caps != null && caps.SampleRate == 16000 && (caps.Codecs & 0x02) != 0)
                {
                    lock (_stateLock)
                    {
                        if (capturedConnGen != _connectionGeneration) return;
                        _frameSize = caps.FrameSize > 0 ? caps.FrameSize : 120;
                    }
                    _capabilitiesTcs?.TrySetResult(true);
                }
                else
                {
                    _capabilitiesTcs?.TrySetResult(false);
                }
                break;

            case ATVVProtocol.OpcodeAudioStart: // 0x04
                if (ATVVProtocol.ParseAudioStart(data, out _, out byte codec, out byte sessionId))
                {
                    AppLogger.Info($"ATVV packet AudioStart (0x04): Codec=0x{codec:X2}, SessionId={sessionId}", "BluetoothManager");
                    lock (_stateLock)
                    {
                        if (capturedConnGen != _connectionGeneration) return;
                    }

                    // Reject non-16kHz codec (e.g. 8kHz)
                    if (codec != 0x02)
                    {
                        AppLogger.Warn($"Rejecting non-16kHz codec (0x{codec:X2}), sending MicClose", "BluetoothManager");
                        _ = SendClosePacketAsync(sessionId);
                        bool shouldEmitEnded = false;
                        lock (_stateLock)
                        {
                            if (capturedConnGen != _connectionGeneration) return;
                            _audioPlayer.Stop();
                            _sessionState = BluetoothSessionState.Idle;
                            StopKeepaliveTimer();
                            if (_voiceSessionStartedEmitted)
                            {
                                shouldEmitEnded = true;
                                _voiceSessionStartedEmitted = false;
                            }
                        }
                        if (shouldEmitEnded)
                        {
                            VoiceSessionEnded?.Invoke();
                        }
                        return;
                    }

                    // Xiaomi remotes can send AudioStart directly, without a HID
                    // voice key or StartSearch. Adopt that stream instead of closing it.
                    bool wasCancelledOrEnded = false;
                    bool remoteInitiated = false;
                    int remoteSessionGen = 0;
                    lock (_stateLock)
                    {
                        if (capturedConnGen != _connectionGeneration) return;
                        _currentSessionId = sessionId;
                        bool isStaleDuplicateSession = _lastEndedSessionId.HasValue &&
                            _lastEndedSessionId.Value == sessionId &&
                            (Environment.TickCount64 - _lastEndedSessionTimeMs) < 500;

                        if (_cancelOpenPending || _sessionState == BluetoothSessionState.Draining ||
                            (_sessionState == BluetoothSessionState.Idle && (!IsReady || isStaleDuplicateSession)))
                        {
                            wasCancelledOrEnded = true;
                            _lastEndedSessionId = sessionId;
                            _lastEndedSessionTimeMs = Environment.TickCount64;
                        }
                        else if (_sessionState == BluetoothSessionState.Idle)
                        {
                            if (!_audioPlayer.Start())
                            {
                                wasCancelledOrEnded = true;
                                AppLogger.Error("Cannot accept remote AudioStart: audio output failed", component: "BluetoothManager");
                            }
                            else
                            {
                                _lastEndedSessionId = null;
                                _audioStopReceived = false;
                                _sessionState = BluetoothSessionState.Opening;
                                remoteSessionGen = ++_sessionGeneration;
                                _activeOpeningOperationId++;
                                remoteInitiated = true;
                            }
                        }
                    }

                    if (wasCancelledOrEnded)
                    {
                        AppLogger.Info($"AudioStart received while state={_sessionState}/cancelPending={_cancelOpenPending}, sending close", "BluetoothManager");
                        _ = SendClosePacketAsync(sessionId);
                    }
                    else if (remoteInitiated)
                    {
                        // Reset without nesting the state and audio locks.
                        lock (_audioLock)
                        {
                            if (capturedConnGen != Volatile.Read(ref _connectionGeneration) ||
                                remoteSessionGen != Volatile.Read(ref _sessionGeneration)) return;
                            _audioGeneration++;
                            _frameAccumulator.Reset();
                            _decoder.Reset();
                        }
                        lock (_stateLock)
                        {
                            if (capturedConnGen != _connectionGeneration || remoteSessionGen != _sessionGeneration ||
                                _cancelOpenPending || _sessionState != BluetoothSessionState.Opening)
                                return;
                            _sessionState = BluetoothSessionState.Streaming;
                            _voiceSessionStartedEmitted = true;
                            StartKeepaliveTimer();
                        }
                        AppLogger.Info($"Accepted remote-initiated voice session {sessionId}. State -> Streaming", "BluetoothManager");
                        VoiceButtonPressed?.Invoke();
                        VoiceSessionStarted?.Invoke();
                    }
                }
                break;

            case ATVVProtocol.OpcodeMicrophoneRefused: // 0x0C
                AppLogger.Warn("ATVV packet MicrophoneRefused (0x0C) received from remote", "BluetoothManager");
                bool emitRefusedEnded = false;
                lock (_stateLock)
                {
                    if (capturedConnGen != _connectionGeneration) return;
                    _activeOpeningOperationId++;
                    if (_sessionState == BluetoothSessionState.Opening || _sessionState == BluetoothSessionState.Streaming)
                    {
                        StopKeepaliveTimer();
                        _audioPlayer.Stop();
                        _sessionState = BluetoothSessionState.Idle;
                        if (_voiceSessionStartedEmitted)
                        {
                            emitRefusedEnded = true;
                            _voiceSessionStartedEmitted = false;
                        }
                    }
                }
                if (emitRefusedEnded)
                {
                    VoiceSessionEnded?.Invoke();
                }
                break;

            case ATVVProtocol.OpcodeAudioSync: // 0x0A
                if (ATVVProtocol.ParseAudioSync(data, out short predictor, out byte stepIndex))
                {
                    AppLogger.Debug($"ATVV packet AudioSync (0x0A): Predictor={predictor}, StepIndex={stepIndex}", "BluetoothManager");
                    lock (_audioLock)
                    {
                        if (capturedAudioGen != _audioGeneration) return;
                        _frameAccumulator.Synchronize(predictor, stepIndex);
                    }
                }
                break;

            case ATVVProtocol.OpcodeStartSearch: // 0x08
                AppLogger.Info("ATVV packet StartSearch (0x08) received from remote (voice key pressed)", "BluetoothManager");
                lock (_stateLock)
                {
                    if (capturedConnGen != _connectionGeneration) return;
                }
                VoiceButtonPressed?.Invoke();
                _ = StartVoiceSessionAsync();
                break;

            case ATVVProtocol.OpcodeAudioStop: // 0x00
                AppLogger.Info($"ATVV AudioStop received: Reason={(data.Length > 1 ? $"0x{data[1]:X2}" : "missing")}, " +
                    $"State={_sessionState}, SessionId={_currentSessionId}", "BluetoothManager");
                lock (_stateLock)
                {
                    if (capturedConnGen != _connectionGeneration) return;
                    if (_sessionState == BluetoothSessionState.Idle) _cancelOpenPending = false;
                    _audioStopReceived = true;
                }
                _audioStopTcs?.TrySetResult(true);
                if (_sessionState == BluetoothSessionState.Streaming || _sessionState == BluetoothSessionState.Opening)
                {
                    _ = StopVoiceSessionAsync();
                }
                break;
        }
    }

    public void Disconnect()
    {
        AppLogger.Info("Disconnect initiated: cleaning up Bluetooth connection and audio resources", "BluetoothManager");
        bool emitEnded = false;
        BluetoothLEDevice? devToDispose = null;
        lock (_stateLock)
        {
            _connectionGeneration++;
            _sessionGeneration++;
            _activeOpeningOperationId++;
            if (_voiceSessionStartedEmitted)
            {
                emitEnded = true;
                _voiceSessionStartedEmitted = false;
            }
            _sessionState = BluetoothSessionState.Idle;
            _cancelOpenPending = false;
            StopKeepaliveTimer();
            _audioPlayer.Stop();

            if (_connectedDevice != null)
            {
                devToDispose = _connectedDevice;
                _connectedDevice.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
                _connectedDevice = null;
            }

            if (_audioCharacteristic != null)
            {
                _audioCharacteristic.ValueChanged -= OnAudioValueChanged;
                _audioCharacteristic = null;
            }

            if (_controlCharacteristic != null)
            {
                _controlCharacteristic.ValueChanged -= OnControlValueChanged;
                _controlCharacteristic = null;
            }

            if (_gattService != null)
            {
                try { _gattService.Dispose(); } catch { }
                _gattService = null;
            }

            _txCharacteristic = null;
            IsReady = false;
            _lastEndedSessionId = null;
            _audioStopReceived = false;
            ConnectedDeviceContainerId = null;
            _capabilitiesTcs?.TrySetCanceled();
            _audioStopTcs?.TrySetCanceled();
        }

        // Reset audio pipeline outside _stateLock to prevent AB-BA deadlock
        lock (_audioLock)
        {
            _audioGeneration++;
            _frameAccumulator.Reset();
            _decoder.Reset();
        }

        if (emitEnded)
        {
            VoiceSessionEnded?.Invoke();
        }

        devToDispose?.Dispose();
    }

    public void Dispose()
    {
        StopScanning();
        Disconnect();
    }
}
