using System.Text.Json;

namespace CertificateAutomation.Infrastructure.Data;

/// <summary>
/// مسیرهای برنامه. مسیر پایگاه داده خودش یک تنظیم است، بنابراین نمی‌تواند داخل پایگاه داده
/// نگهداری شود؛ برای همین در یک فایل کوچک bootstrap.json کنار برنامه ذخیره می‌شود.
/// همه مسیرها زیر %ProgramData% هستند تا کاربر بدون دسترسی Administrator هم بتواند بنویسد.
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "CertificateAutomation";

    /// <summary>پوشه ریشه داده‌های برنامه.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AppFolderName);

    public static string BootstrapFile => Path.Combine(Root, "bootstrap.json");
    public static string LogsFolder => Path.Combine(Root, "logs");
    public static string TempFolder => Path.Combine(Root, "temp");
    public static string DefaultTemplatesFolder => Path.Combine(Root, "Templates");
    public static string DefaultOutputFolder => Path.Combine(Root, "Output");
    public static string DefaultBackupFolder => Path.Combine(Root, "Backups");

    private static BootstrapConfig? _config;

    /// <summary>مسیر کامل فایل پایگاه داده.</summary>
    public static string DatabaseFile => Path.Combine(LoadConfig().DatabaseFolder, "certdb.sqlite");

    /// <summary>ساخت تمام پوشه‌های موردنیاز در اولین اجرا.</summary>
    public static void EnsureFolders()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsFolder);
        Directory.CreateDirectory(TempFolder);
        Directory.CreateDirectory(DefaultTemplatesFolder);
        Directory.CreateDirectory(DefaultOutputFolder);
        Directory.CreateDirectory(DefaultBackupFolder);
        Directory.CreateDirectory(LoadConfig().DatabaseFolder);
    }

    public static BootstrapConfig LoadConfig()
    {
        if (_config is not null) return _config;

        try
        {
            if (File.Exists(BootstrapFile))
            {
                var json = File.ReadAllText(BootstrapFile);
                _config = JsonSerializer.Deserialize<BootstrapConfig>(json);
            }
        }
        catch
        {
            // فایل خراب: به مقادیر پیش‌فرض برمی‌گردیم تا برنامه بالا بیاید.
            _config = null;
        }

        _config ??= new BootstrapConfig { DatabaseFolder = Path.Combine(Root, "Data") };
        return _config;
    }

    public static void SaveConfig(BootstrapConfig config)
    {
        Directory.CreateDirectory(Root);
        _config = config;
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(BootstrapFile, json);
    }
}

/// <summary>تنظیمات پایه که پیش از باز شدن پایگاه داده لازم‌اند.</summary>
public class BootstrapConfig
{
    public string DatabaseFolder { get; set; } = string.Empty;
}
