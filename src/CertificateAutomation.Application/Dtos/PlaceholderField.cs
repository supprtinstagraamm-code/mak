namespace CertificateAutomation.Application.Dtos;

/// <summary>یک فیلد قالب به‌همراه مقدار و منبع آن، برای نمایش در صفحه پیش‌نمایش.</summary>
public class PlaceholderField
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Value { get; set; }

    /// <summary>منبع مقدار: سیستمی، اطلاعات فرد، ستون اضافی یا ورودی دستی.</summary>
    public string Source { get; set; } = string.Empty;

    public bool IsRequired { get; set; } = true;
    public bool IsMissing => IsRequired && string.IsNullOrWhiteSpace(Value);
}
