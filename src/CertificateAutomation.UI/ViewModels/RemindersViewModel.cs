using System.Collections.ObjectModel;
using System.Windows;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>
/// صفحهٔ یادآورهای ماهانه. هر یادآور فقط یک‌بار ساخته می‌شود (روز مشخصی از ماه)
/// و خودکار هر ماه دوباره در فهرست «انجام‌نشده» ظاهر می‌شود.
/// </summary>
public partial class RemindersViewModel : PageViewModelBase
{
    private readonly IReminderService _reminders;
    private readonly IUserSession _session;
    private readonly IAppLogger _logger;

    public RemindersViewModel(IReminderService reminders, IUserSession session, IAppLogger logger)
    {
        _reminders = reminders;
        _session = session;
        _logger = logger;
    }

    public override string Title => "یادآورهای ماهانه";
    public override string Subtitle => "کارهای تکرارشوندهٔ هر ماه — یک بار بسازید، هر ماه خودکار برمی‌گردد";

    public ObservableCollection<Reminder> Items { get; } = new();

    // فرم افزودن/ویرایش
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private long _editingId;
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editDescription = string.Empty;
    [ObservableProperty] private int _editDayOfMonth = 1;
    [ObservableProperty] private bool _editIsActive = true;
    [ObservableProperty] private string _editorHeading = "یادآور جدید";

    public override async Task OnNavigatedToAsync() => await LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var list = await _reminders.GetChecklistAsync();
            Items.Clear();
            foreach (var r in list) Items.Add(r);

            if (Items.Count == 0)
                StatusMessage = "هنوز یادآوری ثبت نشده. با دکمهٔ «یادآور جدید» شروع کنید.";
            else
                StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری یادآورها ناموفق بود.", ex);
            StatusMessage = "بارگذاری یادآورها با خطا مواجه شد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenNewEditor()
    {
        EditingId = 0;
        EditTitle = string.Empty;
        EditDescription = string.Empty;
        EditDayOfMonth = 1;
        EditIsActive = true;
        EditorHeading = "یادآور جدید";
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void OpenEditEditor(Reminder? reminder)
    {
        if (reminder is null) return;
        EditingId = reminder.Id;
        EditTitle = reminder.Title;
        EditDescription = reminder.Description ?? string.Empty;
        EditDayOfMonth = reminder.DayOfMonth;
        EditIsActive = reminder.IsActive;
        EditorHeading = "ویرایش یادآور";
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CloseEditor() => IsEditorOpen = false;

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            ShowMessage("عنوان یادآور نمی‌تواند خالی باشد.", "یادآور", MessageBoxImage.Warning);
            return;
        }

        if (EditDayOfMonth is < 1 or > 31)
        {
            ShowMessage("روز ماه باید بین ۱ تا ۳۱ باشد.", "یادآور", MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (EditingId == 0)
            {
                await _reminders.AddAsync(EditTitle, EditDescription, EditDayOfMonth);
            }
            else
            {
                await _reminders.UpdateAsync(EditingId, EditTitle, EditDescription, EditDayOfMonth, EditIsActive);
            }

            IsEditorOpen = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("ذخیرهٔ یادآور ناموفق بود.", ex);
            ShowMessage($"ذخیره با خطا مواجه شد:\n{ex.Message}", "خطا", MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(Reminder? reminder)
    {
        if (reminder is null) return;

        var confirm = MessageBox.Show(
            $"یادآور «{reminder.Title}» حذف شود؟ این یادآور دیگر در هیچ ماهی نمایش داده نمی‌شود.",
            "حذف یادآور", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await _reminders.DeleteAsync(reminder.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("حذف یادآور ناموفق بود.", ex);
        }
    }

    /// <summary>تیک زدن/برداشتن «انجام شد» برای ماه جاری.</summary>
    [RelayCommand]
    private async Task ToggleDoneAsync(Reminder? reminder)
    {
        if (reminder is null) return;

        try
        {
            if (reminder.IsCompletedThisMonth)
                await _reminders.UnmarkDoneAsync(reminder.Id);
            else
                await _reminders.MarkDoneAsync(reminder.Id, _session.UserName);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("تغییر وضعیت یادآور ناموفق بود.", ex);
        }
    }

    private static void ShowMessage(string message, string title, MessageBoxImage icon)
        => MessageBox.Show(message, title, MessageBoxButton.OK, icon,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
}
