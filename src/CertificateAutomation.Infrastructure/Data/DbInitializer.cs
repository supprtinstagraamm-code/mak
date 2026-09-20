using System.Data;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using Dapper;

namespace CertificateAutomation.Infrastructure.Data;

/// <summary>
/// ساخت پایگاه داده در اولین اجرا و اعمال به‌روزرسانی‌های بعدی اسکیما.
/// این کلاس تنها جایی است که ساختار جدول‌ها تعریف می‌شود.
/// </summary>
public class DbInitializer
{
    /// <summary>نسخه فعلی اسکیما. با هر تغییر ساختار، یک واحد افزایش می‌یابد.</summary>
    public const int CurrentSchemaVersion = 2;

    private readonly ISqliteConnectionFactory _factory;
    private readonly IPasswordHasher _hasher;
    private readonly IAppLogger _logger;

    public DbInitializer(ISqliteConnectionFactory factory, IPasswordHasher hasher, IAppLogger logger)
    {
        _factory = factory;
        _hasher = hasher;
        _logger = logger;
    }

    /// <summary>ساخت/به‌روزرسانی پایگاه داده. در صورت خطا برنامه نباید بالا بیاید.</summary>
    public Result Initialize()
    {
        try
        {
            AppPaths.EnsureFolders();

            using var db = _factory.Create();
            var version = GetSchemaVersion(db);

            if (version == 0)
            {
                _logger.Info("پایگاه داده یافت نشد؛ ساخت اسکیما اولیه آغاز شد.");
                CreateSchema(db);
                SeedData(db);
                SetSchemaVersion(db, CurrentSchemaVersion);
                _logger.Info("پایگاه داده با موفقیت ساخته شد.");
            }
            else if (version < CurrentSchemaVersion)
            {
                _logger.Info($"ارتقای اسکیما از نسخه {version} به {CurrentSchemaVersion}.");
                Migrate(db, version);
                SetSchemaVersion(db, CurrentSchemaVersion);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("ساخت پایگاه داده ناموفق بود.", ex);
            return Result.Failure($"ساخت پایگاه داده ناموفق بود: {ex.Message}");
        }
    }

    private static int GetSchemaVersion(IDbConnection db)
    {
        var exists = db.ExecuteScalar<string?>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='SchemaVersion';");
        return exists is null ? 0 : db.ExecuteScalar<int>("SELECT Version FROM SchemaVersion LIMIT 1;");
    }

    private static void SetSchemaVersion(IDbConnection db, int version)
    {
        db.Execute("DELETE FROM SchemaVersion;");
        db.Execute("INSERT INTO SchemaVersion(Version) VALUES (@version);", new { version });
    }

    /// <summary>محل اعمال تغییرات اسکیما در نسخه‌های بعدی.</summary>
    private void Migrate(IDbConnection db, int fromVersion)
    {
        if (fromVersion < 2)
        {
            // افزودن جدول‌های یادآور برای دیتابیس‌های ساخته‌شده پیش از این نسخه.
            db.Execute(@"
CREATE TABLE IF NOT EXISTS Reminders (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Title       TEXT NOT NULL,
    Description TEXT,
    DayOfMonth  INTEGER NOT NULL,
    IsActive    INTEGER NOT NULL DEFAULT 1,
    CreatedAt   TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ReminderCompletions (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    ReminderId  INTEGER NOT NULL,
    YearMonth   TEXT NOT NULL,
    CompletedAt TEXT NOT NULL,
    CompletedBy TEXT,
    FOREIGN KEY (ReminderId) REFERENCES Reminders(Id) ON DELETE CASCADE,
    UNIQUE(ReminderId, YearMonth)
);
CREATE TABLE IF NOT EXISTS ReminderNotifications (
    ReminderId    INTEGER NOT NULL,
    YearMonth     TEXT NOT NULL,
    NotifiedAt    TEXT NOT NULL,
    PRIMARY KEY (ReminderId, YearMonth),
    FOREIGN KEY (ReminderId) REFERENCES Reminders(Id) ON DELETE CASCADE
);");
        }
    }

    private static void CreateSchema(IDbConnection db)
    {
        db.Execute(SchemaSql);
    }

    private void SeedData(IDbConnection db)
    {
        // کاربر مدیر پیش‌فرض؛ برنامه در اولین ورود، تغییر رمز را اجباری می‌کند.
        var (hash, salt) = _hasher.Hash("admin123");
        db.Execute(@"
INSERT INTO AppUsers (Username, PasswordHash, Salt, Role, IsActive, CreatedAt)
VALUES ('admin', @hash, @salt, 'Admin', 1, @now);",
            new { hash, salt, now = DateTime.UtcNow.ToString("o") });

        // تنظیمات پیش‌فرض
        var defaults = new Dictionary<string, string>
        {
            [SettingKeys.CompanyName] = "شرکت نمونه",
            [SettingKeys.LogoPath] = "",
            [SettingKeys.ExcelPath] = "",
            [SettingKeys.TemplatesFolder] = AppPaths.DefaultTemplatesFolder,
            [SettingKeys.OutputFolder] = AppPaths.DefaultOutputFolder,
            [SettingKeys.BackupFolder] = AppPaths.DefaultBackupFolder,
            [SettingKeys.DefaultPrinter] = "",
            [SettingKeys.NumberFormat] = "{year}/{serial:00000}",
            [SettingKeys.Theme] = "Light",
            [SettingKeys.UsePersianDigits] = "true",
            [SettingKeys.ArchivePdfEnabled] = "true",
            [SettingKeys.AutoImportOnStartup] = "false",
            [SettingKeys.AdminLockMinutes] = "15",
            [SettingKeys.MustChangePassword] = "true"
        };

        foreach (var kv in defaults)
        {
            db.Execute("INSERT INTO Settings (Key, Value, UpdatedAt) VALUES (@Key, @Value, @Now);",
                new { Key = kv.Key, Value = kv.Value, Now = DateTime.UtcNow.ToString("o") });
        }

        // نگاشت ستون‌های استاندارد Excel به کلیدهای Placeholder
        var mappings = new (string Header, string Key, string Display, bool Core, int Sort)[]
        {
            ("کد پرسنلی",    "کد_پرسنلی",    "کد پرسنلی",    true, 1),
            ("شماره عضویت",  "شماره_عضویت",  "شماره عضویت",  true, 2),
            ("نام",          "نام",          "نام",          true, 3),
            ("نام خانوادگی", "نام_خانوادگی", "نام خانوادگی", true, 4),
            ("نام پدر",      "نام_پدر",      "نام پدر",      true, 5),
            ("کد ملی",       "کد_ملی",       "کد ملی",       true, 6),
            ("سمت",          "سمت",          "سمت",          true, 7),
            ("واحد",         "واحد",         "واحد",         true, 8),
            ("شماره تماس",   "شماره_تماس",   "شماره تماس",   true, 9),
            ("تاریخ استخدام","تاریخ_استخدام","تاریخ استخدام",true, 10),
            // فیلدهای اضافی پرکاربرد در قالب‌های گواهی عضویت و حکم ماموریت
            // (غیر Core: در ExtraDataJson ذخیره می‌شوند و در قالب‌ها قابل استفاده‌اند)
            ("محل صدور",     "محل_صدور",     "محل صدور",     false, 11),
            ("تاریخ تولد",   "تاریخ_تولد",   "تاریخ تولد",   false, 12),
            ("شماره شناسنامه","شماره_شناسنامه","شماره شناسنامه",false, 13),
            ("نوع عضویت",    "نوع_عضویت",    "نوع عضویت",    false, 14)
        };

        foreach (var m in mappings)
        {
            db.Execute(@"
INSERT INTO ColumnMappings (ExcelHeader, PlaceholderKey, DisplayName, IsCore, SortOrder)
VALUES (@Header, @Key, @Display, @Core, @Sort);",
                new { m.Header, m.Key, m.Display, Core = m.Core ? 1 : 0, m.Sort });
        }

        // شمارنده سال جاری
        db.Execute(@"
INSERT INTO NumberSequences (JalaliYear, LastSerial, Format, UpdatedAt)
VALUES (@year, 0, '{year}/{serial:00000}', @now);",
            new { year = PersianDate.CurrentYear, now = DateTime.UtcNow.ToString("o") });
    }

    /// <summary>اسکریپت کامل ساخت اسکیما.</summary>
    private const string SchemaSql = @"
CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);

CREATE TABLE Employees (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    PersonnelCode   TEXT,
    MembershipNo    TEXT,
    FirstName       TEXT NOT NULL,
    LastName        TEXT NOT NULL,
    FatherName      TEXT,
    NationalId      TEXT,
    Position        TEXT,
    Unit            TEXT,
    Phone           TEXT,
    HireDate        TEXT,
    ExtraDataJson   TEXT,
    RowHash         TEXT,
    IsActive        INTEGER NOT NULL DEFAULT 1,
    ImportedAt      TEXT NOT NULL
);
CREATE INDEX IX_Emp_National   ON Employees(NationalId);
CREATE INDEX IX_Emp_Membership ON Employees(MembershipNo);
CREATE INDEX IX_Emp_Name       ON Employees(LastName, FirstName);

CREATE TABLE ColumnMappings (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    ExcelHeader    TEXT NOT NULL UNIQUE,
    PlaceholderKey TEXT NOT NULL,
    DisplayName    TEXT NOT NULL,
    IsCore         INTEGER NOT NULL DEFAULT 0,
    SortOrder      INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE LetterTemplates (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Title         TEXT NOT NULL,
    FileName      TEXT NOT NULL,
    Category      TEXT,
    DefaultCopies INTEGER NOT NULL DEFAULT 1,
    IsActive      INTEGER NOT NULL DEFAULT 1,
    LastScannedAt TEXT,
    CreatedAt     TEXT NOT NULL
);
CREATE UNIQUE INDEX IX_Tpl_File ON LetterTemplates(FileName);

CREATE TABLE TemplatePlaceholders (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    TemplateId     INTEGER NOT NULL REFERENCES LetterTemplates(Id) ON DELETE CASCADE,
    PlaceholderKey TEXT NOT NULL,
    IsRequired     INTEGER NOT NULL DEFAULT 1,
    UNIQUE(TemplateId, PlaceholderKey)
);

CREATE TABLE NumberSequences (
    JalaliYear  INTEGER PRIMARY KEY,
    LastSerial  INTEGER NOT NULL DEFAULT 0,
    Format      TEXT    NOT NULL DEFAULT '{year}/{serial:00000}',
    UpdatedAt   TEXT    NOT NULL
);

CREATE TABLE Letters (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    LetterNumber      TEXT    NOT NULL UNIQUE,
    JalaliYear        INTEGER NOT NULL,
    Serial            INTEGER NOT NULL,
    IssuedAtUtc       TEXT    NOT NULL,
    JalaliDate        TEXT    NOT NULL,
    IssuedTime        TEXT    NOT NULL,
    EmployeeId        INTEGER REFERENCES Employees(Id) ON DELETE SET NULL,
    SnapFirstName     TEXT,
    SnapLastName      TEXT,
    SnapNationalId    TEXT,
    SnapMembershipNo  TEXT,
    SnapPosition      TEXT,
    SnapUnit          TEXT,
    TemplateId        INTEGER REFERENCES LetterTemplates(Id) ON DELETE SET NULL,
    SnapTemplateTitle TEXT NOT NULL,
    DataSnapshotJson  TEXT,
    PrintCount        INTEGER NOT NULL DEFAULT 0,
    LastPrintedAt     TEXT,
    ArchivePdfPath    TEXT,
    Description       TEXT,
    IsDeleted         INTEGER NOT NULL DEFAULT 0,
    DeletedAt         TEXT,
    DeletedBy         TEXT,
    UNIQUE(JalaliYear, Serial)
);
CREATE INDEX IX_Let_Date ON Letters(JalaliDate);
CREATE INDEX IX_Let_Nid  ON Letters(SnapNationalId);
CREATE INDEX IX_Let_Tpl  ON Letters(TemplateId);

CREATE TABLE PrintJobs (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    LetterId    INTEGER NOT NULL REFERENCES Letters(Id) ON DELETE CASCADE,
    PrinterName TEXT,
    Copies      INTEGER NOT NULL DEFAULT 1,
    PrintedAt   TEXT NOT NULL,
    Status      TEXT NOT NULL,
    ErrorText   TEXT
);

CREATE TABLE Settings (
    Key       TEXT PRIMARY KEY,
    Value     TEXT,
    UpdatedAt TEXT
);

CREATE TABLE AppUsers (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Username     TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    Salt         TEXT NOT NULL,
    Role         TEXT NOT NULL,
    IsActive     INTEGER NOT NULL DEFAULT 1,
    CreatedAt    TEXT NOT NULL
);

CREATE TABLE AuditLog (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    ActionType TEXT NOT NULL,
    EntityType TEXT,
    EntityId   INTEGER,
    UserName   TEXT,
    OccurredAt TEXT NOT NULL,
    Details    TEXT
);
CREATE INDEX IX_Audit_Date ON AuditLog(OccurredAt);

-- یادآورهای ماهانهٔ تکرارشونده (مثلاً «روز ۲۰ هر ماه: پر کردن فرم سپهر»)
CREATE TABLE Reminders (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Title       TEXT NOT NULL,
    Description TEXT,
    DayOfMonth  INTEGER NOT NULL,   -- ۱ تا ۳۱؛ اگر ماه کوتاه‌تر بود، آخرین روز ماه در نظر گرفته می‌شود
    IsActive    INTEGER NOT NULL DEFAULT 1,
    CreatedAt   TEXT NOT NULL
);

-- ثبت انجام‌شدن هر یادآور در هر ماه به‌طور جداگانه؛ با شروع ماه جدید خودکار پاک (تیک نخورده) دیده می‌شود
CREATE TABLE ReminderCompletions (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    ReminderId  INTEGER NOT NULL,
    YearMonth   TEXT NOT NULL,      -- قالب ""YYYY-MM"" میلادی، فقط برای کلید یکتای داخلی
    CompletedAt TEXT NOT NULL,
    CompletedBy TEXT,
    FOREIGN KEY (ReminderId) REFERENCES Reminders(Id) ON DELETE CASCADE,
    UNIQUE(ReminderId, YearMonth)
);

-- آخرین باری که هر یادآور به کاربر نمایش/اعلان داده شده، تا در یک ماه دوبار مزاحم نشود
CREATE TABLE ReminderNotifications (
    ReminderId    INTEGER NOT NULL,
    YearMonth     TEXT NOT NULL,
    NotifiedAt    TEXT NOT NULL,
    PRIMARY KEY (ReminderId, YearMonth),
    FOREIGN KEY (ReminderId) REFERENCES Reminders(Id) ON DELETE CASCADE
);
";
}
