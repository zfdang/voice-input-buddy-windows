using System.Reflection;
using System.Runtime.InteropServices;
using VoiceInputBuddy.Platform.Windows.Input;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class RemoteVoiceKeyGuardTests
{
    [Fact]
    public void ActiveGuard_BlocksF5Repeat_ButPreservesPhysicalRelease()
    {
        // Do not install a system hook or inject input in this test.
        using var guard = new RemoteVoiceKeyGuard(() => true);
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
            Assert.NotEqual((IntPtr)1, (IntPtr)callback.Invoke(guard, [0, (IntPtr)0x100, packet])!);
        }
        finally
        {
            Marshal.FreeHGlobal(packet);
        }
    }
}
