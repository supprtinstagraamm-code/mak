using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Common;
using ClosedXML.Excel;

namespace CertificateAutomation.Infrastructure.Reports;

/// <summary>
/// ساخت فایل Excel از یک تحلیل آماری: عنوان، جدول داده و تصویر نمودار، همگی راست‌چین.
///
/// نکتهٔ کلیدی راست‌چین بودن: تنها راست‌چین کردن متن ستون‌ها کافی نیست — خودِ کاربرگ باید
/// RightToLeft باشد تا ترتیب ستون‌ها هم از راست به چپ نمایش داده شود (دقیقاً مثل یک سند
/// فارسی واقعی)، نه اینکه فقط متن‌ها راست‌چین باشند ولی ستون A هنوز سمت چپ بیفتد.
/// </summary>
public class ExcelReportExporter
{
    /// <summary>ساخت فایل Excel و ذخیره در request.OutputPath.</summary>
    public string Build(ReportExportRequest request)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("گزارش");

        // مهم‌ترین خط برای راست‌چین واقعی: کل کاربرگ RTL می‌شود.
        ws.RightToLeft = true;

        var row = 1;

        // --- عنوان ---
        var titleCell = ws.Cell(row, 1);
        titleCell.Value = request.Title;
        titleCell.Style.Font.Bold = true;
        titleCell.Style.Font.FontSize = 16;
        titleCell.Style.Font.FontColor = XLColor.FromArgb(0x1E, 0x3A, 0x5F);
        ws.Range(row, 1, row, 4).Merge();
        ws.Row(row).Height = 28;
        row += 1;

        // --- توضیح فیلتر (در صورت وجود) ---
        if (!string.IsNullOrWhiteSpace(request.FilterDescription))
        {
            var filterCell = ws.Cell(row, 1);
            filterCell.Value = request.FilterDescription;
            filterCell.Style.Font.Italic = true;
            filterCell.Style.Font.FontColor = XLColor.FromArgb(0x6E, 0x78, 0x87);
            ws.Range(row, 1, row, 4).Merge();
            row += 1;
        }

        row += 1; // یک ردیف خالی فاصله

        // --- تصویر نمودار (در صورت وجود) ---
        if (request.ChartImagePng is { Length: > 0 })
        {
            try
            {
                using var imgStream = new MemoryStream(request.ChartImagePng);
                // از سرریز بدون مشخص کردن فرمت استفاده می‌شود؛ ClosedXML خودش فرمت را
                // از محتوای جریان تشخیص می‌دهد، پس نیازی به شمارش دستی نوع تصویر نیست.
                var picture = ws.AddPicture(imgStream)
                    .MoveTo(ws.Cell(row, 1))
                    .WithSize(760, 420);

                // فضای کافی زیر تصویر برای جدول بعدی رزرو می‌شود (تقریبی بر اساس ارتفاع تصویر)
                row += (picture.Height / 20) + 2;
            }
            catch
            {
                // اگر جاسازی تصویر به هر دلیل ناموفق بود، گزارش بدون تصویر ادامه می‌یابد؛
                // مهم‌تر این است که جدول داده هرگز از دست نرود.
            }
        }

        // --- جدول داده ---
        if (request.Categories.Count > 0)
        {
            var headerRow = row;
            var headers = new[] { "عنوان", "تعداد", "درصد" };
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(headerRow, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0x1E, 0x3A, 0x5F);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            row++;

            foreach (var c in request.Categories)
            {
                ws.Cell(row, 1).Value = c.Label;
                ws.Cell(row, 2).Value = c.Count;
                if (c.Percent > 0)
                    ws.Cell(row, 3).Value = Math.Round(c.Percent, 1) + "%";

                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (row % 2 == 0)
                    ws.Range(row, 1, row, 3).Style.Fill.BackgroundColor = XLColor.FromArgb(0xF6, 0xF8, 0xFA);

                row++;
            }

            var tableRange = ws.Range(headerRow, 1, row - 1, 3);
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        }

        // --- خلاصهٔ عددی (در صورت وجود) ---
        if (request.Numeric is { } n)
        {
            row += 1;
            var lines = new[]
            {
                $"میانگین: {Fa(Math.Round(n.Average, 1))}",
                $"میانه: {Fa(Math.Round(n.Median, 1))}",
                $"کمترین: {Fa(n.Min)}",
                $"بیشترین: {Fa(n.Max)}",
                n.StdDev > 0 ? $"انحراف معیار: {Fa(Math.Round(n.StdDev, 2))}" : null
            }.Where(l => l is not null);

            foreach (var line in lines)
            {
                var cell = ws.Cell(row, 1);
                cell.Value = line;
                cell.Style.Font.FontColor = XLColor.FromArgb(0x6E, 0x78, 0x87);
                row++;
            }
        }

        ws.Columns(1, 4).AdjustToContents();
        // ستون اول (عناوین) معمولاً بلندترین متن را دارد؛ سقفی برایش می‌گذاریم تا ستون بیش‌ازحد پهن نشود.
        if (ws.Column(1).Width > 55) ws.Column(1).Width = 55;

        var dir = Path.GetDirectoryName(request.OutputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        workbook.SaveAs(request.OutputPath);

        return request.OutputPath;
    }

    private static string Fa(double v) => PersianDate.ToPersianDigits(
        (Math.Abs(v - Math.Round(v)) < 1e-9 ? Math.Round(v).ToString("0") : v.ToString("0.##")));
}
