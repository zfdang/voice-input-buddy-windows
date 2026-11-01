using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;
using VoiceInputBuddy.Core.Audio;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Core.Protocols;
using VoiceInputBuddy.Platform.Windows.Audio;

namespace VoiceInputBuddy.Platform.Windows.Bluetooth;

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
    private bool _isStreaming = false;

    public event Action<RemoteDevice>? DeviceDiscovered;
    public event Action<bool, string>? ConnectionStatusChanged;
    public event Action<float>? AudioLevelChanged;
    public event Action? VoiceSessionStarted;
    public event Action? VoiceSessionEnded;

    public bool IsConnected => _connectedDevice != null && _connectedDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
    public bool IsStreaming => _isStreaming;

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
                ConnectionStatusChanged?.Invoke(connected, s.Name);
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

            if (_audioCharacteristic != null)
            {
                await _audioCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
                _audioCharacteristic.ValueChanged += OnAudioValueChanged;
            }

            if (_controlCharacteristic != null)
            {
                await _controlCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
                _controlCharacteristic.ValueChanged += OnControlValueChanged;
            }

            // Capability query
            if (_txCharacteristic != null)
            {
                byte[] getCaps = ATVVProtocol.BuildGetCapabilities();
                var buffer = CryptographicBuffer.CreateFromByteArray(getCaps);
                await _txCharacteristic.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);
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
        if (_txCharacteristic == null || _isStreaming) return false;

        _ringBuffer.Clear();
        _decoder.Reset();
        _audioPlayer.Start();

        byte[] micOpen = ATVVProtocol.BuildMicrophoneOpen();
        var buffer = CryptographicBuffer.CreateFromByteArray(micOpen);
        await _txCharacteristic.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);

        _isStreaming = true;
        VoiceSessionStarted?.Invoke();
        return true;
    }

    public async Task<bool> StopVoiceSessionAsync()
    {
        if (_txCharacteristic == null || !_isStreaming) return false;

        // Sequence: Send MicClose first, drain audio, then stop
        byte[] micClose = ATVVProtocol.BuildMicrophoneClose(_currentSessionId);
        var buffer = CryptographicBuffer.CreateFromByteArray(micClose);
        await _txCharacteristic.WriteValueAsync(buffer, GattWriteOption.WriteWithoutResponse);

        _isStreaming = false;

        // Drain pipeline
        _audioPlayer.Drain(50);
        _audioPlayer.Stop();

        VoiceSessionEnded?.Invoke();
        return true;
    }

    private void OnAudioValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        if (!_isStreaming) return;

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
            case ATVVProtocol.OpcodeAudioStart:
                if (ATVVProtocol.ParseAudioStart(data, out _, out _, out byte sessionId))
                {
                    _currentSessionId = sessionId;
                }
                break;

            case ATVVProtocol.OpcodeAudioSync:
                if (ATVVProtocol.ParseAudioSync(data, out short predictor, out byte stepIndex))
                {
                    _decoder.Synchronize(predictor, stepIndex);
                }
                break;

            case ATVVProtocol.OpcodeStartSearch:
                _ = StartVoiceSessionAsync();
                break;

            case ATVVProtocol.OpcodeAudioStop:
                _ = StopVoiceSessionAsync();
                break;
        }
    }

    public void Disconnect()
    {
        _isStreaming = false;
        _audioPlayer.Stop();

        if (_connectedDevice != null)
        {
            _connectedDevice.Dispose();
            _connectedDevice = null;
        }

        _txCharacteristic = null;
        _audioCharacteristic = null;
        _controlCharacteristic = null;
    }

    public void Dispose()
    {
        StopScanning();
        Disconnect();
    }
}
