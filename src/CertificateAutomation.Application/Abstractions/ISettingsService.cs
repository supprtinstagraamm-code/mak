namespace CertificateAutomation.Application.Abstractions;

/// <summary>خواندن و نوشتن تنظیمات برنامه. مقادیر در جدول Settings نگهداری می‌شوند.</summary>
public interface ISettingsService
{
    /// <summary>بارگذاری تمام تنظیمات در حافظه. یک بار هنگام شروع برنامه صدا زده می‌شود.</summary>
    Task LoadAsync(CancellationToken ct = default);

    string Get(string key, string defaultValue = "");
    bool GetBool(string key, bool defaultValue = false);
    int GetInt(string key, int defaultValue = 0);

    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct = default);

    /// <summary>هنگام تغییر هر تنظیمی صدا زده می‌شود (برای مثال تعویض تم).</summary>
    event EventHandler<string>? SettingChanged;
}
