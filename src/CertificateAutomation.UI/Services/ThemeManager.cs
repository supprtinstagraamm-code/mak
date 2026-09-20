using System.Windows;
using CertificateAutomation.Application.Abstractions;

// alias برای جلوگیری از تداخل نام: پروژه‌ای به نام CertificateAutomation.Application داریم،
// بنابراین نوشتن Application.Current درون این Namespace به‌جای WPF به آن پروژه ارجاع می‌دهد.
using WpfApp = System.Windows.Application;

namespace CertificateAutomation.UI.Services;

/// <summary>
/// تعویض تم روشن/تیره در زمان اجرا. کلیدهای هر دو فایل تم یکسان‌اند،
/// بنابراین جایگزینی دیکشنری اول کافی است و نیازی به ری‌استارت نیست.
/// </summary>
public class ThemeManager
{
    private readonly ISettingsService _settings;

    public ThemeManager(ISettingsService settings) => _settings = settings;

    public bool IsDark { get; private set; }

    /// <summary>اعمال تم ذخیره‌شده هنگام شروع برنامه.</summary>
    public void ApplySaved()
        => Apply(string.Equals(_settings.Get(SettingKeys.Theme, "Light"), "Dark", StringComparison.OrdinalIgnoreCase));

    public void Apply(bool dark)
    {
        IsDark = dark;

        var uri = new Uri(dark
            ? "pack://application:,,,/Themes/Dark.xaml"
            : "pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);
        var dictionary = new ResourceDictionary { Source = uri };

        // دیکشنری تم همیشه اولین عضو است؛ فقط همان جایگزین می‌شود تا استایل‌ها حفظ شوند.
        var merged = WpfApp.Current.Resources.MergedDictionaries;
        if (merged.Count == 0) merged.Add(dictionary);
        else merged[0] = dictionary;
    }

    public async Task ToggleAsync()
    {
        Apply(!IsDark);
        await _settings.SetAsync(SettingKeys.Theme, IsDark ? "Dark" : "Light");
    }
}
