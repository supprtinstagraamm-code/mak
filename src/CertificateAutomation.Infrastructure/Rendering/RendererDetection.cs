using Microsoft.Win32;

namespace CertificateAutomation.Infrastructure.Rendering;

/// <summary>
/// تشخیص اینکه چه ابزارهایی برای تبدیل/چاپ سند روی این سیستم نصب‌اند.
/// نتیجه یک‌بار محاسبه و کش می‌شود تا هر بار رجیستری/دیسک خوانده نشود.
/// </summary>
public static class RendererDetection
{
    private static bool? _wordAvailable;
    private static string? _libreOfficePath;
    private static bool _libreChecked;

    /// <summary>آیا Microsoft Word نصب است (از طریق ثبت COM).</summary>
    public static bool IsWordAvailable()
    {
        if (_wordAvailable.HasValue) return _wordAvailable.Value;

        try
        {
            // اگر ProgID مربوط به Word.Application در رجیستری باشد، یعنی Word نصب است.
            var type = Type.GetTypeFromProgID("Word.Application");
            _wordAvailable = type is not null;
        }
        catch
        {
            _wordAvailable = false;
        }

        return _wordAvailable.Value;
    }

    /// <summary>مسیر اجرایی LibreOffice (soffice.exe) در صورت نصب بودن، وگرنه null.</summary>
    public static string? FindLibreOffice()
    {
        if (_libreChecked) return _libreOfficePath;
        _libreChecked = true;

        // مسیرهای معمول نصب LibreOffice روی ویندوز
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "LibreOffice", "program", "soffice.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "LibreOffice", "program", "soffice.exe")
        };

        // بررسی رجیستری برای مسیر نصب سفارشی
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\LibreOffice\UNO\InstallPath")
                          ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\LibreOffice\UNO\InstallPath");
            if (key?.GetValue(null) is string installPath)
                candidates.Add(Path.Combine(installPath, "soffice.exe"));
        }
        catch { /* رجیستری در دسترس نیست: نادیده */ }

        _libreOfficePath = candidates.FirstOrDefault(File.Exists);
        return _libreOfficePath;
    }

    public static bool IsLibreOfficeAvailable() => FindLibreOffice() is not null;
}
