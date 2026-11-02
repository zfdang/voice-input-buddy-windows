using System.Windows.Controls;
using System.Windows;
using VoiceInputBuddy.App.ViewModels;
using VoiceInputBuddy.Core.Models;

namespace VoiceInputBuddy.App.Views.Pages;

public partial class MappingPage : UserControl
{
    public MappingPage()
    {
        InitializeComponent();
    }

    private void SelectRemoteButton(object sender, RoutedEventArgs e)
    {
        if (DataContext is MappingViewModel vm && sender is Button { Tag: string tag } &&
            Enum.TryParse<RemoteButton>(tag, out var button))
            vm.SelectedMapping = vm.CurrentMappings.FirstOrDefault(item => item.Button == button);
    }
}
