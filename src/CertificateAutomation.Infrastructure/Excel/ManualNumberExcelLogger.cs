using System.Threading;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Infrastructure.Data;
using ClosedXML.Excel;

namespace CertificateAutomation.Infrastructure.Excel;

/// <summary>
/// ثبت هر «ثبت دستی شماره» در یک فایل اکسل مجزا (Data\ManualNumbers.xlsx) — جدا از
/// دیتابیس اصلی، تا قابل باز کردن، چاپ یا پیوست به ایمیل به‌تنهایی باشد.
///
/// این فایل ممکن است هم‌زمان از برنامهٔ اصلی و هم از ابزار شناور نوشته شود (دو فایل اجرایی
/// جدا)، برای همین نوشتن با یک Mutex سراسری سیستم سریال‌سازی می‌شود تا دو نوشتن هم‌زمان
/// سطر همدیگر را از بین نبرند.
/// </summary>
public class ManualNumberExcelLogger : IManualNumberLogService
{
    private const string MutexName = "Global\\CertificateAutomation_ManualNumbersExcel";
    private static readonly string[] Headers =
    {
        "ردیف", "تاریخ", "ساعت", "شمارهٔ نامه", "سال", "سریال", "توضیحات", "ثبت‌شده توسط", "ثبت در"
    };

    private readonly IAppLogger _logger;

    public ManualNumberExcelLogger(IAppLogger logger) => _logger = logger;

    public string ExcelFilePath => AppPaths.ManualNumbersExcelFile;

    public Result AppendEntry(
        string jalaliDate, string time, string letterNumber,
        int jalaliYear, int serial, string description, string? registeredBy)
    {
        using var mutex = new Mutex(false, MutexName);
        var acquired = false;

        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                // فرآیند قبلی بدون آزادسازی بسته شده؛ همچنان ادامه می‌دهیم.
                acquired = true;
            }

            if (!acquired)
                return Result.Failure("فایل اکسل ثبت‌های دستی توسط فرآیند دیگری قفل است؛ کمی بعد دوباره تلاش کنید.");

            Directory.CreateDirectory(AppPaths.Root);

            using var workbook = File.Exists(ExcelFilePath)
                ? new XLWorkbook(ExcelFilePath)
                : CreateNewWorkbook();

            var sheet = workbook.Worksheets.Contains("ثبت‌های دستی")
                ? workbook.Worksheet("ثبت‌های دستی")
                : workbook.Worksheets.First();

            var nextRow = sheet.LastRowUsed()?.RowNumber() + 1 ?? 2;
            var rowIndex = nextRow - 1; // ردیف نمایشی (بدون سرستون)

            sheet.Cell(nextRow, 1).Value = rowIndex;
            sheet.Cell(nextRow, 2).Value = jalaliDate;
            sheet.Cell(nextRow, 3).Value = time;
            sheet.Cell(nextRow, 4).Value = letterNumber;
            sheet.Cell(nextRow, 5).Value = jalaliYear;
            sheet.Cell(nextRow, 6).Value = serial;
            sheet.Cell(nextRow, 7).Value = description ?? string.Empty;
            sheet.Cell(nextRow, 8).Value = string.IsNullOrWhiteSpace(registeredBy) ? "—" : registeredBy;
            sheet.Cell(nextRow, 9).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            sheet.Columns(1, Headers.Length).AdjustToContents();
            sheet.RightToLeft = true;

            workbook.SaveAs(ExcelFilePath);

            return Result.Success();
        }
        catch (IOException)
        {
            // معمولاً یعنی فایل در همین لحظه در Excel باز است.
            return Result.Failure(
                $"فایل اکسل ثبت‌های دستی باز است (احتمالاً در Excel باز است) و نوشتن ممکن نشد:\n{ExcelFilePath}\n" +
                "آن را ببندید و دوباره تلاش کنید؛ خودِ شماره در دیتابیس ثبت شده است.");
        }
        catch (Exception ex)
        {
            _logger.Error("ثبت سطر در فایل اکسل ثبت‌های دستی ناموفق بود.", ex);
            return Result.Failure($"ثبت در فایل اکسل ناموفق بود: {ex.Message}");
        }
        finally
        {
            if (acquired)
            {
                try { mutex.ReleaseMutex(); } catch { /* ignore */ }
            }
        }
    }

    private static XLWorkbook CreateNewWorkbook()
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("ثبت‌های دستی");
        sheet.RightToLeft = true;

        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
        }

        return workbook;
    }
}
