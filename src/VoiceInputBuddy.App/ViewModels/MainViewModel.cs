using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Diagnostics;
using VoiceInputBuddy.Core.Models;
using VoiceInputBuddy.Core.Protocols;
using VoiceInputBuddy.Platform.Windows.Bluetooth;
using VoiceInputBuddy.Platform.Windows.Input;

namespace VoiceInputBuddy.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private record struct ActiveVoiceSession(VoiceTriggerMode TriggerMode, string Shortcut, TargetInputTool Tool);

    private readonly ConfigService _configService;
    private readonly WinRtBluetoothManager _bluetoothManager;
    private readonly RawInputReceiver _rawInputReceiver = new();
    private readonly RemoteVoiceKeyGuard _voiceKeyGuard;
    private readonly Dictionary<IntPtr, (Guid? Target, bool Accepted)> _deviceAdmissionLog = new();
    private ActiveVoiceSession? _activeVoiceSession;

    private System.Threading.Timer? _repeatTimer;
    private int _repeatGeneration = 0;
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

    private OnboardingViewModel? _activeOnboardingVm;
    public OnboardingViewModel? ActiveOnboardingVm
    {
        get => _activeOnboardingVm;
        set
        {
            _activeOnboardingVm = value;
            StopRepeatTimer();
        }
    }

    public event Action? RequestOpenOnboarding;

    public MainViewModel(ConfigService configService, WinRtBluetoothManager bluetoothManager)
    {
        _configService = configService;
        _bluetoothManager = bluetoothManager;
        _voiceKeyGuard = new RemoteVoiceKeyGuard(() =>
            _bluetoothManager.SessionState is BluetoothSessionState.Opening or BluetoothSessionState.Streaming);
        _rawInputReceiver.PhysicalVoiceKeyPressed += _voiceKeyGuard.Arm;

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
            if (!connected)
            {
                _voiceKeyGuard.Clear();
                StopRepeatTimer();
            }
        };

        _bluetoothManager.AudioLevelChanged += (level) =>
        {
            CurrentAudioLevel = level;
        };

        _bluetoothManager.VoiceSessionStarted += () =>
        {
            IsVoiceActive = true;
            if (ActiveOnboardingVm is { CurrentStep: not 7 }) return;
            var currentConfig = _configService.Current.InputTool;
            var session = new ActiveVoiceSession(currentConfig.TriggerMode, currentConfig.Shortcut, currentConfig.Tool);
            _activeVoiceSession = session;
            AppLogger.Info($"Voice session started: Tool={session.Tool}, Mode={session.TriggerMode}, Shortcut='{session.Shortcut}'", "MainViewModel");
            if (session.TriggerMode == VoiceTriggerMode.HoldToTalk)
            {
                bool sent = NativeInputSender.SendShortcutDown(session.Shortcut);
                AppLogger.Info($"Input tool shortcut down: Success={sent}", "MainViewModel");
            }
            else
            {
                bool sent = NativeInputSender.SendShortcutClick(session.Shortcut);
                AppLogger.Info($"Input tool shortcut click: Success={sent}", "MainViewModel");
            }
        };

        _bluetoothManager.VoiceSessionEnded += () =>
        {
            _voiceKeyGuard.Clear();
            IsVoiceActive = false;
            CurrentAudioLevel = 0f;
            if (_activeVoiceSession is { } session)
            {
                _activeVoiceSession = null;
                AppLogger.Info($"Voice session ended: Mode={session.TriggerMode}, Shortcut='{session.Shortcut}'", "MainViewModel");
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
        _bluetoothManager.VoiceButtonPressed += () =>
        {
            App.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ActiveOnboardingVm?.CurrentStep == 4)
                    ActiveOnboardingVm.NotifyButtonPressed(RemoteButton.Voice);
            }));
        };
    }

    partial void OnActivePageNameChanged(string value)
    {
        StopRepeatTimer();
        if (value == "Mapping")
        {
            MappingVm.LoadMappings();
        }
        else if (value == "Integrations")
        {
            IntegrationsVm.Reload();
        }
    }

    public void RegisterRawInput(IntPtr hwnd)
    {
        AppLogger.Info($"Remote voice F5 guard installed: {_voiceKeyGuard.Install()}", "MainViewModel");
        _rawInputReceiver.Register(hwnd);
    }
    public void ProcessRawInput(IntPtr lParam) => _rawInputReceiver.ProcessMessage(lParam);

    private bool IsTargetDevice(IntPtr hDevice, string devicePath)
    {
        // 1. Establish HID vs BLE correspondence through official Windows Container ID
        Guid? targetContainerId = _bluetoothManager.ConnectedDeviceContainerId
            ?? ActiveOnboardingVm?.SelectedDevice?.ContainerId
            ?? _configService.Current.Devices.FirstOrDefault(d => d.Id == _configService.Current.SelectedDeviceId)?.ContainerId;

        bool accepted = RawInputReceiver.EvaluateDeviceMatch(targetContainerId, hDevice, devicePath);
        if (!_deviceAdmissionLog.TryGetValue(hDevice, out var previous) ||
            previous != (targetContainerId, accepted))
        {
            _deviceAdmissionLog[hDevice] = (targetContainerId, accepted);
            AppLogger.Info($"RawInput device admission: Accepted={accepted}, TargetContainer={targetContainerId}, " +
                $"InputContainer={RawInputReceiver.GetDeviceContainerId(devicePath)}, Path='{devicePath}'", "MainViewModel");
        }
        return accepted;
    }

    private void OnRawButtonEvent(IntPtr hDevice, RemoteButton button, bool isDown)
    {
        if (ActiveOnboardingVm != null)
        {
            StopRepeatTimer();
            if (isDown)
            {
                ActiveOnboardingVm.NotifyButtonPressed(button);
            }
            if (button != RemoteButton.Voice || ActiveOnboardingVm.CurrentStep is not (5 or 7))
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

        AppLogger.Info($"Button mapping event: {button} isDown={isDown}, Action={mapping.ClickAction}", "MainViewModel");

        if (isDown)
        {
            ExecuteButtonAction(mapping.ClickAction, mapping.ClickShortcut, mapping.ClickMedia, mapping.ClickText);

            if (mapping.LongPressAction == ButtonActionType.ContinuousRepeat)
            {
                StopRepeatTimer();
                _heldButton = button;
                _activeRepeatMapping = mapping;
                var capturedMapping = mapping;
                int currentGen = _repeatGeneration;
                _repeatTimer = new System.Threading.Timer(_ =>
                {
                    if (Volatile.Read(ref _repeatGeneration) != currentGen) return;
                    if (!_bluetoothManager.IsConnected || ActiveOnboardingVm != null)
                    {
                        StopRepeatTimer();
                        return;
                    }
                    AppLogger.Debug($"Repeating button action: {capturedMapping.Button} ({capturedMapping.ClickAction})", "MainViewModel");
                    ExecuteButtonAction(capturedMapping.ClickAction, capturedMapping.ClickShortcut, capturedMapping.ClickMedia, capturedMapping.ClickText);
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
        Interlocked.Increment(ref _repeatGeneration);
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

    public void Dispose()
    {
        _voiceKeyGuard.Dispose();
        StopRepeatTimer();
        _rawInputReceiver.Dispose();
    }
}
