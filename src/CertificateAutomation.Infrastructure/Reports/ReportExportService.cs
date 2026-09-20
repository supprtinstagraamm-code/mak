using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Infrastructure.Data;

namespace CertificateAutomation.Infrastructure.Reports;

/// <summary>
/// نقطهٔ ورود خروجی گزارش: Excel با ClosedXML، و PDF با ساخت یک سند Word موقت و تبدیل آن
/// از طریق همان لایهٔ Renderer که برای نامه‌ها هم استفاده می‌شود (Word/LibreOffice).
/// </summary>
public class ReportExportService : IReportExportService
{
    private readonly ExcelReportExporter _excelExporter = new();
    private readonly WordReportBuilder _wordBuilder = new();
    private readonly IDocumentRenderer _renderer;
    private readonly IAppLogger _logger;

    public ReportExportService(IDocumentRenderer renderer, IAppLogger logger)
    {
        _renderer = renderer;
        _logger = logger;
    }

    public Task<Result<string>> ExportExcelAsync(ReportExportRequest request, CancellationToken ct = default)
    {
        try
        {
            var path = _excelExporter.Build(request);
            _logger.Info($"خروجی Excel گزارش ساخته شد: {path}");
            return Task.FromResult(Result.Success(path));
        }
        catch (Exception ex)
        {
            _logger.Error("ساخت خروجی Excel ناموفق بود.", ex);
            return Task.FromResult(Result.Failure<string>($"ساخت فایل Excel ممکن نشد: {ex.Message}"));
        }
    }

    public async Task<Result<string>> ExportPdfAsync(ReportExportRequest request, CancellationToken ct = default)
    {
        string? tempDocx = null;
        try
        {
            // یک سند Word موقت ساخته و سپس به PDF تبدیل می‌شود؛ فایل Word موقت در پایان حذف می‌شود.
            tempDocx = Path.Combine(AppPaths.TempFolder, $"report_{Guid.NewGuid():N}.docx");
            _wordBuilder.Build(request, tempDocx);

            var pdfResult = await _renderer.ConvertToPdfAsync(tempDocx, request.OutputPath, ct);

            if (pdfResult.IsFailure)
                return Result.Failure<string>(pdfResult.Error!);

            _logger.Info($"خروجی PDF گزارش ساخته شد: {request.OutputPath}");
            return Result.Success(request.OutputPath);
        }
        catch (Exception ex)
        {
            _logger.Error("ساخت خروجی PDF ناموفق بود.", ex);
            return Result.Failure<string>($"ساخت فایل PDF ممکن نشد: {ex.Message}");
        }
        finally
        {
            if (tempDocx is not null && File.Exists(tempDocx))
            {
                try { File.Delete(tempDocx); } catch { /* بی‌اثر است */ }
            }
        }
    }
}
