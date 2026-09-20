namespace CertificateAutomation.Application.Dtos;

/// <summary>معیارهای جستجوی سوابق نامه‌ها. هر فیلد خالی نادیده گرفته می‌شود.</summary>
public class LetterSearchQuery
{
    public string? LetterNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NationalId { get; set; }
    public string? MembershipNo { get; set; }
    public long? TemplateId { get; set; }

    /// <summary>تاریخ شمسی شروع بازه، مثلاً 1405/01/01.</summary>
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }

    /// <summary>یک عبارت که هم‌زمان در نام، کد ملی، شماره عضویت و شماره نامه جستجو می‌شود.</summary>
    public string? FreeText { get; set; }

    public bool IncludeDeleted { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
