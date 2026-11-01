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
    private string _transcriptionTestText = string.Empty;

    [ObservableProperty]
    private bool _isTranscriptionVerified = false;

    [ObservableProperty]
    private string _validationError = string.Empty;

    public event Action? RequestClose;

    public OnboardingViewModel(ConfigService config, WinRtBluetoothManager bt)
    {
        _config = config;
        _bt = bt;

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
        };

        _bt.AudioLevelChanged += (level) =>
        {
            LiveAudioLevel = level;
            if (level > 0.05f)
            {
                HasAudioTested = true;
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

            case 7: // Live Transcription Verification
                if (string.IsNullOrWhiteSpace(TranscriptionTestText) && !IsTranscriptionVerified)
                {
                    ValidationError = "请在文本框内进行一次语音转写测试，并确认文字成功上屏。";
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
            6 => "第 6 步：目标输入法麦克风与快捷键配置",
            7 => "第 7 步：真实语音输入转写验收",
            8 => "第 8 步：环境配置完成，开始使用",
            _ => "设置向导"
        };
    }
}
