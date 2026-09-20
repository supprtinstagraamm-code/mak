using System.Globalization;
using System.Text;

namespace CertificateAutomation.Domain.Common;

/// <summary>
/// کمک‌کننده تاریخ شمسی. تمام تاریخ‌های نمایشی و ذخیره‌شده در پایگاه داده
/// با قالب yyyy/MM/dd شمسی و ارقام انگلیسی نگهداری می‌شوند تا مرتب‌سازی
/// و جستجوی متنی به‌درستی کار کند. تبدیل به ارقام فارسی فقط در لایه نمایش انجام می‌شود.
/// </summary>
public static class PersianDate
{
    private static readonly PersianCalendar Calendar = new();

    private const string EnglishDigits = "0123456789";
    private const string PersianDigits = "۰۱۲۳۴۵۶۷۸۹";

    /// <summary>سال شمسی جاری، مثلاً 1405.</summary>
    public static int CurrentYear => Calendar.GetYear(DateTime.Now);

    /// <summary>تبدیل تاریخ میلادی به رشته شمسی، مثلاً 1405/03/11.</summary>
    public static string ToPersian(DateTime date)
    {
        var year = Calendar.GetYear(date);
        var month = Calendar.GetMonth(date);
        var day = Calendar.GetDayOfMonth(date);
        return $"{year:0000}/{month:00}/{day:00}";
    }

    /// <summary>تاریخ شمسی امروز.</summary>
    public static string Today() => ToPersian(DateTime.Now);

    /// <summary>ساعت جاری با قالب HH:mm.</summary>
    public static string CurrentTime() => DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// تلاش برای تبدیل رشته شمسی (با ارقام فارسی یا انگلیسی و جداکننده / یا - یا .) به تاریخ میلادی.
    /// </summary>
    public static bool TryParse(string? input, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var normalized = ToEnglishDigits(input).Replace('-', '/').Replace('.', '/').Trim();
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;

        if (!int.TryParse(parts[0], out var y) ||
            !int.TryParse(parts[1], out var m) ||
            !int.TryParse(parts[2], out var d)) return false;

        if (y < 1300 || y > 1500 || m < 1 || m > 12 || d < 1 || d > 31) return false;
        if (d > Calendar.GetDaysInMonth(y, m)) return false;

        try
        {
            result = Calendar.ToDateTime(y, m, d, 0, 0, 0, 0);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>تبدیل ارقام فارسی و عربی به انگلیسی (برای جستجو و ذخیره‌سازی).</summary>
    public static string ToEnglishDigits(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            sb.Append(ch switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)(ch - '\u06F0' + '0'), // ارقام فارسی
                >= '\u0660' and <= '\u0669' => (char)(ch - '\u0660' + '0'), // ارقام عربی
                _ => ch
            });
        }
        return sb.ToString();
    }

    /// <summary>تبدیل ارقام انگلیسی به فارسی (فقط برای نمایش).</summary>
    public static string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            var index = EnglishDigits.IndexOf(ch);
            sb.Append(index >= 0 ? PersianDigits[index] : ch);
        }
        return sb.ToString();
    }
}
