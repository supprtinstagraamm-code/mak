namespace CertificateAutomation.Domain.Entities;

/// <summary>
/// یک یادآور ماهانهٔ تکرارشونده، مثلاً «روز ۲۰ هر ماه: پر کردن فرم سپهر».
/// فقط یک بار ساخته می‌شود و هر ماه خودکار دوباره فعال است؛ نیازی به ساخت دوبارهٔ
/// آن هر ماه نیست. وضعیت «انجام شد» برای هر ماه جداگانه در ReminderCompletion ثبت می‌شود.
/// </summary>
public class Reminder
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>روز ماه که یادآور باید نمایش/اعلان داده شود (۱ تا ۳۱).</summary>
    public int DayOfMonth { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>پرشده در زمان اجرا: آیا این یادآور برای ماه جاری انجام‌شده علامت خورده.</summary>
    public bool IsCompletedThisMonth { get; set; }
}

/// <summary>ثبت انجام‌شدن یک یادآور در یک ماه مشخص.</summary>
public class ReminderCompletion
{
    public long Id { get; set; }
    public long ReminderId { get; set; }

    /// <summary>قالب "yyyy-MM" میلادی؛ فقط کلید داخلی برای تفکیک هر ماه، به کاربر نمایش داده نمی‌شود.</summary>
    public string YearMonth { get; set; } = string.Empty;

    public DateTime CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
}
