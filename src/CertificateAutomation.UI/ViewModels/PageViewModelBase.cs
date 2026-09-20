using CommunityToolkit.Mvvm.ComponentModel;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>پایه تمام ViewModelهای صفحه‌ها.</summary>
public abstract partial class PageViewModelBase : ObservableObject
{
    /// <summary>عنوان صفحه که در سربرگ نمایش داده می‌شود.</summary>
    public abstract string Title { get; }

    /// <summary>توضیح کوتاه زیر عنوان.</summary>
    public virtual string Subtitle => string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>هنگام ورود به صفحه صدا زده می‌شود. برای بارگذاری داده.</summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
}
