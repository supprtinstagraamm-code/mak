using System.Globalization;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Entities;
using CertificateAutomation.Domain.Enums;
using CertificateAutomation.Infrastructure.Data;
using Dapper;

namespace CertificateAutomation.Infrastructure.Repositories;

/// <inheritdoc />
public class UserRepository : IUserRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public UserRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public Task<AppUser?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        using var db = _factory.Create();

        var row = db.QueryFirstOrDefault<UserRow>(@"
SELECT Id, Username, PasswordHash, Salt, Role, IsActive, CreatedAt
FROM AppUsers WHERE Username = @username;", new { username });

        if (row is null) return Task.FromResult<AppUser?>(null);

        var user = new AppUser
        {
            Id = row.Id,
            Username = row.Username,
            PasswordHash = row.PasswordHash,
            Salt = row.Salt,
            // نقش به‌صورت متن ذخیره می‌شود تا در پایگاه داده خوانا باشد.
            Role = Enum.TryParse<UserRole>(row.Role, ignoreCase: true, out var role) ? role : UserRole.Operator,
            IsActive = row.IsActive != 0,
            // تاریخ‌ها همیشه با قالب ISO و فرهنگ Invariant ذخیره و خوانده می‌شوند،
            // چون فرهنگ جاری برنامه fa-IR (تقویم شمسی) است و نباید در تبدیل دخالت کند.
            CreatedAt = DateTime.TryParse(row.CreatedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var created) ? created : DateTime.UtcNow
        };

        return Task.FromResult<AppUser?>(user);
    }

    public Task UpdatePasswordAsync(long userId, string hash, string salt, CancellationToken ct = default)
    {
        using var db = _factory.Create();
        db.Execute("UPDATE AppUsers SET PasswordHash = @hash, Salt = @salt WHERE Id = @userId;",
            new { hash, salt, userId });
        return Task.CompletedTask;
    }

    /// <summary>ساختار سطر جدول AppUsers برای نگاشت توسط Dapper.</summary>
    private class UserRow
    {
        public long Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public string Role { get; set; } = "Operator";
        public long IsActive { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }
}
