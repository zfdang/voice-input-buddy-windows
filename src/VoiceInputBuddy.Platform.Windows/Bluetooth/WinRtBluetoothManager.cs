using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;
using VoiceInputBuddy.Core.Audio;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Core.Protocols;
using VoiceInputBuddy.Platform.Windows.Audio;

namespace VoiceInputBuddy.Platform.Windows.Bluetooth;

public enum BluetoothSessionState
{
    Idle,
    Opening,
    Streaming,
    Draining
}

public sealed class WinRtBluetoothManager : IDisposable
{
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private BluetoothLEDevice? _connectedDevice;
    private GattCharacteristic? _txCharacteristic;
    private GattCharacteristic? _audioCharacteristic;
    private GattCharacteristic? _controlCharacteristic;

    private readonly ImaAdpcmDecoder _decoder = new();
    private readonly AudioRingBuffer _ringBuffer = new();
    private readonly WasapiAudioPlayer _audioPlayer = new();
    private readonly ATVVFrameAccumulator _frameAccumulator = new();
    private readonly object _audioLock = new();

    private byte _currentSessionId = 0;
    private int _frameSize = 120;
    private BluetoothSessionState _sessionState = BluetoothSessionState.Idle;
    private bool _cancelOpenPending = false;
    private bool _voiceSessionStartedEmitted = false;
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

    public bool IsConnected => _connectedDevice != null && _connectedDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
    public bool IsStreaming => _sessionState == BluetoothSessionState.Streaming;
    public BluetoothSessionState SessionState => _sessionState;
    public Guid? ConnectedDeviceContainerId { get; private set; }

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
            _watcher.Start();
        }
        catch { }
    }

    public void StopScanning()
    {
        try
        {
            _watcher.Stop();
        }
        catch { }
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string name = args.Advertisement.LocalName;
        if (string.IsNullOrWhiteSpace(name)) return;

        if (XiaomiProtocol.IsCandidate(name) || G9N9NProtocol.IsCandidate(name))
        {
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
                if (targetConnGen == _connectionGeneration)
                {
                    Disconnect();
                }
            }
        }

        try
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
            if (device == null)
            {
                CleanupAttempt(null);
                return false;
            }

            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }
                _connectedDevice = device;
                _connectedDevice.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;
            }

            // Discover Service
            var servicesResult = await device.GetGattServicesForUuidAsync(ATVVProtocol.ServiceUuid);
            if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            {
                CleanupAttempt(device);
                return false;
            }

            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }
            }

            var service = servicesResult.Services[0];
            var charsResult = await service.GetCharacteristicsAsync();
            if (charsResult.Status != GattCommunicationStatus.Success)
            {
                CleanupAttempt(device);
                return false;
            }

            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
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
                CleanupAttempt(device);
                return false;
            }

            _txCharacteristic = tx;
            _audioCharacteristic = audio;
            _controlCharacteristic = control;

            // Subscribe notifications
            var audioStatus = await audio.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (audioStatus != GattCommunicationStatus.Success)
            {
                CleanupAttempt(device);
                return false;
            }

            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }
            }
            audio.ValueChanged += OnAudioValueChanged;

            var controlStatus = await control.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (controlStatus != GattCommunicationStatus.Success)
            {
                CleanupAttempt(device);
                return false;
            }

            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }
            }
            control.ValueChanged += OnControlValueChanged;

            // Negotiate capabilities before declaring ready
            _capabilitiesTcs = new TaskCompletionSource<bool>();
            byte[] getCaps = ATVVProtocol.BuildGetCapabilities();
            var buffer = CryptographicBuffer.CreateFromByteArray(getCaps);
            var writeStatus = await tx.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);
            if (writeStatus != GattCommunicationStatus.Success)
            {
                CleanupAttempt(device);
                ConnectionStatusChanged?.Invoke(false, "下发能力协商命令失败");
                return false;
            }

            var completedTask = await Task.WhenAny(_capabilitiesTcs.Task, Task.Delay(5000));
            if (completedTask != _capabilitiesTcs.Task || !await _capabilitiesTcs.Task)
            {
                CleanupAttempt(device);
                ConnectionStatusChanged?.Invoke(false, "能力协商超时或不支持 16kHz 编码");
                return false;
            }

            lock (_stateLock)
            {
                if (targetConnGen != _connectionGeneration)
                {
                    device.Dispose();
                    return false;
                }
            }

            // Retrieve ContainerId for device matching
            try
            {
                var devInfo = await global::Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(
                    device.DeviceId,
                    new[] { "System.Devices.ContainerId" });
                if (devInfo != null && devInfo.Properties.TryGetValue("System.Devices.ContainerId", out object? cObj) && cObj is Guid guidVal)
                {
                    ConnectedDeviceContainerId = guidVal;
                }
            }
            catch { }

            if (ConnectedDeviceContainerId == null)
            {
                ConnectedDeviceContainerId = Input.RawInputReceiver.GetDeviceContainerId(device.DeviceId);
            }

            ConnectionStatusChanged?.Invoke(true, device.Name);
            return true;
        }
        catch
        {
            CleanupAttempt(null);
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
                return false;
            }

            // Output must be ready before opening mic
            if (!_audioPlayer.Start())
            {
                return false;
            }

            _sessionState = BluetoothSessionState.Opening;
            _cancelOpenPending = false;
            _ringBuffer.Clear();
            currentConnGen = _connectionGeneration;
            currentSessGen = ++_sessionGeneration;
            opId = ++_activeOpeningOperationId;
        }

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
                lock (_stateLock)
                {
                    if (currentConnGen == _connectionGeneration && currentSessGen == _sessionGeneration && opId == _activeOpeningOperationId)
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
                    // Stale operation: cancelled, disconnected, or superseded.
                    return false;
                }

                if (_sessionState != BluetoothSessionState.Opening || _cancelOpenPending)
                {
                    // Fast release occurred during await on this SAME session
                    _ = SendClosePacketAsync(_currentSessionId);
                    _audioPlayer.Stop();
                    _sessionState = BluetoothSessionState.Idle;
                    return false;
                }

                _sessionState = BluetoothSessionState.Streaming;
                _voiceSessionStartedEmitted = true;
                StartKeepaliveTimer();
            }

            VoiceSessionStarted?.Invoke();
            return true;
        }
        catch
        {
            lock (_stateLock)
            {
                if (currentConnGen == _connectionGeneration && currentSessGen == _sessionGeneration && opId == _activeOpeningOperationId)
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
        lock (_stateLock)
        {
            if (_sessionState == BluetoothSessionState.Opening)
            {
                _cancelOpenPending = true;
                _activeOpeningOperationId++;
                return true;
            }

            if (_sessionState != BluetoothSessionState.Streaming)
            {
                return false;
            }

            _sessionState = BluetoothSessionState.Draining;
            sessionIdToClose = _currentSessionId;
            currentConnGen = _connectionGeneration;
            currentSessGen = _sessionGeneration;
            StopKeepaliveTimer();
            _audioStopTcs = new TaskCompletionSource<bool>();
        }

        // Send 0x0D MicClose
        await SendClosePacketAsync(sessionIdToClose);

        // Await AUDIO_STOP (0x00) or bounded tail timeout (250ms)
        if (_audioStopTcs != null)
        {
            await Task.WhenAny(_audioStopTcs.Task, Task.Delay(250));
        }

        bool emitEnded = false;
        lock (_stateLock)
        {
            if (currentConnGen != _connectionGeneration || currentSessGen != _sessionGeneration)
            {
                // Stale stopping operation: a newer session or disconnect has taken over.
                return false;
            }

            // Drain audio player
            _audioPlayer.Drain(50);
            _audioPlayer.Stop();

            _sessionState = BluetoothSessionState.Idle;
            if (_voiceSessionStartedEmitted)
            {
                emitEnded = true;
                _voiceSessionStartedEmitted = false;
            }
        }

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
        // Periodically send 0x0E MIC_EXTEND every 8 seconds
        _keepaliveTimer = new System.Threading.Timer(async _ =>
        {
            if (_sessionState == BluetoothSessionState.Streaming && _txCharacteristic != null)
            {
                try
                {
                    byte[] extend = ATVVProtocol.BuildMicrophoneExtend(_currentSessionId);
                    var buffer = CryptographicBuffer.CreateFromByteArray(extend);
                    await _txCharacteristic.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);
                }
                catch { }
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
        int currentAudioGen;
        int frameSize;
        lock (_stateLock)
        {
            if (sender != _audioCharacteristic) return;
            if (_sessionState != BluetoothSessionState.Streaming && _sessionState != BluetoothSessionState.Draining)
            {
                return;
            }
            currentAudioGen = _audioGeneration;
            frameSize = _frameSize;
        }

        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
        if (data.Length == 0) return;

        // Process audio in _audioLock ONLY (never nest _stateLock inside to prevent deadlock)
        lock (_audioLock)
        {
            if (currentAudioGen != _audioGeneration)
            {
                return;
            }

            var frames = _frameAccumulator.Append(data, frameSize);
            short[] decoded = new short[frameSize * 2];
            foreach (var frame in frames)
            {
                if (_frameAccumulator.TryConsumeSyncForNextFrame(out short predictor, out byte stepIndex))
                {
                    _decoder.Synchronize(predictor, stepIndex);
                }

                int count = _decoder.Decode(frame, decoded);

                _ringBuffer.Write(decoded.AsSpan(0, count));
                _audioPlayer.WriteSamples(decoded.AsSpan(0, count));

                float level = AudioLevelMeter.CalculateRms(decoded.AsSpan(0, count));
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
                    lock (_stateLock)
                    {
                        if (capturedConnGen != _connectionGeneration) return;
                    }

                    // Reject non-16kHz codec (e.g. 8kHz)
                    if (codec != 0x02)
                    {
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

                    // If cancelled during Opening, or in Draining, or in Idle: MUST send close!
                    bool wasCancelledOrEnded = false;
                    lock (_stateLock)
                    {
                        if (capturedConnGen != _connectionGeneration) return;
                        _currentSessionId = sessionId;
                        if (_cancelOpenPending || _sessionState == BluetoothSessionState.Draining || _sessionState == BluetoothSessionState.Idle)
                        {
                            wasCancelledOrEnded = true;
                        }
                    }

                    if (wasCancelledOrEnded)
                    {
                        _ = SendClosePacketAsync(sessionId);
                    }
                }
                break;

            case ATVVProtocol.OpcodeMicrophoneRefused: // 0x0C
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
                    lock (_audioLock)
                    {
                        if (capturedAudioGen != _audioGeneration) return;
                        _frameAccumulator.Synchronize(predictor, stepIndex);
                    }
                }
                break;

            case ATVVProtocol.OpcodeStartSearch: // 0x08
                lock (_stateLock)
                {
                    if (capturedConnGen != _connectionGeneration) return;
                }
                _ = StartVoiceSessionAsync();
                break;

            case ATVVProtocol.OpcodeAudioStop: // 0x00
                lock (_stateLock)
                {
                    if (capturedConnGen != _connectionGeneration) return;
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

            _txCharacteristic = null;
            ConnectedDeviceContainerId = null;
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
