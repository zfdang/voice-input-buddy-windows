// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

namespace VoiceInputBuddy.Core.Models;

public enum RemoteButton
{
    None = 0,
    Up,
    Down,
    Left,
    Right,
    Ok,
    Back,
    Home,
    Menu,
    Power,
    VolumeUp,
    VolumeDown,
    Mute,
    Voice,
    YouTube,
    Netflix,
    Input,
    PlayPause,
    LiveTv,
    Custom
}

public enum ButtonActionType
{
    None = 0,
    MediaControl,
    KeyboardShortcut,
    TextSnippet,
    ContinuousRepeat
}

public enum MediaCommand
{
    None = 0,
    PlayPause,
    VolumeUp,
    VolumeDown,
    Mute,
    NextTrack,
    PreviousTrack
}

public class ButtonMapping
{
    public RemoteButton Button { get; set; }
    public ButtonActionType ClickAction { get; set; } = ButtonActionType.None;
    public string? ClickShortcut { get; set; }
    public string? ClickText { get; set; }
    public MediaCommand ClickMedia { get; set; } = MediaCommand.None;

    public ButtonActionType DoubleClickAction { get; set; } = ButtonActionType.None;
    public string? DoubleClickShortcut { get; set; }
    public string? DoubleClickText { get; set; }
    public MediaCommand DoubleClickMedia { get; set; } = MediaCommand.None;

    public ButtonActionType LongPressAction { get; set; } = ButtonActionType.None;
    public string? LongPressShortcut { get; set; }
    public string? LongPressText { get; set; }
    public MediaCommand LongPressMedia { get; set; } = MediaCommand.None;
}
