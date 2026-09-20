using System.Diagnostics;
using System.Runtime.Versioning;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Enums;

namespace CertificateAutomation.Infrastructure.Rendering;

/// <summary>
/// آخرین گزینه: استفاده از قابلیت چاپ داخلی ویندوز (ShellExecute با verb «print»).
/// همیشه در دسترس است اما امکاناتش محدود است: تبدیل PDF ندارد و تعداد نسخه/چاپگر
/// را نمی‌تواند دقیق کنترل کند. صرفاً تضمین می‌کند که برنامه هرگز بدون هیچ راه چاپی نماند.
/// </summary>
[SupportedOSPlatform("windows")]
public class ShellPrintRenderer : IDocumentRenderer
{
    private readonly IAppLogger _logger;

    public ShellPrintRenderer(IAppLogger logger) => _logger = logger;

    public RendererKind Kind => RendererKind.ShellOnly;

    // همیشه در دسترس است (به‌عنوان تضمین نهایی).
    public bool IsAvailable => true;

    /// <summary>Shell تبدیل PDF ندارد؛ این قابلیت پشتیبانی نمی‌شود.</summary>
    public Task<Result<string>> ConvertToPdfAsync(string docxPath, string pdfPath, CancellationToken ct = default)
        => Task.FromResult(Result.Failure<string>(
            "برای خروجی PDF باید Microsoft Word یا LibreOffice نصب باشد. " +
            "می‌توانید سند Word را باز کرده و از منوی «ذخیره به‌صورت PDF» استفاده کنید."));

    public Task<Result> PrintAsync(string documentPath, string? printerName, int copies, CancellationToken ct = default)
    {
        if (!File.Exists(documentPath))
            return Task.FromResult(Result.Failure($"فایل یافت نشد:\n{documentPath}"));

        try
        {
            // verb «print» سند را با برنامهٔ پیش‌فرض (معمولاً Word) چاپ می‌کند.
            for (var i = 0; i < Math.Max(1, copies); i++)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = documentPath,
                    Verb = "print",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }

            _logger.Info("سند برای چاپ به برنامهٔ پیش‌فرض ویندوز ارسال شد.");
            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            _logger.Error("چاپ با روش پیش‌فرض ویندوز ناموفق بود.", ex);
            return Task.FromResult(Result.Failure(
                $"چاپ ممکن نشد. سند در مسیر زیر ذخیره شده و می‌توانید آن را دستی باز و چاپ کنید:\n{documentPath}\n\nشرح فنی: {ex.Message}"));
        }
    }
}
