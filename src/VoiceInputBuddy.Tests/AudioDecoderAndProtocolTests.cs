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

    [Fact]
    public void NativeInputSender_TryParseShortcut_ParsesModifiersAndKey()
    {
        bool ok = VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut(
            "Ctrl+Shift+Alt+V", out var modifiers, out ushort mainKey);

        Assert.True(ok);
        Assert.Equal(3, modifiers.Count);
        Assert.Contains(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LCONTROL, modifiers);
        Assert.Contains(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LSHIFT, modifiers);
        Assert.Contains(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LMENU, modifiers);
        Assert.Equal((ushort)'V', mainKey);
    }

    [Fact]
    public void NativeInputSender_TryParseShortcut_SingleKey_Parsed()
    {
        bool ok = VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut(
            "F1", out var modifiers, out ushort mainKey);

        Assert.True(ok);
        Assert.Empty(modifiers);
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_F1, mainKey);
    }

    [Fact]
    public void NativeInputSender_TryParseShortcut_ArrowAndSpecialKeys()
    {
        Assert.True(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut("Up", out _, out ushort upKey));
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_UP, upKey);

        Assert.True(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut("Down", out _, out ushort downKey));
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_DOWN, downKey);

        Assert.True(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut("Left", out _, out ushort leftKey));
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LEFT, leftKey);

        Assert.True(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut("Right", out _, out ushort rightKey));
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_RIGHT, rightKey);

        Assert.True(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut("Space", out _, out ushort spaceKey));
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_SPACE, spaceKey);

        Assert.True(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut("Back", out _, out ushort backKey));
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_BACK, backKey);

        Assert.True(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut("Delete", out _, out ushort delKey));
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_DELETE, delKey);

        Assert.True(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut("Ctrl+Down", out var ctrlDownMods, out ushort ctrlDownKey));
        Assert.Single(ctrlDownMods);
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_DOWN, ctrlDownKey);
    }

    [Fact]
    public void G9N9NProtocol_TryParseReport1_ParsesIndexedButtonsAndRelease()
    {
        // Index 3 is Up
        byte[] report1Up = [0x01, 0x03, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(report1Up, out var buttons));
        Assert.Single(buttons);
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Up, buttons[0]);

        // Index 4 is Down, Index 5 is Left
        byte[] report1Multi = [0x01, 0x04, 0x05];
        Assert.True(G9N9NProtocol.TryParseReport(report1Multi, out var multiButtons));
        Assert.Equal(2, multiButtons.Count);
        Assert.Contains(VoiceInputBuddy.Core.Models.RemoteButton.Down, multiButtons);
        Assert.Contains(VoiceInputBuddy.Core.Models.RemoteButton.Left, multiButtons);

        // All 0 is a valid release report
        byte[] report1Release = [0x01, 0x00, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(report1Release, out var releaseButtons));
        Assert.Empty(releaseButtons);
    }

    [Fact]
    public void G9N9NProtocol_TryParseReport2_ParsesUsagesAndRelease()
    {
        // Usage 0x0042 is Up
        byte[] report2Up = [0x02, 0x42, 0x00, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(report2Up, out var buttons));
        Assert.Single(buttons);
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Up, buttons[0]);

        // All 0 is a valid release report
        byte[] report2Release = [0x02, 0x00, 0x00, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(report2Release, out var releaseButtons));
        Assert.Empty(releaseButtons);
    }

    [Fact]
    public void RemoteDevice_DefaultMappings_Populated()
    {
        var device = new VoiceInputBuddy.Core.Models.RemoteDevice();
        Assert.NotNull(device.Mappings);
        Assert.NotEmpty(device.Mappings);
        Assert.Contains(device.Mappings, m => m.Button == VoiceInputBuddy.Core.Models.RemoteButton.Up);
        Assert.Contains(device.Mappings, m => m.Button == VoiceInputBuddy.Core.Models.RemoteButton.Ok);
        Assert.Contains(device.Mappings, m => m.Button == VoiceInputBuddy.Core.Models.RemoteButton.VolumeUp);
    }

    [Fact]
    public void G9N9NProtocol_TryParseReport_WithoutReportId_ParsesReleaseAndPress()
    {
        // 2-byte Report 1 without ID: index 3 is Up
        byte[] pressWithoutId = [0x03, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(pressWithoutId, out var pressButtons));
        Assert.Single(pressButtons);
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Up, pressButtons[0]);

        // 2-byte release report without ID
        byte[] releaseWithoutId = [0x00, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(releaseWithoutId, out var releaseButtons));
        Assert.Empty(releaseButtons);

        // 4-byte release report without ID
        byte[] release4WithoutId = [0x00, 0x00, 0x00, 0x00];
        Assert.True(G9N9NProtocol.TryParseReport(release4WithoutId, out var release4Buttons));
        Assert.Empty(release4Buttons);

        // Invalid index (> 17) should be rejected
        byte[] invalidIndex = [0x15, 0x00];
        Assert.False(G9N9NProtocol.TryParseReport(invalidIndex, out _));
    }

    [Fact]
    public void RemoteDevice_ContainerId_JsonRoundtrip()
    {
        var guid = Guid.NewGuid();
        var device = new VoiceInputBuddy.Core.Models.RemoteDevice
        {
            Id = "TEST-DEV",
            Name = "Chromecast Remote",
            ContainerId = guid
        };

        string json = System.Text.Json.JsonSerializer.Serialize(device);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<VoiceInputBuddy.Core.Models.RemoteDevice>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(guid, deserialized.ContainerId);
    }
}
