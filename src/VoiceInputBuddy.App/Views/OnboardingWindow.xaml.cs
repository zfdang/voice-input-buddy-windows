using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using VoiceInputBuddy.App.ViewModels;
using VoiceInputBuddy.Platform.Windows.Input;

namespace VoiceInputBuddy.App.Views;

public partial class OnboardingWindow : Window
{
    private readonly RawInputReceiver _rawInputReceiver = new();

    public OnboardingWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is OnboardingViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            UpdatePanels(vm.CurrentStep);

            _rawInputReceiver.ButtonEvent += (btn, isDown) =>
            {
                if (isDown)
                {
                    Dispatcher.Invoke(() => vm.NotifyButtonPressed(btn));
                }
            };
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        _rawInputReceiver.Register(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x00FF) // WM_INPUT
        {
            _rawInputReceiver.ProcessMessage(lParam);
        }
        return IntPtr.Zero;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _rawInputReceiver.Dispose();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OnboardingViewModel.CurrentStep) && sender is OnboardingViewModel vm)
        {
            UpdatePanels(vm.CurrentStep);
        }
    }

    private void UpdatePanels(int step)
    {
        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        Step5Panel.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;
        Step6Panel.Visibility = step == 6 ? Visibility.Visible : Visibility.Collapsed;
        Step7Panel.Visibility = step == 7 ? Visibility.Visible : Visibility.Collapsed;
        Step8Panel.Visibility = step == 8 ? Visibility.Visible : Visibility.Collapsed;
    }
}
