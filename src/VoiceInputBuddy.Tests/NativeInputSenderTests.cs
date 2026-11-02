// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using VoiceInputBuddy.Platform.Windows.Input;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class NativeInputSenderTests
{
    [Theory]
    [InlineData("Ctrl+Alt+V", new ushort[] { 0xA2, 0xA4 }, (ushort)'V')]
    [InlineData("RCtrl+RAlt+V", new ushort[] { 0xA3, 0xA5 }, (ushort)'V')]
    [InlineData("LShift+RShift", new ushort[] { 0xA0, 0xA1 }, (ushort)0)]
    [InlineData("Win+F5", new ushort[] { 0x5B }, (ushort)0x74)]
    [InlineData("RWin+Space", new ushort[] { 0x5C }, (ushort)0x20)]
    [InlineData("Up", new ushort[] { }, (ushort)0x26)]
    [InlineData("Down", new ushort[] { }, (ushort)0x28)]
    [InlineData("Left", new ushort[] { }, (ushort)0x25)]
    [InlineData("Right", new ushort[] { }, (ushort)0x27)]
    public void TryParseShortcut_ParsesValidShortcutsAndModifiers(string shortcut, ushort[] expectedModifiers, ushort expectedMainKey)
    {
        bool success = NativeInputSender.TryParseShortcut(shortcut, out var modifiers, out var mainKey);

        Assert.True(success);
        Assert.Equal(expectedModifiers, modifiers);
        Assert.Equal(expectedMainKey, mainKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+NonExistentKey")]
    [InlineData("InvalidModifier+A")]
    [InlineData("Ctrl+A+B")]
    [InlineData("F0")]
    [InlineData("F13")]
    public void TryParseShortcut_RejectsInvalidShortcuts(string shortcut)
    {
        bool success = NativeInputSender.TryParseShortcut(shortcut, out _, out _);
        Assert.False(success);
    }

    [Theory]
    [InlineData("F1", true)]
    [InlineData("F12", true)]
    [InlineData("Alt+9", true)]
    [InlineData("Home", true)]
    [InlineData("End", true)]
    [InlineData("PageUp", true)]
    [InlineData("PageDown", true)]
    public void TryParseShortcut_ValidatesNavigationAndFunctionKeys(string shortcut, bool expected)
    {
        bool success = NativeInputSender.TryParseShortcut(shortcut, out _, out _);
        Assert.Equal(expected, success);
    }

    [Theory]
    [InlineData(0x25, true)]  // VK_LEFT
    [InlineData(0x28, true)]  // VK_DOWN
    [InlineData(0x5B, true)]  // VK_LWIN
    [InlineData(0xA3, true)]  // VK_RCONTROL
    [InlineData(0xA2, false)] // VK_LCONTROL
    [InlineData(0x41, false)] // 'A'
    public void IsExtendedKey_ClassifiesExtendedKeysCorrectly(ushort vk, bool expected)
    {
        Assert.Equal(expected, NativeInputSender.IsExtendedKey(vk));
    }

    [Fact]
    public void CreateKeyInput_AttachesSyntheticEventMarker()
    {
        var input = NativeInputSender.CreateKeyInput(0x41, false);
        Assert.Equal(NativeInputSender.SyntheticEventMarker, input.u.ki.dwExtraInfo);
    }
}
