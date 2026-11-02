// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceInputBuddy.Core.Bluetooth;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Diagnostics;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Platform.Windows.Audio;
using VoiceInputBuddy.Platform.Windows.Bluetooth;
using VoiceInputBuddy.Platform.Windows.Services;

namespace VoiceInputBuddy.App.ViewModels;

public partial class DevicesViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly IBluetoothManager _bt;
    private readonly Action? _openOnboarding;

    [ObservableProperty]
    private ObservableCollection<RemoteDevice> _discoveredDevices = new();

    [ObservableProperty]
    private RemoteDevice? _selectedDevice;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private float _deviceGain = 1.0f;

    public string ConnectedDeviceName => _bt.IsConnected ? _bt.ConnectedDeviceName : "未连接设备";
    public bool IsConnected => _bt.IsConnected;

    public string VoiceIntegrationSummary
    {
        get
        {
            var tool = _config.Current.InputTool;
            string toolName = tool.Tool switch
            {
                TargetInputTool.WeChatInput => "微信输入法",
                TargetInputTool.DoubaoInput => "豆包输入法",
                TargetInputTool.WindowsDictation => "Windows 听写",
                TargetInputTool.Typeless => "Typeless",
                _ => "自定义工具"
            };
            string mode = tool.TriggerMode == VoiceTriggerMode.HoldToTalk ? "长按说话" : "点按切换";
            return $"{toolName} ({mode} · {tool.Shortcut})";
        }
    }

    public DevicesViewModel(ConfigService config, IBluetoothManager bt, Action? openOnboarding = null)
    {
        _config = config;
        _bt = bt;
        _openOnboarding = openOnboarding;

        foreach (var dev in _config.Current.Devices)
        {
            DiscoveredDevices.Add(dev);
        }

        _selectedDevice = DiscoveredDevices.FirstOrDefault(d => d.Id == _config.Current.SelectedDeviceId);
        if (_selectedDevice != null)
        {
            _bt.CurrentGain = _selectedDevice.Gain;
        }

        _bt.DeviceDiscovered += (dev) =>
        {
            App.Current?.Dispatcher?.BeginInvoke(() =>
            {
                var existing = DiscoveredDevices.FirstOrDefault(d => d.BluetoothAddress == dev.BluetoothAddress);
                if (existing == null)
                {
                    DiscoveredDevices.Add(dev);
                }
                else
                {
                    existing.Name = dev.Name;
                    existing.ModelType = dev.ModelType;
                }
            });
        };

        _bt.ConnectionStatusChanged += (connected, name) =>
        {
            App.Current?.Dispatcher?.BeginInvoke(() =>
            {
                OnPropertyChanged(nameof(ConnectedDeviceName));
                OnPropertyChanged(nameof(IsConnected));
                foreach (var dev in DiscoveredDevices)
                {
                    dev.IsConnected = connected && (dev.Name == name || dev.BluetoothAddress.ToString("X") == _config.Current.SelectedDeviceId);
                }
            });
        };
    }

    [RelayCommand]
    public void AddDevice()
    {
        _openOnboarding?.Invoke();
    }

    [RelayCommand]
    public void ToggleScan()
    {
        IsScanning = !IsScanning;
        if (IsScanning)
        {
            _bt.StartScanning();
        }
        else
        {
            _bt.StopScanning();
        }
    }

    [RelayCommand]
    public async Task ConnectDeviceAsync(RemoteDevice? device)
    {
        if (device == null) return;
        bool ok = await _bt.ConnectAsync(device.BluetoothAddress);
        if (ok)
        {
            SelectedDevice = device;
            _bt.CurrentGain = device.Gain;
            device.IsConnected = true;
            device.ContainerId = _bt.ConnectedDeviceContainerId;
            if (device.Mappings == null || device.Mappings.Count == 0)
            {
                device.Mappings = RemoteDevice.CreateDefaultMappings(device.ModelType);
            }
            _config.Current.SelectedDeviceId = device.Id;
            if (!_config.Current.Devices.Any(d => d.Id == device.Id))
            {
                _config.Current.Devices.Add(device);
            }
            _config.Save();
            OnPropertyChanged(nameof(ConnectedDeviceName));
            OnPropertyChanged(nameof(IsConnected));
        }
    }

    [RelayCommand]
    public void DisconnectDevice()
    {
        _bt.Disconnect();
        if (SelectedDevice != null)
        {
            SelectedDevice.IsConnected = false;
        }
        OnPropertyChanged(nameof(ConnectedDeviceName));
        OnPropertyChanged(nameof(IsConnected));
    }

    [RelayCommand]
    public void RemoveDevice(RemoteDevice? device)
    {
        if (device == null) return;
        if (device.BluetoothAddress == SelectedDevice?.BluetoothAddress)
        {
            _bt.Disconnect();
            SelectedDevice = null;
        }
        DiscoveredDevices.Remove(device);
        _config.Current.Devices.RemoveAll(d => d.Id == device.Id);
        if (_config.Current.SelectedDeviceId == device.Id)
        {
            _config.Current.SelectedDeviceId = null;
        }
        _config.Save();
        OnPropertyChanged(nameof(ConnectedDeviceName));
        OnPropertyChanged(nameof(IsConnected));
    }

    [RelayCommand]
    public void SaveGain(RemoteDevice? device)
    {
        if (device == null) return;
        _bt.CurrentGain = device.Gain;
        _config.Save();
    }

    partial void OnSelectedDeviceChanged(RemoteDevice? value)
    {
        if (value != null)
        {
            _bt.CurrentGain = value.Gain;
            _config.Current.SelectedDeviceId = value.Id;
            if (value.Mappings == null || value.Mappings.Count == 0)
            {
                value.Mappings = RemoteDevice.CreateDefaultMappings(value.ModelType);
            }
            _config.Save();
        }
    }
}

public partial class MappingItemViewModel : ObservableObject
{
    public ButtonMapping Source { get; }
    public RemoteButton Button => Source.Button;
    public string ButtonName => Button switch
    {
        RemoteButton.Power => "电源键", RemoteButton.Voice => "语音键",
        RemoteButton.Up => "上键", RemoteButton.Down => "下键",
        RemoteButton.Left => "左键", RemoteButton.Right => "右键",
        RemoteButton.Ok => "确定键", RemoteButton.Back => "返回键",
        RemoteButton.Home => "主页键", RemoteButton.Menu => "菜单键",
        RemoteButton.VolumeUp => "音量 +", RemoteButton.VolumeDown => "音量 −",
        RemoteButton.LiveTv => "TV 键", RemoteButton.Mute => "静音键",
        RemoteButton.PlayPause => "播放 / 暂停", _ => Button.ToString()
    };

    [ObservableProperty]
    private ButtonActionType _clickAction;

    [ObservableProperty]
    private string _clickShortcut = string.Empty;

    [ObservableProperty]
    private MediaCommand _clickMedia;

    [ObservableProperty]
    private string _clickText = string.Empty;

    [ObservableProperty]
    private string _parameterText = string.Empty;

    [ObservableProperty]
    private ButtonActionType _longPressAction;

    public System.Windows.Visibility TextOrShortcutVisibility =>
        (ClickAction == ButtonActionType.KeyboardShortcut || ClickAction == ButtonActionType.TextSnippet)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility MediaVisibility =>
        ClickAction == ButtonActionType.MediaControl
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public static ButtonActionType[] AvailableActions { get; } =
        [ButtonActionType.None, ButtonActionType.KeyboardShortcut, ButtonActionType.MediaControl, ButtonActionType.TextSnippet];

    public static MediaCommand[] AvailableMediaCommands { get; } =
        [MediaCommand.PlayPause, MediaCommand.VolumeUp, MediaCommand.VolumeDown, MediaCommand.Mute, MediaCommand.NextTrack, MediaCommand.PreviousTrack];

    public static ButtonActionType[] AvailableLongPressActions { get; } =
        [ButtonActionType.None, ButtonActionType.ContinuousRepeat];

    private readonly Action? _onModified;

    public MappingItemViewModel(ButtonMapping source, Action? onModified = null)
    {
        Source = source;
        _onModified = onModified;
        ClickAction = source.ClickAction;
        ClickShortcut = source.ClickShortcut ?? string.Empty;
        ClickMedia = source.ClickMedia != MediaCommand.None ? source.ClickMedia : MediaCommand.PlayPause;
        ClickText = source.ClickText ?? string.Empty;
        LongPressAction = source.LongPressAction;

        if (source.ClickAction == ButtonActionType.TextSnippet)
        {
            ParameterText = ClickText;
        }
        else
        {
            ParameterText = ClickShortcut;
        }
    }

    partial void OnClickActionChanged(ButtonActionType value)
    {
        OnPropertyChanged(nameof(TextOrShortcutVisibility));
        OnPropertyChanged(nameof(MediaVisibility));

        if (value == ButtonActionType.TextSnippet && string.IsNullOrEmpty(ParameterText))
        {
            ParameterText = ClickText;
        }
        else if (value == ButtonActionType.KeyboardShortcut && string.IsNullOrEmpty(ParameterText))
        {
            ParameterText = ClickShortcut;
        }

        ApplyBack();
        _onModified?.Invoke();
    }

    partial void OnParameterTextChanged(string value)
    {
        ApplyBack();
        _onModified?.Invoke();
    }

    partial void OnClickMediaChanged(MediaCommand value)
    {
        ApplyBack();
        _onModified?.Invoke();
    }

    partial void OnLongPressActionChanged(ButtonActionType value)
    {
        ApplyBack();
        _onModified?.Invoke();
    }

    public void ApplyBack()
    {
        Source.ClickAction = ClickAction;
        Source.LongPressAction = LongPressAction;

        if (ClickAction == ButtonActionType.KeyboardShortcut)
        {
            ClickShortcut = ParameterText;
            Source.ClickShortcut = string.IsNullOrWhiteSpace(ParameterText) ? null : ParameterText.Trim();
            Source.ClickText = null;
            Source.ClickMedia = MediaCommand.None;
        }
        else if (ClickAction == ButtonActionType.TextSnippet)
        {
            ClickText = ParameterText;
            Source.ClickText = string.IsNullOrWhiteSpace(ParameterText) ? null : ParameterText;
            Source.ClickShortcut = null;
            Source.ClickMedia = MediaCommand.None;
        }
        else if (ClickAction == ButtonActionType.MediaControl)
        {
            Source.ClickMedia = ClickMedia;
            Source.ClickShortcut = null;
            Source.ClickText = null;
        }
        else
        {
            Source.ClickShortcut = null;
            Source.ClickText = null;
            Source.ClickMedia = MediaCommand.None;
        }
    }
}

public partial class MappingViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly Dictionary<string, List<MappingItemViewModel>> _drafts = new();

    [ObservableProperty]
    private ObservableCollection<MappingItemViewModel> _currentMappings = new();

    public IReadOnlyList<RemoteDevice> Devices => _config.Current.Devices;

    [ObservableProperty]
    private RemoteDevice? _selectedDevice;

    [ObservableProperty]
    private MappingItemViewModel? _selectedMapping;

    [ObservableProperty]
    private bool _isMappingSelectionLocked = true;

    [ObservableProperty]
    private bool _customMappingEnabled = true;

    [ObservableProperty]
    private RemoteButton _activeHardwareButton = RemoteButton.None;

    public bool IsG9N9N => SelectedDevice?.ModelType is RemoteModelType.GoogleG9N9N or RemoteModelType.GoogleGY3LE;

    public string DeviceNote => IsG9N9N
        ? "G9N9N 仅支持 OK＋方向键发送 Enter，单键映射不可用。短按 OK＋任意方向键可发送 Enter。"
        : "选择实体按键设置单击与长按动作。点击左侧遥控器按键快速定位，修改后自动保存。";

    public MappingViewModel(ConfigService config)
    {
        _config = config;
        _selectedDevice = config.Current.Devices.FirstOrDefault(d => d.Id == config.Current.SelectedDeviceId);
        LoadMappings();
    }

    partial void OnSelectedDeviceChanged(RemoteDevice? value)
    {
        OnPropertyChanged(nameof(DeviceNote));
        OnPropertyChanged(nameof(IsG9N9N));
        LoadMappings();
    }

    [RelayCommand]
    public void SelectButton(string buttonName)
    {
        if (Enum.TryParse<RemoteButton>(buttonName, out var btn))
        {
            var match = CurrentMappings.FirstOrDefault(m => m.Button == btn);
            if (match != null)
            {
                SelectedMapping = match;
            }
        }
    }

    [RelayCommand]
    public void ClearSelectedMapping()
    {
        if (SelectedMapping != null)
        {
            SelectedMapping.ClickAction = ButtonActionType.None;
            SelectedMapping.LongPressAction = ButtonActionType.None;
            SelectedMapping.ParameterText = string.Empty;
            SelectedMapping.ClickShortcut = string.Empty;
            SelectedMapping.ClickText = string.Empty;
            SelectedMapping.ApplyBack();
            SaveMappings();
        }
    }

    [RelayCommand]
    public void ResetToDefaults()
    {
        var selectedDev = SelectedDevice;
        if (selectedDev != null)
        {
            selectedDev.Mappings = RemoteDevice.CreateDefaultMappings(selectedDev.ModelType);
            _drafts.Remove(selectedDev.Id);
            _config.Save();
            LoadMappings();
        }
    }

    public void LoadMappings()
    {
        CurrentMappings.Clear();
        OnPropertyChanged(nameof(Devices));
        if (SelectedDevice == null && _config.Current.Devices.Count > 0)
        {
            SelectedDevice = _config.Current.Devices.FirstOrDefault(d => d.Id == _config.Current.SelectedDeviceId)
                ?? _config.Current.Devices[0];
            return;
        }
        var selectedDev = SelectedDevice;
        if (selectedDev != null)
        {
            if (_drafts.TryGetValue(selectedDev.Id, out var draft))
            {
                foreach (var item in draft) CurrentMappings.Add(item);
                SelectedMapping = CurrentMappings.FirstOrDefault();
                return;
            }
            if (selectedDev.Mappings == null || selectedDev.Mappings.Count == 0)
            {
                selectedDev.Mappings = RemoteDevice.CreateDefaultMappings(selectedDev.ModelType);
                _config.Save();
            }
            var buttons = selectedDev.ModelType is RemoteModelType.XiaomiRC001 or RemoteModelType.XiaomiRC003
                ? new[] { RemoteButton.Power, RemoteButton.Up, RemoteButton.Left, RemoteButton.Right, RemoteButton.Ok,
                    RemoteButton.Down, RemoteButton.Back, RemoteButton.Home, RemoteButton.Menu,
                    RemoteButton.VolumeUp, RemoteButton.VolumeDown, RemoteButton.LiveTv }
                : selectedDev.Mappings.Select(m => m.Button).ToArray();
            foreach (var button in buttons)
            {
                var mapping = selectedDev.Mappings.FirstOrDefault(m => m.Button == button)
                    ?? new ButtonMapping { Button = button, ClickAction = ButtonActionType.None };
                CurrentMappings.Add(new MappingItemViewModel(mapping, OnMappingItemModified));
            }
            _drafts[selectedDev.Id] = CurrentMappings.ToList();
        }
        SelectedMapping = CurrentMappings.FirstOrDefault();
    }

    private void OnMappingItemModified()
    {
        var selectedDev = SelectedDevice;
        if (selectedDev != null)
        {
            var visibleButtons = CurrentMappings.Select(i => i.Button).ToHashSet();
            selectedDev.Mappings = selectedDev.Mappings.Where(m => !visibleButtons.Contains(m.Button))
                .Concat(CurrentMappings.Select(i => i.Source)).ToList();
            _config.Save();
        }
    }

    [RelayCommand]
    public void SaveMappings()
    {
        var selectedDev = SelectedDevice;
        if (selectedDev != null)
        {
            foreach (var item in CurrentMappings)
            {
                item.ApplyBack();
            }
            var visibleButtons = CurrentMappings.Select(i => i.Button).ToHashSet();
            selectedDev.Mappings = selectedDev.Mappings.Where(m => !visibleButtons.Contains(m.Button))
                .Concat(CurrentMappings.Select(i => i.Source)).ToList();
            _config.Save();
        }
    }
}

public partial class IntegrationsViewModel : ObservableObject
{
    private readonly ConfigService _config;

    [ObservableProperty]
    private TargetInputTool _selectedTool;

    [ObservableProperty]
    private VoiceTriggerMode _triggerMode;

    [ObservableProperty]
    private string _shortcutText = string.Empty;

    partial void OnShortcutTextChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentShortcutDisplay));
    }

    [ObservableProperty]
    private bool _saveNoticeVisible;

    public string CurrentToolDisplayName => SelectedTool switch
    {
        TargetInputTool.WeChatInput => "微信输入法",
        TargetInputTool.DoubaoInput => "豆包输入法 / 客户端",
        TargetInputTool.WindowsDictation => "Windows 语音听写 (Win+H)",
        TargetInputTool.Typeless => "Typeless 智能转写",
        _ => "自定义快捷键"
    };

    public string CurrentShortcutDisplay => string.IsNullOrWhiteSpace(ShortcutText) ? "未配置" : ShortcutText;

    public string CurrentTriggerModeDisplay => TriggerMode == VoiceTriggerMode.HoldToTalk ? "按住说话 (Hold to Talk)" : "点按切换 (Click to Toggle)";

    public string VirtualCableStatusDescription
    {
        get
        {
            var status = VirtualAudioAuditor.AuditCableInputDevice();
            return status switch
            {
                DriverReadinessStatus.Ready => "CABLE Input (VB-Audio Virtual Cable) 驱动已就绪",
                DriverReadinessStatus.NotInstalled => "未检测到 VB-CABLE 驱动，请前往【环境与驱动】安装",
                DriverReadinessStatus.RebootRequired => "VB-CABLE 需要重启系统完成初始化",
                _ => "虚拟音频设备未就绪"
            };
        }
    }

    public bool IsHoldToTalk
    {
        get => TriggerMode == VoiceTriggerMode.HoldToTalk;
        set
        {
            if (value && TriggerMode != VoiceTriggerMode.HoldToTalk)
            {
                TriggerMode = VoiceTriggerMode.HoldToTalk;
                OnPropertyChanged(nameof(IsHoldToTalk));
                OnPropertyChanged(nameof(IsClickToToggle));
                OnPropertyChanged(nameof(CurrentTriggerModeDisplay));
                Save();
            }
        }
    }

    public bool IsClickToToggle
    {
        get => TriggerMode == VoiceTriggerMode.ClickToToggle;
        set
        {
            if (value && TriggerMode != VoiceTriggerMode.ClickToToggle)
            {
                TriggerMode = VoiceTriggerMode.ClickToToggle;
                OnPropertyChanged(nameof(IsHoldToTalk));
                OnPropertyChanged(nameof(IsClickToToggle));
                OnPropertyChanged(nameof(CurrentTriggerModeDisplay));
                Save();
            }
        }
    }

    public IntegrationsViewModel(ConfigService config)
    {
        _config = config;
        Reload();
    }

    public void Reload()
    {
        SelectedTool = _config.Current.InputTool.Tool;
        TriggerMode = _config.Current.InputTool.TriggerMode;
        ShortcutText = _config.Current.InputTool.Shortcut;
        SaveNoticeVisible = false;
        OnPropertyChanged(nameof(IsHoldToTalk));
        OnPropertyChanged(nameof(IsClickToToggle));
        OnPropertyChanged(nameof(CurrentToolDisplayName));
        OnPropertyChanged(nameof(CurrentShortcutDisplay));
        OnPropertyChanged(nameof(CurrentTriggerModeDisplay));
        OnPropertyChanged(nameof(VirtualCableStatusDescription));
    }

    [RelayCommand]
    public void ApplyPreset(string toolName)
    {
        if (Enum.TryParse<TargetInputTool>(toolName, out var tool))
        {
            SelectedTool = tool;
            ShortcutText = tool switch
            {
                TargetInputTool.WeChatInput => "Ctrl+Win",
                TargetInputTool.DoubaoInput => "F1",
                TargetInputTool.WindowsDictation => "Win+H",
                TargetInputTool.Typeless => "Ctrl+Alt+V",
                _ => "Ctrl+Shift+V"
            };
            TriggerMode = (tool == TargetInputTool.WindowsDictation)
                ? VoiceTriggerMode.ClickToToggle
                : VoiceTriggerMode.HoldToTalk;
            OnPropertyChanged(nameof(IsHoldToTalk));
            OnPropertyChanged(nameof(IsClickToToggle));
            OnPropertyChanged(nameof(CurrentToolDisplayName));
            OnPropertyChanged(nameof(CurrentShortcutDisplay));
            OnPropertyChanged(nameof(CurrentTriggerModeDisplay));
            Save();
        }
    }

    partial void OnTriggerModeChanged(VoiceTriggerMode value)
    {
        OnPropertyChanged(nameof(IsHoldToTalk));
        OnPropertyChanged(nameof(IsClickToToggle));
        OnPropertyChanged(nameof(CurrentTriggerModeDisplay));
    }

    [RelayCommand]
    public void Save()
    {
        _config.Current.InputTool.Tool = SelectedTool;
        _config.Current.InputTool.TriggerMode = TriggerMode;
        _config.Current.InputTool.Shortcut = ShortcutText;
        _config.Save();
        SaveNoticeVisible = true;
        OnPropertyChanged(nameof(CurrentToolDisplayName));
        OnPropertyChanged(nameof(CurrentShortcutDisplay));
        OnPropertyChanged(nameof(CurrentTriggerModeDisplay));
        OnPropertyChanged(nameof(VirtualCableStatusDescription));
    }
}

public partial class EnvironmentViewModel : ObservableObject
{
    [ObservableProperty]
    private DriverReadinessStatus _driverStatus;

    [ObservableProperty]
    private string _statusDescription = string.Empty;

    public EnvironmentViewModel()
    {
        RefreshStatus();
    }

    [RelayCommand]
    public void RefreshStatus()
    {
        DriverStatus = VirtualAudioAuditor.AuditCableInputDevice();
        StatusDescription = DriverStatus switch
        {
            DriverReadinessStatus.Ready => "VB-CABLE 虚拟麦克风已就绪，音频服务工作正常。",
            DriverReadinessStatus.NotInstalled => "未检测到 VB-CABLE。请从官方网站下载并安装。",
            DriverReadinessStatus.RebootRequired => "检测到虚拟声卡安装未完全生效，请重启电脑完成系统音频服务初始化。",
            _ => "虚拟麦克风初始化失败，请在设备管理器中检查设备状态。"
        };
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly Action _openOnboarding;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose;

    [ObservableProperty]
    private string _appVersion;

    [ObservableProperty]
    private string _logDirectoryPath;

    public SettingsViewModel(ConfigService config, Action openOnboarding)
    {
        _config = config;
        _openOnboarding = openOnboarding;
        LaunchAtStartup = StartupService.IsLaunchAtStartupEnabled();
        MinimizeToTrayOnClose = _config.Current.MinimizeToTrayOnClose;
        LogDirectoryPath = AppLogger.LogDirectory;

        var infoVer = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrEmpty(infoVer))
        {
            AppVersion = "v" + infoVer.Split('+')[0];
        }
        else
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            AppVersion = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v1.0.0";
        }
    }

    partial void OnLaunchAtStartupChanged(bool value)
    {
        StartupService.SetLaunchAtStartup(value);
        _config.Current.LaunchAtStartup = value;
        _config.Save();
    }

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        _config.Current.MinimizeToTrayOnClose = value;
        _config.Save();
    }

    [RelayCommand]
    public void RerunWizard()
    {
        _openOnboarding.Invoke();
    }

    [RelayCommand]
    public void OpenLogFolder()
    {
        AppLogger.OpenLogFolder();
    }
}
