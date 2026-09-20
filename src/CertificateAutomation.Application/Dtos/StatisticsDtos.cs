namespace CertificateAutomation.Application.Dtos;

/// <summary>یک ردیف تحلیلی: تمام فیلدهای یک شخص به‌صورت کلید/مقدار (ستون‌های ثابت + ستون‌های Excel).</summary>
public class AnalyticsRow
{
    public long EmployeeId { get; set; }
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
}

/// <summary>یک دسته در توزیع فراوانی (مثلاً «تهران: ۱۲ نفر»).</summary>
public class CategoryCount
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percent { get; set; }
}

/// <summary>خلاصهٔ آماری یک ستون عددی.</summary>
public class NumericSummary
{
    public int Count { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Average { get; set; }
    public double Median { get; set; }
    public double StdDev { get; set; }
}

/// <summary>یک نقطه در نمودار پراکندگی.</summary>
public class ScatterPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public string? Label { get; set; }
}

/// <summary>نتیجهٔ کامل یک تحلیل، آمادهٔ نمایش.</summary>
public class AnalysisResult
{
    /// <summary>عنوان توصیفی تحلیل، برای نمایش بالای نمودار.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>توزیع فراوانی (برای نمودار میله‌ای، دایره‌ای، هیستوگرام).</summary>
    public List<CategoryCount> Categories { get; } = new();

    /// <summary>خلاصهٔ عددی، در صورتی که ستون انتخابی عددی باشد.</summary>
    public NumericSummary? Numeric { get; set; }

    /// <summary>نقاط پراکندگی، وقتی دو ستون عددی مقایسه می‌شوند.</summary>
    public List<ScatterPoint> Points { get; } = new();

    /// <summary>ضریب همبستگی دو متغیر عددی (بین ۱- و ۱+)، در صورت وجود.</summary>
    public double? Correlation { get; set; }

    /// <summary>ضرایب خط رگرسیون y = a*x + b، در صورت وجود.</summary>
    public double? RegressionSlope { get; set; }
    public double? RegressionIntercept { get; set; }

    /// <summary>تعداد رکوردهایی که برای این تحلیل مقدار معتبر داشتند.</summary>
    public int ValidCount { get; set; }

    /// <summary>تعداد رکوردهایی که مقدار نداشتند و کنار گذاشته شدند.</summary>
    public int MissingCount { get; set; }
}

/// <summary>درخواست خروجی گزارش (Excel یا PDF) از یک تحلیل آماری.</summary>
public class ReportExportRequest
{
    public string Title { get; set; } = string.Empty;

    /// <summary>توضیح فیلتر فعال، مثلاً «فقط پروژه: خدمات دریایی» — یا خالی اگر فیلتری نبود.</summary>
    public string? FilterDescription { get; set; }

    public List<CategoryCount> Categories { get; } = new();
    public NumericSummary? Numeric { get; set; }

    /// <summary>تصویر PNG نمودار جاری (از بوم WPF گرفته می‌شود)؛ می‌تواند null باشد.</summary>
    public byte[]? ChartImagePng { get; set; }

    /// <summary>مسیر کامل فایل خروجی.</summary>
    public string OutputPath { get; set; } = string.Empty;
}
