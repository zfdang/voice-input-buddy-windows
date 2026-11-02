// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using VoiceInputBuddy.Core.Audio;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class AudioRingBufferTests
{
    [Fact]
    public void RingBuffer_WriteAndRead_PreservesSequence()
    {
        var ring = new AudioRingBuffer(capacity: 8);
        short[] input = [10, 20, 30, 40];

        ring.Write(input);
        Assert.Equal(4, ring.Available);

        short[] output = new short[4];
        int read = ring.Read(output);

        Assert.Equal(4, read);
        Assert.Equal(input, output);
        Assert.Equal(0, ring.Available);
    }

    [Fact]
    public void RingBuffer_WrapAround_OverwritesOldestWhenFull()
    {
        var ring = new AudioRingBuffer(capacity: 4);

        ring.Write((short[])[1, 2, 3]);
        Assert.Equal(3, ring.Available);

        // Writing 3 more items overflows capacity (4), discarding oldest 2 items (1, 2)
        ring.Write((short[])[4, 5, 6]);
        Assert.Equal(4, ring.Available);

        short[] output = new short[4];
        int read = ring.Read(output);

        Assert.Equal(4, read);
        Assert.Equal((short[])[3, 4, 5, 6], output);
        Assert.Equal(0, ring.Available);
    }

    [Fact]
    public void RingBuffer_Clear_ResetsPointersAndCount()
    {
        var ring = new AudioRingBuffer(capacity: 8);
        ring.Write((short[])[100, 200, 300]);
        Assert.Equal(3, ring.Available);

        ring.Clear();
        Assert.Equal(0, ring.Available);

        short[] output = new short[4];
        int read = ring.Read(output);
        Assert.Equal(0, read);
    }

    [Fact]
    public void AudioLevelMeter_CalculateRms_ReturnsZeroOnSilence()
    {
        short[] silence = new short[160];
        float level = AudioLevelMeter.CalculateRms(silence);
        Assert.Equal(0f, level);
    }

    [Fact]
    public void AudioLevelMeter_CalculateRms_ScalesCorrectlyWithSignal()
    {
        short[] signal = new short[160];
        Array.Fill(signal, (short)16384); // 50% amplitude
        float level = AudioLevelMeter.CalculateRms(signal);
        Assert.True(level > 0.5f);
        Assert.True(level <= 1.0f);
    }
}
