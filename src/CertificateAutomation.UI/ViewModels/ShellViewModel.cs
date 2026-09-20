using System.Linq;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>
/// ViewModel پنجره اصلی. مسئول ناوبری بین صفحه‌ها و تعویض تم.
/// صفحه‌ها یک‌بار ساخته و نگهداری می‌شوند تا جابه‌جایی سریع باشد.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly ThemeManager _theme;
    private readonly ISettingsService _settings;
    private readonly IUserSession _session;

    private readonly DashboardViewModel _dashboard;
    private readonly EmployeesViewModel _employees;
    private readonly IssueLetterViewModel _issue;
    private readonly HistoryViewModel _history;
    private readonly TemplatesViewModel _templates;
    private readonly SettingsViewModel _settingsPage;
    private readonly StatisticsViewModel _statistics;
    private readonly RemindersViewModel _reminders;
    private readonly IReminderService _reminderService;
    private readonly AdvancedSearchViewModel _advancedSearch;
    private readonly ProjectComparisonViewModel _projectComparison;
    private readonly ActivityLogViewModel _activityLog;
    private readonly DataQualityViewModel _dataQuality;

    public ShellViewModel(
        ThemeManager theme,
        ISettingsService settings,
        IUserSession session,
        DashboardViewModel dashboard,
        EmployeesViewModel employees,
        IssueLetterViewModel issue,
        HistoryViewModel history,
        TemplatesViewModel templates,
        SettingsViewModel settingsPage,
        StatisticsViewModel statistics,
        RemindersViewModel reminders,
        IReminderService reminderService,
        AdvancedSearchViewModel advancedSearch,
        ProjectComparisonViewModel projectComparison,
        ActivityLogViewModel activityLog,
        DataQualityViewModel dataQuality)
    {
        _theme = theme;
        _settings = settings;
        _session = session;
        _dashboard = dashboard;
        _employees = employees;
        _issue = issue;
        _history = history;
        _templates = templates;
        _settingsPage = settingsPage;
        _statistics = statistics;
        _reminders = reminders;
        _reminderService = reminderService;
        _advancedSearch = advancedSearch;
        _projectComparison = projectComparison;
        _activityLog = activityLog;
        _dataQuality = dataQuality;

        // وقتی مدیر تنظیمات (از جمله لوگو) را ذخیره کرد، نوار کناری فوری به‌روز شود.
        _settingsPage.OnSettingsSaved = RefreshHeader;

        _currentPage = dashboard;
        IsDark = theme.IsDark;
    }

    [ObservableProperty] private PageViewModelBase _currentPage;
    [ObservableProperty] private bool _isDark;

    /// <summary>تعداد یادآورهایی که روزشان رسیده یا گذشته و هنوز این ماه انجام نشده‌اند.</summary>
    [ObservableProperty] private int _dueReminderCount;

    public string CompanyName => _settings.Get(SettingKeys.CompanyName, "شرکت شما");

    public string AppVersion => "نسخه ۱٫۰٫۰";

    /// <summary>تصویر لوگو برای نمایش در بالای نوار کناری.</summary>
    [ObservableProperty] private System.Windows.Media.ImageSource? _logoImage;

    /// <summary>آیا لوگویی برای نمایش وجود دارد.</summary>
    public bool HasLogo => LogoImage is not null;

    partial void OnLogoImageChanged(System.Windows.Media.ImageSource? value)
        => OnPropertyChanged(nameof(HasLogo));

    /// <summary>
    /// بارگذاری لوگو از مسیر تنظیم‌شده. تصویر به‌صورت کامل در حافظه خوانده می‌شود
    /// (BitmapCacheOption.OnLoad) تا فایل قفل نماند و مدیر بتواند بعداً آن را عوض کند.
    /// این متد از بخش تنظیمات هم پس از تغییر لوگو صدا زده می‌شود تا فوری اعمال شود.
    /// </summary>
    public void RefreshHeader()
    {
        OnPropertyChanged(nameof(CompanyName));
        OnPropertyChanged(nameof(CurrentUserName));

        var path = _settings.Get(SettingKeys.LogoPath);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            LogoImage = null;
            return;
        }

        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            LogoImage = bitmap;
        }
        catch
        {
            // اگر فایل خراب بود یا فرمت پشتیبانی نمی‌شد، لوگو نمایش داده نمی‌شود (بدون خطا).
            LogoImage = null;
        }
    }

    /// <summary>بارگذاری اولیه پس از نمایش پنجره.</summary>
    public async Task InitializeAsync()
    {
        RefreshHeader();
        await RefreshReminderBadgeAsync();
        await CurrentPage.OnNavigatedToAsync();
    }

    /// <summary>
    /// به‌روزرسانی نشان تعداد یادآورهای «رسیده و انجام‌نشده» روی آیتم نوار کناری.
    /// از بیرون (مثلاً پس از تیک زدن یک یادآور، یا با تایمر دوره‌ای) هم صدا زده می‌شود.
    /// </summary>
    public async Task RefreshReminderBadgeAsync()
    {
        try
        {
            var list = await _reminderService.GetChecklistAsync();
            var today = DateTime.Now;

            DueReminderCount = list.Count(r =>
                r.IsActive && !r.IsCompletedThisMonth &&
                Math.Min(r.DayOfMonth, DateTime.DaysInMonth(today.Year, today.Month)) <= today.Day);
        }
        catch
        {
            // نشان صرفاً یک کمک بصری است؛ خطا نباید مانع بالا آمدن برنامه شود.
        }
    }

    [RelayCommand]
    private async Task NavigateAsync(string? key)
    {
        _session.Touch();

        PageViewModelBase target = key switch
        {
            "employees" => _employees,
            "issue" => _issue,
            "history" => _history,
            "templates" => _templates,
            "statistics" => _statistics,
            "reminders" => _reminders,
            "advancedsearch" => _advancedSearch,
            "projectcomparison" => _projectComparison,
            "activitylog" => _activityLog,
            "dataquality" => _dataQuality,
            "settings" => _settingsPage,
            _ => _dashboard
        };

        if (ReferenceEquals(target, CurrentPage)) return;

        CurrentPage = target;
        await target.OnNavigatedToAsync();
        await RefreshReminderBadgeAsync();
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        await _theme.ToggleAsync();
        IsDark = _theme.IsDark;
    }

    /// <summary>نام کاربر جاری برای نمایش در نوار بالا.</summary>
    public string CurrentUserName =>
        _session.IsAdmin ? $"مدیر: {_session.UserName}" : "کاربر عادی";

    /// <summary>
    /// از سوی ShellWindow تزریق می‌شود تا خروج واقعی (نه فقط مخفی‌شدن در سینی سیستم)
    /// از طریق پنجره انجام شود.
    /// </summary>
    public Action? RequestExit { get; set; }

    /// <summary>بستن برنامه، با گرفتن تأیید از کاربر.</summary>
    [RelayCommand]
    private void Exit()
    {
        var answer = System.Windows.MessageBox.Show(
            "از برنامه خارج می‌شوید؟ (یادآورهای ماهانه دیگر بررسی نمی‌شوند تا دفعهٔ بعد که برنامه را باز کنید.)",
            "خروج",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No,
            System.Windows.MessageBoxOptions.RtlReading | System.Windows.MessageBoxOptions.RightAlign);

        if (answer != System.Windows.MessageBoxResult.Yes) return;

        if (RequestExit is not null) RequestExit.Invoke();
        else System.Windows.Application.Current?.Shutdown();
    }
}
