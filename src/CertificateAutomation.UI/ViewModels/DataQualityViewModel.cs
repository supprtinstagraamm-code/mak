using System.Collections.ObjectModel;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>یک نوع مشکل کیفیت داده، به‌همراه چند نمونه برای بررسی سریع.</summary>
public class DataQualityIssue
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Severity { get; set; } = "warning"; // warning | error
    public List<string> Examples { get; } = new();

    public string ExamplesText => Examples.Count == 0
        ? string.Empty
        : "نمونه: " + string.Join("، ", Examples) + (Count > Examples.Count ? " و …" : "");
}

/// <summary>
/// بررسی سلامت دادهٔ پرسنل: کد ملی نامعتبر یا تکراری، فیلدهای مهم خالی‌مانده و غیره.
/// با ۵۲ ستون یا بیشتر، پیدا کردن دستی این نقص‌ها عملاً غیرممکن است؛ این صفحه خودکار پیدا می‌کند.
/// </summary>
public partial class DataQualityViewModel : PageViewModelBase
{
    private readonly IStatisticsService _stats;
    private readonly IAppLogger _logger;

    /// <summary>فیلدهایی که «مهم» در نظر گرفته می‌شوند و خالی بودنشان هشدار می‌دهد.</summary>
    private static readonly (string Key, string Label)[] ImportantFields =
    {
        ("نام", "نام"), ("نام_خانوادگی", "نام خانوادگی"), ("کد_ملی", "کد ملی"),
        ("سمت", "سمت"), ("واحد", "واحد")
    };

    public DataQualityViewModel(IStatisticsService stats, IAppLogger logger)
    {
        _stats = stats;
        _logger = logger;
    }

    public override string Title => "سلامت داده‌ها";
    public override string Subtitle => "بررسی خودکار کد ملی، تکراری‌ها و فیلدهای مهم خالی‌مانده";

    public ObservableCollection<DataQualityIssue> Issues { get; } = new();

    private int _totalRecords;
    public int TotalRecords => _totalRecords;
    public int TotalIssues => Issues.Sum(i => i.Count);
    public bool HasNoIssues => Issues.Count == 0 && !IsBusy;

    public override Task OnNavigatedToAsync() => ScanAsync();

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            Issues.Clear();

            var rows = await _stats.GetRowsAsync();
            _totalRecords = rows.Count;
            OnPropertyChanged(nameof(TotalRecords));

            string Name(Dictionary<string, string> v) =>
                ($"{v.GetValueOrDefault("نام")} {v.GetValueOrDefault("نام_خانوادگی")}").Trim() is { Length: > 0 } n
                    ? n : "(بدون نام)";

            // ۱) کد ملی خالی
            var missingNid = rows.Where(r => string.IsNullOrWhiteSpace(r.Values.GetValueOrDefault("کد_ملی"))).ToList();
            if (missingNid.Count > 0)
                AddIssue("کد ملی ثبت نشده", "این افراد در فایل Excel هیچ کد ملی ندارند.",
                    missingNid.Select(r => Name(r.Values)), "warning");

            // ۲) کد ملی نامعتبر (فرمت/رقم کنترلی اشتباه)
            var invalidNid = rows.Where(r =>
            {
                var v = r.Values.GetValueOrDefault("کد_ملی");
                return !string.IsNullOrWhiteSpace(v) && !IranianNationalId.IsValid(v);
            }).ToList();
            if (invalidNid.Count > 0)
                AddIssue("کد ملی نامعتبر", "رقم کنترلی یا فرمت این کدهای ملی درست به‌نظر نمی‌رسد.",
                    invalidNid.Select(r => $"{Name(r.Values)} ({r.Values.GetValueOrDefault("کد_ملی")})"), "error");

            // ۳) کد ملی تکراری
            var dupGroups = rows.Where(r => !string.IsNullOrWhiteSpace(r.Values.GetValueOrDefault("کد_ملی")))
                .GroupBy(r => r.Values["کد_ملی"])
                .Where(g => g.Count() > 1)
                .ToList();
            if (dupGroups.Count > 0)
            {
                var totalDup = dupGroups.Sum(g => g.Count());
                var examples = dupGroups.Select(g => $"{g.Key} ({g.Count()} نفر)");
                AddIssueRaw("کد ملی تکراری", "چند نفر کد ملی یکسان دارند — احتمال ثبت دوبارهٔ یک نفر.",
                    totalDup, examples, "error");
            }

            // ۴) فیلدهای مهم خالی — فقط اگر «بعضی» رکوردها آن را دارند و بعضی ندارند.
            // اگر یک فیلد در همهٔ رکوردها خالی باشد، یعنی آن سازمان اصلاً این ستون را ثبت نمی‌کند
            // (نه یک نقص داده)، پس هشدار داده نمی‌شود.
            foreach (var (key, label) in ImportantFields)
            {
                var missing = rows.Where(r => string.IsNullOrWhiteSpace(r.Values.GetValueOrDefault(key))).ToList();
                if (missing.Count > 0 && missing.Count < rows.Count)
                    AddIssue($"«{label}» خالی است", $"این افراد ستون «{label}» را در Excel ندارند.",
                        missing.Select(r => Name(r.Values)), "warning");
            }

            StatusMessage = null;
            OnPropertyChanged(nameof(TotalIssues));
            OnPropertyChanged(nameof(HasNoIssues));
        }
        catch (Exception ex)
        {
            _logger.Error("بررسی سلامت داده‌ها ناموفق بود.", ex);
            StatusMessage = "بررسی با خطا مواجه شد.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasNoIssues));
        }
    }

    private void AddIssue(string title, string description, IEnumerable<string> names, string severity)
    {
        var list = names.ToList();
        AddIssueRaw(title, description, list.Count, list, severity);
    }

    private void AddIssueRaw(string title, string description, int count, IEnumerable<string> examples, string severity)
    {
        var issue = new DataQualityIssue { Title = title, Description = description, Count = count, Severity = severity };
        foreach (var e in examples.Take(5)) issue.Examples.Add(e);
        Issues.Add(issue);
    }
}
