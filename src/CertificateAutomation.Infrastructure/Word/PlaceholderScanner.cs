using System.Text.RegularExpressions;

namespace CertificateAutomation.Infrastructure.Word;

/// <summary>
/// شناسایی الگوی Placeholder به‌شکل {{کلید}} در یک متن.
/// این کلاس فقط با رشته کار می‌کند تا هم برای اسکن قالب و هم برای جایگزینی قابل استفاده باشد.
/// </summary>
public static class PlaceholderScanner
{
    /// <summary>
    /// الگوی Placeholder: دو آکولاد باز، هر چیزی به‌جز آکولاد، دو آکولاد بسته.
    /// فاصله‌های اطراف کلید نادیده گرفته می‌شوند تا {{ نام }} و {{نام}} یکسان باشند.
    /// </summary>
    public static readonly Regex Pattern = new(
        @"\{\{\s*([^{}]+?)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>استخراج فهرست یکتای کلیدها از یک متن (بدون آکولاد).</summary>
    public static IReadOnlyList<string> Extract(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Pattern.Matches(text))
        {
            var key = m.Groups[1].Value.Trim();
            if (key.Length > 0 && seen.Add(key))
                keys.Add(key);
        }

        return keys;
    }

    /// <summary>نرمال‌سازی کلید: «ي/ك» عربی → فارسی، فاصله → زیرخط، حذف فاصله‌های اضافی.</summary>
    public static string NormalizeKey(string key)
        => key.Trim().Replace('ي', 'ی').Replace('ك', 'ک').Replace("  ", " ").Replace(' ', '_');

    /// <summary>
    /// جایگزینی همه Placeholderها در یک متن با مقادیر داده‌شده.
    /// ابتدا تطبیق دقیق امتحان می‌شود؛ اگر نبود، تطبیق نرمال‌شده (برای مواردی که کاربر
    /// در Word حروف عربی «ي/ك» تایپ کرده ولی ستون Excel فارسی است، یا برعکس).
    /// </summary>
    public static string Replace(
        string text,
        IReadOnlyDictionary<string, string> values,
        Func<string, string>? onMissing = null)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // نگاشت کمکی بر اساس کلید نرمال‌شده، برای تطبیق مقاوم
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in values)
        {
            var nk = NormalizeKey(kv.Key);
            normalized[nk] = kv.Value;
        }

        return Pattern.Replace(text, match =>
        {
            var key = match.Groups[1].Value.Trim();

            if (values.TryGetValue(key, out var value))
                return value ?? string.Empty;

            if (normalized.TryGetValue(NormalizeKey(key), out var value2))
                return value2 ?? string.Empty;

            // کلید بدون مقدار: یا رشته خالی، یا رفتار سفارشی
            return onMissing?.Invoke(key) ?? string.Empty;
        });
    }
}
