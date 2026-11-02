// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
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
            using var testOut = new WasapiOut(cableInput, AudioClientShareMode.Shared, useEventSync: true, latency: 20);
            IWaveProvider dummyProvider = new BufferedWaveProvider(new WaveFormat(16000, 16, 1));
            testOut.Init(dummyProvider);
            AppLogger.Info("VB-CABLE audit result: Ready (WASAPI test open succeeded)", "VirtualAudioAuditor");
            return DriverReadinessStatus.Ready;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"VB-CABLE audit failed with exception: {ex.Message}", "VirtualAudioAuditor");
            return DriverReadinessStatus.InitializationFailed;
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
    private byte[] _sampleByteBuffer = new byte[4096];
    private readonly object _lock = new();

    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _wasapiOut != null && _wasapiOut.PlaybackState == PlaybackState.Playing;
            }
        }
    }

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

                var mixFormat = device.AudioClient.MixFormat;
                _wasapiOut.Init(_waveProvider);
                _wasapiOut.Play();
                AppLogger.Info($"WasapiAudioPlayer started playback to CABLE Input (Source: 16kHz 16-bit Mono, Native: {mixFormat.SampleRate}Hz {mixFormat.Channels}ch)", "WasapiAudioPlayer");
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
        if (samples.IsEmpty) return;

        lock (_lock)
        {
            if (_waveProvider == null) return;

            int byteCount = samples.Length * 2;
            if (_sampleByteBuffer.Length < byteCount)
            {
                Array.Resize(ref _sampleByteBuffer, Math.Max(byteCount, _sampleByteBuffer.Length * 2));
            }

            MemoryMarshal.AsBytes(samples).CopyTo(_sampleByteBuffer);
            _waveProvider.AddSamples(_sampleByteBuffer, 0, byteCount);
        }
    }

    public async Task DrainAsync(int timeoutMs = 60, CancellationToken ct = default)
    {
        int waitTime;
        lock (_lock)
        {
            if (_waveProvider != null)
            {
                int bytesLeft = _waveProvider.BufferedBytes;
                // 16kHz 16-bit mono = 32,000 bytes/sec
                int msLeft = (int)((bytesLeft / 32000.0) * 1000.0);
                waitTime = Math.Clamp(msLeft + 20, 20, 500);
            }
            else
            {
                waitTime = timeoutMs;
            }
        }

        try
        {
            await Task.Delay(waitTime, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    public void Drain(int timeoutMs = 60)
    {
        int waitTime;
        lock (_lock)
        {
            if (_waveProvider != null)
            {
                int bytesLeft = _waveProvider.BufferedBytes;
                int msLeft = (int)((bytesLeft / 32000.0) * 1000.0);
                waitTime = Math.Clamp(msLeft + 20, 20, 500);
            }
            else
            {
                waitTime = timeoutMs;
            }
        }

        Thread.Sleep(waitTime);
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
