using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Core.Protocols;
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
        // 1. Establish HID vs BLE correspondence through official Windows Container ID
        Guid? deviceContainerId = RawInputReceiver.GetDeviceContainerId(devicePath);
        if (deviceContainerId.HasValue)
        {
            // Match active BLE connection container
            if (_bluetoothManager.ConnectedDeviceContainerId.HasValue &&
                _bluetoothManager.ConnectedDeviceContainerId.Value == deviceContainerId.Value)
            {
                return true;
            }

            // Match onboarding device container
            var onboardingDev = ActiveOnboardingVm?.SelectedDevice;
            if (onboardingDev?.ContainerId != null &&
                onboardingDev.ContainerId.Value == deviceContainerId.Value)
            {
                return true;
            }

            // Match selected device container from configuration
            var selectedDev = _configService.Current.Devices.FirstOrDefault(d => d.Id == _configService.Current.SelectedDeviceId);
            if (selectedDev?.ContainerId != null &&
                selectedDev.ContainerId.Value == deviceContainerId.Value)
            {
                return true;
            }
        }

        // 2. Query official HID device properties (VID/PID) via RIDI_DEVICEINFO
        if (RawInputReceiver.GetHidDeviceInfo(hDevice, out uint vid, out uint pid))
        {
            // Google G9N9N Voice Remote (VID 0x18D1, PID 0x9450)
            if (vid == G9N9NProtocol.VendorId && pid == G9N9NProtocol.ProductId)
            {
                return true;
            }

            // Xiaomi Bluetooth Remote (VID 0x2717)
            if (vid == 0x2717)
            {
                return true;
            }

            // If it's a known non-remote USB desktop keyboard or mouse, reject it
            if (vid != 0)
            {
                return false;
            }
        }

        // 3. Fallback when Container ID or VID/PID cannot be queried (e.g. mock test harnesses)
        return true;
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
