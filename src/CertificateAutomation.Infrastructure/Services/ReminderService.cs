using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Entities;

namespace CertificateAutomation.Infrastructure.Services;

/// <summary>
/// پیاده‌سازی یادآورهای ماهانهٔ تکرارشونده.
///
/// نکتهٔ اصلی: یادآور فقط «یک بار» ساخته می‌شود (مثلاً «روز ۲۰ هر ماه: فرم سپهر»).
/// هیچ رکورد جدیدی برای ماه‌های بعد لازم نیست — تشخیص «امروز نوبت کدام یادآورهاست»
/// و «آیا این ماه انجام شده یا نه» در زمان اجرا و بر اساس تاریخ امروز محاسبه می‌شود.
/// جدول ReminderCompletions فقط برای هر ماه یک ردیف دارد (کلید یکتا: شناسهٔ یادآور + ماه)،
/// پس با شروع ماه جدید، خودکار «انجام‌نشده» دیده می‌شود.
/// </summary>
public class ReminderService : IReminderService
{
    private readonly IReminderRepository _repo;
    private readonly IAppLogger _logger;

    public ReminderService(IReminderRepository repo, IAppLogger logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Reminder>> GetChecklistAsync(CancellationToken ct = default)
    {
        var all = await _repo.GetAllAsync(onlyActive: false, ct);
        var ym = CurrentYearMonth();

        var list = new List<Reminder>();
        foreach (var r in all)
        {
            r.IsCompletedThisMonth = await _repo.IsCompletedAsync(r.Id, ym, ct);
            list.Add(r);
        }

        // ترتیب نمایش: ابتدا یادآورهای فعال و انجام‌نشده (نیاز به توجه دارند)، سپس بقیه.
        return list
            .OrderBy(r => !r.IsActive)
            .ThenBy(r => r.IsCompletedThisMonth)
            .ThenBy(r => r.DayOfMonth)
            .ToList();
    }

    public async Task<long> AddAsync(string title, string? description, int dayOfMonth, CancellationToken ct = default)
    {
        var day = Math.Clamp(dayOfMonth, 1, 31);
        var id = await _repo.AddAsync(new Reminder
        {
            Title = title.Trim(),
            Description = description?.Trim(),
            DayOfMonth = day,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        }, ct);

        _logger.Info($"یادآور جدید «{title}» برای روز {day} هر ماه ساخته شد.");
        return id;
    }

    public async Task UpdateAsync(long id, string title, string? description, int dayOfMonth, bool isActive, CancellationToken ct = default)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing is null) return;

        existing.Title = title.Trim();
        existing.Description = description?.Trim();
        existing.DayOfMonth = Math.Clamp(dayOfMonth, 1, 31);
        existing.IsActive = isActive;

        await _repo.UpdateAsync(existing, ct);
    }

    public Task DeleteAsync(long id, CancellationToken ct = default) => _repo.DeleteAsync(id, ct);

    public Task MarkDoneAsync(long id, string byUser, CancellationToken ct = default)
        => _repo.MarkCompletedAsync(id, CurrentYearMonth(), byUser, ct);

    public Task UnmarkDoneAsync(long id, CancellationToken ct = default)
        => _repo.UnmarkCompletedAsync(id, CurrentYearMonth(), ct);

    public async Task<IReadOnlyList<Reminder>> GetDueForNotificationAsync(CancellationToken ct = default)
    {
        var today = DateTime.Now;
        var ym = CurrentYearMonth();
        var effectiveDay = today.Day; // برای تطبیق نهایی، هر یادآور جداگانه کلمپ می‌شود.

        var all = await _repo.GetAllAsync(onlyActive: true, ct);
        var due = new List<Reminder>();

        foreach (var r in all)
        {
            var thisMonthDay = ClampToMonth(r.DayOfMonth, today.Year, today.Month);
            if (thisMonthDay != today.Day) continue;

            if (await _repo.IsCompletedAsync(r.Id, ym, ct)) continue;
            if (await _repo.WasNotifiedAsync(r.Id, ym, ct)) continue;

            await _repo.MarkNotifiedAsync(r.Id, ym, ct);
            due.Add(r);
        }

        return due;
    }

    /// <summary>
    /// اگر روز تعیین‌شده از تعداد روزهای ماه بیشتر باشد (مثلاً ۳۱ در ماهی ۳۰روزه)،
    /// به آخرین روز همان ماه محدود می‌شود؛ یعنی یادآور «آخر ماه» هرگز جا نمی‌ماند.
    /// </summary>
    private static int ClampToMonth(int dayOfMonth, int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        return Math.Min(dayOfMonth, daysInMonth);
    }

    private static string CurrentYearMonth() => DateTime.Now.ToString("yyyy-MM");
}
