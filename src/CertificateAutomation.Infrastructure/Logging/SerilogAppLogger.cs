using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Infrastructure.Data;
using Serilog;
using Serilog.Events;

namespace CertificateAutomation.Infrastructure.Logging;

/// <summary>
/// پیاده‌سازی لاگ با Serilog. فایل‌ها روزانه چرخش می‌کنند و ۳۰ روز نگهداری می‌شوند.
/// لاگ فقط روی دیسک محلی نوشته می‌شود و هیچ ارسال شبکه‌ای ندارد.
/// </summary>
public class SerilogAppLogger : IAppLogger, IDisposable
{
    private readonly Serilog.Core.Logger _logger;

    public SerilogAppLogger()
    {
        Directory.CreateDirectory(AppPaths.LogsFolder);

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: Path.Combine(AppPaths.LogsFolder, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public void Info(string message) => _logger.Information(message);
    public void Warn(string message) => _logger.Warning(message);
    public void Debug(string message) => _logger.Debug(message);
    public void Error(string message, Exception? ex = null) => _logger.Error(ex, message);

    public void Dispose() => _logger.Dispose();
}
