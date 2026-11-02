namespace VoiceInputBuddy.Core.Audio;

public sealed class AudioRingBuffer
{
    private readonly short[] _buffer;
    private readonly int _capacity;
    private int _head;
    private int _tail;
    private int _count;
    private readonly object _lock = new();

    public int Capacity => _capacity;
    public int Available => _count;

    public AudioRingBuffer(int capacity = 16000 * 2) // default ~2 seconds of 16kHz mono audio
    {
        _capacity = capacity;
        _buffer = new short[capacity];
    }

    public void Write(ReadOnlySpan<short> samples)
    {
        lock (_lock)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                _buffer[_head] = samples[i];
                _head = (_head + 1) % _capacity;

                if (_count < _capacity)
                {
                    _count++;
                }
                else
                {
                    // Overwrite oldest sample
                    _tail = (_tail + 1) % _capacity;
                }
            }
        }
    }

    public int Read(Span<short> destination)
    {
        lock (_lock)
        {
            int toRead = Math.Min(destination.Length, _count);
            for (int i = 0; i < toRead; i++)
            {
                destination[i] = _buffer[_tail];
                _tail = (_tail + 1) % _capacity;
            }
            _count -= toRead;
            return toRead;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }
}

public static class AudioLevelMeter
{
    public static float CalculateRms(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty) return 0f;

        double sumOfSquares = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double normalized = samples[i] / 32768.0;
            sumOfSquares += normalized * normalized;
        }

        double rms = Math.Sqrt(sumOfSquares / samples.Length);
        // Map to 0.0 ~ 1.0 with a non-linear perceptual scale
        float level = (float)Math.Clamp(rms * 4.0, 0.0, 1.0);
        return level;
    }
}
