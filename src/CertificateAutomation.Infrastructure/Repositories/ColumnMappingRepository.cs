using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Entities;
using CertificateAutomation.Infrastructure.Data;
using Dapper;

namespace CertificateAutomation.Infrastructure.Repositories;

/// <inheritdoc />
public class ColumnMappingRepository : IColumnMappingRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public ColumnMappingRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public Task<IReadOnlyList<ColumnMapping>> GetAllAsync(CancellationToken ct = default)
    {
        using var db = _factory.Create();
        var rows = db.Query<ColumnMapping>(@"
SELECT Id, ExcelHeader, PlaceholderKey, DisplayName, IsCore, SortOrder
FROM ColumnMappings ORDER BY SortOrder, Id;").ToList();
        return Task.FromResult<IReadOnlyList<ColumnMapping>>(rows);
    }

    public Task UpsertAsync(ColumnMapping mapping, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        db.Execute(@"
INSERT INTO ColumnMappings (ExcelHeader, PlaceholderKey, DisplayName, IsCore, SortOrder)
VALUES (@ExcelHeader, @PlaceholderKey, @DisplayName, @IsCore, @SortOrder)
ON CONFLICT(ExcelHeader) DO UPDATE SET
    PlaceholderKey = @PlaceholderKey,
    DisplayName    = @DisplayName,
    SortOrder      = @SortOrder;",
            new
            {
                mapping.ExcelHeader,
                mapping.PlaceholderKey,
                mapping.DisplayName,
                IsCore = mapping.IsCore ? 1 : 0,
                mapping.SortOrder
            });
        return Task.CompletedTask;
    }
}
