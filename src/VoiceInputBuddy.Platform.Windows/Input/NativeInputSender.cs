// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using VoiceInputBuddy.Core.Models;

namespace VoiceInputBuddy.Platform.Windows.Input;

[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint type;
    public InputUnion u;
}

[StructLayout(LayoutKind.Explicit)]
public struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public UIntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public UIntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
}

public static class NativeInputSender
{
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    // Virtual-Key codes
    public const ushort VK_LCONTROL = 0xA2;
    public const ushort VK_RCONTROL = 0xA3;
    public const ushort VK_LSHIFT = 0xA0;
    public const ushort VK_LMENU = 0xA4; // Alt
    public const ushort VK_RMENU = 0xA5;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;
    public const ushort VK_BACK = 0x08;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_SPACE = 0x20;
    public const ushort VK_LEFT = 0x25;
    public const ushort VK_UP = 0x26;
    public const ushort VK_RIGHT = 0x27;
    public const ushort VK_DOWN = 0x28;
    public const ushort VK_PRIOR = 0x21; // Page Up
    public const ushort VK_NEXT = 0x22;  // Page Down
    public const ushort VK_END = 0x23;
    public const ushort VK_HOME = 0x24;
    public const ushort VK_INSERT = 0x2D;
    public const ushort VK_DELETE = 0x2E;
    public const ushort VK_APPS = 0x5D;
    public const ushort VK_F1 = 0x70;
    public const ushort VK_VOLUME_MUTE = 0xAD;
    public const ushort VK_VOLUME_DOWN = 0xAE;
    public const ushort VK_VOLUME_UP = 0xAF;
    public const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
    public const ushort VK_MEDIA_PREV_TRACK = 0xB1;
    public const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;

    public static readonly UIntPtr SyntheticEventMarker = new(0x56494201); // "VIB1"
    private static readonly System.Collections.Concurrent.ConcurrentBag<ushort> _heldSyntheticKeys = new();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsKeyPhysicallyDown(ushort vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static void TrackHeldKey(ushort vk) => _heldSyntheticKeys.Add(vk);

    public static void ReleaseAllHeldSyntheticKeys()
    {
        var keysToRelease = new HashSet<ushort>();
        while (_heldSyntheticKeys.TryTake(out var key)) keysToRelease.Add(key);
        if (keysToRelease.Count == 0) return;

        var inputs = keysToRelease.Select(k => CreateKeyInput(k, isKeyUp: true)).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static bool IsExtendedKey(ushort vk) => vk switch
    {
        VK_LWIN or VK_RWIN or 0x5D /* VK_APPS */ => true,
        VK_RCONTROL or VK_RMENU => true,
        VK_LEFT or VK_UP or VK_RIGHT or VK_DOWN => true,
        0x21 /* VK_PRIOR/PageUp */ or 0x22 /* VK_NEXT/PageDown */ or 0x23 /* VK_END */ or 0x24 /* VK_HOME */ or 0x2D /* VK_INSERT */ or VK_DELETE => true,
        VK_VOLUME_MUTE or VK_VOLUME_DOWN or VK_VOLUME_UP or VK_MEDIA_NEXT_TRACK or VK_MEDIA_PREV_TRACK or VK_MEDIA_PLAY_PAUSE => true,
        _ => false
    };

    public static bool SendKey(ushort vk, bool isKeyUp)
    {
        var inputs = new INPUT[1];
        inputs[0] = CreateKeyInput(vk, isKeyUp);
        uint sent = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        return sent == 1;
    }

    public static bool SendModifiedKey(ushort modifierVk, ushort keyVk)
    {
        var inputs = new INPUT[4];
        inputs[0] = CreateKeyInput(modifierVk, false);
        inputs[1] = CreateKeyInput(keyVk, false);
        inputs[2] = CreateKeyInput(keyVk, true);
        inputs[3] = CreateKeyInput(modifierVk, true);

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    public static bool TryParseShortcut(string shortcut, out List<ushort> modifiers, out ushort mainKey)
    {
        modifiers = new List<ushort>();
        mainKey = 0;
        if (string.IsNullOrWhiteSpace(shortcut)) return false;

        string[] parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            string upper = p.ToUpperInvariant();
            if (upper is "CTRL" or "CONTROL" or "LCTRL") modifiers.Add(VK_LCONTROL);
            else if (upper == "RCTRL") modifiers.Add(VK_RCONTROL);
            else if (upper is "SHIFT" or "LSHIFT") modifiers.Add(VK_LSHIFT);
            else if (upper == "RSHIFT") modifiers.Add(0xA1 /* VK_RSHIFT */);
            else if (upper is "ALT" or "LALT") modifiers.Add(VK_LMENU);
            else if (upper == "RALT") modifiers.Add(VK_RMENU);
            else if (upper is "WIN" or "WINDOWS" or "LWIN") modifiers.Add(VK_LWIN);
            else if (upper == "RWIN") modifiers.Add(VK_RWIN);
            else
            {
                ushort parsedKey = 0;
                if (upper is "ENTER" or "RETURN") parsedKey = VK_RETURN;
                else if (upper is "ESC" or "ESCAPE") parsedKey = VK_ESCAPE;
                else if (upper == "TAB") parsedKey = VK_TAB;
                else if (upper == "SPACE") parsedKey = VK_SPACE;
                else if (upper is "BACK" or "BACKSPACE") parsedKey = VK_BACK;
                else if (upper is "DEL" or "DELETE") parsedKey = VK_DELETE;
                else if (upper == "UP") parsedKey = VK_UP;
                else if (upper == "DOWN") parsedKey = VK_DOWN;
                else if (upper == "LEFT") parsedKey = VK_LEFT;
                else if (upper == "RIGHT") parsedKey = VK_RIGHT;
                else if (upper == "HOME") parsedKey = VK_HOME;
                else if (upper == "END") parsedKey = VK_END;
                else if (upper is "PAGEUP" or "PGUP") parsedKey = VK_PRIOR;
                else if (upper is "PAGEDOWN" or "PGDN") parsedKey = VK_NEXT;
                else if (upper is "INSERT" or "INS") parsedKey = VK_INSERT;
                else if (upper is "APPS" or "MENU") parsedKey = 0x5D;
                else if (upper.StartsWith("F") && int.TryParse(upper[1..], out int fNum) && fNum >= 1 && fNum <= 12)
                {
                    parsedKey = (ushort)(VK_F1 + (fNum - 1));
                }
                else if (upper.Length == 1 && upper[0] >= 'A' && upper[0] <= 'Z') parsedKey = upper[0];
                else if (upper.Length == 1 && upper[0] >= '0' && upper[0] <= '9') parsedKey = upper[0];
                else return false;

                if (mainKey != 0) return false; // Duplicate main key is invalid
                mainKey = parsedKey;
            }
        }

        return mainKey != 0 || modifiers.Count > 0;
    }

    public static bool SendShortcutDown(string shortcut)
    {
        if (!TryParseShortcut(shortcut, out var modifiers, out ushort mainKey)) return false;
        var inputs = new List<INPUT>();
        foreach (var mod in modifiers)
        {
            inputs.Add(CreateKeyInput(mod, false));
            TrackHeldKey(mod);
        }
        if (mainKey != 0)
        {
            inputs.Add(CreateKeyInput(mainKey, false));
            TrackHeldKey(mainKey);
        }
        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        return sent == inputs.Count;
    }

    public static bool SendShortcutUp(string shortcut)
    {
        if (!TryParseShortcut(shortcut, out var modifiers, out ushort mainKey)) return false;
        var inputs = new List<INPUT>();
        if (mainKey != 0) inputs.Add(CreateKeyInput(mainKey, true));
        // Do not inject KeyUp for modifiers that the user is actively holding down on their physical keyboard
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (!IsKeyPhysicallyDown(modifiers[i]))
            {
                inputs.Add(CreateKeyInput(modifiers[i], true));
            }
        }
        if (inputs.Count == 0) return true;
        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        return sent == inputs.Count;
    }

    public static bool SendShortcutClick(string shortcut)
    {
        if (!TryParseShortcut(shortcut, out var modifiers, out ushort mainKey)) return false;

        // Only press and release modifiers that are not already physically held by the user
        var ownedModifiers = modifiers.Where(m => !IsKeyPhysicallyDown(m)).ToList();

        var inputs = new List<INPUT>();
        foreach (var mod in ownedModifiers) inputs.Add(CreateKeyInput(mod, false));
        if (mainKey != 0)
        {
            inputs.Add(CreateKeyInput(mainKey, false));
            inputs.Add(CreateKeyInput(mainKey, true));
        }
        for (int i = ownedModifiers.Count - 1; i >= 0; i--) inputs.Add(CreateKeyInput(ownedModifiers[i], true));

        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        return sent == inputs.Count;
    }

    public static bool SendShortcutString(string shortcut) => SendShortcutClick(shortcut);

    public static bool SendMedia(MediaCommand command)
    {
        ushort vk = command switch
        {
            MediaCommand.PlayPause => VK_MEDIA_PLAY_PAUSE,
            MediaCommand.VolumeUp => VK_VOLUME_UP,
            MediaCommand.VolumeDown => VK_VOLUME_DOWN,
            MediaCommand.Mute => VK_VOLUME_MUTE,
            MediaCommand.NextTrack => VK_MEDIA_NEXT_TRACK,
            MediaCommand.PreviousTrack => VK_MEDIA_PREV_TRACK,
            _ => 0
        };

        if (vk == 0) return false;
        var inputs = new[] { CreateKeyInput(vk, false), CreateKeyInput(vk, true) };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    public static bool SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        string normalized = text.Replace("\r\n", "\r").Replace("\n", "\r");
        var inputs = new List<INPUT>();
        foreach (char c in normalized)
        {
            if (c == '\r')
            {
                inputs.Add(CreateKeyInput(VK_RETURN, false));
                inputs.Add(CreateKeyInput(VK_RETURN, true));
                continue;
            }

            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE, dwExtraInfo = SyntheticEventMarker }
                }
            });
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, dwExtraInfo = SyntheticEventMarker }
                }
            });
        }

        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        return sent == inputs.Count;
    }

    public static INPUT CreateKeyInput(ushort vk, bool isKeyUp)
    {
        uint flags = isKeyUp ? KEYEVENTF_KEYUP : 0;
        if (IsExtendedKey(vk))
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        return new()
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)MapVirtualKeyW(vk, 0),
                    dwFlags = flags,
                    dwExtraInfo = SyntheticEventMarker
                }
            }
        };
    }
}
