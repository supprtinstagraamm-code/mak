using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Entities;
using CertificateAutomation.Domain.Enums;

namespace CertificateAutomation.Application.Abstractions;

/// <summary>بارگذاری اطلاعات کارکنان از فایل Excel.</summary>
public interface IEmployeeImportService
{
    Task<Result<ImportResult>> ImportAsync(string excelPath, CancellationToken ct = default);
}

/// <summary>اسکن قالب‌های Word و شناسایی Placeholderها.</summary>
public interface ITemplateScanner
{
    /// <summary>استخراج کلیدهای {{...}} از یک فایل Word.</summary>
    Result<IReadOnlyList<string>> ScanPlaceholders(string docxPath);

    /// <summary>همگام‌سازی پوشه قالب‌ها با پایگاه داده.</summary>
    Task<Result<int>> SyncTemplatesFolderAsync(string folder, CancellationToken ct = default);
}

/// <summary>موتور جایگزینی Placeholder در فایل Word.</summary>
public interface ITemplateEngine
{
    /// <summary>
    /// یک کپی از قالب می‌سازد و تمام Placeholderها را با مقادیر داده‌شده جایگزین می‌کند.
    /// کلیدها بدون آکولاد ارسال می‌شوند.
    /// </summary>
    Result<string> Fill(string templatePath, string outputPath, IReadOnlyDictionary<string, string> values);
}

/// <summary>تولید شماره نامه به‌صورت اتمیک و بدون تکرار.</summary>
public interface INumberGenerator
{
    /// <summary>شماره بعدی را بدون مصرف کردن آن، فقط برای نمایش، برمی‌گرداند.</summary>
    Task<string> PeekNextAsync(CancellationToken ct = default);

    /// <summary>آخرین سریال ثبت‌شده سال جاری.</summary>
    Task<int> GetLastSerialAsync(int jalaliYear, CancellationToken ct = default);

    /// <summary>تغییر دستی شمارنده. فقط برای مدیر.</summary>
    Task<Result> SetLastSerialAsync(int jalaliYear, int lastSerial, string byUser, CancellationToken ct = default);

    /// <summary>ساخت رشتهٔ شمارهٔ نامه از سال و سریال، با همان قالب تنظیمات (برای نمایش پیش‌نمایش).</summary>
    string FormatNumber(int jalaliYear, int serial);
}

/// <summary>سرویس اصلی صدور نامه: شماره‌گذاری، تولید سند، ثبت سابقه و چاپ.</summary>
public interface ILetterIssueService
{
    /// <summary>ساخت فهرست فیلدهای یک قالب برای یک شخص، جهت نمایش پیش‌نمایش.</summary>
    Task<Result<IReadOnlyList<PlaceholderField>>> BuildFieldsAsync(long employeeId, long templateId, CancellationToken ct = default);

    /// <summary>صدور نهایی نامه. شماره فقط در همین مرحله مصرف می‌شود.</summary>
    Task<Result<IssuedLetterResult>> IssueAsync(IssueLetterRequest request, CancellationToken ct = default);

    /// <summary>چاپ مجدد یک نامه با همان شماره و همان مقادیر قبلی.</summary>
    Task<Result<IssuedLetterResult>> ReprintAsync(long letterId, string? printerName, int copies, CancellationToken ct = default);

    /// <summary>چاپ یک سند تولیدشده و ثبت سابقهٔ چاپ. شمارندهٔ چاپ نامه افزایش می‌یابد.</summary>
    Task<Result> PrintDocumentAsync(long letterId, string documentPath, string? printerName, int copies, CancellationToken ct = default);

    /// <summary>ساخت خروجی PDF از یک نامه و ذخیرهٔ مسیر آن.</summary>
    Task<Result<string>> ExportPdfAsync(long letterId, string docxPath, CancellationToken ct = default);
}

/// <summary>تبدیل سند Word به PDF و چاپ آن.</summary>
public interface IDocumentRenderer
{
    /// <summary>موتور موجود روی این سیستم.</summary>
    RendererKind Kind { get; }

    /// <summary>آیا این موتور روی سیستم فعلی قابل استفاده است.</summary>
    bool IsAvailable { get; }

    Task<Result<string>> ConvertToPdfAsync(string docxPath, string pdfPath, CancellationToken ct = default);
    Task<Result> PrintAsync(string documentPath, string? printerName, int copies, CancellationToken ct = default);
}

/// <summary>دسترسی به چاپگرهای ویندوز.</summary>
public interface IPrintService
{
    IReadOnlyList<string> GetPrinters();
    string? GetSystemDefaultPrinter();
    Task<Result> PrintAsync(string documentPath, string? printerName, int copies, CancellationToken ct = default);
}

/// <summary>تهیه و بازیابی نسخه پشتیبان.</summary>
public interface IBackupService
{
    Task<Result<string>> CreateBackupAsync(string? targetFolder = null, CancellationToken ct = default);
    Task<Result> RestoreBackupAsync(string zipPath, CancellationToken ct = default);
    IReadOnlyList<string> ListBackups(string folder);
}

/// <summary>هش و بررسی رمز عبور.</summary>
public interface IPasswordHasher
{
    (string Hash, string Salt) Hash(string password);
    bool Verify(string password, string hash, string salt);
}

/// <summary>وضعیت ورود مدیر در جلسه جاری.</summary>
public interface IUserSession
{
    AppUser? CurrentUser { get; }
    bool IsAdmin { get; }
    string UserName { get; }

    Task<Result> LoginAsync(string username, string password, CancellationToken ct = default);
    void Logout();

    /// <summary>تمدید مهلت قفل خودکار پس از بی‌فعالیتی.</summary>
    void Touch();

    event EventHandler? SessionChanged;
}

/// <summary>
/// سرویس آمار و تحلیل داده‌های پرسنل.
/// همهٔ ستون‌های موجود (ثابت + ستون‌های Excel) را برای تحلیل در دسترس قرار می‌دهد.
/// </summary>
public interface IStatisticsService
{
    /// <summary>فهرست تمام فیلدهای قابل تحلیل، به‌همراه نام نمایشی.</summary>
    Task<IReadOnlyList<(string Key, string DisplayName)>> GetAvailableFieldsAsync(CancellationToken ct = default);

    /// <summary>بارگذاری همهٔ ردیف‌های تحلیلی (یک ردیف به ازای هر شخص فعال).</summary>
    Task<IReadOnlyList<Dtos.AnalyticsRow>> GetRowsAsync(CancellationToken ct = default);

    /// <summary>
    /// تحلیل یک یا دو فیلد. اگر فقط fieldX داده شود: توزیع فراوانی و در صورت عددی بودن،
    /// خلاصهٔ آماری. اگر fieldY هم داده شود: مقایسه/همبستگی دو متغیر.
    /// با filterField/filterValue می‌توان تحلیل را به یک زیرمجموعه محدود کرد (مثلاً یک پروژه).
    /// </summary>
    Task<Dtos.AnalysisResult> AnalyzeAsync(
        string fieldX, string? fieldY = null, int histogramBins = 8,
        string? filterField = null, string? filterValue = null, CancellationToken ct = default);

    /// <summary>
    /// چند آمار کلیدی آماده برای نمایش سریع در بالای صفحه.
    /// در صورت تعیین فیلتر، آمارها فقط برای همان زیرمجموعه محاسبه می‌شوند.
    /// </summary>
    Task<IReadOnlyList<(string Label, string Value)>> GetHighlightsAsync(
        string? filterField = null, string? filterValue = null, CancellationToken ct = default);

    /// <summary>مقادیر متمایز یک فیلد، برای ساخت فهرست فیلتر (مثلاً فهرست پروژه‌ها).</summary>
    Task<IReadOnlyList<string>> GetDistinctValuesAsync(string field, CancellationToken ct = default);
}

/// <summary>
/// مدیریت یادآورهای ماهانهٔ تکرارشونده: ساخت یک‌بار، تکرار خودکار هر ماه،
/// و تشخیص اینکه امروز باید کدام یادآورها اعلان شوند.
/// </summary>
public interface IReminderService
{
    /// <summary>فهرست همهٔ یادآورها به‌همراه وضعیت انجام‌شدن در ماه جاری.</summary>
    Task<IReadOnlyList<Domain.Entities.Reminder>> GetChecklistAsync(CancellationToken ct = default);

    Task<long> AddAsync(string title, string? description, int dayOfMonth, CancellationToken ct = default);
    Task UpdateAsync(long id, string title, string? description, int dayOfMonth, bool isActive, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>علامت زدن یادآور به‌عنوان انجام‌شده برای ماه جاری.</summary>
    Task MarkDoneAsync(long id, string byUser, CancellationToken ct = default);

    /// <summary>برداشتن علامت انجام‌شده برای ماه جاری.</summary>
    Task UnmarkDoneAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// یادآورهایی که «امروز» باید اعلان شوند: روزشان با امروز تطبیق دارد، هنوز برای این ماه
    /// انجام‌شده علامت نخورده‌اند، و قبلاً در همین ماه اعلانشان نمایش داده نشده است.
    /// فراخوانی این متد، یادآورهای برگردانده‌شده را «اعلان‌شده» علامت می‌زند.
    /// </summary>
    Task<IReadOnlyList<Domain.Entities.Reminder>> GetDueForNotificationAsync(CancellationToken ct = default);
}

/// <summary>
/// ساخت خروجی گزارش (Excel یا PDF) از یک تحلیل آماری، با چیدمان کاملاً راست‌چین
/// و در صورت وجود، تصویر نمودار جاسازی‌شده در همان فایل.
/// </summary>
public interface IReportExportService
{
    Task<Result<string>> ExportExcelAsync(Dtos.ReportExportRequest request, CancellationToken ct = default);
    Task<Result<string>> ExportPdfAsync(Dtos.ReportExportRequest request, CancellationToken ct = default);
}
