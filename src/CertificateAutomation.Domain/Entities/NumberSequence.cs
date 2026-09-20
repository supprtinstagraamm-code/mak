namespace CertificateAutomation.Domain.Entities;

/// <summary>شمارنده نامه برای هر سال شمسی. با شروع سال جدید، ردیف جدید ساخته می‌شود.</summary>
public class NumberSequence
{
    public int JalaliYear { get; set; }
    public int LastSerial { get; set; }

    /// <summary>قالب شماره، مثلاً {year}/{serial:00000}.</summary>
    public string Format { get; set; } = "{year}/{serial:00000}";

    public DateTime UpdatedAt { get; set; }
}
