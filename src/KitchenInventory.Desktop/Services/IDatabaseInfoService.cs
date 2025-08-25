using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace KitchenInventory.Desktop.Services;

public enum DatabaseProvider
{
    Sqlite,
    Npgsql,
    Unknown
}

public record DatabaseHealth(string Status, string Message);

public record DatabaseInfo(
    DatabaseProvider Provider,
    string Target,
    string ProviderVersion,
    int AppliedMigrations,
    int PendingMigrations,
    DatabaseHealth Health,
    string RedactedConnection,
    string LogsDirectory,
    string? DatabaseFilePath
);

public interface IDatabaseInfoService
{
    Task<DatabaseInfo> GetInfoAsync(CancellationToken ct = default);
    string RedactConnectionString(string connectionString);
}