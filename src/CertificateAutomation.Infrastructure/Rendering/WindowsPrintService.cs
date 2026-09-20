using System.Runtime.Versioning;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;

namespace CertificateAutomation.Infrastructure.Rendering;

/// <summary>
/// دسترسی به چاپگرهای نصب‌شدهٔ ویندوز و ارسال چاپ از طریق Renderer.
/// شمارش چاپگرها با System.Drawing.Printing انجام می‌شود.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsPrintService : IPrintService
{
    private readonly IDocumentRenderer _renderer;
    private readonly IAppLogger _logger;

    public WindowsPrintService(IDocumentRenderer renderer, IAppLogger logger)
    {
        _renderer = renderer;
        _logger = logger;
    }

    public IReadOnlyList<string> GetPrinters()
    {
        try
        {
            var printers = new List<string>();
            foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                printers.Add(name);
            return printers;
        }
        catch (Exception ex)
        {
            _logger.Error("خواندن فهرست چاپگرها ناموفق بود.", ex);
            return Array.Empty<string>();
        }
    }

    public string? GetSystemDefaultPrinter()
    {
        try
        {
            var settings = new System.Drawing.Printing.PrinterSettings();
            return settings.PrinterName;
        }
        catch (Exception ex)
        {
            _logger.Error("خواندن چاپگر پیش‌فرض ناموفق بود.", ex);
            return null;
        }
    }

    public Task<Result> PrintAsync(string documentPath, string? printerName, int copies, CancellationToken ct = default)
        => _renderer.PrintAsync(documentPath, printerName, copies, ct);
}
