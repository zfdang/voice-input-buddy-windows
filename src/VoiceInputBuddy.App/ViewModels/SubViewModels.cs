using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Platform.Windows.Audio;
using VoiceInputBuddy.Platform.Windows.Bluetooth;
using VoiceInputBuddy.Platform.Windows.Services;

namespace VoiceInputBuddy.App.ViewModels;

public partial class DevicesViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly WinRtBluetoothManager _bt;

    [ObservableProperty]
    private ObservableCollection<RemoteDevice> _discoveredDevices = new();

    [ObservableProperty]
    private RemoteDevice? _selectedDevice;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private float _deviceGain = 1.0f;

    public DevicesViewModel(ConfigService config, WinRtBluetoothManager bt)
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

public partial class MappingViewModel : ObservableObject
{
    private readonly ConfigService _config;

    [ObservableProperty]
    private ObservableCollection<ButtonMapping> _currentMappings = new();

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
                CurrentMappings.Add(m);
            }
        }
        else
        {
            var defaults = RemoteDevice.CreateDefaultMappings(RemoteModelType.XiaomiRC001);
            foreach (var m in defaults)
            {
                CurrentMappings.Add(m);
            }
        }
    }

    [RelayCommand]
    public void SaveMappings()
    {
        var selectedDev = _config.Current.Devices.FirstOrDefault(d => d.Id == _config.Current.SelectedDeviceId);
        if (selectedDev != null)
        {
            selectedDev.Mappings = CurrentMappings.ToList();
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
        SelectedTool = _config.Current.InputTool.Tool;
        TriggerMode = _config.Current.InputTool.TriggerMode;
        ShortcutText = _config.Current.InputTool.Shortcut;
    }

    [RelayCommand]
    public void ApplyPreset(string toolName)
    {
        if (Enum.TryParse<TargetInputTool>(toolName, out var tool))
        {
            SelectedTool = tool;
            ShortcutText = tool switch
            {
                TargetInputTool.WeChatInput => "Ctrl+Shift+Alt+V",
                TargetInputTool.DoubaoInput => "F1",
                TargetInputTool.WindowsDictation => "Win+H",
                TargetInputTool.Typeless => "Ctrl+Alt+V",
                _ => "Ctrl+Shift+V"
            };
            Save();
        }
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
    private string _appVersion = "v26.11.001";

    public SettingsViewModel(ConfigService config, Action openOnboarding)
    {
        _config = config;
        _openOnboarding = openOnboarding;
        LaunchAtStartup = StartupService.IsLaunchAtStartupEnabled();
    }

    partial void OnLaunchAtStartupChanged(bool value)
    {
        StartupService.SetLaunchAtStartup(value);
        _config.Current.LaunchAtStartup = value;
        _config.Save();
    }

    [RelayCommand]
    public void RerunWizard()
    {
        _openOnboarding.Invoke();
    }
}
