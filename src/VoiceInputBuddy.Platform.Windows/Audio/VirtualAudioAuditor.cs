using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceInputBuddy.Core.Diagnostics;

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
                var status = pending ? DriverReadinessStatus.RebootRequired : DriverReadinessStatus.NotInstalled;
                AppLogger.Warn($"VB-CABLE audit result: {status} (Active endpoint not found, pending={pending})", "VirtualAudioAuditor");
                return status;
            }

            // Real WASAPI test open
            var waveFormat = new WaveFormat(16000, 16, 1);
            using var testOut = new WasapiOut(cableInput, AudioClientShareMode.Shared, useEventSync: true, latency: 20);
            var dummyProvider = new BufferedWaveProvider(waveFormat);
            testOut.Init(dummyProvider);
            AppLogger.Info("VB-CABLE audit result: Ready (WASAPI test open succeeded)", "VirtualAudioAuditor");
            return DriverReadinessStatus.Ready;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"VB-CABLE audit failed with exception: {ex.Message}, returning RebootRequired", "VirtualAudioAuditor");
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
            if (device == null)
            {
                AppLogger.Warn("WasapiAudioPlayer.Start failed: CABLE Input device not found", "WasapiAudioPlayer");
                return false;
            }

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
                AppLogger.Info("WasapiAudioPlayer started playback to CABLE Input (16kHz 16-bit Mono)", "WasapiAudioPlayer");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("WasapiAudioPlayer failed to initialize WASAPI stream", ex, "WasapiAudioPlayer");
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
        lock (_lock)
        {
            if (_waveProvider != null)
            {
                int bytesLeft = _waveProvider.BufferedBytes;
                // 16kHz 16-bit mono = 32,000 bytes/sec
                int msLeft = (int)((bytesLeft / 32000.0) * 1000.0);
                int waitTime = Math.Clamp(msLeft + 20, 20, 500);
                Thread.Sleep(waitTime);
            }
            else
            {
                Thread.Sleep(timeoutMs);
            }
        }
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
