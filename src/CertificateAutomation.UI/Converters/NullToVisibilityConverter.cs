using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CertificateAutomation.UI.Converters;

/// <summary>مقدار خالی یا null را پنهان می‌کند. با پارامتر "invert" رفتار برعکس می‌شود.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is not null && !string.IsNullOrWhiteSpace(value.ToString());
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            hasValue = !hasValue;

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
