namespace CertificateAutomation.Domain.Entities;

/// <summary>
/// نگاشت عنوان ستون فایل Excel به کلید Placeholder داخل قالب‌های Word.
/// مثال: عنوان «کد ملی» ← کلید «کد_ملی».
/// </summary>
public class ColumnMapping
{
    public long Id { get; set; }

    /// <summary>عنوان ستون همان‌گونه که در سطر اول Excel آمده است.</summary>
    public string ExcelHeader { get; set; } = string.Empty;

    /// <summary>کلیدی که در قالب Word به‌صورت {{کلید}} نوشته می‌شود.</summary>
    public string PlaceholderKey { get; set; } = string.Empty;

    /// <summary>عنوان نمایشی در رابط کاربری.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>اگر true باشد، به یکی از ستون‌های ثابت جدول Employees نگاشت می‌شود.</summary>
    public bool IsCore { get; set; }

    public int SortOrder { get; set; }
}
