// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;
using System.Runtime.InteropServices;
using VoiceInputBuddy.Platform.Windows.Input;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class RemoteVoiceKeyGuardTests
{
    [Fact]
    public void LongVoiceSession_ContinuesToBlockPhysicalF5Repeats()
    {
        using var guard = new RemoteVoiceKeyGuard(() => true) { IsSuppressionEnabled = false };
        guard.BeginShortcutHold("Ctrl+F5");
        typeof(RemoteVoiceKeyGuard).GetField("_holdStartedAt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(guard, Environment.TickCount64 - 6000);
        var callback = typeof(RemoteVoiceKeyGuard).GetMethod("OnKeyboard", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IntPtr packet = Marshal.AllocHGlobal(24);
        try
        {
            for (int i = 0; i < 24; i++) Marshal.WriteByte(packet, i, 0);
            Marshal.WriteInt32(packet, 0, 0x74);
            Assert.Equal((IntPtr)1, (IntPtr)callback.Invoke(guard, [0, (IntPtr)0x100, packet])!);
            Assert.Equal((IntPtr)1, (IntPtr)callback.Invoke(guard, [0, (IntPtr)0x101, packet])!);
        }
        finally { Marshal.FreeHGlobal(packet); }
    }

    [Theory]
    [InlineData(0x100, 0, true)]
    [InlineData(0x101, 0x80, true)]
    [InlineData(0x100, 0x10, false)]
    [InlineData(0x101, 0x90, false)]
    public void OwnedF5_BlocksPhysicalEventsButPassesInjectedEvents_AndRestoresOnClear(int message, int flags, bool blocked)
    {
        using var guard = new RemoteVoiceKeyGuard(() => false) { IsSuppressionEnabled = false };
        guard.BeginShortcutHold("Ctrl+F5");
        var callback = typeof(RemoteVoiceKeyGuard).GetMethod("OnKeyboard", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IntPtr packet = Marshal.AllocHGlobal(24);
        try
        {
            for (int i = 0; i < 24; i++) Marshal.WriteByte(packet, i, 0);
            Marshal.WriteInt32(packet, 0, 0x74);
            Marshal.WriteInt32(packet, 8, flags);
            Assert.Equal(blocked, (IntPtr)callback.Invoke(guard, [0, (IntPtr)message, packet])! == (IntPtr)1);
            guard.Clear();
            Assert.NotEqual((IntPtr)1, (IntPtr)callback.Invoke(guard, [0, (IntPtr)message, packet])!);
            guard.BeginShortcutHold("Ctrl+Win");
            Assert.NotEqual((IntPtr)1, (IntPtr)callback.Invoke(guard, [0, (IntPtr)message, packet])!);
        }
        finally { Marshal.FreeHGlobal(packet); }
    }

    [Fact]
    public void ActiveGuard_BlocksF5Repeat_ButPreservesPhysicalRelease()
    {
        // Do not install a system hook or inject input in this test.
        bool sessionActive = true;
        using var guard = new RemoteVoiceKeyGuard(() => sessionActive);
        typeof(RemoteVoiceKeyGuard).GetField("_armed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(guard, true);
        var callback = typeof(RemoteVoiceKeyGuard).GetMethod("OnKeyboard", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IntPtr packet = Marshal.AllocHGlobal(24);
        try
        {
            for (int i = 0; i < 24; i++) Marshal.WriteByte(packet, i, 0);
            Marshal.WriteInt32(packet, 0, 0x74);
            Assert.Equal((IntPtr)1, (IntPtr)callback.Invoke(guard, [0, (IntPtr)0x100, packet])!);
            callback.Invoke(guard, [0, (IntPtr)0x101, packet]);
            Assert.False((bool)typeof(RemoteVoiceKeyGuard).GetField("_armed", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(guard)!);
            sessionActive = false;
            Assert.NotEqual((IntPtr)1, (IntPtr)callback.Invoke(guard, [0, (IntPtr)0x100, packet])!);
        }
        finally
        {
            Marshal.FreeHGlobal(packet);
        }
    }

    [Theory]
    [InlineData(true, 0x74, 0, true)]
    [InlineData(false, 0x74, 0, false)]
    [InlineData(true, 0x74, 0x10, false)]
    [InlineData(true, 0x41, 0, false)]
    public void SessionGuard_BeforeRawInputArms_OnlyBlocksPhysicalF5DuringSession(
        bool active, int key, int flags, bool blocked)
    {
        using var guard = new RemoteVoiceKeyGuard(() => active);
        var callback = typeof(RemoteVoiceKeyGuard).GetMethod("OnKeyboard", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IntPtr packet = Marshal.AllocHGlobal(24);
        try
        {
            for (int i = 0; i < 24; i++) Marshal.WriteByte(packet, i, 0);
            Marshal.WriteInt32(packet, 0, key);
            Marshal.WriteInt32(packet, 8, flags);
            var result = (IntPtr)callback.Invoke(guard, [0, (IntPtr)0x100, packet])!;
            Assert.Equal(blocked, result == (IntPtr)1);
        }
        finally
        {
            Marshal.FreeHGlobal(packet);
        }
    }

    [Fact]
    public void Guard_WhenSuppressionDisabled_DoesNotArmOrBlockF5()
    {
        using var guard = new RemoteVoiceKeyGuard(() => true)
        {
            IsSuppressionEnabled = false
        };
        guard.Arm();
        Assert.False((bool)typeof(RemoteVoiceKeyGuard).GetField("_armed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(guard)!);

        var callback = typeof(RemoteVoiceKeyGuard).GetMethod("OnKeyboard", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IntPtr packet = Marshal.AllocHGlobal(24);
        try
        {
            for (int i = 0; i < 24; i++) Marshal.WriteByte(packet, i, 0);
            Marshal.WriteInt32(packet, 0, 0x74);
            Assert.NotEqual((IntPtr)1, (IntPtr)callback.Invoke(guard, [0, (IntPtr)0x100, packet])!);
        }
        finally
        {
            Marshal.FreeHGlobal(packet);
        }
    }
}
