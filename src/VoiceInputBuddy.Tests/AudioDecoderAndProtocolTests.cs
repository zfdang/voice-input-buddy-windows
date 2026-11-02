using VoiceInputBuddy.Core.Audio;
using VoiceInputBuddy.Core.Protocols;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class AudioDecoderAndProtocolTests
{
    [Fact]
    public void WeChatShortcut_ParsesModifierOnlyChord_AndRejectsUnknownTokens()
    {
        Assert.True(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut(
            "Ctrl + Win", out var modifiers, out var mainKey));
        Assert.Equal(new ushort[] { 0xA2, 0x5B }, modifiers);
        Assert.Equal(0, mainKey);
        Assert.False(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.TryParseShortcut(
            "Ctrl+UnknownKey", out _, out _));
    }

    [Theory]
    [InlineData(0xC0, 0x29, VoiceInputBuddy.Core.Models.RemoteButton.LiveTv)]
    [InlineData(0xFF, 0x5E, VoiceInputBuddy.Core.Models.RemoteButton.Power)]
    [InlineData(0xFF, 0x00, VoiceInputBuddy.Core.Models.RemoteButton.None)]
    public void RawKeyboard_CapturedXiaomiReports_UseVirtualKeyAndScanCode(
        ushort virtualKey, ushort scanCode, VoiceInputBuddy.Core.Models.RemoteButton expected)
    {
        Assert.Equal(expected, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(virtualKey, scanCode));
    }

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

    [Theory]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LWIN, true)]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_RWIN, true)]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_UP, true)]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_DOWN, true)]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LEFT, true)]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_RIGHT, true)]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_DELETE, true)]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LCONTROL, false)]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LSHIFT, false)]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LMENU, false)]
    [InlineData(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_F1, false)]
    [InlineData(0x74, false)] // F5
    public void NativeInputSender_IsExtendedKey_IdentifiesExtendedKeys(ushort vk, bool expected)
    {
        Assert.Equal(expected, VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.IsExtendedKey(vk));
    }

    [Fact]
    public void NativeInputSender_CreateKeyInput_SetsExtendedFlagForExtendedKeys()
    {
        var winDown = VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.CreateKeyInput(
            VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LWIN, false);
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.KEYEVENTF_EXTENDEDKEY, winDown.u.ki.dwFlags);

        var winUp = VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.CreateKeyInput(
            VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LWIN, true);
        Assert.Equal(
            VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.KEYEVENTF_KEYUP |
            VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.KEYEVENTF_EXTENDEDKEY,
            winUp.u.ki.dwFlags);

        var ctrlDown = VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.CreateKeyInput(
            VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LCONTROL, false);
        Assert.Equal(0u, ctrlDown.u.ki.dwFlags);

        var ctrlUp = VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.CreateKeyInput(
            VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.VK_LCONTROL, true);
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Input.NativeInputSender.KEYEVENTF_KEYUP, ctrlUp.u.ki.dwFlags);
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

    [Fact]
    public void XiaomiProtocol_IsCandidate_RecognizesChineseAndEnglishBroadcastNames()
    {
        Assert.True(XiaomiProtocol.IsCandidate("小米蓝牙语音遥控器"));
        Assert.True(XiaomiProtocol.IsCandidate("小米蓝牙遥控器2"));
        Assert.True(XiaomiProtocol.IsCandidate("小米蓝牙遥控器2 pro"));
        Assert.True(XiaomiProtocol.IsCandidate("arn9"));
        Assert.True(XiaomiProtocol.IsCandidate("MI RC"));
        Assert.True(XiaomiProtocol.IsCandidate("xiaomi remote"));
        Assert.False(XiaomiProtocol.IsCandidate("Logitech Keyboard"));
        Assert.False(XiaomiProtocol.IsCandidate(null));
        Assert.False(XiaomiProtocol.IsCandidate(""));
    }

    [Fact]
    public void XiaomiProtocol_TryParseReport_ParsesReport1AndRelease()
    {
        // Report 1 with Up (0x0052)
        byte[] upWithId = [0x01, 0x52, 0x00];
        Assert.True(XiaomiProtocol.TryParseReport(upWithId, out var upButtons));
        Assert.Single(upButtons);
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Up, upButtons[0]);

        // Report 1 with OK (0x0028)
        byte[] okWithId = [0x01, 0x28, 0x00];
        Assert.True(XiaomiProtocol.TryParseReport(okWithId, out var okButtons));
        Assert.Single(okButtons);
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Ok, okButtons[0]);

        // Without Report ID: Up (0x0052)
        byte[] upWithoutId = [0x52, 0x00];
        Assert.True(XiaomiProtocol.TryParseReport(upWithoutId, out var upNoIdButtons));
        Assert.Single(upNoIdButtons);
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Up, upNoIdButtons[0]);

        // All zero release with Report ID
        byte[] releaseWithId = [0x01, 0x00, 0x00];
        Assert.True(XiaomiProtocol.TryParseReport(releaseWithId, out var releaseButtons));
        Assert.Empty(releaseButtons);

        // All zero release without Report ID
        byte[] releaseWithoutId = [0x00, 0x00];
        Assert.True(XiaomiProtocol.TryParseReport(releaseWithoutId, out var releaseNoIdButtons));
        Assert.Empty(releaseNoIdButtons);
    }

    [Fact]
    public void ATVVFrameAccumulator_DelaysSyncWhenReceivedMidFrame()
    {
        var accumulator = new ATVVFrameAccumulator();
        int frameSize = 120;

        // Step 1: Send partial 40 bytes
        byte[] chunk1 = new byte[40];
        Array.Fill(chunk1, (byte)0x11);
        var frames1 = accumulator.Append(chunk1, frameSize);
        Assert.Empty(frames1);
        Assert.Equal(40, accumulator.PendingCount);

        // Step 2: Sync arrives mid-frame (predictor = 1000, stepIndex = 10)
        accumulator.Synchronize(1000, 10);

        // Step 3: Send remaining 80 bytes for the first frame
        byte[] chunk2 = new byte[80];
        Array.Fill(chunk2, (byte)0x11);
        var frames2 = accumulator.Append(chunk2, frameSize);
        Assert.Single(frames2);
        Assert.Equal(120, frames2[0].Length);
        Assert.Equal(0, accumulator.PendingCount);

        // First frame should NOT apply sync because sync was received mid-frame
        bool syncForFirstFrame = accumulator.TryConsumeSyncForNextFrame(out short p1, out byte s1);
        Assert.False(syncForFirstFrame);
        Assert.Equal(0, p1);
        Assert.Equal(0, s1);

        // Step 4: Next 120 bytes arrive
        byte[] chunk3 = new byte[120];
        Array.Fill(chunk3, (byte)0x22);
        var frames3 = accumulator.Append(chunk3, frameSize);
        Assert.Single(frames3);

        // Second frame MUST apply the delayed sync!
        bool syncForSecondFrame = accumulator.TryConsumeSyncForNextFrame(out short p2, out byte s2);
        Assert.True(syncForSecondFrame);
        Assert.Equal(1000, p2);
        Assert.Equal(10, s2);

        // Consumed sync should not apply again
        Assert.False(accumulator.TryConsumeSyncForNextFrame(out _, out _));
    }

    [Fact]
    public void ATVVFrameAccumulator_AppliesSyncImmediatelyWhenAccumulatorEmpty()
    {
        var accumulator = new ATVVFrameAccumulator();
        int frameSize = 120;

        // Accumulator is empty: sync arrives
        accumulator.Synchronize(500, 8);

        // Next frame arrives
        byte[] chunk = new byte[120];
        var frames = accumulator.Append(chunk, frameSize);
        Assert.Single(frames);

        // Sync should be consumed immediately for this frame
        bool applied = accumulator.TryConsumeSyncForNextFrame(out short p, out byte s);
        Assert.True(applied);
        Assert.Equal(500, p);
        Assert.Equal(8, s);
    }

    [Fact]
    public void RawInputReceiver_MapVirtualKeyToRemoteButton_MapsAllStandardKeys()
    {
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Up, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0x26));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Down, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0x28));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Left, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0x25));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Right, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0x27));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Ok, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0x0D));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Back, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0x1B));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Home, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0x24));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.VolumeUp, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0xAF));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.VolumeDown, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0xAE));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.Mute, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0xAD));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.PlayPause, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0xB3));
        Assert.Equal(VoiceInputBuddy.Core.Models.RemoteButton.None, VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.MapVirtualKeyToRemoteButton(0xFF));
    }

    [Fact]
    public void RawInputReceiver_IsKnownRemoteDevice_IdentifiesGoogleAndXiaomiRemotes()
    {
        // Google G9N9N Voice Remote path patterns
        Assert.True(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.IsKnownRemoteDevice(
            IntPtr.Zero, @"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&000218d1_PID&9450#9&30f9a2b5&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
        Assert.True(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.IsKnownRemoteDevice(
            IntPtr.Zero, @"\\?\HID#VID_18D1&PID_9450#9&30f9a2b5&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"));

        // Xiaomi Bluetooth Remote path patterns
        Assert.True(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.IsKnownRemoteDevice(
            IntPtr.Zero, @"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&00022717_PID&0030#..."));
        Assert.True(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.IsKnownRemoteDevice(
            IntPtr.Zero, @"\\?\HID#VID_2717&PID_0030#..."));
        Assert.True(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.IsKnownRemoteDevice(
            IntPtr.Zero, @"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&012717_PID&32b8_REV&00a4_test#..."));
    }

    [Fact]
    public void RawInputReceiver_IsKnownRemoteDevice_RejectsStandardPCKeyboards()
    {
        // Standard laptop internal PS/2 keyboard
        Assert.False(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.IsKnownRemoteDevice(
            IntPtr.Zero, @"\\?\ACPI#PNP0303#4&28f2378f&0#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}"));

        // Logitech USB Keyboard
        Assert.False(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.IsKnownRemoteDevice(
            IntPtr.Zero, @"\\?\HID#VID_046D&PID_C52B&MI_00#8&2120014a&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}"));

        // Microsoft USB Keyboard
        Assert.False(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.IsKnownRemoteDevice(
            IntPtr.Zero, @"\\?\HID#VID_045E&PID_0750#..."));

        // Unverified / arbitrary device path
        Assert.False(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.IsKnownRemoteDevice(
            IntPtr.Zero, "unknown_peripheral_path"));
    }

    [Fact]
    public async Task WinRtBluetoothManager_StopVoiceSessionDuringOpening_CancelsAndResetsToIdle()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var manager = new VoiceInputBuddy.Platform.Windows.Bluetooth.WinRtBluetoothManager();

        // Simulate Opening state via reflection
        var stateField = typeof(VoiceInputBuddy.Platform.Windows.Bluetooth.WinRtBluetoothManager)
            .GetField("_sessionState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(stateField);
        stateField.SetValue(manager, VoiceInputBuddy.Platform.Windows.Bluetooth.BluetoothSessionState.Opening);

        Assert.Equal(VoiceInputBuddy.Platform.Windows.Bluetooth.BluetoothSessionState.Opening, manager.SessionState);

        // User quickly releases voice key while in Opening
        bool stopped = await manager.StopVoiceSessionAsync();

        Assert.True(stopped);
        // Session state MUST immediately reset to Idle so it does not get stuck in Opening
        Assert.Equal(VoiceInputBuddy.Platform.Windows.Bluetooth.BluetoothSessionState.Idle, manager.SessionState);
    }

    [Fact]
    public void RawInputReceiver_EvaluateDeviceMatch_RejectsNormalKeyboardAndAdmitsRemote()
    {
        Guid targetContainer = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");
        string keyboardPath = @"\\?\ACPI#PNP0303#4&28f2378f&0#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";
        string remotePath = @"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&000218d1_PID&9450#9&30f9a2b5&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

        // When target remote is known: normal keyboard MUST be rejected!
        Assert.False(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.EvaluateDeviceMatch(
            targetContainer, (IntPtr)1234, keyboardPath));

        // When target remote is known: remote with matching path/VID is admitted even if interface container ID is null
        Assert.True(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.EvaluateDeviceMatch(
            targetContainer, (IntPtr)1234, remotePath));

        // When target remote is unknown (null): normal keyboard MUST be rejected!
        Assert.False(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.EvaluateDeviceMatch(
            null, (IntPtr)1234, keyboardPath));

        // When target remote is unknown (null): verified remote is admitted
        Assert.True(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.EvaluateDeviceMatch(
            null, (IntPtr)1234, remotePath));

        // Synthetic SendInput events must never re-enter the mapping pipeline.
        Assert.False(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.EvaluateDeviceMatch(
            null, IntPtr.Zero, ""));
        Assert.False(VoiceInputBuddy.Platform.Windows.Input.RawInputReceiver.EvaluateDeviceMatch(
            targetContainer, IntPtr.Zero, remotePath));
    }
}
