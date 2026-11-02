using System.Windows;
using VoiceInputBuddy.App.ViewModels;
using VoiceInputBuddy.App.Views;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Platform.Windows.Bluetooth;

namespace VoiceInputBuddy.App;

public partial class App : Application
{
    private ConfigService? _configService;
    private WinRtBluetoothManager? _bluetoothManager;
    private MainViewModel? _mainViewModel;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        bool startMinimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        // Prevent shutdown when modal onboarding dialog closes
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _configService = new ConfigService();
        _bluetoothManager = new WinRtBluetoothManager();
        _mainViewModel = new MainViewModel(_configService, _bluetoothManager);

        var mainWindow = new MainWindow { DataContext = _mainViewModel };
        MainWindow = mainWindow;

        // Ensure HWND is initialized so RawInputReceiver is active for onboarding
        new System.Windows.Interop.WindowInteropHelper(mainWindow).EnsureHandle();

        _mainViewModel.RequestOpenOnboarding += ShowOnboardingWindow;

        if (!_configService.Current.HasCompletedOnboarding)
        {
            ShowOnboardingWindow();
        }

        // Auto-reconnect to previously saved remote device
        if (_configService.Current.HasCompletedOnboarding && !string.IsNullOrEmpty(_configService.Current.SelectedDeviceId))
        {
            var savedDevice = _configService.Current.Devices.FirstOrDefault(d => d.Id == _configService.Current.SelectedDeviceId);
            if (savedDevice != null && savedDevice.BluetoothAddress != 0)
            {
                _ = _bluetoothManager.ConnectAsync(savedDevice.BluetoothAddress);
            }
        }

        if (!startMinimized || !_configService.Current.HasCompletedOnboarding)
        {
            mainWindow.Show();
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    private void ShowOnboardingWindow()
    {
        if (_configService == null || _bluetoothManager == null || _mainViewModel == null) return;

        var onboardingVm = new OnboardingViewModel(_configService, _bluetoothManager);
        _mainViewModel.ActiveOnboardingVm = onboardingVm;

        var onboardingWin = new OnboardingWindow { DataContext = onboardingVm, Owner = MainWindow };
        onboardingVm.RequestClose += () => onboardingWin.Close();
        try
        {
            onboardingWin.ShowDialog();
        }
        finally
        {
            _mainViewModel.ActiveOnboardingVm = null;
            _mainViewModel.IntegrationsVm.Reload();
            _mainViewModel.MappingVm.LoadMappings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _bluetoothManager?.Dispose();
        base.OnExit(e);
    }
}
