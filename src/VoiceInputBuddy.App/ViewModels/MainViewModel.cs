using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Platform.Windows.Bluetooth;
using VoiceInputBuddy.Platform.Windows.Input;

namespace VoiceInputBuddy.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly WinRtBluetoothManager _bluetoothManager;

    [ObservableProperty]
    private string _currentTitle = "Voice Input Buddy (Windows)";

    [ObservableProperty]
    private string _activePageName = "Devices";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private string _connectedDeviceName = "未连接设备";

    [ObservableProperty]
    private float _currentAudioLevel = 0f;

    [ObservableProperty]
    private bool _isVoiceActive = false;

    public DevicesViewModel DevicesVm { get; }
    public MappingViewModel MappingVm { get; }
    public IntegrationsViewModel IntegrationsVm { get; }
    public EnvironmentViewModel EnvironmentVm { get; }
    public SettingsViewModel SettingsVm { get; }

    public event Action? RequestOpenOnboarding;

    public MainViewModel(ConfigService configService, WinRtBluetoothManager bluetoothManager)
    {
        _configService = configService;
        _bluetoothManager = bluetoothManager;

        DevicesVm = new DevicesViewModel(_configService, _bluetoothManager);
        MappingVm = new MappingViewModel(_configService);
        IntegrationsVm = new IntegrationsViewModel(_configService);
        EnvironmentVm = new EnvironmentViewModel();
        SettingsVm = new SettingsViewModel(_configService, () => RequestOpenOnboarding?.Invoke());

        _bluetoothManager.ConnectionStatusChanged += (connected, name) =>
        {
            IsConnected = connected;
            ConnectedDeviceName = connected ? name : "未连接设备";
        };

        _bluetoothManager.AudioLevelChanged += (level) =>
        {
            CurrentAudioLevel = level;
        };

        _bluetoothManager.VoiceSessionStarted += () =>
        {
            IsVoiceActive = true;
            // Pre-roll & Trigger hotkey
            NativeInputSender.SendShortcutString(_configService.Current.InputTool.Shortcut);
        };

        _bluetoothManager.VoiceSessionEnded += () =>
        {
            IsVoiceActive = false;
            CurrentAudioLevel = 0f;
            // Drain & End hotkey if hold-to-talk
            if (_configService.Current.InputTool.TriggerMode == VoiceTriggerMode.HoldToTalk)
            {
                NativeInputSender.SendShortcutString(_configService.Current.InputTool.Shortcut);
            }
        };
    }

    [RelayCommand]
    public void Navigate(string pageName)
    {
        ActivePageName = pageName;
    }
}
