// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using VoiceInputBuddy.App.ViewModels;
using VoiceInputBuddy.Core.Config;
using VoiceInputBuddy.Core.Models;
using Xunit;

namespace VoiceInputBuddy.Tests;

public class MappingDeviceTests
{
    [Fact]
    public void Rc003Defaults_UseRequestedActions_WithoutChangingOtherModels()
    {
        var mappings = RemoteDevice.CreateDefaultMappings(RemoteModelType.XiaomiRC003);
        var tv = mappings.Single(m => m.Button == RemoteButton.LiveTv);
        Assert.Equal(ButtonActionType.MediaControl, tv.ClickAction);
        Assert.Equal(MediaCommand.PlayPause, tv.ClickMedia);
        var home = mappings.Single(m => m.Button == RemoteButton.Home);
        Assert.Equal(ButtonActionType.KeyboardShortcut, home.ClickAction);
        Assert.Equal("Win+Tab", home.ClickShortcut);
        var menu = mappings.Single(m => m.Button == RemoteButton.Menu);
        Assert.Equal(ButtonActionType.TextSnippet, menu.ClickAction);
        Assert.Null(menu.ClickText);
        var power = mappings.Single(m => m.Button == RemoteButton.Power);
        Assert.Equal(ButtonActionType.KeyboardShortcut, power.ClickAction);
        Assert.Equal("Backspace", power.ClickShortcut);

        var google = RemoteDevice.CreateDefaultMappings(RemoteModelType.GoogleG9N9N);
        Assert.Equal("Alt+Tab", google.Single(m => m.Button == RemoteButton.Home).ClickShortcut);
        Assert.Equal("Apps", google.Single(m => m.Button == RemoteButton.Menu).ClickShortcut);
        Assert.Equal(ButtonActionType.None, google.Single(m => m.Button == RemoteButton.LiveTv).ClickAction);
        Assert.Equal(ButtonActionType.None, google.Single(m => m.Button == RemoteButton.Power).ClickAction);
    }

    [Fact]
    public void TextSnippetMapping_LabelsItsParameterAsCommonPhrase()
    {
        var mapping = new ButtonMapping { Button = RemoteButton.Menu, ClickAction = ButtonActionType.TextSnippet };
        var vm = new MappingItemViewModel(mapping);
        Assert.Equal("常用短语：", vm.ParameterLabel);
        vm.ParameterText = "你好";
        Assert.Equal("你好", mapping.ClickText);
        vm.ClickAction = ButtonActionType.KeyboardShortcut;
        Assert.Equal("目标键盘快捷键：", vm.ParameterLabel);
    }

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
