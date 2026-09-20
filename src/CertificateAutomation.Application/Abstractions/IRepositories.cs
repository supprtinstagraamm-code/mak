using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Entities;

namespace CertificateAutomation.Application.Abstractions;

/// <summary>دسترسی به اطلاعات کارکنان.</summary>
public interface IEmployeeRepository
{
    Task<IReadOnlyList<Employee>> GetAllAsync(bool onlyActive = true, CancellationToken ct = default);
    Task<Employee?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>جستجوی زنده روی نام، نام خانوادگی، کد ملی، شماره عضویت و کد پرسنلی.</summary>
    Task<IReadOnlyList<Employee>> SearchAsync(string term, int limit = 50, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>درج یا به‌روزرسانی دسته‌ای هنگام بارگذاری Excel (بر اساس کد ملی/کد پرسنلی).</summary>
    Task<ImportResult> UpsertBatchAsync(IEnumerable<Employee> employees, CancellationToken ct = default);

    /// <summary>
    /// غیرفعال کردن افرادی که در آخرین بارگذاری وجود نداشتند (بر اساس زمان بارگذاری).
    /// برای وقتی که کاربر می‌خواهد فهرست دقیقاً برابر فایل Excel جدید باشد.
    /// خروجی: تعداد افراد غیرفعال‌شده.
    /// </summary>
    Task<int> DeactivateNotImportedSinceAsync(DateTime importStartedUtc, CancellationToken ct = default);
}

/// <summary>دسترسی به قالب‌ها و Placeholderهای آن‌ها.</summary>
public interface ITemplateRepository
{
    Task<IReadOnlyList<LetterTemplate>> GetAllAsync(bool onlyActive = true, CancellationToken ct = default);
    Task<LetterTemplate?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<long> UpsertAsync(LetterTemplate template, CancellationToken ct = default);
    Task<IReadOnlyList<TemplatePlaceholder>> GetPlaceholdersAsync(long templateId, CancellationToken ct = default);
    Task ReplacePlaceholdersAsync(long templateId, IEnumerable<TemplatePlaceholder> placeholders, CancellationToken ct = default);

    /// <summary>
    /// حذف قالب‌هایی که فایلشان دیگر در پوشه نیست. فهرست نام فایل‌های موجود داده می‌شود
    /// و هر قالبی که در این فهرست نباشد از پایگاه داده حذف می‌گردد.
    /// خروجی: تعداد قالب‌های حذف‌شده.
    /// </summary>
    Task<int> RemoveMissingAsync(IEnumerable<string> existingFileNames, CancellationToken ct = default);

    /// <summary>تغییر دستهٔ یک قالب (توسط کاربر در صفحهٔ قالب‌ها).</summary>
    Task SetCategoryAsync(long templateId, string category, CancellationToken ct = default);

    /// <summary>فهرست دسته‌های موجود (برای منوی انتخاب دسته).</summary>
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct = default);
}

/// <summary>دسترسی به سوابق نامه‌ها.</summary>
public interface ILetterRepository
{
    Task<Letter?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<Letter>> SearchAsync(LetterSearchQuery query, CancellationToken ct = default);
    Task<int> CountAsync(LetterSearchQuery query, CancellationToken ct = default);
    Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default);
    Task RegisterPrintAsync(PrintJob job, CancellationToken ct = default);

    /// <summary>حذف نرم؛ رکورد برای ممیزی باقی می‌ماند.</summary>
    Task SoftDeleteAsync(long letterId, string deletedBy, CancellationToken ct = default);
}

/// <summary>دسترسی به کاربران.</summary>
public interface IUserRepository
{
    Task<AppUser?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task UpdatePasswordAsync(long userId, string hash, string salt, CancellationToken ct = default);
}

/// <summary>ثبت وقایع ممیزی.</summary>
public interface IAuditRepository
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int count = 100, CancellationToken ct = default);
}

/// <summary>نگاشت ستون‌های Excel به کلیدهای Placeholder.</summary>
public interface IColumnMappingRepository
{
    Task<IReadOnlyList<ColumnMapping>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(ColumnMapping mapping, CancellationToken ct = default);
}

/// <summary>دسترسی به یادآورهای ماهانه و وضعیت انجام‌شدن آن‌ها در هر ماه.</summary>
public interface IReminderRepository
{
    Task<IReadOnlyList<Reminder>> GetAllAsync(bool onlyActive = false, CancellationToken ct = default);
    Task<Reminder?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<long> AddAsync(Reminder reminder, CancellationToken ct = default);
    Task UpdateAsync(Reminder reminder, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>علامت زدن یک یادآور به‌عنوان «انجام‌شده» برای یک ماه مشخص (yyyy-MM).</summary>
    Task MarkCompletedAsync(long reminderId, string yearMonth, string byUser, CancellationToken ct = default);

    /// <summary>برداشتن علامت انجام‌شده (اگر کاربر اشتباه تیک زده باشد).</summary>
    Task UnmarkCompletedAsync(long reminderId, string yearMonth, CancellationToken ct = default);

    /// <summary>آیا این یادآور برای این ماه انجام‌شده علامت خورده است.</summary>
    Task<bool> IsCompletedAsync(long reminderId, string yearMonth, CancellationToken ct = default);

    /// <summary>آیا برای این ماه قبلاً یک بار به کاربر اعلان نمایش داده شده (برای جلوگیری از تکرار).</summary>
    Task<bool> WasNotifiedAsync(long reminderId, string yearMonth, CancellationToken ct = default);

    /// <summary>ثبت اینکه اعلان این ماه نمایش داده شد.</summary>
    Task MarkNotifiedAsync(long reminderId, string yearMonth, CancellationToken ct = default);
}
