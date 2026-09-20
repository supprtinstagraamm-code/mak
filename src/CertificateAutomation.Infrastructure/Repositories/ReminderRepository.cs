using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Entities;
using CertificateAutomation.Infrastructure.Data;
using Dapper;

namespace CertificateAutomation.Infrastructure.Repositories;

/// <inheritdoc />
public class ReminderRepository : IReminderRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public ReminderRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public Task<IReadOnlyList<Reminder>> GetAllAsync(bool onlyActive = false, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var sql = "SELECT Id, Title, Description, DayOfMonth, IsActive, CreatedAt FROM Reminders " +
                  (onlyActive ? "WHERE IsActive = 1 " : "") + "ORDER BY DayOfMonth, Id;";
        var rows = db.Query<Reminder>(sql).ToList();
        return Task.FromResult<IReadOnlyList<Reminder>>(rows);
    }

    public Task<Reminder?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var row = db.QueryFirstOrDefault<Reminder>(
            "SELECT Id, Title, Description, DayOfMonth, IsActive, CreatedAt FROM Reminders WHERE Id = @id;",
            new { id });
        return Task.FromResult(row);
    }

    public Task<long> AddAsync(Reminder reminder, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var id = db.ExecuteScalar<long>(@"
INSERT INTO Reminders (Title, Description, DayOfMonth, IsActive, CreatedAt)
VALUES (@Title, @Description, @DayOfMonth, @IsActive, @CreatedAt);
SELECT last_insert_rowid();",
            new
            {
                reminder.Title,
                reminder.Description,
                reminder.DayOfMonth,
                IsActive = reminder.IsActive ? 1 : 0,
                CreatedAt = (reminder.CreatedAt == default ? DateTime.UtcNow : reminder.CreatedAt).ToString("o")
            });
        return Task.FromResult(id);
    }

    public Task UpdateAsync(Reminder reminder, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        db.Execute(@"
UPDATE Reminders SET Title = @Title, Description = @Description,
    DayOfMonth = @DayOfMonth, IsActive = @IsActive
WHERE Id = @Id;",
            new
            {
                reminder.Title,
                reminder.Description,
                reminder.DayOfMonth,
                IsActive = reminder.IsActive ? 1 : 0,
                reminder.Id
            });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(long id, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        // حذف کامل، شامل سوابق انجام‌شدن و اعلان‌ها (به‌خاطر ON DELETE CASCADE)
        db.Execute("DELETE FROM Reminders WHERE Id = @id;", new { id });
        return Task.CompletedTask;
    }

    public Task MarkCompletedAsync(long reminderId, string yearMonth, string byUser, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        db.Execute(@"
INSERT INTO ReminderCompletions (ReminderId, YearMonth, CompletedAt, CompletedBy)
VALUES (@reminderId, @yearMonth, @now, @byUser)
ON CONFLICT(ReminderId, YearMonth) DO UPDATE SET CompletedAt = @now, CompletedBy = @byUser;",
            new { reminderId, yearMonth, now = DateTime.UtcNow.ToString("o"), byUser });
        return Task.CompletedTask;
    }

    public Task UnmarkCompletedAsync(long reminderId, string yearMonth, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        db.Execute("DELETE FROM ReminderCompletions WHERE ReminderId = @reminderId AND YearMonth = @yearMonth;",
            new { reminderId, yearMonth });
        return Task.CompletedTask;
    }

    public Task<bool> IsCompletedAsync(long reminderId, string yearMonth, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var count = db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ReminderCompletions WHERE ReminderId = @reminderId AND YearMonth = @yearMonth;",
            new { reminderId, yearMonth });
        return Task.FromResult(count > 0);
    }

    public Task<bool> WasNotifiedAsync(long reminderId, string yearMonth, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var count = db.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ReminderNotifications WHERE ReminderId = @reminderId AND YearMonth = @yearMonth;",
            new { reminderId, yearMonth });
        return Task.FromResult(count > 0);
    }

    public Task MarkNotifiedAsync(long reminderId, string yearMonth, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        db.Execute(@"
INSERT INTO ReminderNotifications (ReminderId, YearMonth, NotifiedAt)
VALUES (@reminderId, @yearMonth, @now)
ON CONFLICT(ReminderId, YearMonth) DO UPDATE SET NotifiedAt = @now;",
            new { reminderId, yearMonth, now = DateTime.UtcNow.ToString("o") });
        return Task.CompletedTask;
    }
}
