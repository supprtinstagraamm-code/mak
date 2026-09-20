namespace CertificateAutomation.Domain.Entities;

/// <summary>یک Placeholder که هنگام اسکن قالب Word شناسایی شده است.</summary>
public class TemplatePlaceholder
{
    public long Id { get; set; }
    public long TemplateId { get; set; }

    /// <summary>کلید بدون آکولاد، مثلاً «کد_ملی».</summary>
    public string PlaceholderKey { get; set; } = string.Empty;

    /// <summary>اگر true باشد، بدون مقدار اجازه صدور نامه داده نمی‌شود.</summary>
    public bool IsRequired { get; set; } = true;
}
