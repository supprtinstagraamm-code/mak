namespace CertificateAutomation.Domain.Entities;

/// <summary>
/// یک نامه صادرشده. مقادیر Snap* عمداً کپی (Snapshot) گرفته می‌شوند تا اگر
/// بعداً فایل Excel تغییر کرد یا قالب حذف شد، سابقه دست‌نخورده باقی بماند.
/// </summary>
public class Letter
{
    public long Id { get; set; }

    /// <summary>شماره کامل نامه، مثلاً 1405/00001.</summary>
    public string LetterNumber { get; set; } = string.Empty;

    public int JalaliYear { get; set; }
    public int Serial { get; set; }

    public DateTime IssuedAtUtc { get; set; }

    /// <summary>تاریخ شمسی صدور، مثلاً 1405/03/11.</summary>
    public string JalaliDate { get; set; } = string.Empty;

    /// <summary>ساعت صدور، مثلاً 14:32.</summary>
    public string IssuedTime { get; set; } = string.Empty;

    public long? EmployeeId { get; set; }
    public string? SnapFirstName { get; set; }
    public string? SnapLastName { get; set; }
    public string? SnapNationalId { get; set; }
    public string? SnapMembershipNo { get; set; }
    public string? SnapPosition { get; set; }
    public string? SnapUnit { get; set; }

    public long? TemplateId { get; set; }
    public string SnapTemplateTitle { get; set; } = string.Empty;

    /// <summary>تمام مقادیر جایگزین‌شده در قالب، به‌صورت JSON. برای چاپ مجدد دقیقاً همان سند.</summary>
    public string? DataSnapshotJson { get; set; }

    public int PrintCount { get; set; }
    public DateTime? LastPrintedAt { get; set; }

    /// <summary>مسیر فایل PDF آرشیوشده، در صورت فعال بودن آرشیو.</summary>
    public string? ArchivePdfPath { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// true یعنی این فقط یک «ثبت شماره» است (مثلاً از ابزار شناور، برای نامه‌ای که
    /// بیرون از این سامانه نوشته شده)، نه یک نامهٔ واقعاً تولیدشده با قالب و کارمند.
    /// </summary>
    public bool IsManualEntry { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public string SnapFullName => $"{SnapFirstName} {SnapLastName}".Trim();

    /// <summary>نام نمایشی طرف نامه در لیست‌ها؛ برای ثبت‌های دستی به‌جای نام خالی.</summary>
    public string DisplayPerson => IsManualEntry ? "— ثبت دستی خارج از سامانه —" : SnapFullName;

    /// <summary>عنوان نمایشی نوع نامه در لیست‌ها.</summary>
    public string DisplayTemplateTitle => IsManualEntry ? "ثبت دستی شماره" : SnapTemplateTitle;
}
