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
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    // Virtual-Key codes
    public const ushort VK_LCONTROL = 0xA2;
    public const ushort VK_LSHIFT = 0xA0;
    public const ushort VK_LMENU = 0xA4; // Alt
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_F1 = 0x70;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_VOLUME_MUTE = 0xAD;
    public const ushort VK_VOLUME_DOWN = 0xAE;
    public const ushort VK_VOLUME_UP = 0xAF;
    public const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
    public const ushort VK_MEDIA_PREV_TRACK = 0xB1;
    public const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    public static bool SendKey(ushort vk, bool isKeyUp)
    {
        var inputs = new INPUT[1];
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = isKeyUp ? KEYEVENTF_KEYUP : 0
                }
            }
        };

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

    public static bool SendShortcutString(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) return false;

        string[] parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = new List<ushort>();
        ushort mainKey = 0;

        foreach (var p in parts)
        {
            string upper = p.ToUpperInvariant();
            if (upper is "CTRL" or "CONTROL") modifiers.Add(VK_LCONTROL);
            else if (upper is "SHIFT") modifiers.Add(VK_LSHIFT);
            else if (upper is "ALT") modifiers.Add(VK_LMENU);
            else if (upper is "WIN" or "WINDOWS") modifiers.Add(VK_LWIN);
            else if (upper == "ENTER") mainKey = VK_RETURN;
            else if (upper is "ESC" or "ESCAPE") mainKey = VK_ESCAPE;
            else if (upper == "TAB") mainKey = VK_TAB;
            else if (upper == "F1") mainKey = VK_F1;
            else if (upper.Length == 1 && upper[0] >= 'A' && upper[0] <= 'Z') mainKey = upper[0];
            else if (upper.Length == 1 && upper[0] >= '0' && upper[0] <= '9') mainKey = upper[0];
        }

        if (mainKey == 0) return false;

        var inputs = new List<INPUT>();
        foreach (var mod in modifiers) inputs.Add(CreateKeyInput(mod, false));
        inputs.Add(CreateKeyInput(mainKey, false));
        inputs.Add(CreateKeyInput(mainKey, true));
        for (int i = modifiers.Count - 1; i >= 0; i--) inputs.Add(CreateKeyInput(modifiers[i], true));

        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        return sent == inputs.Count;
    }

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
        return SendKey(vk, false) && SendKey(vk, true);
    }

    public static bool SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var inputs = new List<INPUT>();
        foreach (char c in text)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE }
                }
            });
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP }
                }
            });
        }

        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        return sent == inputs.Count;
    }

    private static INPUT CreateKeyInput(ushort vk, bool isKeyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = isKeyUp ? KEYEVENTF_KEYUP : 0
            }
        }
    };
}
