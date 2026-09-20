using System.Collections.ObjectModel;
using System.Windows;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

// هم Microsoft.Win32 (برای OpenFileDialog) و هم System.IO یک نوع به‌نام File دارند.
// این alias مشخص می‌کند که منظور همان File سیستم فایل است.
using File = System.IO.File;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>
/// فهرست کارکنان با جستجوی زنده و امکان بارگذاری از فایل Excel.
/// جستجو با تأخیر کوتاه انجام می‌شود تا با هر حرف یک کوئری به دیتابیس نزند.
/// </summary>
public partial class EmployeesViewModel : PageViewModelBase
{
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeImportService _importer;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;

    private CancellationTokenSource? _searchCts;

    public EmployeesViewModel(
        IEmployeeRepository employees,
        IEmployeeImportService importer,
        ISettingsService settings,
        IAppLogger logger)
    {
        _employees = employees;
        _importer = importer;
        _settings = settings;
        _logger = logger;
    }

    public override string Title => "کارکنان";
    public override string Subtitle => "فهرست افراد و بارگذاری از فایل Excel";

    public ObservableCollection<Employee> Employees { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private Employee? _selectedEmployee;

    /// <summary>هنگام تغییر متن جستجو، با کمی تأخیر جستجو اجرا می‌شود (Debounce).</summary>
    partial void OnSearchTextChanged(string value) => _ = DebouncedSearchAsync(value);

    public override Task OnNavigatedToAsync() => LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusMessage = null;

            var list = string.IsNullOrWhiteSpace(SearchText)
                ? await _employees.GetAllAsync()
                : await _employees.SearchAsync(SearchText);

            Employees.Clear();
            foreach (var e in list) Employees.Add(e);

            TotalCount = await _employees.CountAsync();

            if (TotalCount == 0)
                StatusMessage = "هنوز اطلاعاتی بارگذاری نشده است. دکمه «بارگذاری از Excel» را بزنید.";
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری فهرست کارکنان ناموفق بود.", ex);
            StatusMessage = "خواندن فهرست با خطا مواجه شد. جزئیات در فایل لاگ ثبت شد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DebouncedSearchAsync(string term)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            await Task.Delay(280, token); // صبر کوتاه تا کاربر تایپ را تمام کند
            if (token.IsCancellationRequested) return;

            var list = string.IsNullOrWhiteSpace(term)
                ? await _employees.GetAllAsync(ct: token)
                : await _employees.SearchAsync(term, ct: token);

            if (token.IsCancellationRequested) return;

            Employees.Clear();
            foreach (var e in list) Employees.Add(e);
        }
        catch (OperationCanceledException) { /* جستجوی جدید جایگزین شد */ }
        catch (Exception ex)
        {
            _logger.Error("جستجوی کارکنان ناموفق بود.", ex);
        }
    }

    [RelayCommand]
    private async Task ImportFromExcelAsync()
    {
        // مسیر ذخیره‌شده در تنظیمات پیشنهاد اولیه است؛ کاربر می‌تواند فایل دیگری انتخاب کند.
        var savedPath = _settings.Get(SettingKeys.ExcelPath);

        var dialog = new OpenFileDialog
        {
            Title = "انتخاب فایل Excel کارکنان",
            Filter = "فایل Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|همه فایل‌ها (*.*)|*.*",
            CheckFileExists = true
        };

        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
            dialog.FileName = savedPath;

        if (dialog.ShowDialog() != true) return;

        try
        {
            IsBusy = true;
            StatusMessage = "در حال بارگذاری فایل...";

            // زمان شروع، برای تشخیص افرادی که در فایل جدید نیستند
            var importStarted = DateTime.UtcNow.AddSeconds(-1);

            var result = await _importer.ImportAsync(dialog.FileName);

            if (result.IsFailure)
            {
                ShowMessage(result.Error!, "خطا در بارگذاری", MessageBoxImage.Error);
                StatusMessage = null;
                return;
            }

            // مسیر فایل موفق را برای دفعه بعد ذخیره می‌کنیم.
            await _settings.SetAsync(SettingKeys.ExcelPath, dialog.FileName);

            var r = result.Value;
            var summary =
                $"بارگذاری کامل شد.\n\n" +
                $"کل سطرها: {r.TotalRows}\n" +
                $"رکورد جدید: {r.Inserted}\n" +
                $"به‌روزرسانی: {r.Updated}\n" +
                $"بدون تغییر: {r.Skipped}";

            if (r.NewColumns.Count > 0)
                summary += $"\n\nستون‌های جدید شناسایی‌شده: {string.Join("، ", r.NewColumns)}\n" +
                           "این ستون‌ها اکنون در قالب‌های Word قابل استفاده‌اند.";

            if (r.Warnings.Count > 0)
            {
                var shown = r.Warnings.Take(10);
                summary += $"\n\nهشدارها ({r.Warnings.Count}):\n" + string.Join("\n", shown);
                if (r.Warnings.Count > 10) summary += "\n…";
            }

            ShowMessage(summary, "نتیجه بارگذاری", MessageBoxImage.Information);

            // اگر افرادی در سیستم هستند که در فایل جدید نبودند، پیشنهاد پاک‌سازی
            await OfferCleanupAsync(importStarted);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری Excel ناموفق بود.", ex);
            ShowMessage($"بارگذاری با خطای غیرمنتظره متوقف شد:\n{ex.Message}", "خطا", MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }

    /// <summary>
    /// پیشنهاد غیرفعال کردن افرادی که در فایل Excel جدید وجود نداشتند، تا فهرست برنامه
    /// دقیقاً با فایل هماهنگ شود. حذف فیزیکی نیست، پس سوابق نامه‌ها سالم می‌ماند.
    /// </summary>
    private async Task OfferCleanupAsync(DateTime importStarted)
    {
        var answer = MessageBox.Show(
            "آیا افرادی که در فایل جدید وجود نداشتند از فهرست حذف شوند؟\n\n" +
            "با «بله» فهرست دقیقاً برابر فایل Excel جدید می‌شود.\n" +
            "با «خیر» افراد قبلی هم در فهرست باقی می‌مانند.\n\n" +
            "توجه: سوابق نامه‌های صادرشده در هر صورت حفظ می‌شود.",
            "هماهنگ‌سازی با فایل جدید",
            MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        if (answer != MessageBoxResult.Yes) return;

        try
        {
            var removed = await _employees.DeactivateNotImportedSinceAsync(importStarted);
            StatusMessage = removed > 0
                ? $"{removed} نفر که در فایل جدید نبودند از فهرست حذف شدند."
                : "همهٔ افراد فهرست در فایل جدید موجود بودند.";
        }
        catch (Exception ex)
        {
            _logger.Error("پاک‌سازی افراد قدیمی ناموفق بود.", ex);
        }
    }

    private static void ShowMessage(string message, string title, MessageBoxImage icon)
        => MessageBox.Show(message, title, MessageBoxButton.OK, icon,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
}
