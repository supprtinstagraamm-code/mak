using System.Collections.ObjectModel;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>داشبورد: آمار امروز، این ماه، آخرین شماره و آخرین فعالیت‌ها.</summary>
public partial class DashboardViewModel : PageViewModelBase
{
    private readonly ILetterRepository _letters;
    private readonly ISettingsService _settings;
    private readonly IReminderService _reminders;
    private readonly IStatisticsService _stats;
    private readonly IAppLogger _logger;

    public DashboardViewModel(
        ILetterRepository letters,
        ISettingsService settings,
        IReminderService reminders,
        IStatisticsService stats,
        IAppLogger logger)
    {
        _letters = letters;
        _settings = settings;
        _reminders = reminders;
        _stats = stats;
        _logger = logger;
    }

    public override string Title => "داشبورد";
    public override string Subtitle => "نمای کلی فعالیت‌های سامانه";

    [ObservableProperty] private int _todayCount;
    [ObservableProperty] private int _monthCount;
    [ObservableProperty] private int _yearCount;
    [ObservableProperty] private int _employeeCount;
    [ObservableProperty] private string _lastLetterNumber = "—";

    // ویجت یادآورهای این ماه
    [ObservableProperty] private int _pendingReminderCount;
    [ObservableProperty] private string? _nextReminderText;

    // ویجت آمار سریع پرسنل (از همان صفحهٔ آمار)
    public ObservableCollection<Highlight> QuickStats { get; } = new();

    public ObservableCollection<Letter> RecentLetters { get; } = new();

    public string CompanyName => _settings.Get(SettingKeys.CompanyName, "شرکت شما");

    public override Task OnNavigatedToAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            var stats = await _letters.GetDashboardStatsAsync();

            TodayCount = stats.TodayCount;
            MonthCount = stats.MonthCount;
            YearCount = stats.YearCount;
            EmployeeCount = stats.EmployeeCount;
            LastLetterNumber = string.IsNullOrWhiteSpace(stats.LastLetterNumber) ? "—" : stats.LastLetterNumber!;

            RecentLetters.Clear();
            foreach (var letter in stats.RecentLetters)
                RecentLetters.Add(letter);

            await LoadReminderWidgetAsync();
            await LoadQuickStatsAsync();

            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری آمار داشبورد ناموفق بود.", ex);
            StatusMessage = "خواندن آمار با خطا مواجه شد. جزئیات در فایل لاگ ثبت شد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>یادآورهایی که این ماه هنوز انجام نشده‌اند، برای نمایش سریع در داشبورد.</summary>
    private async Task LoadReminderWidgetAsync()
    {
        try
        {
            var list = await _reminders.GetChecklistAsync();
            var pending = list.Where(r => r.IsActive && !r.IsCompletedThisMonth)
                              .OrderBy(r => r.DayOfMonth)
                              .ToList();

            PendingReminderCount = pending.Count;
            NextReminderText = pending.Count == 0
                ? null
                : $"روز {pending[0].DayOfMonth}: {pending[0].Title}" +
                  (pending.Count > 1 ? $"   (و {pending.Count - 1} مورد دیگر)" : "");
        }
        catch (Exception ex)
        {
            _logger.Warn($"بارگذاری ویجت یادآورها ناموفق بود: {ex.Message}");
        }
    }

    /// <summary>چند آمار سریع پرسنل، از همان موتور صفحهٔ آمار.</summary>
    private async Task LoadQuickStatsAsync()
    {
        try
        {
            var highlights = await _stats.GetHighlightsAsync();
            QuickStats.Clear();
            foreach (var (label, value) in highlights.Take(4))
                QuickStats.Add(new Highlight { Label = label, Value = value });
        }
        catch (Exception ex)
        {
            _logger.Warn($"بارگذاری آمار سریع داشبورد ناموفق بود: {ex.Message}");
        }
    }
}
