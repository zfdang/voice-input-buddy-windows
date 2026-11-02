using System.Runtime.InteropServices;

namespace VoiceInputBuddy.Platform.Windows.Input;

/// <summary>Stops a verified remote's held F5 from contaminating its IME chord.</summary>
public sealed class RemoteVoiceKeyGuard(Func<bool> voiceSessionActive) : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    private IntPtr _hook;
    private volatile bool _armed;
    private long _armedAt;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int id, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? module);

    private HookProc? _rootedCallback;

    // Install on the UI thread, which owns the hook's message pump.
    public bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        _rootedCallback = OnKeyboard;
        _hook = SetWindowsHookExW(13, _rootedCallback, GetModuleHandleW(null), 0);
        return _hook != IntPtr.Zero;
    }

    // Called only after Raw Input has verified the physical remote's identity.
    public void Arm()
    {
        _armedAt = Environment.TickCount64;
        _armed = true;
        NativeInputSender.SendKey(0x74, true);
    }

    public void Clear() => _armed = false;

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && _armed && Marshal.ReadInt32(data) == 0x74)
        {
            uint flags = (uint)Marshal.ReadInt32(data, 8);
            // Let injected cleanup events through. Observe no other keys.
            if ((flags & 0x10) == 0)
            {
                if (!voiceSessionActive() && Environment.TickCount64 - _armedAt > 2000)
                    _armed = false;
                else
                {
                    // The first down may already have reached another hook.
                    // Always balance it with the real physical key-up.
                    if (message.ToInt64() is 0x101 or 0x105) _armed = false;
                    else return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        _armed = false;
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.KeepAlive(_rootedCallback);
    }
}
