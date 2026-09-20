using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Entities;
using CertificateAutomation.Infrastructure.Data;
using Dapper;

namespace CertificateAutomation.Infrastructure.Repositories;

/// <inheritdoc />
public class AuditRepository : IAuditRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public AuditRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        db.Execute(@"
INSERT INTO AuditLog (ActionType, EntityType, EntityId, UserName, OccurredAt, Details)
VALUES (@ActionType, @EntityType, @EntityId, @UserName, @OccurredAt, @Details);",
            new
            {
                entry.ActionType,
                entry.EntityType,
                entry.EntityId,
                entry.UserName,
                OccurredAt = (entry.OccurredAt == default ? DateTime.UtcNow : entry.OccurredAt).ToString("o"),
                entry.Details
            });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int count = 100, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var rows = db.Query<AuditEntry>(
            "SELECT Id, ActionType, EntityType, EntityId, UserName, OccurredAt, Details FROM AuditLog ORDER BY Id DESC LIMIT @count;",
            new { count }).ToList();
        return Task.FromResult<IReadOnlyList<AuditEntry>>(rows);
    }
}
