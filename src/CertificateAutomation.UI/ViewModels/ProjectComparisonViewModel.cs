using System.Collections.ObjectModel;
using System.Globalization;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>یک سطر جدول مقایسه: یک گروه (مثلاً یک پروژه) با آمارهایش.</summary>
public class ComparisonRow
{
    public string GroupLabel { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percent { get; set; }
    public string? MetricValue { get; set; }
    public string? TopCategoryValue { get; set; }
}

/// <summary>
/// مقایسهٔ گروه‌ها (مثلاً پروژه‌ها یا واحدها) در یک جدول: تعداد نفرات، میانگین یک فیلد عددی
/// (مثل سن) و پرتکرارترین مقدار یک فیلد دسته‌ای (مثل مدرک تحصیلی) در هر گروه.
/// </summary>
public partial class ProjectComparisonViewModel : PageViewModelBase
{
    private readonly IStatisticsService _stats;
    private readonly IAppLogger _logger;

    private IReadOnlyList<AnalyticsRow> _allRows = Array.Empty<AnalyticsRow>();

    public ProjectComparisonViewModel(IStatisticsService stats, IAppLogger logger)
    {
        _stats = stats;
        _logger = logger;
    }

    public override string Title => "مقایسهٔ گروه‌ها";
    public override string Subtitle => "مقایسهٔ پروژه‌ها، واحدها یا هر فیلد دیگر در یک جدول";

    public ObservableCollection<FieldOption> Fields { get; } = new();
    public ObservableCollection<ComparisonRow> Rows { get; } = new();

    [ObservableProperty] private FieldOption? _groupField;
    [ObservableProperty] private FieldOption? _numericMetricField;
    [ObservableProperty] private FieldOption? _categoryMetricField;

    partial void OnGroupFieldChanged(FieldOption? value) => Compute();
    partial void OnNumericMetricFieldChanged(FieldOption? value) => Compute();
    partial void OnCategoryMetricFieldChanged(FieldOption? value) => Compute();

    public override async Task OnNavigatedToAsync()
    {
        try
        {
            IsBusy = true;
            var fields = await _stats.GetAvailableFieldsAsync();
            Fields.Clear();
            foreach (var (key, name) in fields)
                Fields.Add(new FieldOption { Key = key, DisplayName = name });

            _allRows = await _stats.GetRowsAsync();

            GroupField = Fields.FirstOrDefault(f => f.Key.Contains("پروژه", StringComparison.Ordinal))
                      ?? Fields.FirstOrDefault(f => f.Key.Contains("واحد", StringComparison.Ordinal))
                      ?? Fields.FirstOrDefault();

            NumericMetricField = Fields.FirstOrDefault(f => f.Key == "سن_فرد");
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری صفحهٔ مقایسهٔ گروه‌ها ناموفق بود.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Compute()
    {
        Rows.Clear();
        if (GroupField is null || _allRows.Count == 0) return;

        var groupKey = GroupField.Key;
        var total = _allRows.Count(r => r.Values.ContainsKey(groupKey));

        var groups = _allRows
            .Where(r => r.Values.ContainsKey(groupKey))
            .GroupBy(r => r.Values[groupKey])
            .OrderByDescending(g => g.Count());

        foreach (var g in groups)
        {
            var row = new ComparisonRow
            {
                GroupLabel = g.Key,
                Count = g.Count(),
                Percent = total == 0 ? 0 : Math.Round(g.Count() * 100.0 / total, 1)
            };

            if (NumericMetricField is not null)
            {
                var values = g.Select(r => r.Values.GetValueOrDefault(NumericMetricField.Key))
                              .Where(v => !string.IsNullOrWhiteSpace(v))
                              .Select(TryParseNumber)
                              .Where(v => v.HasValue)
                              .Select(v => v!.Value)
                              .ToList();

                if (values.Count > 0)
                    row.MetricValue = $"{Math.Round(values.Average(), 1)}  (میانگین {NumericMetricField.DisplayName})";
            }

            if (CategoryMetricField is not null)
            {
                var top = g.Where(r => r.Values.ContainsKey(CategoryMetricField.Key))
                           .GroupBy(r => r.Values[CategoryMetricField.Key])
                           .OrderByDescending(x => x.Count())
                           .FirstOrDefault();

                if (top is not null)
                    row.TopCategoryValue = $"{top.Key} ({top.Count()} نفر)";
            }

            Rows.Add(row);
        }

        StatusMessage = Rows.Count == 0 ? "داده‌ای برای این فیلد یافت نشد." : null;
    }

    private static double? TryParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = CertificateAutomation.Domain.Common.PersianDate.ToEnglishDigits(raw.Trim()).Replace(",", "");
        if (s.Contains('/') || s.Contains('-')) return null;
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
