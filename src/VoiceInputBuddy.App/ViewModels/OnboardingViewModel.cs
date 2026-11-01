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
    private float _liveAudioLevel;

    [ObservableProperty]
    private string _transcriptionTestText = string.Empty;

    [ObservableProperty]
    private bool _isTranscriptionVerified = false;

    public event Action? RequestClose;

    public OnboardingViewModel(ConfigService config, WinRtBluetoothManager bt)
    {
        _config = config;
        _bt = bt;

        _bt.AudioLevelChanged += (level) =>
        {
            LiveAudioLevel = level;
        };

        UpdateStepMetadata();
    }

    [RelayCommand]
    public void NextStep()
    {
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
            3 => "第 3 步：选择并配对电视遥控器",
            4 => "第 4 步：遥控器按键响应测试",
            5 => "第 5 步：麦克风拾音与动态波形测试",
            6 => "第 6 步：目标输入法麦克风与快捷键配置",
            7 => "第 7 步：真实语音输入转写验收",
            8 => "第 8 步：环境配置完成，开始使用",
            _ => "设置向导"
        };

        if (CurrentStep == 2)
        {
            DriverStatus = VirtualAudioAuditor.AuditCableInputDevice();
        }
    }
}
