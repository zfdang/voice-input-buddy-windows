using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using VoiceInputBuddy.App.ViewModels;

namespace VoiceInputBuddy.App.Views;

public partial class OnboardingWindow : Window
{
    public OnboardingWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // The remote also produces normal Windows keyboard input. During the
        // button test, keep arrows and Enter (OK) from navigating or activating controls;
        // Raw Input still delivers their physical button events independently.
        if (DataContext is OnboardingViewModel { CurrentStep: 4 } &&
            e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Return)
        {
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is OnboardingViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            UpdatePanels(vm.CurrentStep);
        }
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
