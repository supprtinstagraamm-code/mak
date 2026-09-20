using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Entities;
using ClosedXML.Excel;

namespace CertificateAutomation.Infrastructure.Excel;

/// <summary>
/// بارگذاری کارکنان از فایل Excel با ClosedXML.
///
/// نکته کلیدی «ستون‌های پویا»: نگاشت ستون‌های شناخته‌شده (کد ملی، نام و ...) از جدول
/// ColumnMappings خوانده می‌شود. هر ستونی که در این نگاشت نباشد، حذف نمی‌شود بلکه در
/// ExtraDataJson ذخیره می‌گردد. به این ترتیب افزودن ستون جدید به Excel، بدون هیچ تغییری
/// در کد یا ساختار پایگاه داده پشتیبانی می‌شود و همان ستون در قالب‌های Word هم قابل استفاده است.
/// </summary>
public class ClosedXmlEmployeeImporter : IEmployeeImportService
{
    private readonly IEmployeeRepository _employees;
    private readonly IColumnMappingRepository _mappings;
    private readonly IAppLogger _logger;

    public ClosedXmlEmployeeImporter(
        IEmployeeRepository employees,
        IColumnMappingRepository mappings,
        IAppLogger logger)
    {
        _employees = employees;
        _mappings = mappings;
        _logger = logger;
    }

    public async Task<Result<ImportResult>> ImportAsync(string excelPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(excelPath))
            return Result.Failure<ImportResult>("مسیر فایل Excel مشخص نشده است. از بخش تنظیمات آن را تعیین کنید.");

        if (!File.Exists(excelPath))
            return Result.Failure<ImportResult>($"فایل Excel در مسیر زیر یافت نشد:\n{excelPath}");

        try
        {
            // نگاشت عنوان ستون → کلید Placeholder، بدون حساسیت به فاصله‌های اضافی
            var mappings = await _mappings.GetAllAsync(ct);
            var headerToKey = mappings.ToDictionary(
                m => Normalize(m.ExcelHeader),
                m => m.PlaceholderKey,
                StringComparer.Ordinal);

            // مجموعه کلیدهایی که به ستون‌های ثابت جدول نگاشت می‌شوند
            var coreKeys = mappings.Where(m => m.IsCore)
                .ToDictionary(m => m.PlaceholderKey, m => true, StringComparer.Ordinal);

            using var workbook = new XLWorkbook(excelPath);
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet is null)
                return Result.Failure<ImportResult>("فایل Excel هیچ کاربرگی ندارد.");

            var range = sheet.RangeUsed();
            if (range is null || range.RowCount() < 2)
                return Result.Failure<ImportResult>("فایل Excel داده‌ای برای بارگذاری ندارد (حداقل یک سطر عنوان و یک سطر داده لازم است).");

            // --- سطر عنوان ---
            var headerRow = range.FirstRow();
            var columns = new List<(int Col, string Header, string Key, bool IsCore)>();
            var newColumns = new List<string>();

            foreach (var cell in headerRow.Cells())
            {
                var header = cell.GetString().Trim();
                if (string.IsNullOrEmpty(header)) continue;

                var norm = Normalize(header);
                if (headerToKey.TryGetValue(norm, out var key))
                {
                    columns.Add((cell.Address.ColumnNumber, header, key, coreKeys.ContainsKey(key)));
                }
                else
                {
                    // ستون ناشناخته: کلید Placeholder از روی عنوان ساخته می‌شود (فاصله → زیرخط)
                    // کلید Placeholder از عنوان نرمال‌شده ساخته می‌شود (ی/ک فارسی، فاصله → زیرخط)
                    // تا قالبی که کاربر با حروف فارسی می‌نویسد، با ستون Excel عربی‌نویس هم تطبیق یابد.
                    var generatedKey = MakeKey(header);
                    columns.Add((cell.Address.ColumnNumber, header, generatedKey, false));
                    newColumns.Add(header);
                }
            }

            if (columns.Count == 0)
                return Result.Failure<ImportResult>("هیچ عنوان ستونی در سطر اول فایل Excel یافت نشد.");

            // --- سطرهای داده ---
            var employees = new List<Employee>();
            var warnings = new List<string>();
            var now = DateTime.UtcNow;
            int rowIndex = 1;

            foreach (var row in range.Rows().Skip(1))
            {
                rowIndex++;
                var (emp, extra, ok) = MapRow(row, columns);

                if (!ok)
                {
                    warnings.Add($"سطر {rowIndex}: نام یا نام خانوادگی خالی است؛ این سطر نادیده گرفته شد.");
                    continue;
                }

                // اعتبارسنجی نرم کد ملی: فقط هشدار، مانع بارگذاری نمی‌شود.
                if (!string.IsNullOrWhiteSpace(emp.NationalId) && !IsValidNationalId(emp.NationalId))
                    warnings.Add($"سطر {rowIndex}: کد ملی «{emp.NationalId}» معتبر به‌نظر نمی‌رسد.");

                emp.ExtraDataJson = extra.Count > 0
                    ? JsonSerializer.Serialize(extra)
                    : null;
                emp.RowHash = ComputeHash(emp, extra);
                emp.ImportedAt = now;

                employees.Add(emp);
            }

            if (employees.Count == 0)
                return Result.Failure<ImportResult>("هیچ سطر معتبری برای بارگذاری یافت نشد.");

            // ثبت ستون‌های جدید در جدول نگاشت تا در دفعات بعد شناخته شوند و در قالب‌ها قابل استفاده باشند.
            await RegisterNewColumnsAsync(newColumns, mappings.Count, ct);

            var importResult = await _employees.UpsertBatchAsync(employees, ct);
            importResult.NewColumns.AddRange(newColumns);
            importResult.Warnings.AddRange(warnings);

            return Result.Success(importResult);
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری فایل Excel با خطا مواجه شد.", ex);
            return Result.Failure<ImportResult>(
                $"خواندن فایل Excel ممکن نشد. اگر فایل در حال حاضر در برنامه Excel باز است، آن را ببندید و دوباره تلاش کنید.\n\nشرح فنی: {ex.Message}");
        }
    }

    /// <summary>نگاشت یک سطر Excel به یک Employee به‌همراه دیکشنری ستون‌های اضافی.</summary>
    private static (Employee Emp, Dictionary<string, string> Extra, bool Ok) MapRow(
        IXLRangeRow row,
        List<(int Col, string Header, string Key, bool IsCore)> columns)
    {
        var emp = new Employee();
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (col, _, key, isCore) in columns)
        {
            var value = row.Cell(col).GetString().Trim();

            if (isCore)
                AssignCore(emp, key, value);
            else if (!string.IsNullOrEmpty(value))
                extra[key] = value;
        }

        var ok = !string.IsNullOrWhiteSpace(emp.FirstName) || !string.IsNullOrWhiteSpace(emp.LastName);
        return (emp, extra, ok);
    }

    /// <summary>نگاشت کلید Placeholder به ویژگی متناظر در موجودیت Employee.</summary>
    private static void AssignCore(Employee emp, string key, string value)
    {
        // اعداد فارسی در فیلدهای عددی (کد ملی، شماره‌ها) به انگلیسی تبدیل می‌شوند.
        switch (key)
        {
            case "کد_پرسنلی":    emp.PersonnelCode = PersianDate.ToEnglishDigits(value); break;
            case "شماره_عضویت":  emp.MembershipNo = PersianDate.ToEnglishDigits(value); break;
            case "نام":          emp.FirstName = value; break;
            case "نام_خانوادگی": emp.LastName = value; break;
            case "نام_پدر":      emp.FatherName = value; break;
            case "کد_ملی":       emp.NationalId = PersianDate.ToEnglishDigits(value); break;
            case "سمت":          emp.Position = value; break;
            case "واحد":         emp.Unit = value; break;
            case "شماره_تماس":   emp.Phone = PersianDate.ToEnglishDigits(value); break;
            case "تاریخ_استخدام":emp.HireDate = PersianDate.ToEnglishDigits(value); break;
        }
    }

    private async Task RegisterNewColumnsAsync(List<string> newColumns, int existingCount, CancellationToken ct)
    {
        var sort = existingCount + 1;
        foreach (var header in newColumns)
        {
            await _mappings.UpsertAsync(new ColumnMapping
            {
                ExcelHeader = header,
                PlaceholderKey = MakeKey(header),
                DisplayName = header,
                IsCore = false,
                SortOrder = sort++
            }, ct);
        }
    }

    /// <summary>
    /// ساخت کلید Placeholder استاندارد از عنوان ستون Excel.
    /// حروف عربی «ي/ك» به فارسی «ی/ک» تبدیل، فاصله‌ها به زیرخط، و فاصله‌های اضافی حذف می‌شوند.
    /// این تضمین می‌کند قالبی که کاربر با صفحه‌کلید فارسی می‌نویسد، با ستون Excel تطبیق یابد.
    /// </summary>
    public static string MakeKey(string header)
        => Normalize(header).Replace(' ', '_');

    /// <summary>نرمال‌سازی عنوان ستون: حذف فاصله‌های اضافی و یکسان‌سازی «ي/ك» عربی با فارسی.</summary>
    private static string Normalize(string s)
        => s.Trim()
            .Replace('ي', 'ی')
            .Replace('ك', 'ک')
            .Replace("  ", " ");

    /// <summary>هش سطر برای تشخیص تغییر در بارگذاری بعدی (صرفه‌جویی در نوشتن‌های بی‌مورد).</summary>
    private static string ComputeHash(Employee e, Dictionary<string, string> extra)
    {
        var sb = new StringBuilder();
        sb.Append(e.PersonnelCode).Append('|').Append(e.MembershipNo).Append('|')
          .Append(e.FirstName).Append('|').Append(e.LastName).Append('|')
          .Append(e.FatherName).Append('|').Append(e.NationalId).Append('|')
          .Append(e.Position).Append('|').Append(e.Unit).Append('|')
          .Append(e.Phone).Append('|').Append(e.HireDate);

        foreach (var kv in extra.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    /// <summary>اعتبارسنجی کد ملی ایران (۱۰ رقم با رقم کنترلی). فقط برای هشدار استفاده می‌شود.</summary>
    private static bool IsValidNationalId(string code)
    {
        if (code.Length != 10 || !code.All(char.IsDigit)) return false;
        if (new string(code[0], 10) == code) return false; // ارقام یکسان

        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (code[i] - '0') * (10 - i);

        var remainder = sum % 11;
        var check = code[9] - '0';
        return remainder < 2 ? check == remainder : check == 11 - remainder;
    }
}
