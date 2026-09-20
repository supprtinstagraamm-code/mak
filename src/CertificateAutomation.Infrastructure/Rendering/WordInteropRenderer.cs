using System.Runtime.Versioning;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Enums;

namespace CertificateAutomation.Infrastructure.Rendering;

/// <summary>
/// تبدیل و چاپ با Microsoft Word از طریق COM (late-binding با Reflection تا نیازی به
/// ارجاع Interop در زمان کامپایل نباشد؛ این باعث می‌شود برنامه روی سیستم بدون Word هم
/// کامپایل و اجرا شود و فقط این Renderer در دسترس نباشد).
///
/// بالاترین کیفیت خروجی را دارد، اما نیازمند نصب Word است. اگر Word نصب نباشد،
/// IsAvailable برابر false است و CompositeRenderer سراغ گزینهٔ بعدی می‌رود.
/// </summary>
[SupportedOSPlatform("windows")]
public class WordInteropRenderer : IDocumentRenderer
{
    private readonly IAppLogger _logger;

    public WordInteropRenderer(IAppLogger logger) => _logger = logger;

    public RendererKind Kind => RendererKind.WordInterop;
    public bool IsAvailable => RendererDetection.IsWordAvailable();

    // ثابت‌های Word برای فرمت خروجی و رفتار
    private const int WdFormatPDF = 17;
    private const int WdDoNotSaveChanges = 0;

    public Task<Result<string>> ConvertToPdfAsync(string docxPath, string pdfPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(docxPath))
                return Result.Failure<string>($"فایل ورودی یافت نشد:\n{docxPath}");

            object? word = null;
            object? doc = null;

            try
            {
                var wordType = Type.GetTypeFromProgID("Word.Application");
                if (wordType is null)
                    return Result.Failure<string>("Microsoft Word روی این سیستم یافت نشد.");

                word = Activator.CreateInstance(wordType);
                SetProperty(word!, "Visible", false);
                SetProperty(word!, "DisplayAlerts", 0);

                var documents = GetProperty(word!, "Documents");
                doc = Invoke(documents!, "Open", docxPath);

                // ExportAsFixedFormat یا SaveAs2 با فرمت PDF
                Invoke(doc!, "SaveAs2", pdfPath, WdFormatPDF);

                Invoke(doc!, "Close", WdDoNotSaveChanges);
                doc = null;
                Invoke(word!, "Quit");
                word = null;

                return File.Exists(pdfPath)
                    ? Result.Success(pdfPath)
                    : Result.Failure<string>("Word فایل PDF را تولید نکرد.");
            }
            catch (Exception ex)
            {
                _logger.Error("تبدیل به PDF با Word ناموفق بود.", ex);
                return Result.Failure<string>($"تبدیل به PDF با Word ممکن نشد: {ex.Message}");
            }
            finally
            {
                // آزادسازی حتمی پروسهٔ Word تا در حافظه نماند.
                SafeQuit(doc, word);
            }
        }, ct);
    }

    public Task<Result> PrintAsync(string documentPath, string? printerName, int copies, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(documentPath))
                return Result.Failure($"فایل یافت نشد:\n{documentPath}");

            object? word = null;
            object? doc = null;

            try
            {
                var wordType = Type.GetTypeFromProgID("Word.Application");
                if (wordType is null)
                    return Result.Failure("Microsoft Word روی این سیستم یافت نشد.");

                word = Activator.CreateInstance(wordType);
                SetProperty(word!, "Visible", false);
                SetProperty(word!, "DisplayAlerts", 0);

                // تنظیم چاپگر فعال در صورت مشخص بودن
                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    try { SetProperty(word!, "ActivePrinter", printerName); }
                    catch { _logger.Warn($"تنظیم چاپگر «{printerName}» ممکن نشد؛ چاپگر پیش‌فرض استفاده می‌شود."); }
                }

                var documents = GetProperty(word!, "Documents");
                doc = Invoke(documents!, "Open", documentPath);

                // چاپ به تعداد نسخهٔ خواسته‌شده
                for (var i = 0; i < Math.Max(1, copies); i++)
                    Invoke(doc!, "PrintOut");

                Invoke(doc!, "Close", WdDoNotSaveChanges);
                doc = null;
                Invoke(word!, "Quit");
                word = null;

                return Result.Success();
            }
            catch (Exception ex)
            {
                _logger.Error("چاپ با Word ناموفق بود.", ex);
                return Result.Failure($"چاپ با Word ممکن نشد: {ex.Message}");
            }
            finally
            {
                SafeQuit(doc, word);
            }
        }, ct);
    }

    // ---- کمک‌کننده‌های Reflection برای فراخوانی COM ----

    private static object? GetProperty(object obj, string name)
        => obj.GetType().InvokeMember(name,
            System.Reflection.BindingFlags.GetProperty, null, obj, null);

    private static void SetProperty(object obj, string name, object value)
        => obj.GetType().InvokeMember(name,
            System.Reflection.BindingFlags.SetProperty, null, obj, new[] { value });

    private static object? Invoke(object obj, string name, params object[] args)
        => obj.GetType().InvokeMember(name,
            System.Reflection.BindingFlags.InvokeMethod, null, obj, args);

    /// <summary>آزادسازی تضمین‌شدهٔ اشیای COM حتی در صورت خطا.</summary>
    private void SafeQuit(object? doc, object? word)
    {
        try
        {
            if (doc is not null)
            {
                Invoke(doc, "Close", WdDoNotSaveChanges);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(doc);
            }
        }
        catch { /* ignore */ }

        try
        {
            if (word is not null)
            {
                Invoke(word, "Quit");
                System.Runtime.InteropServices.Marshal.ReleaseComObject(word);
            }
        }
        catch { /* ignore */ }
    }
}
