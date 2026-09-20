using System.Text.Json;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Entities;
using CertificateAutomation.Infrastructure.Data;
using Dapper;

namespace CertificateAutomation.Infrastructure.Services;

/// <summary>
/// سرویس اصلی صدور نامه. مسئولیت‌ها:
/// ۱. ساخت فهرست مقادیر هر Placeholder از منابع مختلف (سیستمی، اطلاعات فرد، ستون‌های اضافی، ورودی دستی).
/// ۲. صدور نهایی: در «یک تراکنش» شماره را مصرف و رکورد نامه را ثبت می‌کند، سپس سند Word را می‌سازد.
/// ۳. چاپ مجدد با همان شماره و همان مقادیر قبلی (از روی Snapshot ذخیره‌شده).
/// </summary>
public class LetterIssueService : ILetterIssueService
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly IEmployeeRepository _employees;
    private readonly ITemplateRepository _templates;
    private readonly IColumnMappingRepository _mappings;
    private readonly ITemplateEngine _engine;
    private readonly IAtomicSerialAllocator _allocator;
    private readonly ILetterRepository _letterRepo;
    private readonly IDocumentRenderer _renderer;
    private readonly ISettingsService _settings;
    private readonly IAuditRepository _audit;
    private readonly IAppLogger _logger;

    public LetterIssueService(
        ISqliteConnectionFactory factory,
        IEmployeeRepository employees,
        ITemplateRepository templates,
        IColumnMappingRepository mappings,
        ITemplateEngine engine,
        IAtomicSerialAllocator allocator,
        ILetterRepository letterRepo,
        IDocumentRenderer renderer,
        ISettingsService settings,
        IAuditRepository audit,
        IAppLogger logger)
    {
        _factory = factory;
        _employees = employees;
        _templates = templates;
        _mappings = mappings;
        _engine = engine;
        _allocator = allocator;
        _letterRepo = letterRepo;
        _renderer = renderer;
        _settings = settings;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// ساخت فهرست فیلدهای یک قالب برای یک شخص، برای نمایش در پیش‌نمایش.
    /// مقدار هر فیلد از منابع زیر به‌ترتیب اولویت پر می‌شود.
    /// </summary>
    public async Task<Result<IReadOnlyList<PlaceholderField>>> BuildFieldsAsync(
        long employeeId, long templateId, CancellationToken ct = default)
    {
        var employee = await _employees.GetByIdAsync(employeeId, ct);
        if (employee is null)
            return Result.Failure<IReadOnlyList<PlaceholderField>>("شخص انتخاب‌شده یافت نشد.");

        var template = await _templates.GetByIdAsync(templateId, ct);
        if (template is null)
            return Result.Failure<IReadOnlyList<PlaceholderField>>("قالب انتخاب‌شده یافت نشد.");

        var placeholders = await _templates.GetPlaceholdersAsync(templateId, ct);
        var mappings = await _mappings.GetAllAsync(ct);
        var displayNames = mappings.ToDictionary(m => m.PlaceholderKey, m => m.DisplayName, StringComparer.Ordinal);

        var (values, sources) = BuildValueMap(employee, template, systemNumber: null, systemDate: null);

        var fields = new List<PlaceholderField>();
        foreach (var ph in placeholders)
        {
            values.TryGetValue(ph.PlaceholderKey, out var value);
            sources.TryGetValue(ph.PlaceholderKey, out var source);

            fields.Add(new PlaceholderField
            {
                Key = ph.PlaceholderKey,
                DisplayName = displayNames.TryGetValue(ph.PlaceholderKey, out var dn) ? dn : ph.PlaceholderKey,
                Value = value,
                Source = source ?? "دستی",
                IsRequired = ph.IsRequired
            });
        }

        return Result.Success<IReadOnlyList<PlaceholderField>>(fields);
    }

    /// <summary>
    /// ساخت نگاشت کلید→مقدار و کلید→منبع.
    /// اولویت: مقادیر سیستمی > ستون‌های اصلی فرد > ستون‌های اضافی (ExtraDataJson).
    /// شماره و تاریخ فقط هنگام صدور نهایی مقدار می‌گیرند (systemNumber/systemDate).
    /// </summary>
    private (Dictionary<string, string> Values, Dictionary<string, string> Sources) BuildValueMap(
        Employee emp, LetterTemplate template, string? systemNumber, string? systemDate)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        void Put(string key, string? value, string source)
        {
            values[key] = value ?? string.Empty;
            sources[key] = source;
        }

        // ۱) مقادیر سیستمی
        Put("شماره_نامه", systemNumber ?? "(هنگام صدور تعیین می‌شود)", "سیستمی");
        Put("تاریخ", systemDate ?? PersianDate.Today(), "سیستمی");
        Put("ساعت", PersianDate.CurrentTime(), "سیستمی");
        Put("نام_شرکت", _settings.Get(SettingKeys.CompanyName, "شرکت"), "سیستمی");

        // ۲) ستون‌های اصلی فرد
        Put("کد_پرسنلی", emp.PersonnelCode, "اطلاعات فرد");
        Put("شماره_عضویت", emp.MembershipNo, "اطلاعات فرد");
        Put("نام", emp.FirstName, "اطلاعات فرد");
        Put("نام_خانوادگی", emp.LastName, "اطلاعات فرد");
        Put("نام_پدر", emp.FatherName, "اطلاعات فرد");
        Put("کد_ملی", emp.NationalId, "اطلاعات فرد");
        Put("سمت", emp.Position, "اطلاعات فرد");
        Put("واحد", emp.Unit, "اطلاعات فرد");
        Put("شماره_تماس", emp.Phone, "اطلاعات فرد");
        Put("تاریخ_استخدام", emp.HireDate, "اطلاعات فرد");

        // ۳) ستون‌های اضافی Excel
        if (!string.IsNullOrWhiteSpace(emp.ExtraDataJson))
        {
            try
            {
                var extra = JsonSerializer.Deserialize<Dictionary<string, string>>(emp.ExtraDataJson);
                if (extra is not null)
                    foreach (var kv in extra)
                        Put(kv.Key, kv.Value, "ستون اضافی");
            }
            catch (JsonException) { /* داده خراب: نادیده */ }
        }

        return (values, sources);
    }

    /// <summary>صدور نهایی نامه. شماره فقط در همین متد و داخل تراکنش مصرف می‌شود.</summary>
    public async Task<Result<IssuedLetterResult>> IssueAsync(IssueLetterRequest request, CancellationToken ct = default)
    {
        var employee = await _employees.GetByIdAsync(request.EmployeeId, ct);
        if (employee is null) return Result.Failure<IssuedLetterResult>("شخص انتخاب‌شده یافت نشد.");

        var template = await _templates.GetByIdAsync(request.TemplateId, ct);
        if (template is null) return Result.Failure<IssuedLetterResult>("قالب انتخاب‌شده یافت نشد.");

        var templatesFolder = _settings.Get(SettingKeys.TemplatesFolder);
        var templatePath = Path.Combine(templatesFolder, template.FileName);
        if (!File.Exists(templatePath))
            return Result.Failure<IssuedLetterResult>(
                $"فایل قالب یافت نشد:\n{templatePath}\n\nمسیر پوشهٔ قالب‌ها را در تنظیمات بررسی کنید.");

        var year = PersianDate.CurrentYear;
        var jalaliDate = PersianDate.Today();
        var time = PersianDate.CurrentTime();
        var warnings = new List<string>();

        Letter letter;
        string letterNumber;

        // --- تراکنش اتمیک: رزرو شماره + درج رکورد نامه ---
        using (var db = _factory.Create())
        using (var tx = db.BeginTransaction())
        {
            try
            {
                var (allocYear, serial, number) = _allocator.AllocateSerialInTransaction(db, tx, year, request.PreferredSerial);
                letterNumber = number;

                // ساخت مقادیر نهایی با شمارهٔ واقعی
                var (values, _) = BuildValueMap(employee, template, number, jalaliDate);

                // ورودی‌های دستی کاربر، مقادیر پیش‌فرض را بازنویسی می‌کنند (مثلاً فیلدهای بدون داده در Excel)
                foreach (var kv in request.ManualValues)
                    values[kv.Key] = kv.Value ?? string.Empty;

                // بررسی فیلدهای الزامی بدون مقدار (فقط هشدار، مانع صدور نمی‌شود)
                var placeholders = await _templates.GetPlaceholdersAsync(template.Id, ct);
                var missing = placeholders
                    .Where(p => p.IsRequired && (!values.TryGetValue(p.PlaceholderKey, out var v) || string.IsNullOrWhiteSpace(v)))
                    .Select(p => p.PlaceholderKey)
                    .ToList();
                if (missing.Count > 0)
                    warnings.Add($"فیلدهای بدون مقدار (خالی جایگزین شد): {string.Join("، ", missing)}");

                var dataJson = JsonSerializer.Serialize(values);

                var newId = db.ExecuteScalar<long>(@"
INSERT INTO Letters
(LetterNumber, JalaliYear, Serial, IssuedAtUtc, JalaliDate, IssuedTime, EmployeeId,
 SnapFirstName, SnapLastName, SnapNationalId, SnapMembershipNo, SnapPosition, SnapUnit,
 TemplateId, SnapTemplateTitle, DataSnapshotJson, PrintCount, Description, IsDeleted)
VALUES
(@LetterNumber, @JalaliYear, @Serial, @IssuedAtUtc, @JalaliDate, @IssuedTime, @EmployeeId,
 @SnapFirstName, @SnapLastName, @SnapNationalId, @SnapMembershipNo, @SnapPosition, @SnapUnit,
 @TemplateId, @SnapTemplateTitle, @DataSnapshotJson, 0, @Description, 0);
SELECT last_insert_rowid();",
                    new
                    {
                        LetterNumber = number,
                        JalaliYear = allocYear,
                        Serial = serial,
                        IssuedAtUtc = DateTime.UtcNow.ToString("o"),
                        JalaliDate = jalaliDate,
                        IssuedTime = time,
                        EmployeeId = employee.Id,
                        SnapFirstName = employee.FirstName,
                        SnapLastName = employee.LastName,
                        SnapNationalId = employee.NationalId,
                        SnapMembershipNo = employee.MembershipNo,
                        SnapPosition = employee.Position,
                        SnapUnit = employee.Unit,
                        TemplateId = template.Id,
                        SnapTemplateTitle = template.Title,
                        DataSnapshotJson = dataJson,
                        request.Description
                    }, tx);

                letter = new Letter
                {
                    Id = newId,
                    LetterNumber = number,
                    JalaliYear = allocYear,
                    Serial = serial,
                    JalaliDate = jalaliDate,
                    IssuedTime = time,
                    EmployeeId = employee.Id,
                    SnapFirstName = employee.FirstName,
                    SnapLastName = employee.LastName,
                    SnapNationalId = employee.NationalId,
                    SnapMembershipNo = employee.MembershipNo,
                    SnapPosition = employee.Position,
                    SnapUnit = employee.Unit,
                    TemplateId = template.Id,
                    SnapTemplateTitle = template.Title,
                    DataSnapshotJson = dataJson,
                    Description = request.Description
                };

                // تولید سند Word از روی مقادیر همین تراکنش
                var outputFolder = _settings.Get(SettingKeys.OutputFolder, AppPaths.DefaultOutputFolder);
                Directory.CreateDirectory(outputFolder);
                var safeName = MakeSafeFileName($"{number.Replace('/', '-')}_{employee.LastName}");
                var outputPath = Path.Combine(outputFolder, safeName + ".docx");

                var fill = _engine.Fill(templatePath, outputPath, values);
                if (fill.IsFailure)
                {
                    // اگر ساخت سند شکست خورد، کل تراکنش برگردانده می‌شود و شماره مصرف نمی‌شود.
                    tx.Rollback();
                    return Result.Failure<IssuedLetterResult>(fill.Error!);
                }

                // ثبت مسیر سند و نهایی‌سازی تراکنش
                db.Execute("UPDATE Letters SET ArchivePdfPath = NULL WHERE Id = @id;", new { id = newId }, tx);
                tx.Commit();

                _logger.Info($"نامهٔ شمارهٔ {number} برای «{employee.FullName}» صادر شد.");

                var result = new IssuedLetterResult
                {
                    Letter = letter,
                    GeneratedDocxPath = outputPath,
                    Printed = false
                };
                result.Warnings.AddRange(warnings);

                // ثبت ممیزی خارج از تراکنش اصلی (شکستش نباید صدور را برگرداند)
                await SafeAuditAsync("IssueLetter", "Letter", newId,
                    $"شمارهٔ {number} — {employee.FullName} — {template.Title}", ct);

                return Result.Success(result);
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { /* ignore */ }
                _logger.Error("صدور نامه ناموفق بود؛ تراکنش برگردانده شد.", ex);
                return Result.Failure<IssuedLetterResult>(
                    $"صدور نامه با خطا مواجه شد و هیچ شماره‌ای مصرف نشد.\n\nشرح فنی: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// چاپ مجدد: سند را از روی همان مقادیر ذخیره‌شده (DataSnapshotJson) دوباره می‌سازد،
    /// بدون تولید شمارهٔ جدید. شمارندهٔ چاپ در لایهٔ چاپ (فاز ۶) افزایش می‌یابد.
    /// </summary>
    public async Task<Result<IssuedLetterResult>> ReprintAsync(
        long letterId, string? printerName, int copies, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var letter = db.QueryFirstOrDefault<Letter>(@"
SELECT Id, LetterNumber, JalaliYear, Serial, JalaliDate, IssuedTime, EmployeeId,
       SnapFirstName, SnapLastName, SnapNationalId, SnapMembershipNo, SnapPosition, SnapUnit,
       TemplateId, SnapTemplateTitle, DataSnapshotJson, PrintCount, Description
FROM Letters WHERE Id = @letterId AND IsDeleted = 0;", new { letterId });

        if (letter is null)
            return Result.Failure<IssuedLetterResult>("نامهٔ موردنظر یافت نشد یا حذف شده است.");

        var template = letter.TemplateId is null ? null : await _templates.GetByIdAsync(letter.TemplateId.Value, ct);
        if (template is null)
            return Result.Failure<IssuedLetterResult>(
                "قالب اصلی این نامه دیگر موجود نیست؛ امکان چاپ مجدد وجود ندارد.");

        var templatesFolder = _settings.Get(SettingKeys.TemplatesFolder);
        var templatePath = Path.Combine(templatesFolder, template.FileName);
        if (!File.Exists(templatePath))
            return Result.Failure<IssuedLetterResult>($"فایل قالب یافت نشد:\n{templatePath}");

        Dictionary<string, string> values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, string>>(letter.DataSnapshotJson ?? "{}")
                     ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return Result.Failure<IssuedLetterResult>("اطلاعات ذخیره‌شدهٔ این نامه خوانا نیست.");
        }

        var outputFolder = _settings.Get(SettingKeys.OutputFolder, AppPaths.DefaultOutputFolder);
        Directory.CreateDirectory(outputFolder);
        var safeName = MakeSafeFileName($"{letter.LetterNumber.Replace('/', '-')}_{letter.SnapLastName}_چاپ‌مجدد");
        var outputPath = Path.Combine(outputFolder, safeName + ".docx");

        var fill = _engine.Fill(templatePath, outputPath, values);
        if (fill.IsFailure)
            return Result.Failure<IssuedLetterResult>(fill.Error!);

        _logger.Info($"سند نامهٔ {letter.LetterNumber} برای چاپ مجدد بازتولید شد.");

        var result = new IssuedLetterResult
        {
            Letter = letter,
            GeneratedDocxPath = outputPath,
            Printed = false
        };
        return Result.Success(result);
    }

    /// <summary>چاپ سند و ثبت سابقهٔ چاپ. شمارندهٔ چاپ نامه در صورت موفقیت افزایش می‌یابد.</summary>
    public async Task<Result> PrintDocumentAsync(
        long letterId, string documentPath, string? printerName, int copies, CancellationToken ct = default)
    {
        var effectiveCopies = Math.Max(1, copies);
        var result = await _renderer.PrintAsync(documentPath, printerName, effectiveCopies, ct);

        // سابقهٔ چاپ در هر دو حالت موفق و ناموفق ثبت می‌شود (برای ردیابی).
        await _letterRepo.RegisterPrintAsync(new PrintJob
        {
            LetterId = letterId,
            PrinterName = printerName,
            Copies = effectiveCopies,
            PrintedAt = DateTime.UtcNow,
            Status = result.IsSuccess ? Domain.Enums.PrintStatus.Success : Domain.Enums.PrintStatus.Failed,
            ErrorText = result.IsFailure ? result.Error : null
        }, ct);

        if (result.IsSuccess)
            _logger.Info($"نامهٔ شمارهٔ {letterId} چاپ شد ({effectiveCopies} نسخه).");

        return result;
    }

    /// <summary>ساخت خروجی PDF از سند نامه و ذخیرهٔ مسیر آن در پایگاه داده.</summary>
    public async Task<Result<string>> ExportPdfAsync(long letterId, string docxPath, CancellationToken ct = default)
    {
        if (!File.Exists(docxPath))
            return Result.Failure<string>($"فایل سند یافت نشد:\n{docxPath}");

        var pdfPath = Path.ChangeExtension(docxPath, ".pdf");
        var result = await _renderer.ConvertToPdfAsync(docxPath, pdfPath, ct);

        if (result.IsFailure)
            return result;

        // ثبت مسیر PDF در رکورد نامه
        try
        {
            using var db = _factory.Create();
            db.Execute("UPDATE Letters SET ArchivePdfPath = @path WHERE Id = @id;",
                new { path = pdfPath, id = letterId });

            // ثبت سابقهٔ ذخیرهٔ PDF
            await _letterRepo.RegisterPrintAsync(new PrintJob
            {
                LetterId = letterId,
                Copies = 1,
                PrintedAt = DateTime.UtcNow,
                Status = Domain.Enums.PrintStatus.SavedAsPdf
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.Warn($"ثبت مسیر PDF ناموفق بود (فایل ساخته شد): {ex.Message}");
        }

        _logger.Info($"خروجی PDF نامهٔ {letterId} ساخته شد.");
        return Result.Success(pdfPath);
    }

    private async Task SafeAuditAsync(string action, string entity, long id, string details, CancellationToken ct)
    {
        try
        {
            await _audit.WriteAsync(new AuditEntry
            {
                ActionType = action,
                EntityType = entity,
                EntityId = id,
                UserName = "کاربر",
                OccurredAt = DateTime.UtcNow,
                Details = details
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.Warn($"ثبت ممیزی ناموفق بود (بی‌اثر بر صدور): {ex.Message}");
        }
    }

    /// <summary>حذف کاراکترهای غیرمجاز نام فایل ویندوز.</summary>
    private static string MakeSafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 120 ? name[..120] : name;
    }
}
