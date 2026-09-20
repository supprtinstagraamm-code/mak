using System.Globalization;
using System.Windows.Data;
using CertificateAutomation.Domain.Common;

namespace CertificateAutomation.UI.Converters;

/// <summary>
/// نمایش ارقام به‌صورت فارسی. داده اصلی همیشه با ارقام انگلیسی ذخیره می‌شود و
/// تبدیل فقط در لایه نمایش انجام می‌گیرد.
/// </summary>
public class PersianDigitsConverter : IValueConverter
{
    /// <summary>اگر false شود (از تنظیمات)، تبدیل انجام نمی‌شود.</summary>
    public static bool Enabled { get; set; } = true;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        if (string.IsNullOrEmpty(text)) return text;
        return Enabled ? PersianDate.ToPersianDigits(text) : text;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? null : PersianDate.ToEnglishDigits(value.ToString() ?? string.Empty);
}
