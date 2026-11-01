using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceInputBuddy.Platform.Windows.Audio;

public enum DriverReadinessStatus
{
    Ready,
    NotInstalled,
    RebootRequired,
    InitializationFailed
}

public static class VirtualAudioAuditor
{
    public static DriverReadinessStatus AuditCableInputDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            
            var cableInput = renderDevices.FirstOrDefault(d => 
                d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));

            if (cableInput == null)
            {
                var allDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All);
                bool pending = allDevices.Any(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
                return pending ? DriverReadinessStatus.RebootRequired : DriverReadinessStatus.NotInstalled;
            }

            // Real WASAPI test open
            var waveFormat = new WaveFormat(16000, 16, 1);
            using var testOut = new WasapiOut(cableInput, AudioClientShareMode.Shared, useEventSync: true, latency: 20);
            var dummyProvider = new BufferedWaveProvider(waveFormat);
            testOut.Init(dummyProvider);
            return DriverReadinessStatus.Ready;
        }
        catch
        {
            return DriverReadinessStatus.RebootRequired;
        }
    }

    public static MMDevice? GetCableInputDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            return renderDevices.FirstOrDefault(d => 
                d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }
}

public sealed class WasapiAudioPlayer : IDisposable
{
    private WasapiOut? _wasapiOut;
    private BufferedWaveProvider? _waveProvider;
    private readonly WaveFormat _format = new(16000, 16, 1);
    private readonly object _lock = new();

    public bool IsActive => _wasapiOut != null && _wasapiOut.PlaybackState == PlaybackState.Playing;

    public bool Start(MMDevice? device = null)
    {
        lock (_lock)
        {
            Stop();

            device ??= VirtualAudioAuditor.GetCableInputDevice();
            if (device == null) return false;

            try
            {
                _wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 20);
                _waveProvider = new BufferedWaveProvider(_format)
                {
                    BufferDuration = TimeSpan.FromMilliseconds(500),
                    DiscardOnBufferOverflow = true
                };

                _wasapiOut.Init(_waveProvider);
                _wasapiOut.Play();
                return true;
            }
            catch
            {
                Stop();
                return false;
            }
        }
    }

    public void WriteSamples(ReadOnlySpan<short> samples)
    {
        lock (_lock)
        {
            if (_waveProvider == null) return;

            byte[] bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                bytes[i * 2] = (byte)(samples[i] & 0xFF);
                bytes[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
            }

            _waveProvider.AddSamples(bytes, 0, bytes.Length);
        }
    }

    public void Drain(int timeoutMs = 60)
    {
        Thread.Sleep(timeoutMs);
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_wasapiOut != null)
            {
                try
                {
                    _wasapiOut.Stop();
                }
                catch { }
                _wasapiOut.Dispose();
                _wasapiOut = null;
            }
            _waveProvider = null;
        }
    }

    public void Dispose() => Stop();
}
