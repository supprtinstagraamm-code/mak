namespace CertificateAutomation.Domain.Enums;

/// <summary>موتوری که برای تبدیل/چاپ سند در دسترس است.</summary>
public enum RendererKind
{
    /// <summary>هیچ موتوری شناسایی نشد؛ فقط باز کردن سند با برنامه پیش‌فرض.</summary>
    ShellOnly = 0,
    LibreOffice = 1,
    WordInterop = 2
}
