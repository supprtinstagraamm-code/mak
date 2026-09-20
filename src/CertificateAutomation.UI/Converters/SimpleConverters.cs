using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CertificateAutomation.UI.Converters;

/// <summary>معکوس کردن مقدار بولی؛ برای غیرفعال کردن دکمه هنگام مشغول بودن.</summary>
public class InverseBoolConverter : IValueConverter
{
    /// <summary>نمونه مشترک برای استفاده مستقیم در XAML با x:Static.</summary>
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

/// <summary>اگر رشته خالی بود عنصر را نشان می‌دهد (برای متن راهنمای فیلد جستجو).</summary>
public class EmptyToVisibilityConverter : IValueConverter
{
    public static readonly EmptyToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>اگر تعداد صفر بود عنصر را نشان می‌دهد (برای پیام «موردی وجود ندارد»).</summary>
public class ZeroToVisibilityConverter : IValueConverter
{
    public static readonly ZeroToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>وزن فونت پررنگ برای مرحلهٔ فعال ویزارد، معمولی برای بقیه.</summary>
public class BoolToWeightConverter : IValueConverter
{
    public static readonly BoolToWeightConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? FontWeights.Bold : FontWeights.Normal;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>رنگ تأکید برای مرحلهٔ فعال ویزارد، رنگ کم‌رنگ برای مراحل دیگر.</summary>
public class BoolToAccentConverter : IValueConverter
{
    public static readonly BoolToAccentConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        // رنگ‌ها از منبع پویا خوانده می‌شوند تا با تم روشن/تیره هماهنگ بمانند.
        var app = System.Windows.Application.Current;
        if (value is true && app?.Resources["AccentBrush"] is Brush accent) return accent;
        if (app?.Resources["TextSecondary"] is Brush secondary) return secondary;
        return Brushes.Gray;
    }
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>معکوس BooleanToVisibility: true → Collapsed، false → Visible.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>مقدار بولی true → Visible، false → Collapsed (برای نمایش لوگو در صورت وجود).</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>مقدار بزرگ‌تر از صفر → Visible (برای نشان‌های شمارشی مثل تعداد یادآور امروز).</summary>
public class PositiveCountToVisibilityConverter : IValueConverter
{
    public static readonly PositiveCountToVisibilityConverter Instance = new();
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
