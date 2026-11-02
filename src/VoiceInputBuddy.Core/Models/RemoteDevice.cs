// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;

namespace VoiceInputBuddy.Core.Models;

public enum RemoteModelType
{
    Unknown = 0,
    XiaomiRC001,
    XiaomiRC003,
    XiaomiARN9,
    GoogleG9N9N,
    GoogleGY3LE
}

public partial class RemoteDevice : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private ulong _bluetoothAddress;

    [ObservableProperty]
    private Guid? _containerId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private RemoteModelType _modelType = RemoteModelType.Unknown;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int _batteryLevel = -1;

    [ObservableProperty]
    private float _gain = 1.0f;

    [ObservableProperty]
    private List<ButtonMapping> _mappings = CreateDefaultMappings(RemoteModelType.Unknown);

    public static List<ButtonMapping> CreateDefaultMappings(RemoteModelType type)
    {
        var list = new List<ButtonMapping>();
        void Add(RemoteButton btn, ButtonActionType click, MediaCommand media = MediaCommand.None, string? shortcut = null)
        {
            list.Add(new ButtonMapping
            {
                Button = btn,
                ClickAction = click,
                ClickMedia = media,
                ClickShortcut = shortcut,
                LongPressAction = ButtonActionType.ContinuousRepeat
            });
        }

        Add(RemoteButton.Up, ButtonActionType.KeyboardShortcut, shortcut: "Up");
        Add(RemoteButton.Down, ButtonActionType.KeyboardShortcut, shortcut: "Down");
        Add(RemoteButton.Left, ButtonActionType.KeyboardShortcut, shortcut: "Left");
        Add(RemoteButton.Right, ButtonActionType.KeyboardShortcut, shortcut: "Right");
        Add(RemoteButton.Ok, ButtonActionType.KeyboardShortcut, shortcut: "Enter");
        Add(RemoteButton.Back, ButtonActionType.KeyboardShortcut, shortcut: "Escape");
        Add(RemoteButton.Home, ButtonActionType.KeyboardShortcut, shortcut: "Alt+Tab");
        Add(RemoteButton.VolumeUp, ButtonActionType.MediaControl, media: MediaCommand.VolumeUp);
        Add(RemoteButton.VolumeDown, ButtonActionType.MediaControl, media: MediaCommand.VolumeDown);
        Add(RemoteButton.Mute, ButtonActionType.MediaControl, media: MediaCommand.Mute);
        Add(RemoteButton.PlayPause, ButtonActionType.MediaControl, media: MediaCommand.PlayPause);
        Add(RemoteButton.Menu, ButtonActionType.KeyboardShortcut, shortcut: "Apps");
        Add(RemoteButton.LiveTv, ButtonActionType.None);
        Add(RemoteButton.Power, ButtonActionType.None);

        if (type is RemoteModelType.GoogleG9N9N or RemoteModelType.GoogleGY3LE)
        {
            Add(RemoteButton.YouTube, ButtonActionType.None);
            Add(RemoteButton.Netflix, ButtonActionType.None);
            Add(RemoteButton.Input, ButtonActionType.None);
        }

        return list;
    }
}

public enum TargetInputTool
{
    WeChatInput,
    DoubaoInput,
    WindowsDictation,
    Typeless,
    Custom
}

public enum VoiceTriggerMode
{
    HoldToTalk,
    ClickToToggle
}

public class InputToolConfig
{
    public const string DefaultWeChatShortcut = "Ctrl+F5";
    public TargetInputTool Tool { get; set; } = TargetInputTool.WeChatInput;
    public VoiceTriggerMode TriggerMode { get; set; } = VoiceTriggerMode.HoldToTalk;
    public string Shortcut { get; set; } = DefaultWeChatShortcut;
    public string PreferredAudioInputDevice { get; set; } = "CABLE Output";
}

public class AppConfig
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "system"; // "system", "zh-CN", "en-US"
    public bool LaunchAtStartup { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool HasCompletedOnboarding { get; set; } = false;

    public InputToolConfig InputTool { get; set; } = new();
    public List<RemoteDevice> Devices { get; set; } = new();
    public string? SelectedDeviceId { get; set; }
}
