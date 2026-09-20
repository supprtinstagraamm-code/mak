using System.Collections.ObjectModel;
using System.Globalization;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>یک شرط جستجو، مثلاً «پروژه برابر است با خدمات دریایی».</summary>
public partial class SearchCondition : ObservableObject
{
    public ObservableCollection<FieldOption> AvailableFields { get; }
    public ObservableCollection<string> Operators { get; } = new()
    {
        "برابر است با", "شامل می‌شود", "بزرگ‌تر از", "کوچک‌تر از"
    };

    [ObservableProperty] private FieldOption? _field;
    [ObservableProperty] private string _operator = "برابر است با";
    [ObservableProperty] private string _value = string.Empty;

    public SearchCondition(ObservableCollection<FieldOption> availableFields)
        => AvailableFields = availableFields;

    /// <summary>آیا یک ردیف داده با این شرط مطابقت دارد.</summary>
    public bool Matches(AnalyticsRow row)
    {
        if (Field is null || string.IsNullOrWhiteSpace(Value)) return true;

        row.Values.TryGetValue(Field.Key, out var raw);
        raw ??= string.Empty;

        return Operator switch
        {
            "شامل می‌شود" => raw.Contains(Value, StringComparison.OrdinalIgnoreCase),
            "بزرگ‌تر از" => TryCompareNumeric(raw, Value, out var c1) && c1 > 0,
            "کوچک‌تر از" => TryCompareNumeric(raw, Value, out var c2) && c2 < 0,
            _ => string.Equals(raw.Trim(), Value.Trim(), StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool TryCompareNumeric(string a, string b, out int result)
    {
        result = 0;
        var na = CertificateAutomation.Domain.Common.PersianDate.ToEnglishDigits(a);
        var nb = CertificateAutomation.Domain.Common.PersianDate.ToEnglishDigits(b);
        if (!double.TryParse(na, NumberStyles.Any, CultureInfo.InvariantCulture, out var da)) return false;
        if (!double.TryParse(nb, NumberStyles.Any, CultureInfo.InvariantCulture, out var db)) return false;
        result = da.CompareTo(db);
        return true;
    }
}

/// <summary>یک ردیف نتیجهٔ جستجو، برای نمایش سادهٔ جدول (بدون ستون‌های پویا).</summary>
public class SearchResultRow
{
    public string FullName { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public string? Position { get; set; }
    public string? Unit { get; set; }

    /// <summary>مقادیر فیلدهایی که در شرط‌های جستجو استفاده شده‌اند، برای نمایش سریع.</summary>
    public string MatchedFieldsText { get; set; } = string.Empty;
}

/// <summary>
/// جستجوی چندشرطی روی همهٔ ستون‌های پرسنل (نه فقط چند فیلد ثابت).
/// مثال: «پروژه = خدمات دریایی» و «سن > ۳۰» هم‌زمان.
/// </summary>
public partial class AdvancedSearchViewModel : PageViewModelBase
{
    private readonly IStatisticsService _stats;
    private readonly IAppLogger _logger;

    private IReadOnlyList<AnalyticsRow> _allRows = Array.Empty<AnalyticsRow>();

    public AdvancedSearchViewModel(IStatisticsService stats, IAppLogger logger)
    {
        _stats = stats;
        _logger = logger;
    }

    public override string Title => "جستجوی پیشرفته";
    public override string Subtitle => "ترکیب چند شرط هم‌زمان روی هر ستون از فایل Excel";

    public ObservableCollection<FieldOption> AvailableFields { get; } = new();
    public ObservableCollection<SearchCondition> Conditions { get; } = new();
    public ObservableCollection<SearchResultRow> Results { get; } = new();

    [ObservableProperty] private int _resultCount;

    public override async Task OnNavigatedToAsync()
    {
        try
        {
            IsBusy = true;
            var fields = await _stats.GetAvailableFieldsAsync();
            AvailableFields.Clear();
            foreach (var (key, name) in fields)
                AvailableFields.Add(new FieldOption { Key = key, DisplayName = name });

            _allRows = await _stats.GetRowsAsync();

            if (Conditions.Count == 0) AddCondition();

            StatusMessage = "شرط(ها) را تنظیم کنید و «جستجو» را بزنید.";
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری صفحهٔ جستجوی پیشرفته ناموفق بود.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddCondition() => Conditions.Add(new SearchCondition(AvailableFields));

    [RelayCommand]
    private void RemoveCondition(SearchCondition? condition)
    {
        if (condition is not null) Conditions.Remove(condition);
    }

    [RelayCommand]
    private void Search()
    {
        var active = Conditions.Where(c => c.Field is not null && !string.IsNullOrWhiteSpace(c.Value)).ToList();

        var matches = active.Count == 0
            ? _allRows
            : _allRows.Where(row => active.All(c => c.Matches(row)));

        Results.Clear();
        foreach (var row in matches.Take(500))
        {
            var v = row.Values;
            var name = $"{v.GetValueOrDefault("نام")} {v.GetValueOrDefault("نام_خانوادگی")}".Trim();

            var matchedText = active.Count == 0
                ? string.Empty
                : string.Join("  |  ", active.Select(c =>
                    $"{c.Field!.DisplayName}: {v.GetValueOrDefault(c.Field.Key, "-")}"));

            Results.Add(new SearchResultRow
            {
                FullName = string.IsNullOrWhiteSpace(name) ? "(بدون نام)" : name,
                NationalId = v.GetValueOrDefault("کد_ملی"),
                Position = v.GetValueOrDefault("سمت"),
                Unit = v.GetValueOrDefault("واحد"),
                MatchedFieldsText = matchedText
            });
        }

        ResultCount = Results.Count;
        StatusMessage = ResultCount == 0 ? "موردی با این شرط‌ها یافت نشد." : null;
    }
}
