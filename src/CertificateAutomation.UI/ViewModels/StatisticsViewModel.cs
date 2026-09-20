using System.Collections.ObjectModel;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>یک فیلد قابل انتخاب در صفحهٔ آمار.</summary>
public class FieldOption
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

/// <summary>یک آمار کلیدی برای نمایش در کارت‌های بالای صفحه.</summary>
public class Highlight
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// صفحهٔ آمار و نمودارها. کاربر یک یا دو فیلد انتخاب می‌کند و نوع نمودار را تعیین می‌کند؛
/// تحلیل متناسب با نوع داده به‌صورت خودکار انجام و رسم می‌شود.
/// </summary>
public partial class StatisticsViewModel : PageViewModelBase
{
    private readonly IStatisticsService _stats;
    private readonly IReportExportService _reportExport;
    private readonly IAppLogger _logger;

    /// <summary>
    /// از View تزریق می‌شود: نمودار جاری روی بوم را به تصویر PNG تبدیل می‌کند
    /// (چون ViewModel به عنصر بصری Canvas دسترسی مستقیم ندارد).
    /// </summary>
    public Func<byte[]?>? CaptureChartImage { get; set; }

    public StatisticsViewModel(IStatisticsService stats, IReportExportService reportExport, IAppLogger logger)
    {
        _stats = stats;
        _reportExport = reportExport;
        _logger = logger;
    }

    public override string Title => "آمار و نمودارها";
    public override string Subtitle => "تحلیل داده‌های پرسنل با نمودارهای قابل انتخاب";

    /// <summary>فیلدهای قابل انتخاب (تمام ستون‌های موجود).</summary>
    public ObservableCollection<FieldOption> Fields { get; } = new();

    /// <summary>آمارهای کلیدی بالای صفحه.</summary>
    public ObservableCollection<Highlight> Highlights { get; } = new();

    /// <summary>انواع نمودار قابل انتخاب.</summary>
    public ObservableCollection<string> ChartTypes { get; } = new()
    {
        "میله‌ای", "دایره‌ای", "افقی", "خطی", "پراکندگی"
    };

    /// <summary>فیلدهایی که می‌توان بر اساسشان فیلتر کرد (مثل پروژه، واحد، استان).</summary>
    public ObservableCollection<FieldOption> FilterFields { get; } = new();

    /// <summary>مقادیر موجود برای فیلد فیلتر انتخاب‌شده، با «همه» در ابتدا.</summary>
    public ObservableCollection<string> FilterValues { get; } = new();

    /// <summary>سطرهای جدول داده زیر نمودار.</summary>
    public ObservableCollection<CategoryCount> TableRows { get; } = new();

    [ObservableProperty] private FieldOption? _selectedFilterField;
    [ObservableProperty] private string? _selectedFilterValue;
    [ObservableProperty] private FieldOption? _selectedFieldX;
    [ObservableProperty] private FieldOption? _selectedFieldY;
    [ObservableProperty] private string _selectedChartType = "میله‌ای";
    [ObservableProperty] private int _histogramBins = 8;

    /// <summary>نتیجهٔ تحلیل جاری؛ نما با تغییر این مقدار، نمودار را دوباره رسم می‌کند.</summary>
    [ObservableProperty] private AnalysisResult? _result;

    [ObservableProperty] private string _analysisTitle = string.Empty;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool _hasData;

    partial void OnSelectedFilterFieldChanged(FieldOption? value) => _ = ReloadFilterValuesAsync();
    partial void OnSelectedFilterValueChanged(string? value) => _ = RefreshAllAsync();
    partial void OnSelectedFieldXChanged(FieldOption? value) => _ = AnalyzeAsync();
    partial void OnSelectedFieldYChanged(FieldOption? value) => _ = AnalyzeAsync();
    partial void OnSelectedChartTypeChanged(string value) => RaiseRedraw();
    partial void OnHistogramBinsChanged(int value) => _ = AnalyzeAsync();

    /// <summary>رویدادی که نما برای رسم دوبارهٔ نمودار به آن گوش می‌دهد.</summary>
    public event EventHandler? RedrawRequested;

    private void RaiseRedraw() => RedrawRequested?.Invoke(this, EventArgs.Empty);

    public override async Task OnNavigatedToAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusMessage = null;

            // فیلدهای قابل انتخاب
            var fields = await _stats.GetAvailableFieldsAsync();
            var previousX = SelectedFieldX?.Key;
            var previousFilterField = SelectedFilterField?.Key;

            Fields.Clear();
            foreach (var (key, name) in fields)
                Fields.Add(new FieldOption { Key = key, DisplayName = name });

            // فیلدهای مناسب برای فیلتر کردن
            FilterFields.Clear();
            foreach (var f in Fields) FilterFields.Add(f);

            // پیش‌فرض فیلتر: پروژه، وگرنه واحد
            SelectedFilterField = FilterFields.FirstOrDefault(f => f.Key == previousFilterField)
                               ?? FilterFields.FirstOrDefault(f => f.Key.Contains("پروژه", StringComparison.Ordinal))
                               ?? FilterFields.FirstOrDefault(f => f.Key.Contains("واحد", StringComparison.Ordinal));

            if (Fields.Count == 0)
            {
                StatusMessage = "داده‌ای برای تحلیل وجود ندارد. ابتدا از بخش «کارکنان» فایل Excel را بارگذاری کنید.";
                HasData = false;
                return;
            }

            // انتخاب پیش‌فرض: فیلد قبلی، وگرنه یک فیلد مفید مثل استان یا اولین فیلد
            SelectedFieldX = Fields.FirstOrDefault(f => f.Key == previousX)
                          ?? Fields.FirstOrDefault(f => f.Key.Contains("استان", StringComparison.Ordinal))
                          ?? Fields.FirstOrDefault(f => f.Key == "سن_فرد")
                          ?? Fields[0];

            await RefreshHighlightsAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری صفحهٔ آمار ناموفق بود.", ex);
            StatusMessage = "بارگذاری آمار با خطا مواجه شد. جزئیات در فایل لاگ ثبت شد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>اجرای تحلیل بر اساس انتخاب‌های جاری.</summary>
    private async Task AnalyzeAsync()
    {
        if (SelectedFieldX is null) return;

        try
        {
            var res = await _stats.AnalyzeAsync(
                SelectedFieldX.Key,
                SelectedFieldY?.Key,
                HistogramBins,
                SelectedFilterField?.Key,
                SelectedFilterValue);

            Result = res;
            AnalysisTitle = BuildTitle(res.Title);
            HasData = res.Categories.Count > 0 || res.Points.Count > 0;

            // جدول داده زیر نمودار
            TableRows.Clear();
            foreach (var c in res.Categories) TableRows.Add(c);

            SummaryText = BuildSummary(res);
            RaiseRedraw();
        }
        catch (Exception ex)
        {
            _logger.Error("تحلیل داده ناموفق بود.", ex);
            StatusMessage = "تحلیل با خطا مواجه شد.";
        }
    }

    /// <summary>بارگذاری مقادیر فیلد فیلتر (مثلاً فهرست پروژه‌ها).</summary>
    private async Task ReloadFilterValuesAsync()
    {
        try
        {
            FilterValues.Clear();
            FilterValues.Add("همه");

            if (SelectedFilterField is not null)
            {
                var values = await _stats.GetDistinctValuesAsync(SelectedFilterField.Key);
                foreach (var v in values) FilterValues.Add(v);
            }

            // انتخاب «همه» به‌صورت پیش‌فرض؛ این خودش تحلیل را تازه می‌کند.
            SelectedFilterValue = "همه";
        }
        catch (Exception ex)
        {
            _logger.Warn($"بارگذاری مقادیر فیلتر ناموفق بود: {ex.Message}");
        }
    }

    /// <summary>تازه‌سازی هم‌زمان آمارهای بالا و نمودار پایین پس از تغییر فیلتر.</summary>
    private async Task RefreshAllAsync()
    {
        await RefreshHighlightsAsync();
        await AnalyzeAsync();
    }

    /// <summary>محاسبهٔ دوبارهٔ کارت‌های آمار با درنظر گرفتن فیلتر جاری.</summary>
    private async Task RefreshHighlightsAsync()
    {
        try
        {
            var highlights = await _stats.GetHighlightsAsync(
                SelectedFilterField?.Key, SelectedFilterValue);

            Highlights.Clear();
            foreach (var (label, value) in highlights)
                Highlights.Add(new Highlight { Label = label, Value = value });
        }
        catch (Exception ex)
        {
            _logger.Warn($"محاسبهٔ آمارهای کلیدی ناموفق بود: {ex.Message}");
        }
    }

    /// <summary>افزودن نام فیلتر فعال به عنوان تحلیل، تا مشخص باشد آمار مربوط به چیست.</summary>
    private string BuildTitle(string baseTitle)
    {
        if (string.IsNullOrWhiteSpace(SelectedFilterValue) || SelectedFilterValue == "همه")
            return baseTitle;

        return $"{baseTitle}  —  فقط {SelectedFilterField?.DisplayName}: {SelectedFilterValue}";
    }

    /// <summary>میان‌برهای آماده برای تحلیل‌های پرکاربرد.</summary>
    [RelayCommand]
    private void Preset(string? which)
    {
        FieldOption? Find(params string[] keys)
        {
            foreach (var k in keys)
            {
                var exact = Fields.FirstOrDefault(f => f.Key == k);
                if (exact is not null) return exact;
            }
            foreach (var k in keys)
            {
                var partial = Fields.FirstOrDefault(f => f.Key.Contains(k, StringComparison.Ordinal));
                if (partial is not null) return partial;
            }
            return null;
        }

        switch (which)
        {
            case "province":
                SelectedFieldY = null;
                SelectedFieldX = Find("استان_محل_سکونت", "استان");
                SelectedChartType = "افقی";
                break;

            case "age":
                SelectedFieldY = null;
                SelectedFieldX = Find("سن_فرد", "سن");
                SelectedChartType = "میله‌ای";
                break;

            case "education":
                SelectedFieldY = null;
                SelectedFieldX = Find("مدرک_تحصیلی", "مدرک");
                SelectedChartType = "افقی";
                break;

            case "marital":
                SelectedFieldY = null;
                SelectedFieldX = Find("وضعیت_تاهل", "تاهل");
                SelectedChartType = "دایره‌ای";
                break;

            case "membership":
                SelectedFieldY = null;
                SelectedFieldX = Find("نوع_عضویت", "عضویت");
                SelectedChartType = "دایره‌ای";
                break;

            case "ageByProvince":
                SelectedFieldX = Find("سن_فرد", "سن");
                SelectedFieldY = Find("استان_محل_سکونت", "استان");
                SelectedChartType = "افقی";
                break;
        }
    }

    /// <summary>ساخت توضیح فیلتر فعال برای درج در گزارش (خالی اگر فیلتری اعمال نشده).</summary>
    private string? BuildFilterDescription()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilterValue) || SelectedFilterValue == "همه")
            return null;
        return $"فقط {SelectedFilterField?.DisplayName}: {SelectedFilterValue}";
    }

    /// <summary>خروجی Excel از نمودار و جدول جاری، با چیدمان کاملاً راست‌چین و تصویر نمودار جاسازی‌شده.</summary>
    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (Result is null || Result.Categories.Count == 0)
        {
            ShowInfo("داده‌ای برای خروجی وجود ندارد.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "ذخیرهٔ گزارش Excel",
            Filter = "فایل Excel|*.xlsx",
            FileName = SafeFileName(AnalysisTitle) + ".xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            IsBusy = true;
            var request = BuildExportRequest(dialog.FileName);
            var result = await _reportExport.ExportExcelAsync(request);

            StatusMessage = result.IsSuccess
                ? $"فایل Excel ذخیره شد: {result.Value}"
                : result.Error;
        }
        catch (Exception ex)
        {
            _logger.Error("خروجی Excel ناموفق بود.", ex);
            StatusMessage = "ساخت فایل Excel با خطا مواجه شد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>خروجی PDF از نمودار و جدول جاری (از طریق ساخت یک سند Word و تبدیل به PDF).</summary>
    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (Result is null || Result.Categories.Count == 0)
        {
            ShowInfo("داده‌ای برای خروجی وجود ندارد.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "ذخیرهٔ گزارش PDF",
            Filter = "فایل PDF|*.pdf",
            FileName = SafeFileName(AnalysisTitle) + ".pdf"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            IsBusy = true;
            StatusMessage = "در حال ساخت PDF...";

            var request = BuildExportRequest(dialog.FileName);
            var result = await _reportExport.ExportPdfAsync(request);

            StatusMessage = result.IsSuccess
                ? $"فایل PDF ذخیره شد: {result.Value}"
                : result.Error;
        }
        catch (Exception ex)
        {
            _logger.Error("خروجی PDF ناموفق بود.", ex);
            StatusMessage = "ساخت فایل PDF با خطا مواجه شد. برای PDF باید Word یا LibreOffice نصب باشد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>ساخت درخواست خروجی مشترک بین Excel و PDF (شامل تصویر نمودار جاری).</summary>
    private ReportExportRequest BuildExportRequest(string outputPath)
    {
        var request = new ReportExportRequest
        {
            Title = AnalysisTitle,
            FilterDescription = BuildFilterDescription(),
            Numeric = Result!.Numeric,
            ChartImagePng = CaptureChartImage?.Invoke(),
            OutputPath = outputPath
        };
        foreach (var c in Result.Categories) request.Categories.Add(c);
        return request;
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "گزارش" : name;
    }

    private void ShowInfo(string message)
        => System.Windows.MessageBox.Show(message, "خروجی", System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information, System.Windows.MessageBoxResult.OK,
            System.Windows.MessageBoxOptions.RtlReading | System.Windows.MessageBoxOptions.RightAlign);

    /// <summary>ذخیرهٔ داده‌های نمودار جاری در یک فایل CSV (قابل باز شدن در Excel).</summary>
    [RelayCommand]
    private void ExportCsv()
    {
        if (Result is null || Result.Categories.Count == 0)
        {
            System.Windows.MessageBox.Show("داده‌ای برای ذخیره وجود ندارد.", "خروجی",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information,
                System.Windows.MessageBoxResult.OK,
                System.Windows.MessageBoxOptions.RtlReading | System.Windows.MessageBoxOptions.RightAlign);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "ذخیرهٔ داده‌های نمودار",
            Filter = "فایل CSV|*.csv",
            FileName = "آمار.csv"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("عنوان,تعداد,درصد");
            foreach (var c in Result.Categories)
            {
                var label = c.Label.Replace(",", "،");
                sb.AppendLine($"{label},{c.Count},{Math.Round(c.Percent, 1)}");
            }

            // UTF-8 با BOM تا Excel فارسی را درست نمایش دهد
            System.IO.File.WriteAllText(dialog.FileName, sb.ToString(),
                new System.Text.UTF8Encoding(true));

            StatusMessage = $"داده‌ها ذخیره شد: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            _logger.Error("ذخیرهٔ CSV ناموفق بود.", ex);
            StatusMessage = "ذخیرهٔ فایل با خطا مواجه شد.";
        }
    }

    /// <summary>ساخت متن خلاصهٔ زیر نمودار.</summary>
    private static string BuildSummary(AnalysisResult r)
    {
        var parts = new List<string>
        {
            $"تعداد رکوردهای معتبر: {Fa(r.ValidCount)}"
        };

        if (r.MissingCount > 0)
            parts.Add($"بدون مقدار: {Fa(r.MissingCount)}");

        if (r.Numeric is { } n)
        {
            parts.Add($"میانگین: {Fa(Math.Round(n.Average, 1))}");
            parts.Add($"میانه: {Fa(Math.Round(n.Median, 1))}");
            parts.Add($"کمترین: {Fa(n.Min)}");
            parts.Add($"بیشترین: {Fa(n.Max)}");
            if (n.StdDev > 0) parts.Add($"انحراف معیار: {Fa(Math.Round(n.StdDev, 2))}");
        }

        if (r.Correlation is { } c)
        {
            var strength = Math.Abs(c) switch
            {
                >= 0.7 => "قوی",
                >= 0.4 => "متوسط",
                >= 0.2 => "ضعیف",
                _ => "بسیار ضعیف"
            };
            var direction = c >= 0 ? "مستقیم" : "معکوس";
            parts.Add($"همبستگی: {Fa(Math.Round(c, 2))} ({strength}، {direction})");
        }

        return string.Join("   |   ", parts);
    }

    private static string Fa(double v)
    {
        var s = Math.Abs(v - Math.Round(v)) < 1e-9
            ? ((long)Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return Domain.Common.PersianDate.ToPersianDigits(s);
    }

    /// <summary>پاک کردن فیلد دوم برای بازگشت به تحلیل تک‌متغیره.</summary>
    [RelayCommand]
    private void ClearSecondField() => SelectedFieldY = null;
}
