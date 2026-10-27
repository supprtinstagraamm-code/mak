using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CertificateAutomation.NumberStamp;

/// <summary>
/// ثبت خودکار ابزار شناور در کلید Run رجیستری کاربر جاری (بدون نیاز به دسترسی مدیر)،
/// تا همیشه با روشن شدن ویندوز، خودش اجرا شود. کلید جدا از برنامهٔ اصلی (ValueName متفاوت)
/// تا با غیرفعال کردن اجرای خودکار برنامهٔ اصلی تداخل نکند.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NumberStampAutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CertificateAutomation_NumberStamp";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// اگر ثبت نشده یا مسیر فایل اجرایی از آخرین بار فرق کرده (مثلاً بعد از جابه‌جایی
    /// پوشهٔ قابل‌حمل روی یک فلش جدید)، دوباره با مسیر درست ثبت می‌شود.
    /// </summary>
    public static void EnsureRegistered()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            var expectedValue = $"\"{exePath}\"";

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            var current = key.GetValue(ValueName) as string;
            if (!string.Equals(current, expectedValue, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, expectedValue);
        }
        catch
        {
            // یک قابلیت جانبی است؛ نبودش نباید مانع اجرای ابزار شود.
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
                EnsureRegistered();
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // نادیده گرفته می‌شود.
        }
    }
}
