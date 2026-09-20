using System.Text;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Entities;
using CertificateAutomation.Infrastructure.Data;
using Dapper;

namespace CertificateAutomation.Infrastructure.Repositories;

/// <inheritdoc />
public class EmployeeRepository : IEmployeeRepository
{
    private const string SelectColumns = @"
Id, PersonnelCode, MembershipNo, FirstName, LastName, FatherName, NationalId,
Position, Unit, Phone, HireDate, ExtraDataJson, RowHash, IsActive, ImportedAt";

    private readonly ISqliteConnectionFactory _factory;
    private readonly IAppLogger _logger;

    public EmployeeRepository(ISqliteConnectionFactory factory, IAppLogger logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public Task<IReadOnlyList<Employee>> GetAllAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var sql = $"SELECT {SelectColumns} FROM Employees {(onlyActive ? "WHERE IsActive = 1" : "")} ORDER BY LastName, FirstName;";
        var rows = db.Query<Employee>(sql).ToList();
        return Task.FromResult<IReadOnlyList<Employee>>(rows);
    }

    public Task<Employee?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var row = db.QueryFirstOrDefault<Employee>(
            $"SELECT {SelectColumns} FROM Employees WHERE Id = @id;", new { id });
        return Task.FromResult(row);
    }

    public Task<IReadOnlyList<Employee>> SearchAsync(string term, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
            return GetAllAsync(onlyActive: true, ct);

        // ارقام فارسی کاربر به انگلیسی تبدیل می‌شوند تا با داده ذخیره‌شده هم‌خوان شود.
        var normalized = PersianDate.ToEnglishDigits(term.Trim());

        using var db = _factory.Create();
        var rows = db.Query<Employee>($@"
SELECT {SelectColumns} FROM Employees
WHERE IsActive = 1 AND (
    FirstName    LIKE @t OR
    LastName     LIKE @t OR
    NationalId   LIKE @t OR
    MembershipNo LIKE @t OR
    PersonnelCode LIKE @t OR
    (FirstName || ' ' || LastName) LIKE @t
)
ORDER BY LastName, FirstName
LIMIT @limit;",
            new { t = $"%{normalized}%", limit }).ToList();

        return Task.FromResult<IReadOnlyList<Employee>>(rows);
    }

    public Task<int> CountAsync(CancellationToken ct = default)
    {
        using var db = _factory.Create();
        return Task.FromResult(db.ExecuteScalar<int>("SELECT COUNT(*) FROM Employees WHERE IsActive = 1;"));
    }

    /// <summary>
    /// درج یا به‌روزرسانی دسته‌ای. کلید یکتایی برای تشخیص «همان شخص» به این ترتیب است:
    /// کد ملی، سپس کد پرسنلی، سپس شماره عضویت. اگر هیچ‌کدام نبود، رکورد جدید درج می‌شود.
    /// همه در یک تراکنش انجام می‌شود تا در صورت خطا، نیمه‌کاره نماند.
    /// </summary>
    public Task<ImportResult> UpsertBatchAsync(IEnumerable<Employee> employees, CancellationToken ct = default)
    {
        var result = new ImportResult();
        using var db = _factory.Create();
        using var tx = db.BeginTransaction();

        try
        {
            foreach (var emp in employees)
            {
                result.TotalRows++;

                var existingId = FindExistingId(db, tx, emp);

                if (existingId is null)
                {
                    db.Execute(@"
INSERT INTO Employees
(PersonnelCode, MembershipNo, FirstName, LastName, FatherName, NationalId,
 Position, Unit, Phone, HireDate, ExtraDataJson, RowHash, IsActive, ImportedAt)
VALUES
(@PersonnelCode, @MembershipNo, @FirstName, @LastName, @FatherName, @NationalId,
 @Position, @Unit, @Phone, @HireDate, @ExtraDataJson, @RowHash, 1, @ImportedAt);",
                        ToParams(emp), tx);
                    result.Inserted++;
                }
                else
                {
                    // اگر هش سطر تغییر نکرده، یعنی داده عوض نشده و از به‌روزرسانی صرف‌نظر می‌کنیم.
                    var currentHash = db.ExecuteScalar<string?>(
                        "SELECT RowHash FROM Employees WHERE Id = @id;", new { id = existingId }, tx);

                    if (!string.IsNullOrEmpty(emp.RowHash) && currentHash == emp.RowHash)
                    {
                        result.Skipped++;
                        continue;
                    }

                    db.Execute(@"
UPDATE Employees SET
    PersonnelCode = @PersonnelCode, MembershipNo = @MembershipNo,
    FirstName = @FirstName, LastName = @LastName, FatherName = @FatherName,
    NationalId = @NationalId, Position = @Position, Unit = @Unit, Phone = @Phone,
    HireDate = @HireDate, ExtraDataJson = @ExtraDataJson, RowHash = @RowHash,
    IsActive = 1, ImportedAt = @ImportedAt
WHERE Id = @Id;",
                        ToParams(emp, existingId.Value), tx);
                    result.Updated++;
                }
            }

            tx.Commit();
            _logger.Info($"بارگذاری کارکنان: {result.Inserted} جدید، {result.Updated} به‌روزرسانی، {result.Skipped} بدون تغییر.");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _logger.Error("بارگذاری کارکنان ناموفق بود؛ تغییرات بازگردانده شد.", ex);
            throw;
        }

        return Task.FromResult(result);
    }

    /// <summary>یافتن شناسه رکورد موجود بر اساس کلیدهای شناسایی، به ترتیب اولویت.</summary>
    private static long? FindExistingId(System.Data.IDbConnection db, System.Data.IDbTransaction tx, Employee emp)
    {
        if (!string.IsNullOrWhiteSpace(emp.NationalId))
        {
            var id = db.ExecuteScalar<long?>(
                "SELECT Id FROM Employees WHERE NationalId = @v LIMIT 1;", new { v = emp.NationalId }, tx);
            if (id is not null) return id;
        }

        if (!string.IsNullOrWhiteSpace(emp.PersonnelCode))
        {
            var id = db.ExecuteScalar<long?>(
                "SELECT Id FROM Employees WHERE PersonnelCode = @v LIMIT 1;", new { v = emp.PersonnelCode }, tx);
            if (id is not null) return id;
        }

        if (!string.IsNullOrWhiteSpace(emp.MembershipNo))
        {
            var id = db.ExecuteScalar<long?>(
                "SELECT Id FROM Employees WHERE MembershipNo = @v LIMIT 1;", new { v = emp.MembershipNo }, tx);
            if (id is not null) return id;
        }

        return null;
    }

    private static object ToParams(Employee e, long? id = null) => new
    {
        Id = id ?? 0,
        e.PersonnelCode,
        e.MembershipNo,
        e.FirstName,
        e.LastName,
        e.FatherName,
        e.NationalId,
        e.Position,
        e.Unit,
        e.Phone,
        e.HireDate,
        e.ExtraDataJson,
        e.RowHash,
        ImportedAt = (e.ImportedAt == default ? DateTime.UtcNow : e.ImportedAt).ToString("o")
    };

    /// <summary>
    /// غیرفعال کردن افرادی که در بارگذاری اخیر به‌روزرسانی نشدند.
    /// حذف فیزیکی انجام نمی‌شود تا سوابق نامه‌های صادرشده برای این افراد سالم بماند.
    /// </summary>
    public Task<int> DeactivateNotImportedSinceAsync(DateTime importStartedUtc, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var affected = db.Execute(@"
UPDATE Employees SET IsActive = 0
WHERE IsActive = 1 AND (ImportedAt IS NULL OR ImportedAt < @stamp);",
            new { stamp = importStartedUtc.ToString("o") });

        if (affected > 0)
            _logger.Info($"{affected} نفر که در فایل جدید نبودند، غیرفعال شدند.");

        return Task.FromResult(affected);
    }
}
