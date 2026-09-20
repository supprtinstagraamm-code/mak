using System.ComponentModel;
using System.Windows;
using CertificateAutomation.UI.ViewModels;

namespace CertificateAutomation.UI.Views;

/// <summary>پنجره اصلی برنامه. تمام منطق در ShellViewModel است.</summary>
public partial class ShellWindow : Window
{
    private readonly ShellViewModel _viewModel;

    /// <summary>
    /// اگر false باشد (پیش‌فرض)، زدن ضربدر پنجره را می‌بندد نه برنامه را — پنجره مخفی و
    /// برنامه در سینی سیستم باقی می‌ماند تا یادآورها همچنان بررسی شوند. برای خروج واقعی
    /// (از دکمهٔ «خروج» یا منوی سینی)، این مقدار پیش از فراخوانی Close/Shutdown، true می‌شود.
    /// </summary>
    public bool AllowClose { get; set; }

    public ShellWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // دکمهٔ «خروج» در نوار بالا، از طریق این callback واقعاً برنامه را می‌بندد.
        _viewModel.RequestExit = () =>
        {
            AllowClose = true;
            Close();
        };

        // آیکون پنجره (نوار عنوان و نوار وظیفه) از همان لوگوی تنظیمات خوانده می‌شود.
        ApplyWindowIcon();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // با تغییر لوگو در تنظیمات، آیکون پنجره هم بلافاصله عوض می‌شود.
        if (e.PropertyName == nameof(ShellViewModel.LogoImage))
            ApplyWindowIcon();
    }

    /// <summary>
    /// تنظیم آیکون پنجره از روی لوگو. اگر لوگویی تنظیم نشده باشد، آیکون پیش‌فرض می‌ماند.
    /// </summary>
    private void ApplyWindowIcon()
    {
        try
        {
            if (_viewModel.LogoImage is System.Windows.Media.ImageSource src)
                Icon = src;
        }
        catch
        {
            // اگر تصویر برای آیکون مناسب نبود، بی‌سروصدا نادیده گرفته می‌شود.
        }
    }
}
