namespace CertificateAutomation.Application.Dtos;

/// <summary>
/// نتیجهٔ «ثبت دستی شماره» — یعنی وقتی فقط یک شمارهٔ نامه رزرو/مستند می‌شود،
/// بدون ساخت سند Word (مثلاً برای نامه‌ای که دستی یا بیرون از این سامانه نوشته شده).
/// معمولاً از ابزار شناور صدا زده می‌شود.
/// </summary>
public class ManualNumberResult
{
    public long LetterId { get; set; }

    /// <summary>شمارهٔ کامل نامه، مثلاً 1405/00001.</summary>
    public string LetterNumber { get; set; } = string.Empty;

    public int JalaliYear { get; set; }
    public int Serial { get; set; }

    /// <summary>تاریخ شمسی ثبت.</summary>
    public string JalaliDate { get; set; } = string.Empty;

    /// <summary>ساعت ثبت.</summary>
    public string Time { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// اگر ثبت سطر در فایل اکسل با خطا مواجه شود (مثلاً فایل باز بوده)،
    /// خود شماره همچنان رزرو و در دیتابیس ثبت شده؛ این فقط یک هشدار جانبی است.
    /// </summary>
    public string? ExcelWarning { get; set; }
}
