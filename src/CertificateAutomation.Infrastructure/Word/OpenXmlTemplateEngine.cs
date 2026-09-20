using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CertificateAutomation.Infrastructure.Word;

/// <summary>
/// موتور جایگزینی Placeholder در فایل Word با DocumentFormat.OpenXml.
///
/// چالش اصلی: Word یک عبارت مثل «{{کد_ملی}}» را اغلب بین چند Run جدا تقسیم می‌کند
/// (به‌دلیل غلط‌گیر املایی، تغییر قالب‌بندی، یا صرفاً نحوه تایپ). بنابراین جایگزینی ساده
/// متنِ هر Run شکست می‌خورد.
///
/// راه‌حل «ادغام Run»: در هر پاراگراف، متن تمام Runها به‌هم چسبانده می‌شود، جایگزینی روی
/// رشته کامل انجام می‌گیرد، سپس نتیجه در اولین Run نوشته و متن بقیه Runها خالی می‌شود.
/// قالب‌بندی اولین Run (فونت، اندازه، رنگ) حفظ می‌ماند.
///
/// پوشش کامل: بدنه سند + سربرگ (Header) + پاصفحه (Footer)، که شامل جدول‌ها و کادرهای متن هم می‌شود
/// چون OpenXML همه این‌ها را به‌صورت درختی از Paragraphها نگه می‌دارد.
/// </summary>
public class OpenXmlTemplateEngine : ITemplateEngine
{
    private readonly IAppLogger _logger;

    public OpenXmlTemplateEngine(IAppLogger logger) => _logger = logger;

    public Result<string> Fill(
        string templatePath,
        string outputPath,
        IReadOnlyDictionary<string, string> values)
    {
        if (!File.Exists(templatePath))
            return Result.Failure<string>($"فایل قالب یافت نشد:\n{templatePath}");

        try
        {
            // قالب اصلی دست‌نخورده می‌ماند؛ روی یک کپی کار می‌کنیم.
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(templatePath, outputPath, overwrite: true);

            using var doc = WordprocessingDocument.Open(outputPath, isEditable: true);
            var main = doc.MainDocumentPart
                ?? throw new InvalidOperationException("سند Word بخش اصلی ندارد (فایل ممکن است خراب باشد).");

            // ۱) بدنه سند
            if (main.Document?.Body is not null)
                ReplaceInElement(main.Document.Body, values);

            // ۲) همه سربرگ‌ها (اول/زوج/فرد)
            foreach (var header in main.HeaderParts)
                if (header.Header is not null)
                    ReplaceInElement(header.Header, values);

            // ۳) همه پاصفحه‌ها
            foreach (var footer in main.FooterParts)
                if (footer.Footer is not null)
                    ReplaceInElement(footer.Footer, values);

            main.Document?.Save();
            return Result.Success(outputPath);
        }
        catch (Exception ex)
        {
            _logger.Error($"جایگزینی Placeholder در قالب ناموفق بود: {templatePath}", ex);
            return Result.Failure<string>(
                $"ساخت سند از قالب ممکن نشد. اگر فایل خروجی در برنامه دیگری باز است آن را ببندید.\n\nشرح فنی: {ex.Message}");
        }
    }

    /// <summary>اعمال جایگزینی روی تمام پاراگراف‌های زیرمجموعه یک عنصر (بدنه، سربرگ یا پاصفحه).</summary>
    private static void ReplaceInElement(OpenXmlElement root, IReadOnlyDictionary<string, string> values)
    {
        foreach (var paragraph in root.Descendants<Paragraph>())
            ReplaceInParagraph(paragraph, values);
    }

    /// <summary>
    /// هستهٔ الگوریتم ادغام Run. فقط روی Runهای مستقیم پاراگراف کار می‌کند (نه Runهای تودرتو
    /// در پاراگراف‌های داخلی) تا ساختار جدول‌ها و کادرها حفظ شود؛ آن پاراگراف‌ها خودشان جداگانه
    /// در حلقهٔ Descendants پردازش می‌شوند.
    /// </summary>
    private static void ReplaceInParagraph(Paragraph paragraph, IReadOnlyDictionary<string, string> values)
    {
        // فقط Runهایی که دقیقاً فرزند همین پاراگراف‌اند و یک عنصر Text دارند.
        var runs = paragraph.Elements<Run>()
            .Where(r => r.Elements<Text>().Any())
            .ToList();

        if (runs.Count == 0) return;

        // متن کامل پاراگراف از کنار هم گذاشتن متن Runها
        var fullText = string.Concat(runs.Select(GetRunText));

        // اگر هیچ Placeholderی نیست، دست نمی‌زنیم (کارایی + حفظ کامل قالب‌بندی)
        if (!fullText.Contains("{{")) return;

        var replaced = PlaceholderScanner.Replace(fullText, values);
        if (replaced == fullText) return;

        // نتیجه را کامل در اولین Run می‌نویسیم و بقیه را خالی می‌کنیم.
        SetRunText(runs[0], replaced);
        for (var i = 1; i < runs.Count; i++)
            SetRunText(runs[i], string.Empty);
    }

    /// <summary>متن یک Run از کنار هم گذاشتن همه عناصر Text آن.</summary>
    private static string GetRunText(Run run)
        => string.Concat(run.Elements<Text>().Select(t => t.Text));

    /// <summary>
    /// نوشتن متن در یک Run: اولین Text را نگه می‌دارد، بقیه Textها را حذف می‌کند و
    /// فضای سفید ابتدا/انتها را با xml:space="preserve" حفظ می‌کند تا فاصله‌ها از بین نروند.
    /// </summary>
    private static void SetRunText(Run run, string text)
    {
        var texts = run.Elements<Text>().ToList();

        // Textهای اضافی را حذف کن تا فقط یکی بماند.
        for (var i = 1; i < texts.Count; i++)
            texts[i].Remove();

        var target = texts[0];
        target.Text = text;
        target.Space = SpaceProcessingModeValues.Preserve;
    }
}
