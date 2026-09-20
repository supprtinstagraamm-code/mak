using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Enums;

namespace CertificateAutomation.Infrastructure.Rendering;

/// <summary>
/// Renderer اصلی که در زمان اجرا بهترین گزینهٔ موجود را انتخاب می‌کند:
/// اولویت ۱: Word (بالاترین کیفیت) → اولویت ۲: LibreOffice → اولویت ۳: چاپ پیش‌فرض ویندوز.
///
/// این کلاس همان چیزی است که بقیهٔ برنامه از طریق IDocumentRenderer با آن کار می‌کند؛
/// جزئیات اینکه کدام موتور واقعاً استفاده می‌شود، از دید لایه‌های بالاتر پنهان است.
/// اگر موتور اول شکست بخورد، برای PDF به‌طور خودکار موتور بعدی امتحان می‌شود.
/// </summary>
public class CompositeDocumentRenderer : IDocumentRenderer
{
    private readonly IReadOnlyList<IDocumentRenderer> _all;
    private readonly IAppLogger _logger;

    public CompositeDocumentRenderer(IEnumerable<IDocumentRenderer> renderers, IAppLogger logger)
    {
        // ترتیب اولویت بر اساس نوع
        _all = renderers
            .OrderByDescending(r => (int)r.Kind) // WordInterop=2 > LibreOffice=1 > ShellOnly=0
            .ToList();
        _logger = logger;
    }

    /// <summary>موتوری که در حال حاضر به‌عنوان بهترین گزینهٔ در دسترس انتخاب می‌شود.</summary>
    public IDocumentRenderer? Best => _all.FirstOrDefault(r => r.IsAvailable);

    public RendererKind Kind => Best?.Kind ?? RendererKind.ShellOnly;
    public bool IsAvailable => _all.Any(r => r.IsAvailable);

    /// <summary>توضیح فارسی از موتور فعال، برای نمایش در تنظیمات.</summary>
    public string DescribeActive() => Best?.Kind switch
    {
        RendererKind.WordInterop => "Microsoft Word (کیفیت کامل، چاپ و PDF)",
        RendererKind.LibreOffice => "LibreOffice (چاپ و PDF)",
        RendererKind.ShellOnly => "چاپ پیش‌فرض ویندوز (بدون خروجی PDF)",
        _ => "نامشخص"
    };

    /// <summary>تبدیل به PDF: موتورها به‌ترتیب اولویت امتحان می‌شوند تا یکی موفق شود.</summary>
    public async Task<Result<string>> ConvertToPdfAsync(string docxPath, string pdfPath, CancellationToken ct = default)
    {
        string? lastError = null;

        foreach (var renderer in _all.Where(r => r.IsAvailable))
        {
            var result = await renderer.ConvertToPdfAsync(docxPath, pdfPath, ct);
            if (result.IsSuccess)
            {
                _logger.Info($"تبدیل PDF با {renderer.Kind} انجام شد.");
                return result;
            }
            lastError = result.Error;
            _logger.Warn($"تبدیل PDF با {renderer.Kind} ناموفق بود؛ موتور بعدی امتحان می‌شود.");
        }

        return Result.Failure<string>(lastError
            ?? "هیچ موتوری برای تبدیل به PDF در دسترس نیست. Microsoft Word یا LibreOffice نصب کنید.");
    }

    /// <summary>چاپ: با بهترین موتور در دسترس؛ در صورت شکست، موتور بعدی.</summary>
    public async Task<Result> PrintAsync(string documentPath, string? printerName, int copies, CancellationToken ct = default)
    {
        string? lastError = null;

        foreach (var renderer in _all.Where(r => r.IsAvailable))
        {
            var result = await renderer.PrintAsync(documentPath, printerName, copies, ct);
            if (result.IsSuccess)
            {
                _logger.Info($"چاپ با {renderer.Kind} انجام شد.");
                return result;
            }
            lastError = result.Error;
            _logger.Warn($"چاپ با {renderer.Kind} ناموفق بود؛ موتور بعدی امتحان می‌شود.");
        }

        return Result.Failure(lastError ?? "هیچ موتوری برای چاپ در دسترس نیست.");
    }
}
