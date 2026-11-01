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
    private readonly RawInputReceiver _rawInputReceiver = new();

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
            string shortcut = _configService.Current.InputTool.Shortcut;
            if (_configService.Current.InputTool.TriggerMode == VoiceTriggerMode.HoldToTalk)
            {
                NativeInputSender.SendShortcutDown(shortcut);
            }
            else
            {
                NativeInputSender.SendShortcutClick(shortcut);
            }
        };

        _bluetoothManager.VoiceSessionEnded += () =>
        {
            IsVoiceActive = false;
            CurrentAudioLevel = 0f;
            string shortcut = _configService.Current.InputTool.Shortcut;
            if (_configService.Current.InputTool.TriggerMode == VoiceTriggerMode.HoldToTalk)
            {
                NativeInputSender.SendShortcutUp(shortcut);
            }
            else
            {
                NativeInputSender.SendShortcutClick(shortcut);
            }
        };

        _rawInputReceiver.ButtonEvent += OnRawButtonEvent;
    }

    public void RegisterRawInput(IntPtr hwnd) => _rawInputReceiver.Register(hwnd);
    public void ProcessRawInput(IntPtr lParam) => _rawInputReceiver.ProcessMessage(lParam);

    private void OnRawButtonEvent(RemoteButton button, bool isDown)
    {
        if (button == RemoteButton.Voice)
        {
            if (isDown)
            {
                _ = _bluetoothManager.StartVoiceSessionAsync();
            }
            else
            {
                _ = _bluetoothManager.StopVoiceSessionAsync();
            }
            return;
        }

        // Non-voice buttons execute action on KeyDown
        if (!isDown) return;

        var selectedDev = _configService.Current.Devices.FirstOrDefault(d => d.Id == _configService.Current.SelectedDeviceId);
        var mapping = selectedDev?.Mappings?.FirstOrDefault(m => m.Button == button);
        if (mapping == null) return;

        switch (mapping.ClickAction)
        {
            case ButtonActionType.KeyboardShortcut:
                if (!string.IsNullOrEmpty(mapping.ClickShortcut))
                {
                    NativeInputSender.SendShortcutClick(mapping.ClickShortcut);
                }
                break;
            case ButtonActionType.MediaControl:
                if (mapping.ClickMedia != MediaCommand.None)
                {
                    NativeInputSender.SendMedia(mapping.ClickMedia);
                }
                break;
            case ButtonActionType.TextSnippet:
                if (!string.IsNullOrEmpty(mapping.ClickText))
                {
                    NativeInputSender.SendUnicodeText(mapping.ClickText);
                }
                break;
        }
    }

    [RelayCommand]
    public void Navigate(string pageName)
    {
        ActivePageName = pageName;
    }
}
