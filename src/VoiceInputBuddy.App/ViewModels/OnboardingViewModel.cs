// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceInputBuddy.Core.Bluetooth;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Diagnostics;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Platform.Windows.Audio;
using VoiceInputBuddy.Platform.Windows.Input;

namespace VoiceInputBuddy.App.ViewModels;

public partial class OnboardingViewModel : ObservableObject, IDisposable
{
    private readonly ConfigService _config;
    private readonly IBluetoothManager _bt;

    private readonly Action<RemoteDevice> _onDeviceDiscovered;
    private readonly Action<bool, string> _onConnectionChanged;
    private readonly Action<float> _onAudioLevelChanged;
    private readonly Action _onVoiceSessionEnded;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private int _totalSteps = 8;

    [ObservableProperty]
    private string _stepTitle = "欢迎使用 Voice Input Buddy";

    [ObservableProperty]
    private DriverReadinessStatus _driverStatus;

    [ObservableProperty]
    private string _driverStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isScanning = false;

    [ObservableProperty]
    private ObservableCollection<RemoteDevice> _discoveredDevices = new();

    [ObservableProperty]
    private RemoteDevice? _selectedDevice;

    [ObservableProperty]
    private bool _isDeviceConnected = false;

    [ObservableProperty]
    private string _deviceConnectionStatus = "尚未连接语音服务。Windows 显示已配对或已连接后，仍需点击下方设备的“连接”。";

    [ObservableProperty]
    private string _lastPressedButtonName = "尚未检测到按键";

    [ObservableProperty]
    private bool _hasButtonTested = false;

    [ObservableProperty]
    private float _liveAudioLevel = 0f;

    [ObservableProperty]
    private bool _hasAudioTested = false;

    [ObservableProperty]
    private TargetInputTool _selectedTool;

    [ObservableProperty]
    private VoiceTriggerMode _triggerMode;

    [ObservableProperty]
    private string _shortcutText = string.Empty;

    [ObservableProperty]
    private string _transcriptionTestText = string.Empty;

    [ObservableProperty]
    private bool _isTranscriptionVerified = false;

    [ObservableProperty]
    private string _validationError = string.Empty;

    private bool _hasVoiceTranscriptionSessionOccurred = false;

    public event Action? RequestClose;

    public OnboardingViewModel(ConfigService config, IBluetoothManager bt)
    {
        _config = config;
        _bt = bt;

        SelectedTool = _config.Current.InputTool.Tool;
        TriggerMode = _config.Current.InputTool.TriggerMode;
        ShortcutText = _config.Current.InputTool.Shortcut;
        IsDeviceConnected = _bt.IsReady;
        SelectedDevice = _config.Current.Devices.FirstOrDefault(d => d.Id == _config.Current.SelectedDeviceId);
        foreach (var device in _config.Current.Devices)
            DiscoveredDevices.Add(device);
        if (IsDeviceConnected) DeviceConnectionStatus = "语音服务已就绪，可以继续。";

        _onDeviceDiscovered = (dev) =>
        {
            App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (!DiscoveredDevices.Any(d => d.BluetoothAddress == dev.BluetoothAddress))
                {
                    DiscoveredDevices.Add(dev);
                }
            }));
        };

        _onConnectionChanged = (connected, name) =>
        {
            void Update()
            {
                IsDeviceConnected = connected;
                DeviceConnectionStatus = connected ? $"语音服务已就绪：{name}" : $"语音服务未就绪：{name}";
                if (!connected)
                {
                    HasButtonTested = false;
                    HasAudioTested = false;
                    LiveAudioLevel = 0f;
                    _hasVoiceTranscriptionSessionOccurred = false;
                }
            }

            if (App.Current?.Dispatcher != null && !App.Current.Dispatcher.CheckAccess())
            {
                App.Current.Dispatcher.BeginInvoke(Update);
            }
            else
            {
                Update();
            }
        };

        _onAudioLevelChanged = (level) =>
        {
            void Update()
            {
                LiveAudioLevel = level;
                if (CurrentStep == 5 && level > 0.05f)
                {
                    HasAudioTested = true;
                }
            }

            if (App.Current?.Dispatcher != null && !App.Current.Dispatcher.CheckAccess())
            {
                App.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, Update);
            }
            else
            {
                Update();
            }
        };

        _onVoiceSessionEnded = () =>
        {
            void Update()
            {
                LiveAudioLevel = 0f;
                if (CurrentStep == 7)
                {
                    _hasVoiceTranscriptionSessionOccurred = true;
                }
            }

            if (App.Current?.Dispatcher != null && !App.Current.Dispatcher.CheckAccess())
            {
                App.Current.Dispatcher.BeginInvoke(Update);
            }
            else
            {
                Update();
            }
        };

        _bt.DeviceDiscovered += _onDeviceDiscovered;
        _bt.ConnectionStatusChanged += _onConnectionChanged;
        _bt.AudioLevelChanged += _onAudioLevelChanged;
        _bt.VoiceSessionEnded += _onVoiceSessionEnded;

        RefreshDriverStatus();
        UpdateStepMetadata();
    }

    public void Dispose()
    {
        if (IsScanning)
        {
            _bt.StopScanning();
            IsScanning = false;
        }
        _bt.DeviceDiscovered -= _onDeviceDiscovered;
        _bt.ConnectionStatusChanged -= _onConnectionChanged;
        _bt.AudioLevelChanged -= _onAudioLevelChanged;
        _bt.VoiceSessionEnded -= _onVoiceSessionEnded;
    }

    [RelayCommand]
    public void RefreshDriverStatus()
    {
        DriverStatus = VirtualAudioAuditor.AuditCableInputDevice();
        DriverStatusMessage = DriverStatus switch
        {
            DriverReadinessStatus.Ready => "✅ VB-CABLE 虚拟麦克风已就绪，音频服务工作正常。",
            DriverReadinessStatus.NotInstalled => "❌ 未检测到 VB-CABLE。请点击下方按钮前往官网下载安装。",
            DriverReadinessStatus.RebootRequired => "⚠️ 检测到虚拟声卡安装未完全生效，请重启电脑完成系统音频服务初始化。",
            _ => "❌ 虚拟麦克风初始化失败，请在设备管理器中检查设备状态。"
        };
    }

    [RelayCommand]
    public void OpenOfficialWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://vb-audio.com/Cable/",
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public void ToggleScan()
    {
        IsScanning = !IsScanning;
        if (IsScanning)
        {
            _bt.StartScanning();
            _ = _bt.DiscoverPairedRemotesAsync();
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
        ValidationError = string.Empty;
        IsDeviceConnected = false;
        DeviceConnectionStatus = $"正在连接 {device.Name} 并协商语音服务，请稍候……";
        AppLogger.Info($"Wizard connect requested: '{device.Name}' (0x{device.BluetoothAddress:X12})", "OnboardingViewModel");
        SelectedDevice = device;
        bool ok = await _bt.ConnectAsync(device.BluetoothAddress);
        if (ok)
        {
            device.IsConnected = true;
            IsDeviceConnected = true;
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
        else
        {
            DeviceConnectionStatus = $"与 {device.Name} 连接失败。";
            ValidationError = $"无法与 {device.Name} 建立语音服务。请确保遥控器处于配对状态、电量充足且未被其他设备独占连接。";
            AppLogger.Warn($"Wizard connection failed: {ValidationError}", "OnboardingViewModel");
        }
    }

    public void NotifyButtonPressed(RemoteButton button)
    {
        if (CurrentStep != 4 || button == RemoteButton.None) return;
        LastPressedButtonName = $"已按下: {GetButtonFriendlyName(button)}";
        HasButtonTested = true;
    }

    private static string GetButtonFriendlyName(RemoteButton button) => button switch
    {
        RemoteButton.Up => "方向上键",
        RemoteButton.Down => "方向下键",
        RemoteButton.Left => "方向左键",
        RemoteButton.Right => "方向右键",
        RemoteButton.Ok => "确认键 (OK)",
        RemoteButton.Back => "返回键",
        RemoteButton.Home => "主页键 (Home)",
        RemoteButton.Voice => "语音键",
        RemoteButton.VolumeUp => "音量加 (+)",
        RemoteButton.VolumeDown => "音量减 (-)",
        RemoteButton.Mute => "静音键",
        RemoteButton.Power => "电源键",
        RemoteButton.LiveTv => "电视直播键",
        RemoteButton.Menu => "菜单键",
        _ => button.ToString()
    };

    partial void OnCurrentStepChanged(int value)
    {
        if (value == 4)
        {
            HasButtonTested = false;
            LastPressedButtonName = "尚未检测到按键";
        }
        if (value == 5) HasAudioTested = false;
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
            SaveToolConfig();
        }
    }

    public bool SaveToolConfig()
    {
        if (!NativeInputSender.TryParseShortcut(ShortcutText, out _, out _))
        {
            ValidationError = "请输入有效的语音快捷键，例如 Ctrl+Win；快捷键不能为空。";
            return false;
        }
        ValidationError = string.Empty;
        _config.Current.InputTool.Tool = SelectedTool;
        _config.Current.InputTool.TriggerMode = TriggerMode;
        _config.Current.InputTool.Shortcut = ShortcutText;
        _config.Save();
        return true;
    }

    public string NextButtonText => CurrentStep == TotalSteps ? "完成" : "下一步";
    public bool CanGoPrevious => CurrentStep > 1;

    [RelayCommand]
    public void NextStep()
    {
        ValidationError = string.Empty;
        if (CurrentStep == 3 && IsScanning)
        {
            _bt.StopScanning();
            IsScanning = false;
        }

        // Validation gates for each step
        switch (CurrentStep)
        {
            case 2: // Driver Check
                RefreshDriverStatus();
                if (DriverStatus != DriverReadinessStatus.Ready)
                {
                    ValidationError = "请先安装 VB-CABLE 并确保音频端点就绪（如需重启请先重启电脑）。";
                    return;
                }
                break;

            case 3: // Device Pairing
                if (!IsDeviceConnected)
                {
                    ValidationError = "请先点击设备的“连接”，等待语音服务就绪。Windows 蓝牙连接状态不能代替应用内语音服务连接。";
                    AppLogger.Warn($"Step 3 blocked: IsDeviceConnected={IsDeviceConnected}, ManagerReady={_bt.IsReady}, Status={DeviceConnectionStatus}", "OnboardingViewModel");
                    return;
                }
                break;

            case 4: // Button Test
                if (!HasButtonTested)
                {
                    ValidationError = "请先在遥控器上按下一个按键以验证按键链路正常。";
                    return;
                }
                break;

            case 5: // Voice Test
                if (!HasAudioTested)
                {
                    ValidationError = "请按住遥控器语音键说话，确保电平条产生跳动反馈。";
                    return;
                }
                break;

            case 6: // IME Tool Configuration
                if (!SaveToolConfig()) return;
                break;

            case 7: // Live Transcription Verification
                if (!_hasVoiceTranscriptionSessionOccurred)
                {
                    ValidationError = "请先按住遥控器语音键在下方输入框中完成一次实际语音转写。";
                    return;
                }
                if (string.IsNullOrWhiteSpace(TranscriptionTestText))
                {
                    ValidationError = "转写文本框内容不能为空，请先通过遥控器转写文字。";
                    return;
                }
                if (!IsTranscriptionVerified)
                {
                    ValidationError = "请勾选确认输入法转写结果正确无误。";
                    return;
                }
                break;
        }

        if (CurrentStep < TotalSteps)
        {
            AppLogger.Info($"Onboarding progressing from Step {CurrentStep} to {CurrentStep + 1}", "OnboardingViewModel");
            CurrentStep++;
            UpdateStepMetadata();
        }
        else
        {
            Complete();
        }
    }

    [RelayCommand]
    public void PreviousStep()
    {
        ValidationError = string.Empty;
        if (CurrentStep > 1)
        {
            CurrentStep--;
            UpdateStepMetadata();
        }
    }

    [RelayCommand]
    public void Complete()
    {
        RefreshDriverStatus();
        if (DriverStatus != DriverReadinessStatus.Ready)
        {
            ValidationError = "虚拟麦克风驱动未就绪，无法完成设置。";
            return;
        }
        if (!IsDeviceConnected)
        {
            ValidationError = "遥控器未连接，无法完成设置。";
            return;
        }
        if (!SaveToolConfig()) return;
        _config.Current.HasCompletedOnboarding = true;
        _config.Save();
        AppLogger.Info("Onboarding wizard completed and configuration saved", "OnboardingViewModel");
        RequestClose?.Invoke();
    }

    private void UpdateStepMetadata()
    {
        StepTitle = CurrentStep switch
        {
            1 => "第 1 步：欢迎与界面语言",
            2 => "第 2 步：虚拟麦克风驱动检查 (VB-CABLE)",
            3 => "第 3 步：选择并连接遥控器",
            4 => "第 4 步：遥控器按键响应测试",
            5 => "第 5 步：麦克风拾音与动态波形测试",
            6 => "第 6 步：配置目标语音输入工具",
            7 => "第 7 步：真实输入法转写验收",
            8 => "第 8 步：设置完成与使用说明",
            _ => "设置向导"
        };
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(CanGoPrevious));
    }
}
