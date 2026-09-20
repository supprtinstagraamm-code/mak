using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>
/// جستجوی سوابق نامه‌ها بر اساس شماره، نام، کد ملی، شماره عضویت، تاریخ و نوع نامه.
/// امکان چاپ مجدد و (برای مدیر) حذف سابقه.
/// </summary>
public partial class HistoryViewModel : PageViewModelBase
{
    private readonly ILetterRepository _letters;
    private readonly ITemplateRepository _templates;
    private readonly ILetterIssueService _issueService;
    private readonly IPrintService _printService;
    private readonly IUserSession _session;
    private readonly IAuditRepository _audit;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;

    public HistoryViewModel(
        ILetterRepository letters,
        ITemplateRepository templates,
        ILetterIssueService issueService,
        IPrintService printService,
        IUserSession session,
        IAuditRepository audit,
        ISettingsService settings,
        IAppLogger logger)
    {
        _letters = letters;
        _templates = templates;
        _issueService = issueService;
        _printService = printService;
        _session = session;
        _audit = audit;
        _settings = settings;
        _logger = logger;
    }

    public override string Title => "سوابق نامه‌ها";
    public override string Subtitle => "جستجو بر اساس شماره، نام، کد ملی، شماره عضویت، تاریخ و نوع نامه";

    public ObservableCollection<Letter> Results { get; } = new();
    public ObservableCollection<LetterTemplate> TemplateFilter { get; } = new();

    // معیارهای جستجو
    [ObservableProperty] private string _freeText = string.Empty;
    [ObservableProperty] private string _letterNumber = string.Empty;
    [ObservableProperty] private string _nationalId = string.Empty;
    [ObservableProperty] private string _fromDate = string.Empty;
    [ObservableProperty] private string _toDate = string.Empty;
    [ObservableProperty] private LetterTemplate? _selectedTemplateFilter;
    [ObservableProperty] private Letter? _selectedLetter;
    [ObservableProperty] private int _resultCount;

    public bool IsAdmin => _session.IsAdmin;

    public override async Task OnNavigatedToAsync()
    {
        if (TemplateFilter.Count == 0)
        {
            var templates = await _templates.GetAllAsync(onlyActive: false);
            TemplateFilter.Clear();
            TemplateFilter.Add(new LetterTemplate { Id = 0, Title = "همهٔ انواع نامه" });
            foreach (var t in templates) TemplateFilter.Add(t);
            SelectedTemplateFilter = TemplateFilter[0];
        }

        await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusMessage = null;

            var query = new LetterSearchQuery
            {
                FreeText = FreeText,
                LetterNumber = LetterNumber,
                NationalId = NationalId,
                FromDate = FromDate,
                ToDate = ToDate,
                TemplateId = SelectedTemplateFilter is { Id: > 0 } t ? t.Id : null,
                PageSize = 500
            };

            var list = await _letters.SearchAsync(query);
            Results.Clear();
            foreach (var l in list) Results.Add(l);
            ResultCount = list.Count;

            if (ResultCount == 0)
                StatusMessage = "موردی با این معیارها یافت نشد.";
        }
        catch (Exception ex)
        {
            _logger.Error("جستجوی سوابق ناموفق بود.", ex);
            StatusMessage = "جستجو با خطا مواجه شد. جزئیات در فایل لاگ ثبت شد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FreeText = LetterNumber = NationalId = FromDate = ToDate = string.Empty;
        SelectedTemplateFilter = TemplateFilter.FirstOrDefault();
        _ = SearchAsync();
    }

    /// <summary>چاپ مجدد نامهٔ انتخاب‌شده با همان شماره و مقادیر قبلی.</summary>
    [RelayCommand]
    private async Task ReprintAsync()
    {
        if (SelectedLetter is null)
        {
            ShowMessage("ابتدا یک نامه را از فهرست انتخاب کنید.", "چاپ مجدد", MessageBoxImage.Information);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "در حال بازتولید سند...";

            var regen = await _issueService.ReprintAsync(SelectedLetter.Id, null, 1);
            if (regen.IsFailure)
            {
                ShowMessage(regen.Error!, "خطا", MessageBoxImage.Error);
                return;
            }

            var docPath = regen.Value.GeneratedDocxPath;

            // چاپ سند بازتولیدشده (شمارندهٔ چاپ افزایش می‌یابد)
            var printerName = _settings.Get(SettingKeys.DefaultPrinter);
            var print = await _issueService.PrintDocumentAsync(
                SelectedLetter.Id, docPath, string.IsNullOrWhiteSpace(printerName) ? null : printerName, 1);

            var msg = $"سند نامهٔ {SelectedLetter.LetterNumber} بازتولید شد.\n\n" +
                      (print.IsSuccess ? "✓ برای چاپ ارسال شد." : $"⚠ چاپ ناموفق بود: {print.Error}") +
                      $"\n\nفایل:\n{docPath}";

            ShowMessage(msg, "چاپ مجدد", MessageBoxImage.Information);
            TryOpen(docPath);

            await SearchAsync(); // به‌روزرسانی شمارندهٔ چاپ در فهرست
        }
        catch (Exception ex)
        {
            _logger.Error("چاپ مجدد ناموفق بود.", ex);
            ShowMessage($"چاپ مجدد با خطا متوقف شد:\n{ex.Message}", "خطا", MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }

    /// <summary>حذف سابقه — فقط برای مدیر. حذف نرم است (رکورد برای ممیزی می‌ماند).</summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedLetter is null)
        {
            ShowMessage("ابتدا یک نامه را انتخاب کنید.", "حذف سابقه", MessageBoxImage.Information);
            return;
        }

        if (!_session.IsAdmin)
        {
            ShowMessage("حذف سابقه فقط برای مدیر مجاز است. ابتدا از بخش تنظیمات با حساب مدیر وارد شوید.",
                "دسترسی محدود", MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"آیا از حذف نامهٔ شمارهٔ «{SelectedLetter.LetterNumber}» مطمئن هستید؟\n\n" +
            "این نامه از فهرست حذف می‌شود اما برای ممیزی در سیستم باقی می‌ماند.",
            "تأیید حذف", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var number = SelectedLetter.LetterNumber;
            await _letters.SoftDeleteAsync(SelectedLetter.Id, _session.UserName);
            await _audit.WriteAsync(new AuditEntry
            {
                ActionType = "DeleteLetter",
                EntityType = "Letter",
                EntityId = SelectedLetter.Id,
                UserName = _session.UserName,
                OccurredAt = DateTime.UtcNow,
                Details = $"حذف نامهٔ شمارهٔ {number}"
            });

            _logger.Info($"نامهٔ {number} توسط «{_session.UserName}» حذف شد.");
            await SearchAsync();
            StatusMessage = $"نامهٔ {number} حذف شد.";
        }
        catch (Exception ex)
        {
            _logger.Error("حذف سابقه ناموفق بود.", ex);
            ShowMessage($"حذف با خطا متوقف شد:\n{ex.Message}", "خطا", MessageBoxImage.Error);
        }
    }

    private void TryOpen(string path)
    {
        try
        {
            if (File.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex) { _logger.Warn($"باز کردن سند ناموفق بود: {ex.Message}"); }
    }

    private static void ShowMessage(string message, string title, MessageBoxImage icon)
        => MessageBox.Show(message, title, MessageBoxButton.OK, icon,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
}
