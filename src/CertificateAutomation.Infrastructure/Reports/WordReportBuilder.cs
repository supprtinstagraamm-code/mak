using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Common;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace CertificateAutomation.Infrastructure.Reports;

/// <summary>
/// ساخت یک سند Word از صفر (نه از روی قالب) برای گزارش آماری: عنوان، تصویر نمودار،
/// جدول داده و خلاصهٔ عددی — همه راست‌چین. این سند سپس با همان لایهٔ Renderer موجود
/// (Word/LibreOffice) به PDF تبدیل می‌شود، بنابراین از موتور تبدیل قبلاً تست‌شده استفاده
/// می‌کند و نیازی به کتابخانهٔ جداگانهٔ PDF نیست.
///
/// نکتهٔ راست‌چین بودن سند: هم در سطح بخش (SectionProperties → BiDi) و هم در هر پاراگراف
/// و جدول (BiDi / BiDiVisual) تنظیم می‌شود تا هم جهت نوشتار و هم ترتیب ستون‌های جدول درست باشد.
/// </summary>
public class WordReportBuilder
{
    private const string TitleColorHex = "1E3A5F";
    private const string AccentColorHex = "0E7C86";
    private const string MutedColorHex = "6E7887";

    public string Build(ReportExportRequest request, string outputDocxPath)
    {
        var dir = Path.GetDirectoryName(outputDocxPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var doc = WordprocessingDocument.Create(outputDocxPath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();

        // عنوان
        body.Append(Paragraph(request.Title, bold: true, size: 32, color: TitleColorHex,
            align: JustificationValues.Center));

        // توضیح فیلتر فعال
        if (!string.IsNullOrWhiteSpace(request.FilterDescription))
            body.Append(Paragraph(request.FilterDescription!, italic: true, size: 20, color: MutedColorHex,
                align: JustificationValues.Center));

        body.Append(Paragraph(string.Empty)); // فاصله

        // تصویر نمودار
        if (request.ChartImagePng is { Length: > 0 })
        {
            try
            {
                var imagePart = mainPart.AddImagePart(ImagePartType.Png);
                using (var ms = new MemoryStream(request.ChartImagePng))
                    imagePart.FeedData(ms);

                var relId = mainPart.GetIdOfPart(imagePart);

                // اندازهٔ نمایش در سند: حداکثر عرض معادل ۱۶ سانتی‌متر، با حفظ نسبت تصویر.
                var (pxW, pxH) = ReadPngSize(request.ChartImagePng) ?? (800, 450);
                const long maxWidthEmu = 16 * 360000L; // ۱۶ سانتی‌متر بر حسب EMU
                var scale = maxWidthEmu / (double)(pxW * 9525L); // 1px ≈ 9525 EMU در ۹۶dpi
                var widthEmu = (long)(pxW * 9525L * Math.Min(1, scale));
                var heightEmu = (long)(pxH * 9525L * Math.Min(1, scale));

                var imgParagraph = new Paragraph(
                    new ParagraphProperties(new Justification { Val = JustificationValues.Center }, new BiDi()),
                    CreateImageRun(relId, widthEmu, heightEmu, "chart"));
                body.Append(imgParagraph);
                body.Append(Paragraph(string.Empty));
            }
            catch
            {
                // اگر جاسازی تصویر شکست خورد، گزارش بدون تصویر ادامه می‌یابد (جدول داده مهم‌تر است).
            }
        }

        // جدول داده
        if (request.Categories.Count > 0)
            body.Append(BuildTable(request.Categories));

        // خلاصهٔ عددی
        if (request.Numeric is { } n)
        {
            body.Append(Paragraph(string.Empty));
            body.Append(Paragraph($"میانگین: {Fa(Math.Round(n.Average, 1))}    |    میانه: {Fa(Math.Round(n.Median, 1))}    |    " +
                                   $"کمترین: {Fa(n.Min)}    |    بیشترین: {Fa(n.Max)}",
                size: 20, color: MutedColorHex, align: JustificationValues.Center));
        }

        // بخش سند: راست‌چین کل سند + اندازهٔ صفحهٔ A4
        var sectionProps = new SectionProperties(
            new PageSize { Width = 11906, Height = 16838 }, // A4 بر حسب Twips
            new PageMargin { Top = 1000, Bottom = 1000, Left = 1000, Right = 1000 },
            new BiDi());
        body.Append(sectionProps);

        mainPart.Document.Append(body);
        mainPart.Document.Save();

        return outputDocxPath;
    }

    /// <summary>ساخت یک جدول Word راست‌چین (ترتیب ستون‌ها هم از راست شروع می‌شود) با سربرگ رنگی.</summary>
    private static Table BuildTable(List<CategoryCount> categories)
    {
        var table = new Table();

        var hasPercent = categories.Any(c => c.Percent > 0);
        var headers = hasPercent ? new[] { "عنوان", "تعداد", "درصد" } : new[] { "عنوان", "تعداد" };

        var tableProps = new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 6, Color = "AAAAAA" },
                new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "AAAAAA" },
                new LeftBorder { Val = BorderValues.Single, Size = 6, Color = "AAAAAA" },
                new RightBorder { Val = BorderValues.Single, Size = 6, Color = "AAAAAA" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "DDDDDD" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "DDDDDD" }),
            new TableJustification { Val = TableRowAlignmentValues.Center },
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            // ترتیب راست‌به‌چپ خودِ جدول؛ بدون این، ستون‌ها برعکس (چپ به راست) نمایش داده می‌شوند.
            new BiDiVisual());
        table.AppendChild(tableProps);

        // سطر سربرگ
        var headerRow = new TableRow();
        foreach (var h in headers)
        {
            var cell = new TableCell(
                new TableCellProperties(
                    new Shading { Fill = TitleColorHex, Val = ShadingPatternValues.Clear },
                    new TableCellWidth { Type = TableWidthUnitValues.Auto }),
                new Paragraph(
                    new ParagraphProperties(new Justification { Val = JustificationValues.Center }, new BiDi()),
                    new Run(
                        new RunProperties(new Bold(), new Color { Val = "FFFFFF" }, new FontSize { Val = "22" }),
                        new Text(h))));
            headerRow.Append(cell);
        }
        table.Append(headerRow);

        // سطرهای داده
        var rowIndex = 0;
        foreach (var c in categories)
        {
            var shadeHex = rowIndex % 2 == 1 ? "F6F8FA" : "FFFFFF";
            var row = new TableRow();

            row.Append(DataCell(c.Label, shadeHex, bold: false));
            row.Append(DataCell(Fa(c.Count), shadeHex, bold: false, center: true));
            if (hasPercent)
                row.Append(DataCell(c.Percent > 0 ? Fa(Math.Round(c.Percent, 1)) + "%" : "-", shadeHex, bold: false, center: true));

            table.Append(row);
            rowIndex++;
        }

        return table;
    }

    private static TableCell DataCell(string text, string shadeHex, bool bold, bool center = false)
    {
        var justification = center ? JustificationValues.Center : JustificationValues.Right;

        // ساخت RunProperties بدون فرزند null: Bold فقط وقتی لازم است اضافه می‌شود
        // (به‌جای گذاشتن یک عنصر null داخل آرایهٔ params که هشدار/خطای null دارد).
        var runProps = new RunProperties(new FontSize { Val = "20" });
        if (bold) runProps.PrependChild(new Bold());

        return new TableCell(
            new TableCellProperties(
                new Shading { Fill = shadeHex, Val = ShadingPatternValues.Clear },
                new TableCellWidth { Type = TableWidthUnitValues.Auto }),
            new Paragraph(
                new ParagraphProperties(new Justification { Val = justification }, new BiDi()),
                new Run(runProps, new Text(text))));
    }

    private static Paragraph Paragraph(
        string text, bool bold = false, bool italic = false, int size = 22,
        string? color = null, JustificationValues? align = null)
    {
        // JustificationValues در OpenXml SDK یک enum سادهٔ #C نیست (یک نوع ساختاری برای
        // پشتیبانی از مقادیر گسترش‌پذیر است)، پس نمی‌تواند مستقیماً مقدار پیش‌فرض پارامتر
        // باشد؛ به همین دلیل nullable گرفته و در بدنهٔ متد مقدار واقعی تعیین می‌شود.
        var effectiveAlign = align ?? JustificationValues.Right;

        var runProps = new RunProperties();
        if (bold) runProps.Append(new Bold());
        if (italic) runProps.Append(new Italic());
        runProps.Append(new FontSize { Val = size.ToString() });
        if (color is not null) runProps.Append(new Color { Val = color });

        return new Paragraph(
            new ParagraphProperties(new Justification { Val = effectiveAlign }, new BiDi()),
            new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    /// <summary>ساخت عنصر Drawing استاندارد OpenXML برای جاسازی یک تصویر در متن سند.</summary>
    private static Run CreateImageRun(string relationshipId, long widthEmu, long heightEmu, string name)
    {
        var element = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = 1U, Name = name },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });

        return new Run(element);
    }

    /// <summary>خواندن عرض/ارتفاع تصویر PNG از هدر فایل (بدون نیاز به کتابخانهٔ تصویر جداگانه).</summary>
    private static (int Width, int Height)? ReadPngSize(byte[] png)
    {
        try
        {
            if (png.Length < 24) return null;
            // در فرمت PNG، عرض و ارتفاع در بایت‌های ۱۶ تا ۲۳ (Big-Endian) ذخیره شده‌اند.
            int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return width > 0 && height > 0 ? (width, height) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Fa(double v) => PersianDate.ToPersianDigits(
        (Math.Abs(v - Math.Round(v)) < 1e-9 ? Math.Round(v).ToString("0") : v.ToString("0.##")));
}
