using VoiceInputBuddy.App.ViewModels;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Models;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class MappingDeviceTests
{
    [Fact]
    public void EditingOneDevicePreservesOtherDeviceAndActiveSelection()
    {
        var config = new ConfigService(Path.Combine(Path.GetTempPath(), $"vib-mapping-{Guid.NewGuid():N}.json"));
        var first = new RemoteDevice { Id = "first", ModelType = RemoteModelType.XiaomiRC003 };
        var second = new RemoteDevice { Id = "second", ModelType = RemoteModelType.XiaomiRC003 };
        config.Current.Devices.AddRange([first, second]);
        config.Current.SelectedDeviceId = first.Id;
        var vm = new MappingViewModel(config) { SelectedDevice = second };
        var up = vm.CurrentMappings.Single(m => m.Button == RemoteButton.Up);
        up.ParameterText = "F2";
        vm.SelectedDevice = first;
        vm.SelectedDevice = second;
        Assert.Equal("F2", vm.CurrentMappings.Single(m => m.Button == RemoteButton.Up).ParameterText);
        vm.SaveMappings();

        Assert.Equal("F2", second.Mappings.Single(m => m.Button == RemoteButton.Up).ClickShortcut);
        Assert.Equal("Up", first.Mappings.Single(m => m.Button == RemoteButton.Up).ClickShortcut);
        Assert.Equal(first.Id, config.Current.SelectedDeviceId);
        Assert.Contains(vm.CurrentMappings, m => m.Button == RemoteButton.Power);
        Assert.Contains(vm.CurrentMappings, m => m.Button == RemoteButton.Menu);
        Assert.Contains(vm.CurrentMappings, m => m.Button == RemoteButton.LiveTv);
        Assert.DoesNotContain(vm.CurrentMappings, m => m.Button == RemoteButton.Mute);
        Assert.Contains(second.Mappings, m => m.Button == RemoteButton.Mute);
        vm.SelectedDevice = first;
        Assert.Equal("Up", vm.CurrentMappings.Single(m => m.Button == RemoteButton.Up).ParameterText);
        vm.SelectedDevice = second;
        Assert.Equal("F2", vm.CurrentMappings.Single(m => m.Button == RemoteButton.Up).ParameterText);
    }
}
