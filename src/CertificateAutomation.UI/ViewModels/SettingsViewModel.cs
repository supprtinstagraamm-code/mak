using System.IO;
using System.Windows;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>
/// تنظیمات برنامه — بخش محافظت‌شده با رمز مدیر.
/// تا زمانی که مدیر وارد نشده باشد، فرم تنظیمات نمایش داده نمی‌شود.
/// </summary>
public partial class SettingsViewModel : PageViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IUserSession _session;
    private readonly IBackupService _backup;
    private readonly IPrintService _printService;
    private readonly INumberGenerator _numbers;
    private readonly IPasswordHasher _hasher;
    private readonly IUserRepository _users;
    private readonly IAppLogger _logger;

    /// <summary>تابعی که دیالوگ ورود را نمایش می‌دهد (از سوی View تزریق می‌شود).</summary>
    public Func<bool>? ShowLoginDialog { get; set; }

    /// <summary>پس از ذخیرهٔ تنظیمات صدا زده می‌شود (مثلاً برای به‌روزرسانی لوگوی نوار کناری).</summary>
    public Action? OnSettingsSaved { get; set; }

    public SettingsViewModel(
        ISettingsService settings,
        IUserSession session,
        IBackupService backup,
        IPrintService printService,
        INumberGenerator numbers,
        IPasswordHasher hasher,
        IUserRepository users,
        IAppLogger logger)
    {
        _settings = settings;
        _session = session;
        _backup = backup;
        _printService = printService;
        _numbers = numbers;
        _hasher = hasher;
        _users = users;
        _logger = logger;
    }

    public override string Title => "تنظیمات";
    public override string Subtitle => "مسیرها، چاپگر، قالب شماره و نسخهٔ پشتیبان — نیازمند ورود مدیر";

    public bool IsUnlocked => _session.IsAdmin;
    public bool IsLocked => !_session.IsAdmin;

    // تنظیمات قابل ویرایش
    [ObservableProperty] private string _companyName = string.Empty;
    [ObservableProperty] private string _logoPath = string.Empty;
    [ObservableProperty] private string _excelPath = string.Empty;
    [ObservableProperty] private string _templatesFolder = string.Empty;
    [ObservableProperty] private string _outputFolder = string.Empty;
    [ObservableProperty] private string _backupFolder = string.Empty;
    [ObservableProperty] private string _numberFormat = string.Empty;
    [ObservableProperty] private string? _selectedPrinter;
    [ObservableProperty] private bool _usePersianDigits;
    [ObservableProperty] private bool _archivePdfEnabled;
    [ObservableProperty] private bool _autoStartEnabled;
    [ObservableProperty] private int _currentSerial;
    [ObservableProperty] private string _rendererInfo = string.Empty;

    public System.Collections.ObjectModel.ObservableCollection<string> Printers { get; } = new();

    public override Task OnNavigatedToAsync()
    {
        // اگر مدیر وارد نشده، درخواست ورود
        if (!_session.IsAdmin)
            PromptLogin();
        else
            LoadSettings();

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Login() => PromptLogin();

    private void PromptLogin()
    {
        var ok = ShowLoginDialog?.Invoke() ?? false;
        NotifyLockState();
        if (ok) LoadSettings();
    }

    [RelayCommand]
    private void Logout()
    {
        _session.Logout();
        NotifyLockState();
    }

    private void NotifyLockState()
    {
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(IsLocked));
    }

    private void LoadSettings()
    {
        CompanyName = _settings.Get(SettingKeys.CompanyName);
        LogoPath = _settings.Get(SettingKeys.LogoPath);
        ExcelPath = _settings.Get(SettingKeys.ExcelPath);
        TemplatesFolder = _settings.Get(SettingKeys.TemplatesFolder);
        OutputFolder = _settings.Get(SettingKeys.OutputFolder);
        BackupFolder = _settings.Get(SettingKeys.BackupFolder);
        NumberFormat = _settings.Get(SettingKeys.NumberFormat, "{year}/{serial:00000}");
        UsePersianDigits = _settings.GetBool(SettingKeys.UsePersianDigits, true);
        ArchivePdfEnabled = _settings.GetBool(SettingKeys.ArchivePdfEnabled, true);
        AutoStartEnabled = Services.WindowsAutoStart.IsEnabled();

        Printers.Clear();
        foreach (var p in _printService.GetPrinters()) Printers.Add(p);
        var configured = _settings.Get(SettingKeys.DefaultPrinter);
        SelectedPrinter = !string.IsNullOrWhiteSpace(configured) && Printers.Contains(configured)
            ? configured : _printService.GetSystemDefaultPrinter();

        _ = LoadSerialAsync();
    }

    private async Task LoadSerialAsync()
    {
        CurrentSerial = await _numbers.GetLastSerialAsync(PersianDate.CurrentYear);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!_session.IsAdmin) return;

        try
        {
            IsBusy = true;
            var values = new Dictionary<string, string>
            {
                [SettingKeys.CompanyName] = CompanyName,
                [SettingKeys.LogoPath] = LogoPath,
                [SettingKeys.ExcelPath] = ExcelPath,
                [SettingKeys.TemplatesFolder] = TemplatesFolder,
                [SettingKeys.OutputFolder] = OutputFolder,
                [SettingKeys.BackupFolder] = BackupFolder,
                [SettingKeys.NumberFormat] = NumberFormat,
                [SettingKeys.DefaultPrinter] = SelectedPrinter ?? string.Empty,
                [SettingKeys.UsePersianDigits] = UsePersianDigits.ToString(),
                [SettingKeys.ArchivePdfEnabled] = ArchivePdfEnabled.ToString()
            };
            await _settings.SetManyAsync(values);

            // اعمال فوری تنظیم ارقام فارسی
            Converters.PersianDigitsConverter.Enabled = UsePersianDigits;

            // اجرای خودکار هنگام روشن شدن ویندوز (برای اینکه یادآورها همیشه بررسی شوند)
            Services.WindowsAutoStart.SetEnabled(AutoStartEnabled);

            // اطلاع به نوار کناری برای به‌روزرسانی فوری لوگو و نام شرکت
            OnSettingsSaved?.Invoke();

            ShowMessage("تنظیمات ذخیره شد.\n\nتوجه: تغییر نمایش ارقام فارسی پس از باز کردن دوبارهٔ صفحه‌ها کامل اعمال می‌شود.",
                "ذخیره", MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error("ذخیرهٔ تنظیمات ناموفق بود.", ex);
            ShowMessage($"ذخیره با خطا مواجه شد:\n{ex.Message}", "خطا", MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    // --- انتخاب مسیرها ---
    [RelayCommand] private void BrowseLogo() => LogoPath = PickFile("تصاویر|*.png;*.jpg;*.jpeg;*.bmp", LogoPath) ?? LogoPath;
    [RelayCommand] private void BrowseExcel() => ExcelPath = PickFile("Excel|*.xlsx;*.xlsm", ExcelPath) ?? ExcelPath;
    [RelayCommand] private void BrowseTemplates() => TemplatesFolder = PickFolder(TemplatesFolder) ?? TemplatesFolder;
    [RelayCommand] private void BrowseOutput() => OutputFolder = PickFolder(OutputFolder) ?? OutputFolder;
    [RelayCommand] private void BrowseBackup() => BackupFolder = PickFolder(BackupFolder) ?? BackupFolder;

    // --- تغییر شمارنده (فقط مدیر) ---
    [RelayCommand]
    private async Task UpdateSerialAsync()
    {
        if (!_session.IsAdmin) return;

        var result = await _numbers.SetLastSerialAsync(PersianDate.CurrentYear, CurrentSerial, _session.UserName);
        if (result.IsFailure)
        {
            ShowMessage(result.Error!, "خطا در تغییر شماره", MessageBoxImage.Warning);
            await LoadSerialAsync(); // بازگردانی مقدار درست
            return;
        }
        ShowMessage($"شمارندهٔ سال {PersianDate.CurrentYear} به {CurrentSerial} تنظیم شد.\nنامهٔ بعدی از شمارهٔ {CurrentSerial + 1} خواهد بود.",
            "تغییر شماره", MessageBoxImage.Information);
    }

    // --- نسخهٔ پشتیبان ---
    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "در حال تهیهٔ نسخهٔ پشتیبان...";
            var result = await _backup.CreateBackupAsync();

            if (result.IsSuccess)
                ShowMessage($"نسخهٔ پشتیبان با موفقیت ساخته شد:\n\n{result.Value}", "پشتیبان‌گیری", MessageBoxImage.Information);
            else
                ShowMessage(result.Error!, "خطا", MessageBoxImage.Error);
        }
        finally { IsBusy = false; StatusMessage = null; }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (!_session.IsAdmin) return;

        var dialog = new OpenFileDialog
        {
            Title = "انتخاب فایل پشتیبان",
            Filter = "فایل پشتیبان|Backup_*.zip|همهٔ فایل‌ها|*.*",
            InitialDirectory = Directory.Exists(BackupFolder) ? BackupFolder : ""
        };
        if (dialog.ShowDialog() != true) return;

        var confirm = MessageBox.Show(
            "بازیابی، اطلاعات فعلی را با نسخهٔ پشتیبان جایگزین می‌کند.\n\n" +
            "پیش از بازیابی، یک نسخهٔ ایمنی خودکار از وضعیت فعلی گرفته می‌شود.\n\nادامه می‌دهید؟",
            "تأیید بازیابی", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            IsBusy = true;
            StatusMessage = "در حال بازیابی...";
            var result = await _backup.RestoreBackupAsync(dialog.FileName);

            if (result.IsSuccess)
                ShowMessage("بازیابی با موفقیت انجام شد.\n\nلطفاً برنامه را ببندید و دوباره باز کنید تا تغییرات کامل اعمال شود.",
                    "بازیابی", MessageBoxImage.Information);
            else
                ShowMessage(result.Error!, "خطا", MessageBoxImage.Error);
        }
        finally { IsBusy = false; StatusMessage = null; }
    }

    private string? PickFile(string filter, string current)
    {
        var d = new OpenFileDialog { Filter = filter };
        if (!string.IsNullOrWhiteSpace(current) && File.Exists(current)) d.FileName = current;
        return d.ShowDialog() == true ? d.FileName : null;
    }

    private string? PickFolder(string current)
    {
        // استفاده از OpenFolderDialog (موجود در .NET 8)
        var d = new OpenFolderDialog();
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current)) d.InitialDirectory = current;
        return d.ShowDialog() == true ? d.FolderName : null;
    }

    private static void ShowMessage(string message, string title, MessageBoxImage icon)
        => MessageBox.Show(message, title, MessageBoxButton.OK, icon,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
}
