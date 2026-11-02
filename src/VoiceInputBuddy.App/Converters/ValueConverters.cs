// Copyright (C) 2026 Voice Input Buddy Contributors
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

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
