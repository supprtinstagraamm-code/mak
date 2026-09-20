using System.Collections.ObjectModel;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Entities;
using CommunityToolkit.Mvvm.Input;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>یک ردیف قابل‌نمایش از گزارش فعالیت‌ها، با برچسب فارسی و آیکون مناسب برای نوع عملیات.</summary>
public class ActivityItem
{
    public string ActionLabel { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? UserName { get; set; }
    public string When { get; set; } = string.Empty;
    public string Icon { get; set; } = "•";
}

/// <summary>
/// نمایش رویدادهای اخیر سیستم: صدور نامه، حذف سابقه، تغییر شماره، ورود مدیر و غیره.
/// از همان جدول ممیزی (AuditLog) استفاده می‌کند که در فازهای قبل ساخته شده بود.
/// </summary>
public partial class ActivityLogViewModel : PageViewModelBase
{
    private readonly IAuditRepository _audit;
    private readonly IAppLogger _logger;

    public ActivityLogViewModel(IAuditRepository audit, IAppLogger logger)
    {
        _audit = audit;
        _logger = logger;
    }

    public override string Title => "لاگ فعالیت‌ها";
    public override string Subtitle => "رویدادهای اخیر سیستم — صدور نامه، تغییرات و ورود مدیر";

    public ObservableCollection<ActivityItem> Items { get; } = new();

    public override Task OnNavigatedToAsync() => LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var entries = await _audit.GetRecentAsync(200);

            Items.Clear();
            foreach (var e in entries) Items.Add(ToActivityItem(e));

            StatusMessage = Items.Count == 0 ? "هنوز رویدادی ثبت نشده است." : null;
        }
        catch (Exception ex)
        {
            _logger.Error("بارگذاری لاگ فعالیت‌ها ناموفق بود.", ex);
            StatusMessage = "بارگذاری لاگ با خطا مواجه شد.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static ActivityItem ToActivityItem(AuditEntry e)
    {
        var (label, icon) = e.ActionType switch
        {
            "IssueLetter" => ("صدور نامه", "📄"),
            "DeleteLetter" => ("حذف سابقهٔ نامه", "🗑"),
            "ChangeNumber" => ("تغییر شمارندهٔ نامه", "🔢"),
            "Login" => ("ورود مدیر", "🔑"),
            "Logout" => ("خروج مدیر", "🚪"),
            "RestoreBackup" => ("بازیابی نسخهٔ پشتیبان", "♻"),
            "CreateBackup" => ("تهیهٔ نسخهٔ پشتیبان", "💾"),
            _ => (e.ActionType, "•")
        };

        return new ActivityItem
        {
            ActionLabel = label,
            Icon = icon,
            Details = e.Details,
            UserName = e.UserName,
            When = $"{PersianDate.ToPersian(e.OccurredAt.ToLocalTime())}  {e.OccurredAt.ToLocalTime():HH:mm}"
        };
    }
}
