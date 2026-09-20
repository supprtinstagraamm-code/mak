using System.Data;
using Microsoft.Data.Sqlite;

namespace CertificateAutomation.Infrastructure.Data;

/// <summary>سازنده اتصال پایگاه داده. تعویض SQLite با موتور دیگر فقط از همین‌جا انجام می‌شود.</summary>
public interface ISqliteConnectionFactory
{
    /// <summary>یک اتصال باز برمی‌گرداند. فراخوان مسئول Dispose است.</summary>
    IDbConnection Create();

    string DatabasePath { get; }
}

/// <inheritdoc />
public class SqliteConnectionFactory : ISqliteConnectionFactory
{
    public SqliteConnectionFactory(string? databasePath = null)
        => DatabasePath = databasePath ?? AppPaths.DatabaseFile;

    public string DatabasePath { get; }

    public IDbConnection Create()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            // اگر پایگاه داده روی درایو شبکه قفل باشد، به‌جای خطای فوری منتظر می‌مانیم.
            DefaultTimeout = 15
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 15000; PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }
}
