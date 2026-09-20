using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CertificateAutomation.UI.Services;

/// <summary>
/// فعال/غیرفعال کردن اجرای خودکار برنامه هنگام روشن شدن ویندوز (کلید Run در رجیستری
/// کاربر جاری، بدون نیاز به دسترسی مدیر). این با آیکون سینی سیستم ترکیب می‌شود تا
/// یادآورهای ماهانه همیشه در پس‌زمینه بررسی شوند، حتی اگر کاربر یادش برود برنامه را باز کند.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsAutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CertificateAutomation";

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

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                    key.SetValue(ValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // اگر به هر دلیل (مثلاً محدودیت سیستمی) نشد، بی‌سروصدا نادیده گرفته می‌شود؛
            // این یک قابلیت جانبی است و نباید مانع کار اصلی برنامه شود.
        }
    }
}
