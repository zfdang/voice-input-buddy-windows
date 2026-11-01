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
        if (_configService == null || _bluetoothManager == null) return;

        var onboardingVm = new OnboardingViewModel(_configService, _bluetoothManager);
        var onboardingWin = new OnboardingWindow { DataContext = onboardingVm };
        onboardingVm.RequestClose += () => onboardingWin.Close();
        onboardingWin.ShowDialog();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _bluetoothManager?.Dispose();
        base.OnExit(e);
    }
}
