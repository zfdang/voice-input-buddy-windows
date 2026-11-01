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

    private byte _currentSessionId = 0;
    private BluetoothSessionState _sessionState = BluetoothSessionState.Idle;
    private bool _cancelOpenPending = false;
    private int _connectionGeneration = 0;
    private int _sessionGeneration = 0;
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

        try
        {
            _connectedDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
            if (_connectedDevice == null) return false;

            _connectedDevice.ConnectionStatusChanged += (s, e) =>
            {
                bool connected = s.ConnectionStatus == BluetoothConnectionStatus.Connected;
                if (!connected)
                {
                    Disconnect();
                    ConnectionStatusChanged?.Invoke(false, s.Name);
                }
            };

            // Discover Service
            var servicesResult = await _connectedDevice.GetGattServicesForUuidAsync(ATVVProtocol.ServiceUuid);
            if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            {
                return false;
            }

            var service = servicesResult.Services[0];
            var charsResult = await service.GetCharacteristicsAsync();
            if (charsResult.Status != GattCommunicationStatus.Success) return false;

            foreach (var ch in charsResult.Characteristics)
            {
                if (ch.Uuid == ATVVProtocol.TransmitUuid) _txCharacteristic = ch;
                else if (ch.Uuid == ATVVProtocol.AudioUuid) _audioCharacteristic = ch;
                else if (ch.Uuid == ATVVProtocol.ControlUuid) _controlCharacteristic = ch;
            }

            if (_txCharacteristic == null || _audioCharacteristic == null || _controlCharacteristic == null)
            {
                Disconnect();
                return false;
            }

            // Subscribe notifications
            var audioStatus = await _audioCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (audioStatus != GattCommunicationStatus.Success)
            {
                Disconnect();
                return false;
            }
            _audioCharacteristic.ValueChanged += OnAudioValueChanged;

            var controlStatus = await _controlCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (controlStatus != GattCommunicationStatus.Success)
            {
                Disconnect();
                return false;
            }
            _controlCharacteristic.ValueChanged += OnControlValueChanged;

            // Negotiate capabilities before declaring ready
            _capabilitiesTcs = new TaskCompletionSource<bool>();
            byte[] getCaps = ATVVProtocol.BuildGetCapabilities();
            var buffer = CryptographicBuffer.CreateFromByteArray(getCaps);
            var writeStatus = await _txCharacteristic.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);
            if (writeStatus != GattCommunicationStatus.Success)
            {
                Disconnect();
                ConnectionStatusChanged?.Invoke(false, "下发能力协商命令失败");
                return false;
            }

            // Await 0x0B response with 5-second timeout
            var completedTask = await Task.WhenAny(_capabilitiesTcs.Task, Task.Delay(5000));
            if (completedTask != _capabilitiesTcs.Task || !await _capabilitiesTcs.Task)
            {
                Disconnect();
                ConnectionStatusChanged?.Invoke(false, "能力协商超时或不支持 16kHz 编码");
                return false;
            }

            // Retrieve ContainerId for device matching
            try
            {
                var devInfo = await global::Windows.Devices.Enumeration.DeviceInformation.CreateFromIdAsync(
                    _connectedDevice.DeviceId,
                    new[] { "System.Devices.ContainerId" });
                if (devInfo != null && devInfo.Properties.TryGetValue("System.Devices.ContainerId", out object? cObj) && cObj is Guid guidVal)
                {
                    ConnectedDeviceContainerId = guidVal;
                }
            }
            catch { }

            if (ConnectedDeviceContainerId == null)
            {
                ConnectedDeviceContainerId = Input.RawInputReceiver.GetDeviceContainerId(_connectedDevice.DeviceId);
            }

            ConnectionStatusChanged?.Invoke(true, _connectedDevice.Name);
            return true;
        }
        catch
        {
            Disconnect();
            return false;
        }
    }

    public async Task<bool> StartVoiceSessionAsync()
    {
        int currentConnGen;
        int currentSessGen;
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
            _decoder.Reset();
            currentConnGen = _connectionGeneration;
            currentSessGen = ++_sessionGeneration;
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
                if (currentConnGen != _connectionGeneration || currentSessGen != _sessionGeneration)
                {
                    // Stale operation: a newer session or disconnect has taken over.
                    // DO NOT clean up new session resources!
                    return false;
                }

                if (_cancelOpenPending)
                {
                    // Fast release occurred during await on this SAME session
                    _ = SendClosePacketAsync(_currentSessionId);
                    _audioPlayer.Stop();
                    _sessionState = BluetoothSessionState.Idle;
                    return false;
                }

                _sessionState = BluetoothSessionState.Streaming;
                StartKeepaliveTimer();
            }

            VoiceSessionStarted?.Invoke();
            return true;
        }
        catch
        {
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
        lock (_stateLock)
        {
            if (_sessionState == BluetoothSessionState.Opening)
            {
                _cancelOpenPending = true;
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

        lock (_stateLock)
        {
            if (currentConnGen != _connectionGeneration || currentSessGen != _sessionGeneration)
            {
                // Stale stopping operation: a newer session or disconnect has taken over.
                // Do not stop player or change state of the new session!
                return false;
            }

            // Drain audio player
            _audioPlayer.Drain(50);
            _audioPlayer.Stop();

            _sessionState = BluetoothSessionState.Idle;
        }

        VoiceSessionEnded?.Invoke();
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
        // Accept audio packets in both Streaming and Draining states
        if (_sessionState != BluetoothSessionState.Streaming && _sessionState != BluetoothSessionState.Draining)
        {
            return;
        }

        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
        if (data.Length == 0) return;

        Span<short> decoded = stackalloc short[data.Length * 2];
        int count = _decoder.Decode(data, decoded);

        _ringBuffer.Write(decoded[..count]);
        _audioPlayer.WriteSamples(decoded[..count]);

        float level = AudioLevelMeter.CalculateRms(decoded[..count]);
        AudioLevelChanged?.Invoke(level);
    }

    private void OnControlValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
        if (data.Length == 0) return;

        byte opcode = data[0];
        switch (opcode)
        {
            case ATVVProtocol.OpcodeCapabilitiesResp: // 0x0B
                var caps = ATVVProtocol.ParseCapabilities(data);
                if (caps != null && caps.SampleRate == 16000 && (caps.Codecs & 0x02) != 0)
                {
                    _capabilitiesTcs?.TrySetResult(true);
                }
                else
                {
                    _capabilitiesTcs?.TrySetResult(false);
                }
                break;

            case ATVVProtocol.OpcodeAudioStart: // 0x04
                if (ATVVProtocol.ParseAudioStart(data, out _, out _, out byte sessionId))
                {
                    _currentSessionId = sessionId;
                }
                break;

            case ATVVProtocol.OpcodeAudioSync: // 0x0A
                if (ATVVProtocol.ParseAudioSync(data, out short predictor, out byte stepIndex))
                {
                    _decoder.Synchronize(predictor, stepIndex);
                }
                break;

            case ATVVProtocol.OpcodeStartSearch: // 0x08
                _ = StartVoiceSessionAsync();
                break;

            case ATVVProtocol.OpcodeAudioStop: // 0x00
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
        bool wasActiveSession = false;
        lock (_stateLock)
        {
            _connectionGeneration++;
            _sessionGeneration++;
            wasActiveSession = (_sessionState != BluetoothSessionState.Idle);
            _sessionState = BluetoothSessionState.Idle;
            _cancelOpenPending = false;
            StopKeepaliveTimer();
            _audioPlayer.Stop();
        }

        if (wasActiveSession)
        {
            VoiceSessionEnded?.Invoke();
        }

        if (_connectedDevice != null)
        {
            _connectedDevice.Dispose();
            _connectedDevice = null;
        }

        _txCharacteristic = null;
        _audioCharacteristic = null;
        _controlCharacteristic = null;
        ConnectedDeviceContainerId = null;
    }

    public void Dispose()
    {
        StopScanning();
        Disconnect();
    }
}
