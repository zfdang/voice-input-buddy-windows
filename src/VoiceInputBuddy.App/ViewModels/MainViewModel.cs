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
    private string? _activeHeldShortcut;

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

    public OnboardingViewModel? ActiveOnboardingVm { get; set; }

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

        _rawInputReceiver.DeviceFilter = IsTargetDevice;

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
                _activeHeldShortcut = shortcut;
                NativeInputSender.SendShortcutDown(shortcut);
            }
            else
            {
                _activeHeldShortcut = null;
                NativeInputSender.SendShortcutClick(shortcut);
            }
        };

        _bluetoothManager.VoiceSessionEnded += () =>
        {
            IsVoiceActive = false;
            CurrentAudioLevel = 0f;
            if (!string.IsNullOrEmpty(_activeHeldShortcut))
            {
                NativeInputSender.SendShortcutUp(_activeHeldShortcut);
                _activeHeldShortcut = null;
            }
            else if (_configService.Current.InputTool.TriggerMode == VoiceTriggerMode.ClickToToggle)
            {
                string shortcut = _configService.Current.InputTool.Shortcut;
                NativeInputSender.SendShortcutClick(shortcut);
            }
        };

        _rawInputReceiver.ButtonEvent += OnRawButtonEvent;
    }

    partial void OnActivePageNameChanged(string value)
    {
        if (value == "Mapping")
        {
            MappingVm.LoadMappings();
        }
    }

    public void RegisterRawInput(IntPtr hwnd) => _rawInputReceiver.Register(hwnd);
    public void ProcessRawInput(IntPtr lParam) => _rawInputReceiver.ProcessMessage(lParam);

    private bool IsTargetDevice(IntPtr hDevice, string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath))
        {
            return string.IsNullOrEmpty(_configService.Current.SelectedDeviceId);
        }

        string lowerPath = devicePath.ToLowerInvariant();

        // Check if device matches the one being tested in onboarding
        var onboardingDev = ActiveOnboardingVm?.SelectedDevice;
        if (onboardingDev != null && onboardingDev.BluetoothAddress != 0)
        {
            if (MatchesBluetoothAddress(lowerPath, onboardingDev.BluetoothAddress)) return true;
        }

        // Check if device matches the configured selected device
        var selectedDev = _configService.Current.Devices.FirstOrDefault(d => d.Id == _configService.Current.SelectedDeviceId);
        if (selectedDev != null && selectedDev.BluetoothAddress != 0)
        {
            if (MatchesBluetoothAddress(lowerPath, selectedDev.BluetoothAddress)) return true;
        }

        // If no device configured yet or during wizard, allow BLE HID or candidate remote devices
        if (selectedDev == null || selectedDev.BluetoothAddress == 0)
        {
            bool isBleHid = lowerPath.Contains("00001812") || lowerPath.Contains("bth") || lowerPath.Contains("bluetooth");
            bool isRemoteCandidate = lowerPath.Contains("g9n9n") || lowerPath.Contains("rc001") || lowerPath.Contains("rc003") || lowerPath.Contains("xiaomi");
            return isBleHid || isRemoteCandidate;
        }

        return false;
    }

    private static bool MatchesBluetoothAddress(string lowerPath, ulong address)
    {
        string hex12 = address.ToString("x12");
        string hexTrim = address.ToString("x");
        if (lowerPath.Contains(hex12) || lowerPath.Contains(hexTrim)) return true;

        byte[] bytes = BitConverter.GetBytes(address);
        string formattedUnderscore = $"{bytes[5]:x2}_{bytes[4]:x2}_{bytes[3]:x2}_{bytes[2]:x2}_{bytes[1]:x2}_{bytes[0]:x2}";
        string formattedColon = $"{bytes[5]:x2}:{bytes[4]:x2}:{bytes[3]:x2}:{bytes[2]:x2}:{bytes[1]:x2}:{bytes[0]:x2}";
        return lowerPath.Contains(formattedUnderscore) || lowerPath.Contains(formattedColon);
    }

    private void OnRawButtonEvent(RemoteButton button, bool isDown)
    {
        if (ActiveOnboardingVm != null)
        {
            if (isDown)
            {
                ActiveOnboardingVm.NotifyButtonPressed(button);
            }
            return;
        }

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
