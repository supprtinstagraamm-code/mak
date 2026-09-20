namespace CertificateAutomation.Application.Dtos;

/// <summary>خلاصه نتیجه بارگذاری فایل Excel.</summary>
public class ImportResult
{
    public int TotalRows { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }

    /// <summary>ستون‌هایی که در Excel بودند و برای اولین بار دیده شدند.</summary>
    public List<string> NewColumns { get; } = new();

    /// <summary>هشدارهای غیربحرانی، مثلاً کد ملی نامعتبر در سطر ۱۲.</summary>
    public List<string> Warnings { get; } = new();
}
