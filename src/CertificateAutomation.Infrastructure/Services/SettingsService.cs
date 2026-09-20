using System.Collections.Concurrent;
using System.Globalization;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Infrastructure.Data;
using Dapper;

namespace CertificateAutomation.Infrastructure.Services;

/// <summary>
/// تنظیمات در حافظه کش می‌شوند تا هر بار خواندن، یک کوئری به پایگاه داده نزند.
/// نوشتن هم‌زمان در حافظه و پایگاه داده انجام می‌شود.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    public SettingsService(ISqliteConnectionFactory factory, IAppLogger logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public event EventHandler<string>? SettingChanged;

    public Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            using var db = _factory.Create();
            var rows = db.Query<SettingRow>("SELECT Key, Value FROM Settings;");

            _cache.Clear();
            foreach (var row in rows)
                _cache[row.Key] = row.Value ?? string.Empty;

            _logger.Debug($"{_cache.Count} تنظیم بارگذاری شد.");
        }
        catch (Exception ex)
        {
            // نبود تنظیمات نباید مانع بالا آمدن برنامه شود؛ مقادیر پیش‌فرض استفاده می‌شوند.
            _logger.Error("بارگذاری تنظیمات ناموفق بود.", ex);
        }

        return Task.CompletedTask;
    }

    public string Get(string key, string defaultValue = "")
        => _cache.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : defaultValue;

    public bool GetBool(string key, bool defaultValue = false)
        => bool.TryParse(Get(key), out var result) ? result : defaultValue;

    public int GetInt(string key, int defaultValue = 0)
        => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
        => await SetManyAsync(new Dictionary<string, string> { [key] = value }, ct);

    public Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        using var tx = db.BeginTransaction();

        foreach (var kv in values)
        {
            db.Execute(@"
INSERT INTO Settings (Key, Value, UpdatedAt) VALUES (@Key, @Value, @Now)
ON CONFLICT(Key) DO UPDATE SET Value = @Value, UpdatedAt = @Now;",
                new { Key = kv.Key, Value = kv.Value, Now = DateTime.UtcNow.ToString("o") }, tx);

            _cache[kv.Key] = kv.Value;
        }

        tx.Commit();

        foreach (var kv in values)
            SettingChanged?.Invoke(this, kv.Key);

        return Task.CompletedTask;
    }
}

/// <summary>ساختار سطر جدول Settings برای نگاشت توسط Dapper.</summary>
internal class SettingRow
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}
