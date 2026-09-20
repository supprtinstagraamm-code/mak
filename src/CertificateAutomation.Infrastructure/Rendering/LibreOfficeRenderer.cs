using System.Diagnostics;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Enums;
using CertificateAutomation.Infrastructure.Data;

namespace CertificateAutomation.Infrastructure.Rendering;

/// <summary>
/// تبدیل و چاپ با LibreOffice از طریق خط فرمان (soffice.exe در حالت headless).
/// نیازی به نصب Word ندارد و روی اکثر سیستم‌ها قابل نصب است. برای سیستم‌های ایران
/// که ممکن است Word فعال‌سازی‌شده نداشته باشند، گزینهٔ مناسبی است.
/// </summary>
public class LibreOfficeRenderer : IDocumentRenderer
{
    private readonly IAppLogger _logger;

    public LibreOfficeRenderer(IAppLogger logger) => _logger = logger;

    public RendererKind Kind => RendererKind.LibreOffice;
    public bool IsAvailable => RendererDetection.IsLibreOfficeAvailable();

    public async Task<Result<string>> ConvertToPdfAsync(string docxPath, string pdfPath, CancellationToken ct = default)
    {
        var soffice = RendererDetection.FindLibreOffice();
        if (soffice is null)
            return Result.Failure<string>("LibreOffice روی این سیستم یافت نشد.");

        if (!File.Exists(docxPath))
            return Result.Failure<string>($"فایل ورودی یافت نشد:\n{docxPath}");

        try
        {
            var outputDir = Path.GetDirectoryName(pdfPath) ?? AppPaths.TempFolder;
            Directory.CreateDirectory(outputDir);

            // soffice --headless --convert-to pdf --outdir <dir> <input>
            var args = $"--headless --norestore --convert-to pdf --outdir \"{outputDir}\" \"{docxPath}\"";
            var exit = await RunProcessAsync(soffice, args, ct);

            if (exit != 0)
                return Result.Failure<string>($"LibreOffice با کد خطای {exit} خارج شد.");

            // خروجی هم‌نام فایل ورودی با پسوند pdf ساخته می‌شود؛ در صورت نیاز تغییر نام می‌دهیم.
            var producedPdf = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(docxPath) + ".pdf");
            if (!File.Exists(producedPdf))
                return Result.Failure<string>("LibreOffice فایل PDF را تولید نکرد.");

            if (!string.Equals(producedPdf, pdfPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(pdfPath)) File.Delete(pdfPath);
                File.Move(producedPdf, pdfPath);
            }

            return Result.Success(pdfPath);
        }
        catch (Exception ex)
        {
            _logger.Error("تبدیل به PDF با LibreOffice ناموفق بود.", ex);
            return Result.Failure<string>($"تبدیل به PDF با LibreOffice ممکن نشد: {ex.Message}");
        }
    }

    public async Task<Result> PrintAsync(string documentPath, string? printerName, int copies, CancellationToken ct = default)
    {
        var soffice = RendererDetection.FindLibreOffice();
        if (soffice is null)
            return Result.Failure("LibreOffice روی این سیستم یافت نشد.");

        if (!File.Exists(documentPath))
            return Result.Failure($"فایل یافت نشد:\n{documentPath}");

        try
        {
            // soffice --headless -p (چاپگر پیش‌فرض) یا --pt "نام چاپگر"
            var printArg = string.IsNullOrWhiteSpace(printerName)
                ? $"-p \"{documentPath}\""
                : $"--pt \"{printerName}\" \"{documentPath}\"";

            for (var i = 0; i < Math.Max(1, copies); i++)
            {
                var exit = await RunProcessAsync(soffice, $"--headless --norestore {printArg}", ct);
                if (exit != 0)
                    return Result.Failure($"چاپ با LibreOffice با کد خطای {exit} خارج شد.");
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("چاپ با LibreOffice ناموفق بود.", ex);
            return Result.Failure($"چاپ با LibreOffice ممکن نشد: {ex.Message}");
        }
    }

    /// <summary>اجرای soffice و انتظار برای پایان، با محدودیت زمانی برای جلوگیری از قفل شدن.</summary>
    private static async Task<int> RunProcessAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        // حداکثر ۶۰ ثانیه صبر می‌کنیم؛ اگر بیشتر شد، پروسه را می‌بندیم.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("LibreOffice در زمان معقول پاسخ نداد.");
        }
    }
}
