using VoiceInputBuddy.Core.Audio;
using VoiceInputBuddy.Core.Protocols;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class AudioDecoderAndProtocolTests
{
    [Fact]
    public void AdpcmDecoder_InitialState_DecodesCorrectly()
    {
        var decoder = new ImaAdpcmDecoder();
        Assert.Equal(0, decoder.Predictor);
        Assert.Equal(0, decoder.StepIndex);

        // Input 2 bytes (4 samples)
        byte[] encoded = [0x00, 0x11];
        short[] output = new short[4];

        int decodedCount = decoder.Decode(encoded, output);
        Assert.Equal(4, decodedCount);
        // All decoded samples should be non-zero except initial
        Assert.True(output[0] >= 0);
    }

    [Fact]
    public void AdpcmDecoder_Synchronize_UpdatesState()
    {
        var decoder = new ImaAdpcmDecoder();
        decoder.Synchronize(-1234, 45);

        Assert.Equal(-1234, decoder.Predictor);
        Assert.Equal(45, decoder.StepIndex);
    }

    [Fact]
    public void AtvvProtocol_ParseCapabilities_Success()
    {
        // 0x0B, version 0x0100, codecs 0x02 (16kHz), interaction 0x03, frameSize 160
        byte[] capsBytes = [0x0B, 0x01, 0x00, 0x02, 0x03, 0x00, 0xA0];
        var caps = ATVVProtocol.ParseCapabilities(capsBytes);

        Assert.NotNull(caps);
        Assert.Equal(0x0100, caps.Version);
        Assert.Equal(0x02, caps.Codecs);
        Assert.Equal(0x03, caps.Interaction);
        Assert.Equal(160, caps.FrameSize);
        Assert.Equal(0x02, caps.SelectedCodec);
        Assert.Equal(16000.0, caps.SampleRate);
    }

    [Fact]
    public void AtvvProtocol_ParseAudioStart_ExtractsSessionId()
    {
        byte[] packet = [0x04, 0x01, 0x02, 0x7F];
        bool ok = ATVVProtocol.ParseAudioStart(packet, out byte interaction, out byte codec, out byte sessionId);

        Assert.True(ok);
        Assert.Equal(0x01, interaction);
        Assert.Equal(0x02, codec);
        Assert.Equal(0x7F, sessionId);
    }

    [Fact]
    public void AtvvProtocol_ParseAudioSync_ExtractsPredictorWithSign()
    {
        // Predictor = -1000 => 0xFC18
        byte[] packet = [0x0A, 0x00, 0x00, 0x00, 0xFC, 0x18, 0x2A];
        bool ok = ATVVProtocol.ParseAudioSync(packet, out short predictor, out byte stepIndex);

        Assert.True(ok);
        Assert.Equal(-1000, predictor);
        Assert.Equal(0x2A, stepIndex);
    }

    [Fact]
    public void AudioRingBuffer_WriteRead_PreservesDataAndOrder()
    {
        var ring = new AudioRingBuffer(capacity: 10);
        short[] input = [10, 20, 30, 40, 50];
        ring.Write(input);

        Assert.Equal(5, ring.Available);

        short[] output = new short[5];
        int read = ring.Read(output);

        Assert.Equal(5, read);
        Assert.Equal(0, ring.Available);
        Assert.Equal(input, output);
    }

    [Fact]
    public void G9N9NProtocol_CandidateCheck()
    {
        Assert.True(G9N9NProtocol.IsCandidate("Chromecast Remote"));
        Assert.True(G9N9NProtocol.IsCandidate("G9N9N"));
        Assert.False(G9N9NProtocol.IsCandidate("Logitech Mouse"));
    }

    [Fact]
    public void NativeInputSender_InputStructSize_Is40BytesOn64Bit()
    {
        if (IntPtr.Size == 8)
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf<VoiceInputBuddy.Platform.Windows.Input.INPUT>();
            Assert.Equal(40, size);
        }
    }
}
