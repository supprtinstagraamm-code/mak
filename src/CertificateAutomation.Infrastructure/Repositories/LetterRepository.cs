using System.Text;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Application.Dtos;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Entities;
using CertificateAutomation.Infrastructure.Data;
using Dapper;

namespace CertificateAutomation.Infrastructure.Repositories;

/// <inheritdoc />
public class LetterRepository : ILetterRepository
{
    private const string SelectColumns = @"
Id, LetterNumber, JalaliYear, Serial, IssuedAtUtc, JalaliDate, IssuedTime, EmployeeId,
SnapFirstName, SnapLastName, SnapNationalId, SnapMembershipNo, SnapPosition, SnapUnit,
TemplateId, SnapTemplateTitle, DataSnapshotJson, PrintCount, LastPrintedAt,
ArchivePdfPath, Description, IsDeleted, DeletedAt, DeletedBy";

    private readonly ISqliteConnectionFactory _factory;

    public LetterRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public Task<Letter?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var letter = db.QueryFirstOrDefault<Letter>(
            $"SELECT {SelectColumns} FROM Letters WHERE Id = @id;", new { id });
        return Task.FromResult(letter);
    }

    public Task<IReadOnlyList<Letter>> SearchAsync(LetterSearchQuery query, CancellationToken ct = default)
    {
        var (where, parameters) = BuildWhere(query);
        var offset = Math.Max(0, (query.Page - 1) * query.PageSize);
        parameters.Add("Take", query.PageSize);
        parameters.Add("Skip", offset);

        using var db = _factory.Create();
        var rows = db.Query<Letter>(
            $"SELECT {SelectColumns} FROM Letters {where} ORDER BY Id DESC LIMIT @Take OFFSET @Skip;",
            parameters).ToList();

        return Task.FromResult<IReadOnlyList<Letter>>(rows);
    }

    public Task<int> CountAsync(LetterSearchQuery query, CancellationToken ct = default)
    {
        var (where, parameters) = BuildWhere(query);
        using var db = _factory.Create();
        var count = db.ExecuteScalar<int>($"SELECT COUNT(*) FROM Letters {where};", parameters);
        return Task.FromResult(count);
    }

    /// <summary>
    /// ساخت شرط WHERE به‌صورت پارامتری. رشته‌ها هرگز مستقیم در SQL درج نمی‌شوند (جلوگیری از SQL Injection).
    /// </summary>
    private static (string Where, DynamicParameters Parameters) BuildWhere(LetterSearchQuery q)
    {
        var sb = new StringBuilder("WHERE 1 = 1");
        var p = new DynamicParameters();

        if (!q.IncludeDeleted)
            sb.Append(" AND IsDeleted = 0");

        if (!string.IsNullOrWhiteSpace(q.LetterNumber))
        {
            sb.Append(" AND LetterNumber LIKE @LetterNumber");
            p.Add("LetterNumber", $"%{PersianDate.ToEnglishDigits(q.LetterNumber.Trim())}%");
        }

        if (!string.IsNullOrWhiteSpace(q.FirstName))
        {
            sb.Append(" AND SnapFirstName LIKE @FirstName");
            p.Add("FirstName", $"%{q.FirstName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(q.LastName))
        {
            sb.Append(" AND SnapLastName LIKE @LastName");
            p.Add("LastName", $"%{q.LastName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(q.NationalId))
        {
            sb.Append(" AND SnapNationalId LIKE @NationalId");
            p.Add("NationalId", $"%{PersianDate.ToEnglishDigits(q.NationalId.Trim())}%");
        }

        if (!string.IsNullOrWhiteSpace(q.MembershipNo))
        {
            sb.Append(" AND SnapMembershipNo LIKE @MembershipNo");
            p.Add("MembershipNo", $"%{PersianDate.ToEnglishDigits(q.MembershipNo.Trim())}%");
        }

        if (q.TemplateId.HasValue)
        {
            sb.Append(" AND TemplateId = @TemplateId");
            p.Add("TemplateId", q.TemplateId.Value);
        }

        // تاریخ‌ها به‌صورت متن شمسی yyyy/MM/dd ذخیره شده‌اند، بنابراین مقایسه متنی درست کار می‌کند.
        if (!string.IsNullOrWhiteSpace(q.FromDate))
        {
            sb.Append(" AND JalaliDate >= @FromDate");
            p.Add("FromDate", PersianDate.ToEnglishDigits(q.FromDate.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(q.ToDate))
        {
            sb.Append(" AND JalaliDate <= @ToDate");
            p.Add("ToDate", PersianDate.ToEnglishDigits(q.ToDate.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(q.FreeText))
        {
            sb.Append(@" AND (SnapFirstName LIKE @Free OR SnapLastName LIKE @Free
                          OR SnapNationalId LIKE @Free OR SnapMembershipNo LIKE @Free
                          OR LetterNumber LIKE @Free OR SnapTemplateTitle LIKE @Free)");
            p.Add("Free", $"%{PersianDate.ToEnglishDigits(q.FreeText.Trim())}%");
        }

        return (sb.ToString(), p);
    }

    public Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        var today = PersianDate.Today();
        var monthPrefix = today[..8];              // 1405/03/
        var yearPrefix = today[..5];               // 1405/

        using var db = _factory.Create();

        var stats = new DashboardStats
        {
            TodayCount = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Letters WHERE IsDeleted = 0 AND JalaliDate = @today;", new { today }),
            MonthCount = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Letters WHERE IsDeleted = 0 AND JalaliDate LIKE @p;", new { p = monthPrefix + "%" }),
            YearCount = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Letters WHERE IsDeleted = 0 AND JalaliDate LIKE @p;", new { p = yearPrefix + "%" }),
            EmployeeCount = db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Employees WHERE IsActive = 1;"),
            LastLetterNumber = db.ExecuteScalar<string?>(
                "SELECT LetterNumber FROM Letters ORDER BY Id DESC LIMIT 1;")
        };

        var recent = db.Query<Letter>(
            $"SELECT {SelectColumns} FROM Letters WHERE IsDeleted = 0 ORDER BY Id DESC LIMIT 10;");
        stats.RecentLetters.AddRange(recent);

        return Task.FromResult(stats);
    }

    public Task RegisterPrintAsync(PrintJob job, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        using var tx = db.BeginTransaction();

        db.Execute(@"
INSERT INTO PrintJobs (LetterId, PrinterName, Copies, PrintedAt, Status, ErrorText)
VALUES (@LetterId, @PrinterName, @Copies, @PrintedAt, @Status, @ErrorText);",
            new
            {
                job.LetterId,
                job.PrinterName,
                job.Copies,
                PrintedAt = (job.PrintedAt == default ? DateTime.UtcNow : job.PrintedAt).ToString("o"),
                Status = job.Status.ToString(),
                job.ErrorText
            }, tx);

        // شمارنده چاپ فقط در صورت موفقیت افزایش می‌یابد.
        if (job.Status == Domain.Enums.PrintStatus.Success)
        {
            db.Execute(@"
UPDATE Letters SET PrintCount = PrintCount + 1, LastPrintedAt = @now WHERE Id = @LetterId;",
                new { job.LetterId, now = DateTime.UtcNow.ToString("o") }, tx);
        }

        tx.Commit();
        return Task.CompletedTask;
    }

    public Task SoftDeleteAsync(long letterId, string deletedBy, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        db.Execute(@"
UPDATE Letters SET IsDeleted = 1, DeletedAt = @now, DeletedBy = @deletedBy WHERE Id = @letterId;",
            new { letterId, deletedBy, now = DateTime.UtcNow.ToString("o") });
        return Task.CompletedTask;
    }
}
