// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VoiceInputBuddy.Core.Models;

namespace VoiceInputBuddy.App.Converters;

public sealed class BoolToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ConnectedBrush = new(Color.FromRgb(0, 204, 102));
    private static readonly SolidColorBrush DisconnectedBrush = new(Color.FromRgb(102, 102, 102));

    static BoolToBrushConverter()
    {
        ConnectedBrush.Freeze();
        DisconnectedBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ConnectedBrush : DisconnectedBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
public sealed class BoolToScanTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "⏹ 停止扫描" : "🔍 扫描设备";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            b = !b;
        }
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool notNull = value != null;
        if (parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            notNull = !notNull;
        }
        return notNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
public sealed class EqualValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? valStr = value?.ToString();
        string? paramStr = parameter?.ToString();
        bool match = string.Equals(valStr, paramStr, StringComparison.OrdinalIgnoreCase);
        return match ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
public sealed class EqualValueToBrushConverter : IValueConverter
{
    public Brush ActiveBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0, 122, 204));
    public Brush InactiveBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? valStr = value?.ToString();
        string? paramStr = parameter?.ToString();
        return string.Equals(valStr, paramStr, StringComparison.OrdinalIgnoreCase) ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EqualValueToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? valStr = value?.ToString();
        string? paramStr = parameter?.ToString();
        return string.Equals(valStr, paramStr, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string paramStr)
        {
            var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (enumType.IsEnum && Enum.TryParse(enumType, paramStr, true, out var result))
            {
                return result;
            }
        }
        return Binding.DoNothing;
    }
}

public sealed class BatteryVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int level && level >= 0) return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EnumToDisplayNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ButtonActionType.None => "无动作",
            ButtonActionType.KeyboardShortcut => "模拟键盘快捷键",
            ButtonActionType.MediaControl => "多媒体控制",
            ButtonActionType.TextSnippet => "文本片段快速输入",
            ButtonActionType.ContinuousRepeat => "持续连发 (连按)",
            MediaCommand.None => "无",
            MediaCommand.PlayPause => "播放 / 暂停",
            MediaCommand.VolumeUp => "音量加 (+)",
            MediaCommand.VolumeDown => "音量减 (-)",
            MediaCommand.Mute => "静音",
            MediaCommand.NextTrack => "下一首",
            MediaCommand.PreviousTrack => "上一首",
            TargetInputTool.WeChatInput => "微信输入法",
            TargetInputTool.DoubaoInput => "豆包输入法 / 客户端",
            TargetInputTool.WindowsDictation => "Windows 语音听写 (Win+H)",
            TargetInputTool.Typeless => "Typeless 智能转写",
            VoiceTriggerMode.HoldToTalk => "按住说话 (Hold to Talk)",
            VoiceTriggerMode.ClickToToggle => "点按切换 (Click to Toggle)",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class GainToDbTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is float gain && gain > 0.001f)
        {
            double db = Math.Round(20.0 * Math.Log10(gain));
            return db >= 0 ? $"+{db:0} dB" : $"{db:0} dB";
        }
        if (value is double dGain && dGain > 0.001)
        {
            double db = Math.Round(20.0 * Math.Log10(dGain));
            return db >= 0 ? $"+{db:0} dB" : $"{db:0} dB";
        }
        return "0 dB";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
