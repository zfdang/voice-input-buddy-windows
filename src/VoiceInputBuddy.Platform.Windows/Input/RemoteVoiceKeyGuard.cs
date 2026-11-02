// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using VoiceInputBuddy.Core.Diagnostics;

namespace VoiceInputBuddy.Platform.Windows.Input;

/// <summary>Stops a verified remote's held F5 from contaminating its IME chord.</summary>
public sealed class RemoteVoiceKeyGuard(Func<bool> voiceSessionActive) : IDisposable
{
    private const long MaxHoldDurationMs = 30000; // 30s safety timeout to prevent permanent suppression

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    private IntPtr _hook;
    private volatile bool _armed;
    private long _armedAt;
    private bool _ownsF5;
    private long _holdStartedAt;
    private int _blockedDowns;
    private int _blockedUps;

    public void BeginShortcutHold(string shortcut)
    {
        _ownsF5 = NativeInputSender.TryParseShortcut(shortcut, out _, out var key) && key == 0x74;
        _holdStartedAt = Environment.TickCount64;
        _blockedDowns = _blockedUps = 0;
    }

    public void EndShortcutHold()
    {
        if (_ownsF5)
            AppLogger.Info($"F5 hold finished: PhysicalDownsBlocked={_blockedDowns}, PhysicalUpsBlocked={_blockedUps}", "RemoteVoiceKeyGuard");
        _ownsF5 = false;
    }

    public bool IsSuppressionEnabled { get; set; } = true;

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
        if (!IsSuppressionEnabled) return;
        _armedAt = Environment.TickCount64;
        _armed = true;
        bool released = NativeInputSender.SendKey(0x74, true);
        AppLogger.Info($"Remote F5 guard armed; cleanup key-up accepted={released}", "RemoteVoiceKeyGuard");
    }

    public void Clear()
    {
        _armed = false;
        EndShortcutHold();
    }

    private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0 && Marshal.ReadInt32(data) == 0x74)
            {
                uint flags = (uint)Marshal.ReadInt32(data, 8);
                // Let injected cleanup events through. Observe no other keys.
                if ((flags & 0x10) == 0)
                {
                    // The app owns the injected F5 until audio drain completes.
                    // Physical repeats can re-trigger the IME; physical key-up
                    // can release the injected hold before our balanced key-up.
                    if (_ownsF5)
                    {
                        if ((Environment.TickCount64 - _holdStartedAt) > MaxHoldDurationMs)
                        {
                            _ownsF5 = false;
                        }
                        else
                        {
                            if (message.ToInt64() is 0x101 or 0x105) _blockedUps++;
                            else _blockedDowns++;
                            return (IntPtr)1;
                        }
                    }
                    if (!IsSuppressionEnabled) return CallNextHookEx(_hook, code, message, data);
                    bool sessionActive = voiceSessionActive();
                    if (!sessionActive && _armed && Environment.TickCount64 - _armedAt > 2000)
                        _armed = false;
                    if (sessionActive || _armed)
                    {
                        // The first down may already have reached another hook.
                        // Always balance it with the real physical key-up.
                        if (message.ToInt64() is 0x101 or 0x105) _armed = false;
                        else return (IntPtr)1;
                    }
                }
            }
        }
        catch { }

        return CallNextHookEx(_hook, code, message, data);
    }

    ~RemoteVoiceKeyGuard()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        EndShortcutHold();
        _armed = false;
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        if (disposing)
        {
            GC.KeepAlive(_rootedCallback);
        }
    }
}
