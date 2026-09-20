using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>
/// ویزارد سه‌مرحله‌ای صدور نامه:
/// مرحله ۱: انتخاب شخص (با جستجوی زنده)
/// مرحله ۲: انتخاب قالب
/// مرحله ۳: پیش‌نمایش فیلدها و صدور
///
/// نکتهٔ مهم: تا فشردن دکمهٔ نهایی «صدور»، هیچ شماره‌ای مصرف نمی‌شود.
/// </summary>
public partial class IssueLetterViewModel : PageViewModelBase
{
    private readonly IEmployeeRepository _employees;
    private readonly ITemplateRepository _templates;
    private readonly ITemplateScanner _scanner;
    private readonly ILetterIssueService _issueService;
    private readonly INumberGenerator _numbers;
    private readonly IPrintService _printService;
    private readonly ISettingsService _settings;
    private readonly IStatisticsService _stats;
    private readonly IAppLogger _logger;

    private CancellationTokenSource? _searchCts;

    public IssueLetterViewModel(
        IEmployeeRepository employees,
        ITemplateRepository templates,
        ITemplateScanner scanner,
        ILetterIssueService issueService,
        INumberGenerator numbers,
        IPrintService printService,
        ISettingsService settings,
        IStatisticsService stats,
        IAppLogger logger)
    {
        _employees = employees;
        _templates = templates;
        _scanner = scanner;
        _issueService = issueService;
        _numbers = numbers;
        _printService = printService;
        _settings = settings;
        _stats = stats;
        _logger = logger;
    }

    public override string Title => "صدور نامه";
    public override string Subtitle => "انتخاب شخص، انتخاب قالب، پیش‌نمایش و صدور";

    // مرحلهٔ جاری: 1، 2 یا 3
    [ObservableProperty] private int _currentStep = 1;

    // مرحله ۱
    public ObservableCollection<Employee> Employees { get; } = new();
    [ObservableProperty] private string _employeeSearch = string.Empty;
    [ObservableProperty] private Employee? _selectedEmployee;

    // --- حالت چاپ گروهی: صدور یک قالب برای چند نفر هم‌زمان ---
    [ObservableProperty] private bool _bulkMode;

    public ObservableCollection<SelectableEmployee> BulkEmployees { get; } = new();
    public ObservableCollection<FieldOption> BulkFilterFields { get; } = new();
    public ObservableCollection<string> BulkFilterValues { get; } = new();

    [ObservableProperty] private FieldOption? _selectedBulkFilterField;
    [ObservableProperty] private string? _selectedBulkFilterValue;
    [ObservableProperty] private int _bulkSelectedCount;

    private Dictionary<long, Dictionary<string, string>> _employeeFieldCache = new();

    partial void OnBulkModeChanged(bool value)
    {
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsStep3Single));
        OnPropertyChanged(nameof(IsStep3Bulk));
        if (value) _ = InitializeBulkModeAsync();
    }

    partial void OnSelectedBulkFilterFieldChanged(FieldOption? value) => _ = ReloadBulkFilterValuesAsync();
    partial void OnSelectedBulkFilterValueChanged(string? value) => ApplyBulkFilter();
    partial void OnBulkSelectedCountChanged(int value) => OnPropertyChanged(nameof(CanGoNext));

    // مرحله ۲
    public ObservableCollection<LetterTemplate> Templates { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    [ObservableProperty] private LetterTemplate? _selectedTemplate;
    [ObservableProperty] private string? _selectedCategory;

    private List<LetterTemplate> _allTemplates = new();

    partial void OnSelectedCategoryChanged(string? value) => ApplyCategoryFilter();

    // مرحله ۳
    public ObservableCollection<PlaceholderField> Fields { get; } = new();
    public ObservableCollection<string> Printers { get; } = new();
    [ObservableProperty] private string _previewNumber = string.Empty;
    // سریال پیشنهادی و پایه، برای دکمه‌های + و − روی شماره
    [ObservableProperty] private int _previewSerial;
    private int _baseSerial;   // شمارهٔ خودکار بعدی (کف مجاز)
    [ObservableProperty] private int _copies = 1;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private bool _hasMissingFields;
    [ObservableProperty] private string? _selectedPrinter;
    [ObservableProperty] private bool _printAfterIssue = true;
    [ObservableProperty] private bool _savePdfAfterIssue;

    // وضعیت دکمه‌های ناوبری
    public bool CanGoNext => (CurrentStep == 1 && !BulkMode && SelectedEmployee is not null)
                          || (CurrentStep == 1 && BulkMode && BulkSelectedCount > 0)
                          || (CurrentStep == 2 && SelectedTemplate is not null);
    public bool CanGoBack => CurrentStep > 1;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep3Single => CurrentStep == 3 && !BulkMode;
    public bool IsStep3Bulk => CurrentStep == 3 && BulkMode;

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep3Single));
        OnPropertyChanged(nameof(IsStep3Bulk));
    }

    partial void OnSelectedEmployeeChanged(Employee? value) => OnPropertyChanged(nameof(CanGoNext));
    partial void OnSelectedTemplateChanged(LetterTemplate? value) => OnPropertyChanged(nameof(CanGoNext));
    partial void OnEmployeeSearchChanged(string value) => _ = DebouncedSearchAsync(value);

    public override async Task OnNavigatedToAsync()
    {
        // با هر بار ورود، ویزارد از ابتدا شروع می‌شود.
        CurrentStep = 1;
        SelectedEmployee = null;
        SelectedTemplate = null;
        EmployeeSearch = string.Empty;
        BulkMode = false;
        Fields.Clear();

        // همگام‌سازی خودکار قالب‌ها تا تغییرات پوشه بدون راه‌اندازی مجدد دیده شود.
        await SyncTemplatesSilentlyAsync();

        await LoadEmployeesAsync(string.Empty);
        await LoadTemplatesAsync();
    }

    /// <summary>همگام‌سازی بی‌صدای پوشهٔ قالب‌ها پیش از نمایش فهرست.</summary>
    private async Task SyncTemplatesSilentlyAsync()
    {
        try
        {
            var folder = _settings.Get(SettingKeys.TemplatesFolder);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                await _scanner.SyncTemplatesFolderAsync(folder);
        }
        catch (Exception ex)
        {
            _logger.Warn($"همگام‌سازی خودکار قالب‌ها ناموفق بود: {ex.Message}");
        }
    }

    private async Task LoadEmployeesAsync(string term)
    {
        try
        {
            var list = string.IsNullOrWhiteSpace(term)
                ? await _employees.GetAllAsync()
                : await _employees.SearchAsync(term);

            Employees.Clear();
            foreach (var e in list) Employees.Add(e);

            if (Employees.Count == 0 && string.IsNullOrWhiteSpace(term))
                StatusMessage = "فهرست کارکنان خالی است. ابتدا از بخش «کارکنان» فایل Excel را بارگذاری کنید.";
            else
                StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری کارکنان در ویزارد ناموفق بود.", ex);
        }
    }

    /// <summary>
    /// آماده‌سازی حالت چاپ گروهی: کپی فهرست افراد به فهرست چندانتخابی، به‌همراه
    /// بارگذاری فیلدهای قابل فیلتر (مثل پروژه، واحد) از همان زیرساخت صفحهٔ آمار.
    /// </summary>
    private async Task InitializeBulkModeAsync()
    {
        try
        {
            IsBusy = true;

            // پاک‌سازی اشتراک رویدادهای قبلی تا نشتی حافظه نداشته باشیم
            foreach (var item in BulkEmployees) item.PropertyChanged -= OnBulkItemPropertyChanged;

            BulkEmployees.Clear();
            foreach (var e in Employees)
            {
                var wrapper = new SelectableEmployee(e);
                wrapper.PropertyChanged += OnBulkItemPropertyChanged;
                BulkEmployees.Add(wrapper);
            }
            BulkSelectedCount = 0;

            // فیلدهای همهٔ افراد (شامل ستون‌های Excel مثل «پروژه») را یک‌بار کش می‌کنیم.
            var rows = await _stats.GetRowsAsync();
            _employeeFieldCache = rows.ToDictionary(r => r.EmployeeId, r => r.Values);

            var fields = await _stats.GetAvailableFieldsAsync();
            BulkFilterFields.Clear();
            foreach (var (key, name) in fields)
                BulkFilterFields.Add(new FieldOption { Key = key, DisplayName = name });

            SelectedBulkFilterField = BulkFilterFields.FirstOrDefault(f => f.Key.Contains("پروژه", StringComparison.Ordinal))
                                    ?? BulkFilterFields.FirstOrDefault(f => f.Key.Contains("واحد", StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            _logger.Error("آماده‌سازی حالت چاپ گروهی ناموفق بود.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnBulkItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableEmployee.IsSelected))
            BulkSelectedCount = BulkEmployees.Count(x => x.IsSelected);
    }

    private async Task ReloadBulkFilterValuesAsync()
    {
        try
        {
            BulkFilterValues.Clear();
            BulkFilterValues.Add("همه");

            if (SelectedBulkFilterField is not null)
            {
                var values = await _stats.GetDistinctValuesAsync(SelectedBulkFilterField.Key);
                foreach (var v in values) BulkFilterValues.Add(v);
            }

            SelectedBulkFilterValue = "همه";
        }
        catch (Exception ex)
        {
            _logger.Warn($"بارگذاری مقادیر فیلتر چاپ گروهی ناموفق بود: {ex.Message}");
        }
    }

    /// <summary>جای‌گاه برای فیلتر نمایشی در آینده؛ فعلاً فیلتر فقط برای «انتخاب فیلترشده‌ها» استفاده می‌شود.</summary>
    private void ApplyBulkFilter() { }

    /// <summary>تیک زدن همهٔ افرادی که با فیلتر جاری مطابقت دارند.</summary>
    [RelayCommand]
    private void SelectAllFiltered()
    {
        foreach (var item in GetFilteredBulkEmployees()) item.IsSelected = true;
    }

    /// <summary>برداشتن تیک از همهٔ افراد (چه فیلترشده چه نشده).</summary>
    [RelayCommand]
    private void ClearBulkSelection()
    {
        foreach (var item in BulkEmployees) item.IsSelected = false;
    }

    /// <summary>فهرست افرادی که مقدار فیلد فیلتر جاری‌شان با مقدار انتخاب‌شده یکی است.</summary>
    private IEnumerable<SelectableEmployee> GetFilteredBulkEmployees()
    {
        if (SelectedBulkFilterField is null ||
            string.IsNullOrWhiteSpace(SelectedBulkFilterValue) || SelectedBulkFilterValue == "همه")
            return BulkEmployees;

        var key = SelectedBulkFilterField.Key;
        var value = SelectedBulkFilterValue;

        return BulkEmployees.Where(item =>
            _employeeFieldCache.TryGetValue(item.Id, out var fields) &&
            fields.TryGetValue(key, out var v) &&
            string.Equals(v, value, StringComparison.Ordinal));
    }

    private async Task DebouncedSearchAsync(string term)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(280, token);
            if (!token.IsCancellationRequested)
                await LoadEmployeesAsync(term);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadTemplatesAsync()
    {
        try
        {
            _allTemplates = (await _templates.GetAllAsync()).ToList();

            // ساخت فهرست دسته‌ها از روی قالب‌های موجود، با «همه» در ابتدا.
            var cats = _allTemplates
                .Select(t => string.IsNullOrWhiteSpace(t.Category) ? "سایر" : t.Category!)
                .Distinct()
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();

            Categories.Clear();
            Categories.Add("همه");
            foreach (var c in cats) Categories.Add(c);

            // پیش‌فرض روی «همه»؛ این خودش ApplyCategoryFilter را صدا می‌زند.
            SelectedCategory = "همه";

            if (_allTemplates.Count == 0)
                StatusMessage = "هیچ قالبی ثبت نشده است. از بخش «قالب‌ها» همگام‌سازی را انجام دهید.";
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری قالب‌ها در ویزارد ناموفق بود.", ex);
        }
    }

    /// <summary>فیلتر کردن قالب‌های نمایش‌داده‌شده بر اساس دستهٔ انتخاب‌شده.</summary>
    private void ApplyCategoryFilter()
    {
        Templates.Clear();

        var filtered = string.IsNullOrEmpty(SelectedCategory) || SelectedCategory == "همه"
            ? _allTemplates
            : _allTemplates.Where(t =>
                (string.IsNullOrWhiteSpace(t.Category) ? "سایر" : t.Category) == SelectedCategory);

        foreach (var t in filtered) Templates.Add(t);

        // اگر قالب انتخاب‌شدهٔ قبلی در فهرست جدید نیست، انتخاب پاک شود.
        if (SelectedTemplate is not null && !Templates.Contains(SelectedTemplate))
            SelectedTemplate = null;
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (CurrentStep == 1 && !BulkMode && SelectedEmployee is not null)
        {
            CurrentStep = 2;
        }
        else if (CurrentStep == 1 && BulkMode && BulkSelectedCount > 0)
        {
            CurrentStep = 2;
        }
        else if (CurrentStep == 2 && SelectedTemplate is not null)
        {
            await BuildPreviewAsync();
            CurrentStep = 3;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1) CurrentStep--;
    }

    /// <summary>ساخت پیش‌نمایش فیلدها و نمایش شمارهٔ احتمالی (بدون مصرف).</summary>
    private async Task BuildPreviewAsync()
    {
        if (SelectedTemplate is null) return;
        if (!BulkMode && SelectedEmployee is null) return;

        try
        {
            IsBusy = true;
            Fields.Clear();

            // در حالت گروهی، ویرایش تک‌تک فیلدها معنا ندارد (برای چند نفر هم‌زمان است)؛
            // فقط شمارهٔ پایه و چاپگر آماده می‌شود و صفحهٔ خلاصه نمایش داده می‌شود.
            if (!BulkMode)
            {
                var result = await _issueService.BuildFieldsAsync(SelectedEmployee!.Id, SelectedTemplate.Id);
                if (result.IsFailure)
                {
                    ShowMessage(result.Error!, "خطا", MessageBoxImage.Error);
                    return;
                }

                foreach (var f in result.Value) Fields.Add(f);
                HasMissingFields = Fields.Any(f => f.IsMissing);
            }

            // بارگذاری فهرست چاپگرها و انتخاب پیش‌فرض
            LoadPrinters();

            // شمارهٔ نمایشی (قطعی نیست)
            PreviewNumber = await _numbers.PeekNextAsync();
            _baseSerial = await _numbers.GetLastSerialAsync(PersianDate.CurrentYear) + 1;
            PreviewSerial = _baseSerial;
            Copies = SelectedTemplate.DefaultCopies;
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.Error("ساخت پیش‌نمایش ناموفق بود.", ex);
            ShowMessage($"ساخت پیش‌نمایش با خطا مواجه شد:\n{ex.Message}", "خطا", MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadPrinters()
    {
        try
        {
            Printers.Clear();
            foreach (var p in _printService.GetPrinters())
                Printers.Add(p);

            // انتخاب پیش‌فرض: چاپگر تنظیمات، وگرنه چاپگر پیش‌فرض ویندوز
            var configured = _settings.Get(SettingKeys.DefaultPrinter);
            SelectedPrinter = !string.IsNullOrWhiteSpace(configured) && Printers.Contains(configured)
                ? configured
                : _printService.GetSystemDefaultPrinter();
        }
        catch (Exception ex)
        {
            _logger.Warn($"بارگذاری چاپگرها ناموفق بود: {ex.Message}");
        }
    }

    /// <summary>وقتی سریال پیشنهادی تغییر کرد، رشتهٔ نمایشی شماره هم به‌روز می‌شود.</summary>
    partial void OnPreviewSerialChanged(int value)
    {
        // شماره را با همان قالب تنظیمات بازسازی می‌کنیم.
        PreviewNumber = _numbers.FormatNumber(PersianDate.CurrentYear, value);
    }

    /// <summary>افزایش شمارهٔ پیشنهادی (دکمهٔ +).</summary>
    [RelayCommand]
    private void IncreaseNumber() => PreviewSerial++;

    /// <summary>کاهش شمارهٔ پیشنهادی (دکمهٔ −). پایین‌تر از شمارهٔ خودکار بعدی نمی‌رود.</summary>
    [RelayCommand]
    private void DecreaseNumber()
    {
        if (PreviewSerial > _baseSerial) PreviewSerial--;
    }

    /// <summary>صدور نهایی. تنها نقطه‌ای که شماره مصرف می‌شود.</summary>
    [RelayCommand]
    private async Task IssueAsync()
    {
        if (SelectedEmployee is null || SelectedTemplate is null) return;

        try
        {
            IsBusy = true;
            StatusMessage = "در حال صدور نامه...";

            var request = new IssueLetterRequest
            {
                EmployeeId = SelectedEmployee.Id,
                TemplateId = SelectedTemplate.Id,
                Description = Description,
                Copies = Math.Max(1, Copies),
                // اگر کاربر شماره را با + جلو برده، همان را ترجیح بده؛ اگر دست‌نخورده بود null.
                PreferredSerial = PreviewSerial > _baseSerial ? PreviewSerial : null,
                PrintImmediately = false
            };

            // مقادیر دستی که کاربر در پیش‌نمایش ویرایش کرده
            foreach (var f in Fields)
                if (!string.IsNullOrEmpty(f.Value))
                    request.ManualValues[f.Key] = f.Value!;

            var result = await _issueService.IssueAsync(request);

            if (result.IsFailure)
            {
                ShowMessage(result.Error!, "خطا در صدور", MessageBoxImage.Error);
                return;
            }

            var r = result.Value;
            var extraNotes = new List<string>();

            // چاپ در صورت انتخاب کاربر
            if (PrintAfterIssue)
            {
                var printResult = await _issueService.PrintDocumentAsync(
                    r.Letter.Id, r.GeneratedDocxPath, SelectedPrinter, Math.Max(1, Copies));
                extraNotes.Add(printResult.IsSuccess
                    ? $"✓ برای چاپ ارسال شد ({Copies} نسخه)."
                    : $"⚠ چاپ ناموفق بود: {printResult.Error}");
            }

            // خروجی PDF در صورت انتخاب
            string? pdfPath = null;
            if (SavePdfAfterIssue)
            {
                var pdfResult = await _issueService.ExportPdfAsync(r.Letter.Id, r.GeneratedDocxPath);
                if (pdfResult.IsSuccess)
                {
                    pdfPath = pdfResult.Value;
                    extraNotes.Add($"✓ فایل PDF ساخته شد:\n{pdfPath}");
                }
                else
                {
                    extraNotes.Add($"⚠ ساخت PDF ناموفق بود: {pdfResult.Error}");
                }
            }

            var msg = $"نامه با موفقیت صادر و ثبت شد.\n\nشمارهٔ نامه: {r.Letter.LetterNumber}\n" +
                      $"تاریخ: {r.Letter.JalaliDate}   ساعت: {r.Letter.IssuedTime}\n\n" +
                      $"فایل تولیدشده:\n{r.GeneratedDocxPath}";

            if (r.Warnings.Count > 0)
                msg += "\n\nهشدارها:\n" + string.Join("\n", r.Warnings);

            if (extraNotes.Count > 0)
                msg += "\n\n" + string.Join("\n\n", extraNotes);

            ShowMessage(msg, "صدور موفق", MessageBoxImage.Information);

            // باز کردن خروجی: اگر PDF ساخته شده آن را، وگرنه سند Word را
            TryOpen(pdfPath ?? r.GeneratedDocxPath);

            // شروع مجدد ویزارد برای نامهٔ بعدی
            await OnNavigatedToAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("صدور نامه ناموفق بود.", ex);
            ShowMessage($"صدور با خطای غیرمنتظره متوقف شد:\n{ex.Message}", "خطا", MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }

    /// <summary>
    /// صدور گروهی: همان قالب برای همهٔ افراد تیک‌خورده صادر می‌شود. هر نفر شمارهٔ خودش را
    /// از همان شمارندهٔ امن و اتمیک می‌گیرد (دقیقاً همان مسیر صدور تکی، فقط پشت‌سرهم)،
    /// پس امکان شمارهٔ تکراری وجود ندارد. مقادیر فیلدها فقط از اطلاعات فرد (Excel) پر می‌شوند؛
    /// ویرایش دستی تک‌تک برای تعداد زیاد عملی نیست.
    /// </summary>
    [RelayCommand]
    private async Task IssueBulkAsync()
    {
        if (SelectedTemplate is null) return;

        var selected = BulkEmployees.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = MessageBox.Show(
            $"برای {selected.Count} نفر، نامهٔ «{SelectedTemplate.Title}» صادر می‌شود و هرکدام شمارهٔ جداگانه می‌گیرند.\n\nادامه می‌دهید؟",
            "تأیید صدور گروهی", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        if (confirm != MessageBoxResult.Yes) return;

        var succeeded = new List<string>();
        var failed = new List<string>();

        try
        {
            IsBusy = true;

            for (var i = 0; i < selected.Count; i++)
            {
                var person = selected[i];
                StatusMessage = $"در حال صدور برای {person.FullName}... ({i + 1} از {selected.Count})";

                try
                {
                    var request = new IssueLetterRequest
                    {
                        EmployeeId = person.Id,
                        TemplateId = SelectedTemplate.Id,
                        Description = Description,
                        Copies = Math.Max(1, Copies),
                        PrintImmediately = false
                    };

                    var result = await _issueService.IssueAsync(request);
                    if (result.IsFailure)
                    {
                        failed.Add($"{person.FullName}: {result.Error}");
                        continue;
                    }

                    var r = result.Value;

                    if (PrintAfterIssue)
                        await _issueService.PrintDocumentAsync(r.Letter.Id, r.GeneratedDocxPath, SelectedPrinter, Math.Max(1, Copies));

                    if (SavePdfAfterIssue)
                        await _issueService.ExportPdfAsync(r.Letter.Id, r.GeneratedDocxPath);

                    succeeded.Add($"{person.FullName} — شمارهٔ {r.Letter.LetterNumber}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"صدور گروهی برای «{person.FullName}» ناموفق بود.", ex);
                    failed.Add($"{person.FullName}: {ex.Message}");
                }
            }

            var summary = $"صدور گروهی پایان یافت.\n\nموفق: {succeeded.Count} نفر\nناموفق: {failed.Count} نفر";
            if (failed.Count > 0)
                summary += "\n\nموارد ناموفق:\n" + string.Join("\n", failed.Take(15)) +
                           (failed.Count > 15 ? $"\n… و {failed.Count - 15} مورد دیگر" : "");

            ShowMessage(summary, "نتیجهٔ صدور گروهی", failed.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

            await OnNavigatedToAsync();
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }

    private void TryOpen(string path)
    {
        try
        {
            if (File.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            _logger.Warn($"باز کردن سند تولیدشده ناموفق بود: {ex.Message}");
        }
    }

    private static void ShowMessage(string message, string title, MessageBoxImage icon)
        => MessageBox.Show(message, title, MessageBoxButton.OK, icon,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
}
