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

    [ObservableProperty]
    private ObservableCollection<RemoteDevice> _discoveredDevices = new();

    [ObservableProperty]
    private RemoteDevice? _selectedDevice;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private float _deviceGain = 1.0f;

    public DevicesViewModel(ConfigService config, IBluetoothManager bt)
    {
        _config = config;
        _bt = bt;

        foreach (var dev in _config.Current.Devices)
        {
            DiscoveredDevices.Add(dev);
        }

        _bt.DeviceDiscovered += (dev) =>
        {
            App.Current?.Dispatcher?.Invoke(() =>
            {
                if (!DiscoveredDevices.Any(d => d.BluetoothAddress == dev.BluetoothAddress))
                {
                    DiscoveredDevices.Add(dev);
                }
            });
        };
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
        SelectedDevice = device;
        bool ok = await _bt.ConnectAsync(device.BluetoothAddress);
        if (ok)
        {
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
        }
    }

    partial void OnSelectedDeviceChanged(RemoteDevice? value)
    {
        if (value != null)
        {
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

    public MappingItemViewModel(ButtonMapping source)
    {
        Source = source;
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

    [ObservableProperty]
    private ObservableCollection<MappingItemViewModel> _currentMappings = new();

    public MappingViewModel(ConfigService config)
    {
        _config = config;
        LoadMappings();
    }

    public void LoadMappings()
    {
        CurrentMappings.Clear();
        var selectedDev = _config.Current.Devices.FirstOrDefault(d => d.Id == _config.Current.SelectedDeviceId);
        if (selectedDev != null)
        {
            if (selectedDev.Mappings == null || selectedDev.Mappings.Count == 0)
            {
                selectedDev.Mappings = RemoteDevice.CreateDefaultMappings(selectedDev.ModelType);
                _config.Save();
            }
            foreach (var m in selectedDev.Mappings)
            {
                CurrentMappings.Add(new MappingItemViewModel(m));
            }
        }
        else
        {
            var defaults = RemoteDevice.CreateDefaultMappings(RemoteModelType.XiaomiRC001);
            foreach (var m in defaults)
            {
                CurrentMappings.Add(new MappingItemViewModel(m));
            }
        }
    }

    [RelayCommand]
    public void SaveMappings()
    {
        var selectedDev = _config.Current.Devices.FirstOrDefault(d => d.Id == _config.Current.SelectedDeviceId);
        if (selectedDev != null)
        {
            foreach (var item in CurrentMappings)
            {
                item.ApplyBack();
            }
            selectedDev.Mappings = CurrentMappings.Select(i => i.Source).ToList();
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
                Save();
            }
        }
    }

    [ObservableProperty]
    private string _shortcutText = string.Empty;

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
        OnPropertyChanged(nameof(IsHoldToTalk));
        OnPropertyChanged(nameof(IsClickToToggle));
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
            Save();
        }
    }

    partial void OnTriggerModeChanged(VoiceTriggerMode value)
    {
        OnPropertyChanged(nameof(IsHoldToTalk));
        OnPropertyChanged(nameof(IsClickToToggle));
    }

    [RelayCommand]
    public void Save()
    {
        _config.Current.InputTool.Tool = SelectedTool;
        _config.Current.InputTool.TriggerMode = TriggerMode;
        _config.Current.InputTool.Shortcut = ShortcutText;
        _config.Save();
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
