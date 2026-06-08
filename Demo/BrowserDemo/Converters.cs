using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BrowserDemo;

/// <summary>
/// 值转换器集合
/// </summary>
public static class Converters
{
    /// <summary>bool → Visibility</summary>
    public static readonly BoolToVisibilityConverter BoolToVisibilityConverter = new();
    /// <summary>bool 反转 → Visibility</summary>
    public static readonly InverseBoolToVisibilityConverter InverseBoolToVisibilityConverter = new();
    /// <summary>bool → GridLength (0 / *)</summary>
    public static readonly BoolToGridLengthConverter BoolToGridLengthConverter = new();
    /// <summary>bool → 激活标签背景色</summary>
    public static readonly ActiveTabBgConverter ActiveTabBgConverter = new();
    /// <summary>bool → 激活标签边框色</summary>
    public static readonly ActiveTabBorderConverter ActiveTabBorderConverter = new();
    /// <summary>bool → 激活标签文字色</summary>
    public static readonly ActiveTabTextConverter ActiveTabTextConverter = new();
    /// <summary>消息角色 → 背景色</summary>
    public static readonly MessageRoleBgConverter MessageRoleBgConverter = new();
    /// <summary>消息角色 → 边框色</summary>
    public static readonly MessageRoleBorderConverter MessageRoleBorderConverter = new();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public class ActiveTabBgConverter : IValueConverter
{
    private static readonly Brush Active = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x32));
    private static readonly Brush Inactive = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x29));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Active : Inactive;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ActiveTabBorderConverter : IValueConverter
{
    private static readonly Brush Active = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x4A));
    private static readonly Brush Inactive = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3E));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Active : Inactive;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ActiveTabTextConverter : IValueConverter
{
    private static readonly Brush Active = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush Inactive = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Active : Inactive;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// ============= AI 对话消息转换器 =============

/// <summary>消息角色 → 背景色</summary>
public class MessageRoleBgConverter : IValueConverter
{
    private static readonly Brush UserBg = new SolidColorBrush(Color.FromRgb(0x1A, 0x5C, 0xB5));
    private static readonly Brush AssistantBg = new SolidColorBrush(Color.FromRgb(0x2C, 0x6E, 0x3C));
    private static readonly Brush SystemBg = new SolidColorBrush(Color.FromRgb(0x6B, 0x4C, 0x3A));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Models.MessageRole.User ? UserBg
            : value is Models.MessageRole.Assistant ? AssistantBg
            : SystemBg;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>消息角色 → 边框色</summary>
public class MessageRoleBorderConverter : IValueConverter
{
    private static readonly Brush UserBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x6C, 0xC5));
    private static readonly Brush AssistantBorder = new SolidColorBrush(Color.FromRgb(0x3C, 0x7E, 0x4C));
    private static readonly Brush SystemBorder = new SolidColorBrush(Color.FromRgb(0x7B, 0x5C, 0x4A));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Models.MessageRole.User ? UserBorder
            : value is Models.MessageRole.Assistant ? AssistantBorder
            : SystemBorder;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool 反转 → Visibility</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}

/// <summary>bool → GridLength (true = "*"/Auto, false = 0)</summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is GridLength g && g.Value > 0;
}
