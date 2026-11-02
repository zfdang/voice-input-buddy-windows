using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Platform.Windows.Audio;
using VoiceInputBuddy.Platform.Windows.Bluetooth;

namespace VoiceInputBuddy.App.ViewModels;

public partial class OnboardingViewModel : ObservableObject
{
    private readonly ConfigService _config;
    private readonly WinRtBluetoothManager _bt;

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

    public OnboardingViewModel(ConfigService config, WinRtBluetoothManager bt)
    {
        _config = config;
        _bt = bt;

        SelectedTool = _config.Current.InputTool.Tool;
        TriggerMode = _config.Current.InputTool.TriggerMode;
        ShortcutText = _config.Current.InputTool.Shortcut;

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

        _bt.ConnectionStatusChanged += (connected, name) =>
        {
            IsDeviceConnected = connected;
            if (!connected)
            {
                HasAudioTested = false;
                _hasVoiceTranscriptionSessionOccurred = false;
            }
        };

        _bt.AudioLevelChanged += (level) =>
        {
            LiveAudioLevel = level;
            if (level > 0.05f)
            {
                HasAudioTested = true;
            }
        };

        _bt.VoiceSessionEnded += () =>
        {
            if (CurrentStep == 7)
            {
                _hasVoiceTranscriptionSessionOccurred = true;
            }
        };

        RefreshDriverStatus();
        UpdateStepMetadata();
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
    }

    public void NotifyButtonPressed(RemoteButton button)
    {
        LastPressedButtonName = $"已按下: {button}";
        HasButtonTested = true;
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
            TriggerMode = (tool == TargetInputTool.WindowsDictation)
                ? VoiceTriggerMode.ClickToToggle
                : VoiceTriggerMode.HoldToTalk;
            SaveToolConfig();
        }
    }

    public void SaveToolConfig()
    {
        _config.Current.InputTool.Tool = SelectedTool;
        _config.Current.InputTool.TriggerMode = TriggerMode;
        _config.Current.InputTool.Shortcut = ShortcutText;
        _config.Save();
    }

    [RelayCommand]
    public void NextStep()
    {
        ValidationError = string.Empty;

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
                    ValidationError = "请先扫描并成功连接一只遥控器后再继续。";
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
                SaveToolConfig();
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
        SaveToolConfig();
        _config.Current.HasCompletedOnboarding = true;
        _config.Save();
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
    }
}
