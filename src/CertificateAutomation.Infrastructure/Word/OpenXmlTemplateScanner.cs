using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Entities;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CertificateAutomation.Infrastructure.Word;

/// <summary>
/// اسکن فایل‌های Word و شناسایی Placeholderهای آن‌ها، و همگام‌سازی پوشه قالب‌ها با پایگاه داده.
/// </summary>
public class OpenXmlTemplateScanner : ITemplateScanner
{
    private readonly ITemplateRepository _templates;
    private readonly IAppLogger _logger;

    public OpenXmlTemplateScanner(ITemplateRepository templates, IAppLogger logger)
    {
        _templates = templates;
        _logger = logger;
    }

    /// <summary>
    /// تشخیص خودکار دستهٔ قالب از روی نام آن. اگر نام شامل «حکم» یا «ماموریت» باشد،
    /// در دستهٔ «ماموریت» قرار می‌گیرد؛ در غیر این صورت «گواهی». این باعث می‌شود در برنامه
    /// تب‌های گواهی و ماموریت از هم جدا نمایش داده شوند.
    /// </summary>
    public static string DetectCategory(string title)
    {
        var t = title.Replace('ي', 'ی').Replace('ك', 'ک');
        if (t.Contains("حکم") || t.Contains("ماموریت") || t.Contains("مأموریت"))
            return "ماموریت";
        if (t.Contains("معرفی") || t.Contains("معرفينامه"))
            return "معرفی‌نامه";
        return "گواهی";
    }

    /// <summary>استخراج تمام کلیدهای {{...}} از یک فایل Word (بدنه + سربرگ + پاصفحه).</summary>
    public Result<IReadOnlyList<string>> ScanPlaceholders(string docxPath)
    {
        if (!File.Exists(docxPath))
            return Result.Failure<IReadOnlyList<string>>($"فایل قالب یافت نشد:\n{docxPath}");

        try
        {
            using var doc = WordprocessingDocument.Open(docxPath, isEditable: false);
            var main = doc.MainDocumentPart;
            if (main is null)
                return Result.Failure<IReadOnlyList<string>>("فایل Word معتبر نیست.");

            var allText = new System.Text.StringBuilder();

            // متن کامل هر پاراگراف را به‌هم می‌چسبانیم تا Placeholderهای تقسیم‌شده بین Runها هم دیده شوند.
            if (main.Document?.Body is not null)
                foreach (var p in main.Document.Body.Descendants<Paragraph>())
                    allText.Append(string.Concat(p.Descendants<Text>().Select(t => t.Text))).Append('\n');

            foreach (var header in main.HeaderParts)
                if (header.Header is not null)
                    foreach (var p in header.Header.Descendants<Paragraph>())
                        allText.Append(string.Concat(p.Descendants<Text>().Select(t => t.Text))).Append('\n');

            foreach (var footer in main.FooterParts)
                if (footer.Footer is not null)
                    foreach (var p in footer.Footer.Descendants<Paragraph>())
                        allText.Append(string.Concat(p.Descendants<Text>().Select(t => t.Text))).Append('\n');

            var keys = PlaceholderScanner.Extract(allText.ToString());
            return Result.Success(keys);
        }
        catch (Exception ex)
        {
            _logger.Error($"اسکن قالب ناموفق بود: {docxPath}", ex);
            return Result.Failure<IReadOnlyList<string>>($"خواندن قالب ممکن نشد.\n\nشرح فنی: {ex.Message}");
        }
    }

    /// <summary>
    /// همگام‌سازی: هر فایل .docx در پوشه که با ~$ شروع نشود، به‌عنوان قالب ثبت/به‌روزرسانی می‌شود
    /// و Placeholderهایش استخراج و ذخیره می‌گردد. عنوان قالب از نام فایل گرفته می‌شود.
    /// </summary>
    public async Task<Result<int>> SyncTemplatesFolderAsync(string folder, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return Result.Failure<int>($"پوشه قالب‌ها یافت نشد:\n{folder}");

        try
        {
            var files = Directory.GetFiles(folder, "*.docx", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).StartsWith("~$")) // فایل‌های موقت Word
                .ToList();

            var count = 0;

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var title = Path.GetFileNameWithoutExtension(file);

                var scan = ScanPlaceholders(file);
                if (scan.IsFailure)
                {
                    _logger.Warn($"قالب «{fileName}» نادیده گرفته شد: {scan.Error}");
                    continue;
                }

                var templateId = await _templates.UpsertAsync(new LetterTemplate
                {
                    Title = title,
                    FileName = fileName,
                    Category = DetectCategory(title),
                    DefaultCopies = 1,
                    IsActive = true,
                    LastScannedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                }, ct);

                var placeholders = scan.Value.Select(key => new TemplatePlaceholder
                {
                    TemplateId = templateId,
                    PlaceholderKey = key,
                    IsRequired = true
                });

                await _templates.ReplacePlaceholdersAsync(templateId, placeholders, ct);
                count++;
            }

            // --- حذف قالب‌هایی که فایلشان دیگر در پوشه نیست ---
            // این تضمین می‌کند فهرست برنامه همیشه دقیقاً برابر محتوای واقعی پوشه باشد:
            // فایل جدید → اضافه، فایل حذف‌شده → حذف، فایل جایگزین‌شده → به‌روزرسانی.
            var presentFileNames = files.Select(Path.GetFileName).Where(n => n is not null).Cast<string>().ToList();
            var removed = await _templates.RemoveMissingAsync(presentFileNames, ct);

            if (removed > 0)
                _logger.Info($"{removed} قالب که فایلشان حذف شده بود، از فهرست پاک شد.");

            _logger.Info($"{count} قالب همگام‌سازی شد.");
            return Result.Success(count);
        }
        catch (Exception ex)
        {
            _logger.Error("همگام‌سازی پوشه قالب‌ها ناموفق بود.", ex);
            return Result.Failure<int>($"همگام‌سازی قالب‌ها ممکن نشد.\n\nشرح فنی: {ex.Message}");
        }
    }
}
