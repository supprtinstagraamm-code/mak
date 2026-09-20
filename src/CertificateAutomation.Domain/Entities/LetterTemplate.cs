namespace CertificateAutomation.Domain.Entities;

/// <summary>یک قالب Word، مثلاً «گواهی اشتغال به کار».</summary>
public class LetterTemplate
{
    public long Id { get; set; }

    /// <summary>عنوان نمایشی قالب.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>نام فایل داخل پوشه قالب‌ها (بدون مسیر کامل تا انتقال پوشه مشکلی ایجاد نکند).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>دسته‌بندی، مثلاً «گواهی» یا «معرفی‌نامه». برای توسعه‌های آینده مانند قرارداد.</summary>
    public string? Category { get; set; }

    public int DefaultCopies { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime? LastScannedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
