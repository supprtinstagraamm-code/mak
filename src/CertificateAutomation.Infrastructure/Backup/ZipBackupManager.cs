using System.IO.Compression;
using System.Text.Json;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Infrastructure.Data;
using Dapper;

namespace CertificateAutomation.Infrastructure.Backup;

/// <summary>
/// تهیه و بازیابی نسخهٔ پشتیبان به‌صورت یک فایل ZIP.
/// نسخهٔ پشتیبان شامل: پایگاه داده + فایل تنظیمات (bootstrap) + پوشهٔ قالب‌ها + لوگو + فایل مانیفست.
///
/// نکتهٔ مهم دربارهٔ پایگاه داده: چون از حالت WAL استفاده می‌کنیم، پیش از کپی، دستور
/// wal_checkpoint اجرا می‌شود تا همهٔ تغییرات معلق در فایل اصلی نوشته شوند و نسخهٔ پشتیبان کامل باشد.
/// </summary>
public class ZipBackupManager : IBackupService
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;

    public ZipBackupManager(ISqliteConnectionFactory factory, ISettingsService settings, IAppLogger logger)
    {
        _factory = factory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Result<string>> CreateBackupAsync(string? targetFolder = null, CancellationToken ct = default)
    {
        try
        {
            var backupFolder = targetFolder ?? _settings.Get(SettingKeys.BackupFolder, AppPaths.DefaultBackupFolder);
            Directory.CreateDirectory(backupFolder);

            // نام فایل با تاریخ و ساعت شمسی: Backup_1405-03-11_1432.zip
            var stamp = $"{PersianDate.Today().Replace('/', '-')}_{DateTime.Now:HHmm}";
            var zipPath = Path.Combine(backupFolder, $"Backup_{stamp}.zip");

            // اطمینان از نوشته‌شدن تغییرات WAL در فایل اصلی
            CheckpointDatabase();

            // فایل‌ها را ابتدا به یک پوشهٔ موقت کپی می‌کنیم تا قفل فایل زنده مشکل ایجاد نکند.
            var tempDir = Path.Combine(AppPaths.TempFolder, "backup_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            try
            {
                // ۱) پایگاه داده
                var dbPath = AppPaths.DatabaseFile;
                if (File.Exists(dbPath))
                    File.Copy(dbPath, Path.Combine(tempDir, "certdb.sqlite"), overwrite: true);

                // ۲) فایل bootstrap (مسیر پایگاه داده)
                if (File.Exists(AppPaths.BootstrapFile))
                    File.Copy(AppPaths.BootstrapFile, Path.Combine(tempDir, "bootstrap.json"), overwrite: true);

                // ۳) قالب‌ها
                var templatesFolder = _settings.Get(SettingKeys.TemplatesFolder);
                if (Directory.Exists(templatesFolder))
                    CopyDirectory(templatesFolder, Path.Combine(tempDir, "Templates"));

                // ۴) لوگو
                var logoPath = _settings.Get(SettingKeys.LogoPath);
                if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                    File.Copy(logoPath, Path.Combine(tempDir, "logo" + Path.GetExtension(logoPath)), overwrite: true);

                // ۵) مانیفست: نسخهٔ برنامه، نسخهٔ اسکیما، زمان تهیه
                var manifest = new
                {
                    AppVersion = "1.0.0",
                    SchemaVersion = DbInitializer.CurrentSchemaVersion,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    CreatedAtJalali = $"{PersianDate.Today()} {PersianDate.CurrentTime()}"
                };
                await File.WriteAllTextAsync(Path.Combine(tempDir, "manifest.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), ct);

                // ساخت فایل ZIP
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                _logger.Info($"نسخهٔ پشتیبان ساخته شد: {zipPath}");
                return Result.Success(zipPath);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("تهیهٔ نسخهٔ پشتیبان ناموفق بود.", ex);
            return Result.Failure<string>($"تهیهٔ نسخهٔ پشتیبان ممکن نشد: {ex.Message}");
        }
    }

    public async Task<Result> RestoreBackupAsync(string zipPath, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            return Result.Failure($"فایل پشتیبان یافت نشد:\n{zipPath}");

        try
        {
            // پیش از بازیابی، از وضعیت فعلی یک نسخهٔ ایمنی می‌گیریم تا اگر بازیابی اشتباه بود، برگشت‌پذیر باشد.
            var safety = await CreateBackupAsync(ct: ct);
            if (safety.IsSuccess)
                _logger.Info($"نسخهٔ ایمنی پیش از بازیابی: {safety.Value}");

            var extractDir = Path.Combine(AppPaths.TempFolder, "restore_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(extractDir);

            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

                // اعتبارسنجی: فایل پایگاه داده باید در پشتیبان باشد.
                var srcDb = Path.Combine(extractDir, "certdb.sqlite");
                if (!File.Exists(srcDb))
                    return Result.Failure("فایل پشتیبان معتبر نیست (پایگاه داده در آن یافت نشد).");

                // بستن اتصال‌های احتمالی WAL پیش از جایگزینی فایل
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                // ۱) بازگردانی پایگاه داده
                var destDb = AppPaths.DatabaseFile;
                Directory.CreateDirectory(Path.GetDirectoryName(destDb)!);
                // فایل‌های جانبی WAL/SHM قدیمی حذف شوند تا با دیتابیس جدید تداخل نکنند.
                foreach (var side in new[] { destDb + "-wal", destDb + "-shm" })
                    if (File.Exists(side)) File.Delete(side);
                File.Copy(srcDb, destDb, overwrite: true);

                // ۲) بازگردانی قالب‌ها
                var srcTemplates = Path.Combine(extractDir, "Templates");
                if (Directory.Exists(srcTemplates))
                {
                    var destTemplates = _settings.Get(SettingKeys.TemplatesFolder, AppPaths.DefaultTemplatesFolder);
                    Directory.CreateDirectory(destTemplates);
                    CopyDirectory(srcTemplates, destTemplates);
                }

                _logger.Info($"بازیابی از نسخهٔ پشتیبان انجام شد: {zipPath}");
                return Result.Success();
            }
            finally
            {
                TryDeleteDirectory(extractDir);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("بازیابی نسخهٔ پشتیبان ناموفق بود.", ex);
            return Result.Failure($"بازیابی ممکن نشد: {ex.Message}");
        }
    }

    public IReadOnlyList<string> ListBackups(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            return Directory.GetFiles(folder, "Backup_*.zip")
                .OrderByDescending(f => File.GetCreationTimeUtc(f))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Error("خواندن فهرست نسخه‌های پشتیبان ناموفق بود.", ex);
            return Array.Empty<string>();
        }
    }

    /// <summary>اجرای wal_checkpoint تا تغییرات معلق در فایل اصلی نوشته شوند.</summary>
    private void CheckpointDatabase()
    {
        try
        {
            using var db = _factory.Create();
            db.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        }
        catch (Exception ex)
        {
            _logger.Warn($"checkpoint پایگاه داده ناموفق بود (نسخهٔ پشتیبان ممکن است کمی قدیمی باشد): {ex.Message}");
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { _logger.Warn($"حذف پوشهٔ موقت ناموفق بود: {ex.Message}"); }
    }
}
