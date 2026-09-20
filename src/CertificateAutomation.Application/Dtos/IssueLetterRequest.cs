namespace CertificateAutomation.Application.Dtos;

/// <summary>درخواست صدور یک نامه جدید.</summary>
public class IssueLetterRequest
{
    public long EmployeeId { get; set; }
    public long TemplateId { get; set; }

    /// <summary>مقادیری که کاربر دستی وارد کرده (برای Placeholderهایی که در Excel نبودند).</summary>
    public Dictionary<string, string> ManualValues { get; } = new(StringComparer.Ordinal);

    public string? Description { get; set; }
    public string? PrinterName { get; set; }
    public int Copies { get; set; } = 1;

    /// <summary>اگر false باشد، نامه فقط صادر و ثبت می‌شود بدون ارسال به چاپگر.</summary>
    public bool PrintImmediately { get; set; } = true;

    /// <summary>
    /// شمارهٔ سریال دلخواه کاربر (از دکمه‌های + و − در پیش‌نمایش). اگر null باشد یا آن شماره
    /// قبلاً استفاده شده باشد، به‌طور خودکار شمارهٔ بعدی امن تخصیص می‌یابد.
    /// </summary>
    public int? PreferredSerial { get; set; }
}
