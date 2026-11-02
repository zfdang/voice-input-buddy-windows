// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

namespace VoiceInputBuddy.Core.Protocols;

public record ATVVCapabilities(
    ushort Version,
    byte Codecs,
    byte Interaction,
    int FrameSize,
    byte SelectedCodec,
    double SampleRate
);

public static class ATVVProtocol
{
    public static readonly Guid ServiceUuid = new("AB5E0001-5A21-4F05-BC7D-AF01F617B664");
    public static readonly Guid TransmitUuid = new("AB5E0002-5A21-4F05-BC7D-AF01F617B664");
    public static readonly Guid AudioUuid = new("AB5E0003-5A21-4F05-BC7D-AF01F617B664");
    public static readonly Guid ControlUuid = new("AB5E0004-5A21-4F05-BC7D-AF01F617B664");

    // Opcodes
    public const byte OpcodeAudioStop = 0x00;
    public const byte OpcodeAudioStart = 0x04;
    public const byte OpcodeStartSearch = 0x08;
    public const byte OpcodeGetCapabilities = 0x0A;
    public const byte OpcodeCapabilitiesResp = 0x0B;
    public const byte OpcodeMicrophoneOpen = 0x0C;
    public const byte OpcodeMicrophoneRefused = 0x0C;
    public const byte OpcodeAudioSync = 0x0A;
    public const byte OpcodeMicrophoneClose = 0x0D;
    public const byte OpcodeMicrophoneExtend = 0x0E;

    public static byte[] BuildGetCapabilities(ushort version = 0x0100)
    {
        return new byte[] { OpcodeGetCapabilities, 0x01, 0x00, 0x00, 0x03, 0x03 };
    }

    public static byte[] BuildMicrophoneOpen(ushort version = 0x0100, byte codec = 0x02)
    {
        if (version >= 0x0100)
        {
            return new byte[] { OpcodeMicrophoneOpen, 0x00 };
        }
        return new byte[] { OpcodeMicrophoneOpen, 0x00, codec };
    }

    public static byte[] BuildMicrophoneClose(byte sessionId, ushort version = 0x0100)
    {
        if (version >= 0x0100)
        {
            return new byte[] { OpcodeMicrophoneClose, sessionId };
        }
        return new byte[] { OpcodeMicrophoneClose };
    }

    public static byte[] BuildMicrophoneExtend(byte sessionId)
    {
        return new byte[] { OpcodeMicrophoneExtend, sessionId };
    }

    public static ATVVCapabilities? ParseCapabilities(ReadOnlySpan<byte> bytes, bool allowingLegacyLayout = true)
    {
        if (bytes.Length < 7 || bytes[0] != OpcodeCapabilitiesResp)
            return null;

        ushort version = (ushort)((bytes[1] << 8) | bytes[2]);
        byte codecs;
        byte interaction;

        if (version >= 0x0100)
        {
            codecs = bytes[3];
            interaction = bytes[4];
            if (allowingLegacyLayout && codecs == 0 && bytes.Length >= 9 && (bytes[4] & 0x03) != 0)
            {
                codecs = bytes[4];
                interaction = 0x03;
            }
        }
        else
        {
            if (bytes.Length < 9) return null;
            codecs = bytes[4];
            interaction = 0;
        }

        int rawFrameSize = (bytes[5] << 8) | bytes[6];
        int frameSize = (rawFrameSize <= 0 || rawFrameSize > 512) ? 120 : rawFrameSize;
        byte selectedCodec = (codecs & 0x02) != 0 ? (byte)0x02 : (byte)0x01;
        double sampleRate = (selectedCodec == 0x02) ? 16000.0 : 8000.0;

        return new ATVVCapabilities(
            Version: version,
            Codecs: codecs,
            Interaction: interaction,
            FrameSize: frameSize,
            SelectedCodec: selectedCodec,
            SampleRate: sampleRate
        );
    }

    public static bool ParseAudioStart(ReadOnlySpan<byte> bytes, out byte interaction, out byte codec, out byte sessionId)
    {
        interaction = 0;
        codec = 0;
        sessionId = 0;

        if (bytes.Length < 3 || bytes[0] != OpcodeAudioStart)
            return false;

        interaction = bytes[1];
        codec = bytes[2];
        if (bytes.Length >= 4)
        {
            sessionId = bytes[3];
        }

        return true;
    }

    public static bool ParseAudioSync(ReadOnlySpan<byte> bytes, out short predictor, out byte stepIndex)
    {
        predictor = 0;
        stepIndex = 0;

        if (bytes.Length < 7 || bytes[0] != OpcodeAudioSync)
            return false;

        ushort predictorBits = (ushort)((bytes[4] << 8) | bytes[5]);
        predictor = unchecked((short)predictorBits);
        stepIndex = bytes[6];

        return true;
    }
}

public sealed class ATVVFrameAccumulator
{
    private readonly List<byte> _pending = new();
    private (short Predictor, byte StepIndex)? _pendingSync;
    private int _framesBeforeSync = 0;

    public int PendingCount => _pending.Count;

    public void Synchronize(short predictor, byte stepIndex)
    {
        _pendingSync = (predictor, stepIndex);
        _framesBeforeSync = _pending.Count == 0 ? 0 : 1;
    }

    public void Reset()
    {
        _pending.Clear();
        _pendingSync = null;
        _framesBeforeSync = 0;
    }

    public List<byte[]> Append(ReadOnlySpan<byte> data, int frameSize)
    {
        if (frameSize <= 0) return new();
        for (int i = 0; i < data.Length; i++)
        {
            _pending.Add(data[i]);
        }

        var frames = new List<byte[]>();
        int offset = 0;
        while (_pending.Count - offset >= frameSize)
        {
            byte[] frame = new byte[frameSize];
            _pending.CopyTo(offset, frame, 0, frameSize);
            frames.Add(frame);
            offset += frameSize;
        }

        if (offset > 0)
        {
            _pending.RemoveRange(0, offset);
        }

        return frames;
    }

    public bool TryConsumeSyncForNextFrame(out short predictor, out byte stepIndex)
    {
        if (_pendingSync.HasValue && _framesBeforeSync == 0)
        {
            var sync = _pendingSync.Value;
            _pendingSync = null;
            predictor = sync.Predictor;
            stepIndex = sync.StepIndex;
            return true;
        }

        if (_framesBeforeSync > 0)
        {
            _framesBeforeSync--;
        }

        predictor = 0;
        stepIndex = 0;
        return false;
    }
}
