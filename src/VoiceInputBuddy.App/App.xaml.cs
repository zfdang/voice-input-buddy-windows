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

        mainWindow.Show();
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
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _bluetoothManager?.Dispose();
        base.OnExit(e);
    }
}
