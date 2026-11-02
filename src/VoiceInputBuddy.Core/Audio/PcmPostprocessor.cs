// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

namespace VoiceInputBuddy.Core.Audio;

/// <summary>
/// Post-processes decoded 16-bit PCM audio samples.
/// Applies a 3-tap causal binomial FIR filter across frame boundaries to suppress
/// 4-bit IMA ADPCM stepping noise, followed by digital gain scaling and hard clipping.
/// </summary>
public sealed class PcmPostprocessor
{
    private int? _previous;
    private int? _preceding;

    public void Reset()
    {
        _previous = null;
        _preceding = null;
    }

    public void Process(Span<short> samples, float gain)
    {
        float safeGain = float.IsFinite(gain) ? Math.Clamp(gain, 0.1f, 20.0f) : 1.0f;
        for (int i = 0; i < samples.Length; i++)
        {
            int value = samples[i];
            int last = _previous ?? value;
            int beforeLast = _preceding ?? last;
            int filtered = (beforeLast + 2 * last + value) >> 2;
            _preceding = last;
            _previous = value;

            int scaled = (int)MathF.Round(filtered * safeGain);
            samples[i] = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
        }
    }
}
