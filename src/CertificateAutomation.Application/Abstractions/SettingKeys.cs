namespace CertificateAutomation.Application.Abstractions;

/// <summary>کلیدهای مجاز جدول Settings. استفاده از رشته خام در سایر بخش‌ها ممنوع است.</summary>
public static class SettingKeys
{
    public const string CompanyName = "CompanyName";
    public const string LogoPath = "LogoPath";
    public const string ExcelPath = "ExcelPath";
    public const string TemplatesFolder = "TemplatesFolder";
    public const string OutputFolder = "OutputFolder";
    public const string DefaultPrinter = "DefaultPrinter";
    public const string NumberFormat = "NumberFormat";
    public const string BackupFolder = "BackupFolder";
    public const string Theme = "Theme";                       // Light | Dark
    public const string UsePersianDigits = "UsePersianDigits";  // true | false
    public const string ArchivePdfEnabled = "ArchivePdfEnabled";
    public const string AutoImportOnStartup = "AutoImportOnStartup";
    public const string AdminLockMinutes = "AdminLockMinutes";
    public const string MustChangePassword = "MustChangePassword";
}
