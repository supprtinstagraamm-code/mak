using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>
/// فهرست قالب‌های Word و Placeholderهای شناسایی‌شده هر قالب.
/// دکمه «همگام‌سازی» پوشه قالب‌ها را دوباره اسکن می‌کند.
/// </summary>
public partial class TemplatesViewModel : PageViewModelBase
{
    private readonly ITemplateRepository _templates;
    private readonly ITemplateScanner _scanner;
    private readonly IColumnMappingRepository _mappings;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;

    public TemplatesViewModel(
        ITemplateRepository templates,
        ITemplateScanner scanner,
        IColumnMappingRepository mappings,
        ISettingsService settings,
        IAppLogger logger)
    {
        _templates = templates;
        _scanner = scanner;
        _mappings = mappings;
        _settings = settings;
        _logger = logger;
    }

    public override string Title => "قالب‌ها";
    public override string Subtitle => "قالب‌های Word و فیلدهای شناسایی‌شده هر قالب";

    public ObservableCollection<LetterTemplate> Templates { get; } = new();
    public ObservableCollection<string> SelectedPlaceholders { get; } = new();

    /// <summary>دسته‌های موجود برای انتخاب. کاربر می‌تواند دستهٔ جدید هم تایپ کند.</summary>
    public ObservableCollection<string> Categories { get; } = new();

    /// <summary>دستهٔ قالب انتخاب‌شده؛ قابل ویرایش و افزودن دستهٔ جدید.</summary>
    [ObservableProperty] private string? _editableCategory;

    /// <summary>فهرست همهٔ فیلدهای قابل استفاده در قالب‌ها (از ستون‌های Excel).</summary>
    public ObservableCollection<string> AvailableFields { get; } = new();

    [ObservableProperty] private LetterTemplate? _selectedTemplate;
    [ObservableProperty] private string _templatesFolder = string.Empty;

    partial void OnSelectedTemplateChanged(LetterTemplate? value)
    {
        EditableCategory = value?.Category ?? string.Empty;
        _ = LoadPlaceholdersAsync(value);
    }

    public override async Task OnNavigatedToAsync()
    {
        // همگام‌سازی خودکار و بی‌صدا هنگام ورود به صفحه، تا تغییرات پوشهٔ قالب‌ها
        // (افزودن، حذف یا جایگزینی فایل) بدون نیاز به خروج و ورود مجدد دیده شود.
        await SyncSilentlyAsync();
        await LoadAsync();
    }

    /// <summary>همگام‌سازی بدون نمایش پیام (برای به‌روزرسانی خودکار).</summary>
    private async Task SyncSilentlyAsync()
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

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusMessage = null;
            TemplatesFolder = _settings.Get(SettingKeys.TemplatesFolder);

            var list = await _templates.GetAllAsync();
            Templates.Clear();
            foreach (var t in list) Templates.Add(t);

            await LoadCategoriesAsync();
            await LoadAvailableFieldsAsync();

            if (Templates.Count == 0)
                StatusMessage = "هنوز قالبی ثبت نشده است. فایل‌های Word قالب را در پوشهٔ قالب‌ها قرار دهید و «همگام‌سازی» را بزنید.";
            else if (SelectedTemplate is null)
                SelectedTemplate = Templates[0];
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری فهرست قالب‌ها ناموفق بود.", ex);
            StatusMessage = "خواندن قالب‌ها با خطا مواجه شد. جزئیات در فایل لاگ ثبت شد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadPlaceholdersAsync(LetterTemplate? template)
    {
        SelectedPlaceholders.Clear();
        if (template is null) return;

        try
        {
            var list = await _templates.GetPlaceholdersAsync(template.Id);
            foreach (var ph in list)
                SelectedPlaceholders.Add(ph.PlaceholderKey);
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری Placeholderهای قالب ناموفق بود.", ex);
        }
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        var folder = _settings.Get(SettingKeys.TemplatesFolder);

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            ShowMessage(
                $"پوشهٔ قالب‌ها یافت نشد:\n{folder}\n\nمی‌توانید فایل‌های Word قالب را در این پوشه قرار دهید یا مسیر آن را در تنظیمات تغییر دهید.",
                "پوشه قالب‌ها", MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "در حال اسکن قالب‌ها...";

            var result = await _scanner.SyncTemplatesFolderAsync(folder);

            if (result.IsFailure)
            {
                ShowMessage(result.Error!, "خطا در همگام‌سازی", MessageBoxImage.Error);
                return;
            }

            ShowMessage(
                $"همگام‌سازی کامل شد.\n\n{result.Value} قالب شناسایی و ثبت شد.",
                "نتیجه همگام‌سازی", MessageBoxImage.Information);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("همگام‌سازی قالب‌ها ناموفق بود.", ex);
            ShowMessage($"همگام‌سازی با خطای غیرمنتظره متوقف شد:\n{ex.Message}", "خطا", MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }

    [RelayCommand]
    private void OpenTemplatesFolder()
    {
        var folder = _settings.Get(SettingKeys.TemplatesFolder);
        try
        {
            if (Directory.Exists(folder))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            _logger.Error("باز کردن پوشهٔ قالب‌ها ناموفق بود.", ex);
        }
    }

    /// <summary>
    /// بارگذاری فهرست دسته‌ها: دسته‌های استفاده‌شده در قالب‌ها به‌علاوهٔ چند دستهٔ پیشنهادی.
    /// کاربر می‌تواند دستهٔ کاملاً جدید هم تایپ کند؛ محدودیتی وجود ندارد.
    /// </summary>
    private async Task LoadCategoriesAsync()
    {
        try
        {
            var used = await _templates.GetCategoriesAsync();

            // دسته‌های پیشنهادی رایج (صرفاً برای راحتی؛ کاربر می‌تواند هر چیزی بنویسد)
            var suggested = new[] { "گواهی عضویت", "تسویه حساب", "کسری", "حکم ماموریت", "معرفی‌نامه", "گواهی اشتغال" };

            var all = used.Concat(suggested)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();

            Categories.Clear();
            foreach (var c in all) Categories.Add(c);
        }
        catch (Exception ex)
        {
            _logger.Warn($"بارگذاری دسته‌ها ناموفق بود: {ex.Message}");
        }
    }

    /// <summary>
    /// فهرست تمام فیلدهایی که می‌توان در قالب‌ها استفاده کرد: ستون‌های فایل Excel
    /// به‌علاوهٔ مقادیر سیستمی. کاربر با دیدن این فهرست می‌داند چه چیزی در دسترس دارد.
    /// </summary>
    private async Task LoadAvailableFieldsAsync()
    {
        try
        {
            var mappings = await _mappings.GetAllAsync();

            var system = new[] { "شماره_نامه", "تاریخ", "ساعت", "نام_شرکت" };

            AvailableFields.Clear();
            foreach (var key in system) AvailableFields.Add(key);
            foreach (var m in mappings.OrderBy(m => m.SortOrder))
                if (!AvailableFields.Contains(m.PlaceholderKey))
                    AvailableFields.Add(m.PlaceholderKey);
        }
        catch (Exception ex)
        {
            _logger.Warn($"بارگذاری فهرست فیلدها ناموفق بود: {ex.Message}");
        }
    }

    /// <summary>ذخیرهٔ دستهٔ انتخاب/تایپ‌شده برای قالب جاری.</summary>
    [RelayCommand]
    private async Task SaveCategoryAsync()
    {
        if (SelectedTemplate is null)
        {
            ShowMessage("ابتدا یک قالب را از فهرست انتخاب کنید.", "دسته‌بندی", MessageBoxImage.Information);
            return;
        }

        var category = (EditableCategory ?? string.Empty).Trim();
        if (category.Length == 0)
        {
            ShowMessage("نام دسته نمی‌تواند خالی باشد.", "دسته‌بندی", MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _templates.SetCategoryAsync(SelectedTemplate.Id, category);
            SelectedTemplate.Category = category;

            StatusMessage = $"دستهٔ قالب «{SelectedTemplate.Title}» به «{category}» تغییر یافت.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("ذخیرهٔ دسته ناموفق بود.", ex);
            ShowMessage($"ذخیرهٔ دسته با خطا مواجه شد:\n{ex.Message}", "خطا", MessageBoxImage.Error);
        }
    }

    /// <summary>کپی کردن یک فیلد به‌صورت {{کلید}} در کلیپ‌بورد، برای چسباندن در Word.</summary>
    [RelayCommand]
    private void CopyField(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        try
        {
            Clipboard.SetText("{{" + key + "}}");
            StatusMessage = $"«{{{{{key}}}}}» در کلیپ‌بورد کپی شد. می‌توانید در فایل Word بچسبانید.";
        }
        catch (Exception ex)
        {
            _logger.Warn($"کپی در کلیپ‌بورد ناموفق بود: {ex.Message}");
        }
    }

    private static void ShowMessage(string message, string title, MessageBoxImage icon)
        => MessageBox.Show(message, title, MessageBoxButton.OK, icon,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
}
