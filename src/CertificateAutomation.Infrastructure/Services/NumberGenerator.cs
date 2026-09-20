using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Infrastructure.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace CertificateAutomation.Infrastructure.Services;

/// <summary>
/// تولید شمارهٔ نامه به‌صورت اتمیک و بدون تکرار.
///
/// تضمین‌های اصلی:
/// ۱. شماره فقط هم‌زمان با درج رکورد نامه و در «همان تراکنش» مصرف می‌شود
///    (متد AllocateSerialInTransaction). بنابراین اگر کاربر پیش‌نمایش را ببیند و منصرف شود،
///    هیچ شماره‌ای هدر نمی‌رود و در توالی «سوراخ» ایجاد نمی‌شود.
/// ۲. با شروع سال جدید شمسی، ردیف جدید به‌صورت خودکار ساخته می‌شود.
/// ۳. قید UNIQUE در سطح پایگاه داده (JalaliYear, Serial) دومین لایهٔ تضمین است.
/// ۴. استفاده از BEGIN IMMEDIATE + WAL، ایمنی در برابر دسترسی هم‌زمان چند کاربر (حتی روی درایو شبکه).
/// </summary>
public class NumberGenerator : INumberGenerator, IAtomicSerialAllocator
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly ISettingsService _settings;
    private readonly IAuditRepository _audit;
    private readonly IAppLogger _logger;

    public NumberGenerator(
        ISqliteConnectionFactory factory,
        ISettingsService settings,
        IAuditRepository audit,
        IAppLogger logger)
    {
        _factory = factory;
        _settings = settings;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// شمارهٔ بعدی را «بدون مصرف کردن» فقط برای نمایش در پیش‌نمایش برمی‌گرداند.
    /// این عدد قطعی نیست؛ شمارهٔ نهایی در لحظهٔ صدور تعیین می‌شود.
    /// </summary>
    public async Task<string> PeekNextAsync(CancellationToken ct = default)
    {
        var year = PersianDate.CurrentYear;
        var last = await GetLastSerialAsync(year, ct);
        return Format(year, last + 1);
    }

    public Task<int> GetLastSerialAsync(int jalaliYear, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var last = db.ExecuteScalar<int?>(
            "SELECT LastSerial FROM NumberSequences WHERE JalaliYear = @jalaliYear;",
            new { jalaliYear });
        return Task.FromResult(last ?? 0);
    }

    /// <summary>
    /// هستهٔ اتمیک: داخل یک تراکنش موجود، سریال بعدی سال جاری را رزرو و شمارنده را
    /// یک واحد افزایش می‌دهد. این متد را سرویس صدور نامه در همان تراکنشی صدا می‌زند که
    /// رکورد Letter را هم درج می‌کند؛ بنابراین رزرو و ثبت تفکیک‌ناپذیرند.
    /// </summary>
    public (int Year, int Serial, string Number) AllocateSerialInTransaction(
        IDbConnection db, IDbTransaction tx, int jalaliYear)
        => AllocateSerialInTransaction(db, tx, jalaliYear, preferredSerial: null);

    /// <summary>
    /// نسخهٔ دارای شمارهٔ دلخواه. اگر preferredSerial داده شود و از شمارندهٔ فعلی بزرگ‌تر
    /// و در جدول نامه‌ها استفاده‌نشده باشد، شمارنده مستقیماً به آن می‌پرد (مثلاً کاربر با
    /// دکمهٔ + شماره را جلو برده). در غیر این صورت، همان رفتار عادی «یکی بیشتر» اجرا می‌شود.
    /// این کار تضمین بدون‌تکرار را نمی‌شکند چون همیشه شماره از شمارندهٔ مشترک و داخل تراکنش گرفته می‌شود.
    /// </summary>
    public (int Year, int Serial, string Number) AllocateSerialInTransaction(
        IDbConnection db, IDbTransaction tx, int jalaliYear, int? preferredSerial)
    {
        // اگر ردیف سال وجود ندارد (اولین نامهٔ سال جدید)، ساخته می‌شود.
        db.Execute(@"
INSERT OR IGNORE INTO NumberSequences (JalaliYear, LastSerial, Format, UpdatedAt)
VALUES (@year, 0, @format, @now);",
            new { year = jalaliYear, format = GetFormat(), now = DateTime.UtcNow.ToString("o") }, tx);

        var current = db.ExecuteScalar<int>(
            "SELECT LastSerial FROM NumberSequences WHERE JalaliYear = @year;",
            new { year = jalaliYear }, tx);

        int target;

        if (preferredSerial is int pref && pref > current)
        {
            // بررسی که این شماره واقعاً استفاده نشده باشد (لایهٔ اطمینان اضافی).
            var used = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Letters WHERE JalaliYear = @year AND Serial = @s AND IsDeleted = 0;",
                new { year = jalaliYear, s = pref }, tx);

            target = used == 0 ? pref : current + 1;
        }
        else
        {
            target = current + 1;
        }

        db.Execute(@"
UPDATE NumberSequences SET LastSerial = @target, UpdatedAt = @now
WHERE JalaliYear = @year;",
            new { year = jalaliYear, target, now = DateTime.UtcNow.ToString("o") }, tx);

        return (jalaliYear, target, Format(jalaliYear, target));
    }

    /// <summary>تغییر دستی شمارنده. فقط برای مدیر و با ثبت اجباری در گزارش ممیزی.</summary>
    public async Task<Result> SetLastSerialAsync(int jalaliYear, int lastSerial, string byUser, CancellationToken ct = default)
    {
        if (lastSerial < 0)
            return Result.Failure("شمارهٔ سریال نمی‌تواند منفی باشد.");

        try
        {
            using var db = _factory.Create();

            // شمارنده نباید کمتر از بیشترین سریال واقعاً استفاده‌شده تنظیم شود،
            // وگرنه در صدور بعدی شمارهٔ تکراری تولید می‌شود.
            var maxUsed = db.ExecuteScalar<int?>(
                "SELECT MAX(Serial) FROM Letters WHERE JalaliYear = @jalaliYear AND IsDeleted = 0;",
                new { jalaliYear }) ?? 0;

            if (lastSerial < maxUsed)
                return Result.Failure(
                    $"مقدار واردشده ({lastSerial}) کمتر از آخرین شمارهٔ استفاده‌شده در این سال ({maxUsed}) است. " +
                    "برای جلوگیری از شمارهٔ تکراری، عددی بزرگ‌تر یا مساوی وارد کنید.");

            db.Execute(@"
INSERT INTO NumberSequences (JalaliYear, LastSerial, Format, UpdatedAt)
VALUES (@year, @serial, @format, @now)
ON CONFLICT(JalaliYear) DO UPDATE SET LastSerial = @serial, UpdatedAt = @now;",
                new { year = jalaliYear, serial = lastSerial, format = GetFormat(), now = DateTime.UtcNow.ToString("o") });

            await _audit.WriteAsync(new Domain.Entities.AuditEntry
            {
                ActionType = "ChangeNumber",
                EntityType = "NumberSequence",
                EntityId = jalaliYear,
                UserName = byUser,
                OccurredAt = DateTime.UtcNow,
                Details = $"شمارندهٔ سال {jalaliYear} به {lastSerial} تغییر یافت."
            }, ct);

            _logger.Info($"شمارندهٔ سال {jalaliYear} توسط «{byUser}» به {lastSerial} تغییر کرد.");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("تغییر شمارنده ناموفق بود.", ex);
            return Result.Failure($"تغییر شمارنده ممکن نشد: {ex.Message}");
        }
    }

    /// <summary>قالب شماره از تنظیمات؛ پیش‌فرض {year}/{serial:00000}.</summary>
    private string GetFormat()
        => _settings.Get(SettingKeys.NumberFormat, "{year}/{serial:00000}");

    /// <summary>
    /// ساخت رشتهٔ شماره از قالب. پشتیبانی از {year} و {serial} با تعداد صفر دلخواه،
    /// مثلاً {serial:00000} یا {serial:0000}. ارقام انگلیسی ذخیره می‌شوند؛ نمایش فارسی در UI.
    /// </summary>
    public string FormatNumber(int jalaliYear, int serial) => Format(jalaliYear, serial);

    private string Format(int year, int serial)
    {
        var format = GetFormat();

        format = format.Replace("{year}", year.ToString(CultureInfo.InvariantCulture));

        // {serial:00000} → سریال با صفرهای ابتدایی به همان تعداد صفر الگو
        format = Regex.Replace(format, @"\{serial(?::(0+))?\}", m =>
        {
            var pad = m.Groups[1].Success ? m.Groups[1].Value.Length : 5;
            return serial.ToString(new string('0', pad), CultureInfo.InvariantCulture);
        });

        return format;
    }
}

/// <summary>
/// امکان صدا زدن AllocateSerialInTransaction از سرویس صدور نامه بدون وابستگی به کلاس بتنی.
/// </summary>
public interface IAtomicSerialAllocator
{
    (int Year, int Serial, string Number) AllocateSerialInTransaction(
        IDbConnection db, IDbTransaction tx, int jalaliYear);

    (int Year, int Serial, string Number) AllocateSerialInTransaction(
        IDbConnection db, IDbTransaction tx, int jalaliYear, int? preferredSerial);
}
