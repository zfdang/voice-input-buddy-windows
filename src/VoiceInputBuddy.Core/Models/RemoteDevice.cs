namespace VoiceInputBuddy.Core.Models;

public enum RemoteModelType
{
    Unknown = 0,
    XiaomiRC001,
    XiaomiRC003,
    GoogleG9N9N,
    GoogleGY3LE
}

public class RemoteDevice
{
    public string Id { get; set; } = string.Empty;
    public ulong BluetoothAddress { get; set; }
    public Guid? ContainerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public RemoteModelType ModelType { get; set; } = RemoteModelType.Unknown;
    public bool IsConnected { get; set; }
    public int BatteryLevel { get; set; } = -1;
    public float Gain { get; set; } = 1.0f;
    public List<ButtonMapping> Mappings { get; set; } = CreateDefaultMappings(RemoteModelType.Unknown);

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
    public TargetInputTool Tool { get; set; } = TargetInputTool.WeChatInput;
    public VoiceTriggerMode TriggerMode { get; set; } = VoiceTriggerMode.HoldToTalk;
    public string Shortcut { get; set; } = "Ctrl+Win";
    public string PreferredAudioInputDevice { get; set; } = "CABLE Output";
}

public class AppConfig
{
    public string Language { get; set; } = "system"; // "system", "zh-CN", "en-US"
    public bool LaunchAtStartup { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool HasCompletedOnboarding { get; set; } = false;

    public InputToolConfig InputTool { get; set; } = new();
    public List<RemoteDevice> Devices { get; set; } = new();
    public string? SelectedDeviceId { get; set; }
}
