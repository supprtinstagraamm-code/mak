using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Entities;
using CertificateAutomation.Infrastructure.Data;
using Dapper;

namespace CertificateAutomation.Infrastructure.Repositories;

/// <inheritdoc />
public class TemplateRepository : ITemplateRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public TemplateRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public Task<IReadOnlyList<LetterTemplate>> GetAllAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var sql = @"
SELECT Id, Title, FileName, Category, DefaultCopies, IsActive, LastScannedAt, CreatedAt
FROM LetterTemplates " + (onlyActive ? "WHERE IsActive = 1 " : "") + "ORDER BY Title;";
        var rows = db.Query<LetterTemplate>(sql).ToList();
        return Task.FromResult<IReadOnlyList<LetterTemplate>>(rows);
    }

    public Task<LetterTemplate?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var row = db.QueryFirstOrDefault<LetterTemplate>(@"
SELECT Id, Title, FileName, Category, DefaultCopies, IsActive, LastScannedAt, CreatedAt
FROM LetterTemplates WHERE Id = @id;", new { id });
        return Task.FromResult(row);
    }

    /// <summary>درج قالب جدید یا به‌روزرسانی قالب موجود بر اساس نام فایل (کلید یکتا).</summary>
    public Task<long> UpsertAsync(LetterTemplate template, CancellationToken ct = default)
    {
        using var db = _factory.Create();

        var existingId = db.ExecuteScalar<long?>(
            "SELECT Id FROM LetterTemplates WHERE FileName = @FileName;", new { template.FileName });

        if (existingId is not null)
        {
            // توجه: Category عمداً به‌روز نمی‌شود تا دسته‌ای که کاربر دستی انتخاب کرده
            // با هر بار همگام‌سازی بازنویسی نشود. تغییر دسته از طریق SetCategoryAsync انجام می‌شود.
            db.Execute(@"
UPDATE LetterTemplates SET
    Title = @Title, DefaultCopies = @DefaultCopies,
    IsActive = @IsActive, LastScannedAt = @LastScannedAt
WHERE Id = @Id;",
                new
                {
                    template.Title,
                    template.DefaultCopies,
                    IsActive = template.IsActive ? 1 : 0,
                    LastScannedAt = template.LastScannedAt?.ToString("o"),
                    Id = existingId.Value
                });
            return Task.FromResult(existingId.Value);
        }

        var newId = db.ExecuteScalar<long>(@"
INSERT INTO LetterTemplates (Title, FileName, Category, DefaultCopies, IsActive, LastScannedAt, CreatedAt)
VALUES (@Title, @FileName, @Category, @DefaultCopies, @IsActive, @LastScannedAt, @CreatedAt);
SELECT last_insert_rowid();",
            new
            {
                template.Title,
                template.FileName,
                template.Category,
                template.DefaultCopies,
                IsActive = template.IsActive ? 1 : 0,
                LastScannedAt = template.LastScannedAt?.ToString("o"),
                CreatedAt = (template.CreatedAt == default ? DateTime.UtcNow : template.CreatedAt).ToString("o")
            });

        return Task.FromResult(newId);
    }

    public Task<IReadOnlyList<TemplatePlaceholder>> GetPlaceholdersAsync(long templateId, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var rows = db.Query<TemplatePlaceholder>(@"
SELECT Id, TemplateId, PlaceholderKey, IsRequired
FROM TemplatePlaceholders WHERE TemplateId = @templateId ORDER BY Id;", new { templateId }).ToList();
        return Task.FromResult<IReadOnlyList<TemplatePlaceholder>>(rows);
    }

    /// <summary>حذف Placeholderهای قبلی یک قالب و درج فهرست جدید، در یک تراکنش.</summary>
    public Task ReplacePlaceholdersAsync(long templateId, IEnumerable<TemplatePlaceholder> placeholders, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        using var tx = db.BeginTransaction();

        db.Execute("DELETE FROM TemplatePlaceholders WHERE TemplateId = @templateId;", new { templateId }, tx);

        foreach (var ph in placeholders)
        {
            db.Execute(@"
INSERT OR IGNORE INTO TemplatePlaceholders (TemplateId, PlaceholderKey, IsRequired)
VALUES (@TemplateId, @PlaceholderKey, @IsRequired);",
                new { TemplateId = templateId, ph.PlaceholderKey, IsRequired = ph.IsRequired ? 1 : 0 }, tx);
        }

        tx.Commit();
        return Task.CompletedTask;
    }

    /// <summary>
    /// حذف قالب‌هایی که فایلشان دیگر در پوشه نیست، به‌همراه Placeholderهایشان.
    /// این تضمین می‌کند فهرست قالب‌های برنامه همیشه دقیقاً برابر فایل‌های داخل پوشه باشد.
    /// </summary>
    public Task<int> RemoveMissingAsync(IEnumerable<string> existingFileNames, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        using var tx = db.BeginTransaction();

        var existing = existingFileNames.ToList();

        // قالب‌هایی که در پوشه نیستند
        var toDelete = existing.Count == 0
            ? db.Query<long>("SELECT Id FROM LetterTemplates;", transaction: tx).ToList()
            : db.Query<long>(
                "SELECT Id FROM LetterTemplates WHERE FileName NOT IN @names;",
                new { names = existing }, tx).ToList();

        foreach (var id in toDelete)
        {
            db.Execute("DELETE FROM TemplatePlaceholders WHERE TemplateId = @id;", new { id }, tx);
            db.Execute("DELETE FROM LetterTemplates WHERE Id = @id;", new { id }, tx);
        }

        tx.Commit();
        return Task.FromResult(toDelete.Count);
    }

    /// <summary>تغییر دستهٔ یک قالب.</summary>
    public Task SetCategoryAsync(long templateId, string category, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        db.Execute("UPDATE LetterTemplates SET Category = @category WHERE Id = @templateId;",
            new { category, templateId });
        return Task.CompletedTask;
    }

    /// <summary>فهرست دسته‌های متمایز موجود در قالب‌ها.</summary>
    public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var rows = db.Query<string>(@"
SELECT DISTINCT Category FROM LetterTemplates
WHERE Category IS NOT NULL AND TRIM(Category) <> ''
ORDER BY Category;").ToList();
        return Task.FromResult<IReadOnlyList<string>>(rows);
    }
}
