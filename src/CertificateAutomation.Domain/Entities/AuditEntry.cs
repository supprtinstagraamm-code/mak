namespace CertificateAutomation.Domain.Entities;

/// <summary>ثبت یک عملیات حساس برای ممیزی (حذف سابقه، تغییر شماره، تغییر تنظیمات و ...).</summary>
public class AuditEntry
{
    public long Id { get; set; }

    /// <summary>نوع عملیات، مثلاً IssueLetter یا DeleteLetter.</summary>
    public string ActionType { get; set; } = string.Empty;

    public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    public string? UserName { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Details { get; set; }
}
