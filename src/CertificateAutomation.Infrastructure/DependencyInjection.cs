using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Infrastructure.Data;
using CertificateAutomation.Infrastructure.Logging;
using CertificateAutomation.Infrastructure.Repositories;
using CertificateAutomation.Infrastructure.Security;
using CertificateAutomation.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CertificateAutomation.Infrastructure;

/// <summary>
/// ثبت سرویس‌های لایه زیرساخت. لایه UI فقط این متد را صدا می‌زند و از جزئیات پیاده‌سازی بی‌خبر است.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppLogger, SerilogAppLogger>();
        services.AddSingleton<ISqliteConnectionFactory>(_ => new SqliteConnectionFactory());
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IUserSession, UserSession>();
        services.AddSingleton<DbInitializer>();

        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<IAuditRepository, AuditRepository>();
        services.AddTransient<ILetterRepository, LetterRepository>();
        services.AddTransient<IEmployeeRepository, EmployeeRepository>();
        services.AddTransient<IColumnMappingRepository, ColumnMappingRepository>();
        services.AddTransient<IEmployeeImportService, Excel.ClosedXmlEmployeeImporter>();
        services.AddTransient<ITemplateRepository, TemplateRepository>();
        services.AddSingleton<ITemplateEngine, Word.OpenXmlTemplateEngine>();
        services.AddTransient<ITemplateScanner, Word.OpenXmlTemplateScanner>();

        // شمارنده: یک نمونهٔ مشترک که هر دو اینترفیس به آن اشاره می‌کنند
        services.AddSingleton<Services.NumberGenerator>();
        services.AddSingleton<INumberGenerator>(sp => sp.GetRequiredService<Services.NumberGenerator>());
        services.AddSingleton<Services.IAtomicSerialAllocator>(sp => sp.GetRequiredService<Services.NumberGenerator>());
        services.AddTransient<ILetterIssueService, Services.LetterIssueService>();

        // موتورهای تبدیل/چاپ: هر سه ثبت می‌شوند و Composite بهترین در دسترس را انتخاب می‌کند.
        services.AddTransient<IDocumentRenderer, Rendering.WordInteropRenderer>();
        services.AddTransient<IDocumentRenderer, Rendering.LibreOfficeRenderer>();
        services.AddTransient<IDocumentRenderer, Rendering.ShellPrintRenderer>();
        services.AddSingleton<Rendering.CompositeDocumentRenderer>();
        services.AddSingleton<IPrintService>(sp => new Rendering.WindowsPrintService(
            sp.GetRequiredService<Rendering.CompositeDocumentRenderer>(),
            sp.GetRequiredService<IAppLogger>()));

        services.AddTransient<IBackupService, Backup.ZipBackupManager>();
        services.AddTransient<IStatisticsService, Services.StatisticsService>();
        services.AddTransient<IReminderRepository, Repositories.ReminderRepository>();
        services.AddTransient<IReminderService, Services.ReminderService>();
        services.AddTransient<IReportExportService, Reports.ReportExportService>();

        return services;
    }
}
