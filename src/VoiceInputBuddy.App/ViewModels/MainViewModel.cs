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
    private record struct ActiveVoiceSession(VoiceTriggerMode TriggerMode, string Shortcut, TargetInputTool Tool);

    private readonly ConfigService _configService;
    private readonly WinRtBluetoothManager _bluetoothManager;
    private readonly RawInputReceiver _rawInputReceiver = new();
    private ActiveVoiceSession? _activeVoiceSession;

    private System.Threading.Timer? _repeatTimer;
    private RemoteButton _heldButton = RemoteButton.None;
    private ButtonMapping? _activeRepeatMapping;

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
            var currentConfig = _configService.Current.InputTool;
            var session = new ActiveVoiceSession(currentConfig.TriggerMode, currentConfig.Shortcut, currentConfig.Tool);
            _activeVoiceSession = session;
            IsVoiceActive = true;
            if (session.TriggerMode == VoiceTriggerMode.HoldToTalk)
            {
                NativeInputSender.SendShortcutDown(session.Shortcut);
            }
            else
            {
                NativeInputSender.SendShortcutClick(session.Shortcut);
            }
        };

        _bluetoothManager.VoiceSessionEnded += () =>
        {
            IsVoiceActive = false;
            CurrentAudioLevel = 0f;
            if (_activeVoiceSession is { } session)
            {
                _activeVoiceSession = null;
                if (session.TriggerMode == VoiceTriggerMode.HoldToTalk)
                {
                    NativeInputSender.SendShortcutUp(session.Shortcut);
                }
                else if (session.TriggerMode == VoiceTriggerMode.ClickToToggle)
                {
                    NativeInputSender.SendShortcutClick(session.Shortcut);
                }
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
        Guid? targetContainerId = _bluetoothManager.ConnectedDeviceContainerId
            ?? ActiveOnboardingVm?.SelectedDevice?.ContainerId
            ?? _configService.Current.Devices.FirstOrDefault(d => d.Id == _configService.Current.SelectedDeviceId)?.ContainerId;

        Guid? deviceContainerId = RawInputReceiver.GetDeviceContainerId(devicePath);

        if (targetContainerId.HasValue)
        {
            if (deviceContainerId.HasValue)
            {
                // Strict check: if both have Container ID, they MUST match!
                return deviceContainerId.Value == targetContainerId.Value;
            }
            // Incoming device has no Container ID, but target device DOES have Container ID.
            if (RawInputReceiver.GetHidDeviceInfo(hDevice, out uint vid, out _))
            {
                if (vid != G9N9NProtocol.VendorId && vid != 0x2717 && vid != 0)
                {
                    return false;
                }
            }
        }

        // 2. Query official HID device properties (VID/PID) via RIDI_DEVICEINFO
        if (RawInputReceiver.GetHidDeviceInfo(hDevice, out uint dVid, out uint dPid))
        {
            // Google G9N9N Voice Remote (VID 0x18D1, PID 0x9450)
            if (dVid == G9N9NProtocol.VendorId && dPid == G9N9NProtocol.ProductId)
            {
                return true;
            }

            // Xiaomi Bluetooth Remote (VID 0x2717)
            if (dVid == 0x2717)
            {
                return true;
            }

            // If it's a known non-remote USB desktop keyboard or mouse, reject it
            if (dVid != 0)
            {
                return false;
            }
        }

        // 3. Fallback when Container ID or VID/PID cannot be queried (e.g. mock test harnesses)
        return true;
    }

    private void OnRawButtonEvent(IntPtr hDevice, RemoteButton button, bool isDown)
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

        var selectedDev = _configService.Current.Devices.FirstOrDefault(d => d.Id == _configService.Current.SelectedDeviceId);
        var mapping = selectedDev?.Mappings?.FirstOrDefault(m => m.Button == button);
        if (mapping == null) return;

        if (isDown)
        {
            ExecuteButtonAction(mapping.ClickAction, mapping.ClickShortcut, mapping.ClickMedia, mapping.ClickText);

            if (mapping.LongPressAction == ButtonActionType.ContinuousRepeat)
            {
                StopRepeatTimer();
                _heldButton = button;
                _activeRepeatMapping = mapping;
                _repeatTimer = new System.Threading.Timer(_ =>
                {
                    if (_activeRepeatMapping != null)
                    {
                        ExecuteButtonAction(_activeRepeatMapping.ClickAction, _activeRepeatMapping.ClickShortcut, _activeRepeatMapping.ClickMedia, _activeRepeatMapping.ClickText);
                    }
                }, null, 400, 100);
            }
        }
        else
        {
            if (button == _heldButton)
            {
                StopRepeatTimer();
            }
        }
    }

    private void StopRepeatTimer()
    {
        _repeatTimer?.Dispose();
        _repeatTimer = null;
        _heldButton = RemoteButton.None;
        _activeRepeatMapping = null;
    }

    private static void ExecuteButtonAction(ButtonActionType actionType, string? shortcut, MediaCommand media, string? text)
    {
        switch (actionType)
        {
            case ButtonActionType.KeyboardShortcut:
                if (!string.IsNullOrEmpty(shortcut))
                {
                    NativeInputSender.SendShortcutClick(shortcut);
                }
                break;
            case ButtonActionType.MediaControl:
                if (media != MediaCommand.None)
                {
                    NativeInputSender.SendMedia(media);
                }
                break;
            case ButtonActionType.TextSnippet:
                if (!string.IsNullOrEmpty(text))
                {
                    NativeInputSender.SendUnicodeText(text);
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
