using System.ComponentModel;
using System.Windows;
using VoiceInputBuddy.App.ViewModels;
using VoiceInputBuddy.App.Views.Pages;

namespace VoiceInputBuddy.App.Views;

public partial class MainWindow : Window
{
    private DevicesPage? _devicesPage;
    private MappingPage? _mappingPage;
    private IntegrationsPage? _integrationsPage;
    private EnvironmentPage? _environmentPage;
    private SettingsPage? _settingsPage;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _devicesPage = new DevicesPage { DataContext = vm.DevicesVm };
            _mappingPage = new MappingPage { DataContext = vm.MappingVm };
            _integrationsPage = new IntegrationsPage { DataContext = vm.IntegrationsVm };
            _environmentPage = new EnvironmentPage { DataContext = vm.EnvironmentVm };
            _settingsPage = new SettingsPage { DataContext = vm.SettingsVm };

            vm.PropertyChanged += OnViewModelPropertyChanged;
            SwitchPage(vm.ActivePageName);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActivePageName) && sender is MainViewModel vm)
        {
            SwitchPage(vm.ActivePageName);
        }
    }

    private void SwitchPage(string pageName)
    {
        PageContentContainer.Content = pageName switch
        {
            "Devices" => _devicesPage,
            "Mapping" => _mappingPage,
            "Integrations" => _integrationsPage,
            "Environment" => _environmentPage,
            "Settings" => _settingsPage,
            _ => _devicesPage
        };
    }
}
