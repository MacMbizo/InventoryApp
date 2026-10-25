using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace KitchenInventory.Data;

/// <summary>
/// One result from an integrity check. <see cref="Area"/> groups the checks;
/// <see cref="IsProblem"/> is false for the "this part is fine" line that accompanies it.
/// </summary>
public sealed record IntegrityFinding(string Area, string Message, bool IsProblem);

/// <summary>The outcome of <see cref="DatabaseMaintenance.CheckAsync"/>.</summary>
public sealed record DatabaseIntegrityReport(
    bool IsHealthy,
    string Summary,
    IReadOnlyList<IntegrityFinding> Findings,
    long FileSizeBytes,
    DateTime? LastWrittenUtc);

/// <summary>
/// Whole-database operations that need the SQLite file rather than a single table:
/// an integrity check and a consistent backup.
/// </summary>
/// <remarks>
/// Backups use <c>VACUUM INTO</c> rather than copying the file. SQLite runs in WAL mode, so a
/// plain file copy can miss the newest rows (they live in the <c>-wal</c> sidecar) or capture a
/// torn write. <c>VACUUM INTO</c> is the engine's own consistent-snapshot export.
/// </remarks>
public static class DatabaseMaintenance
{
    /// <summary>
    /// Runs the engine's own structural check, a foreign-key check and the stock-ledger
    /// reconciliation, and reports one finding per check.
    /// </summary>
    public static async Task<DatabaseIntegrityReport> CheckAsync(
        KitchenInventoryDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var findings = new List<IntegrityFinding>();
        var (fileSize, lastWritten) = DescribeFile(db);

        if (db.Database.IsSqlite())
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            try
            {
                // Returns the single row "ok" when the file is sound, or one row per problem.
                var integrity = await ReadRowsAsync(db, "PRAGMA integrity_check;", cancellationToken);
                var damaged = integrity.FindAll(line =>
                    !string.Equals(line, "ok", StringComparison.OrdinalIgnoreCase));
                if (damaged.Count == 0)
                {
                    findings.Add(new IntegrityFinding(
                        "File structure", "The database file is internally sound.", false));
                }
                else
                {
                    foreach (var line in damaged)
                    {
                        findings.Add(new IntegrityFinding(
                            "File structure", $"The database file is damaged: {line}", true));
                    }
                }

                // Returns one row per link that points at a record that is not there.
                var brokenLinks = await ReadRowsAsync(db, "PRAGMA foreign_key_check;", cancellationToken);
                findings.Add(brokenLinks.Count == 0
                    ? new IntegrityFinding("Links between records",
                        "Every link between records points at a record that exists.", false)
                    : new IntegrityFinding("Links between records",
                        $"{brokenLinks.Count} record link(s) point at a record that no longer exists.", true));
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }

        // The app's own notion of "the numbers add up", and the check that catches what a
        // school would actually notice: stock on hand that does not match the recorded history.
        var discrepancies = await StockLedger.ReconcileAsync(db, cancellationToken);
        if (discrepancies.Count == 0)
        {
            findings.Add(new IntegrityFinding(
                "Stock history", "The stock on hand matches the recorded history for every item.", false));
        }
        else
        {
            foreach (var d in discrepancies)
            {
                findings.Add(new IntegrityFinding(
                    "Stock history",
                    $"{d.ItemName}: {d.RecordedQuantity:0.###} on hand, but the history adds up to " +
                    $"{d.LedgerQuantity:0.###} (out by {d.Difference:+0.###;-0.###;0}).",
                    true));
            }
        }

        var problems = findings.Count(f => f.IsProblem);
        var summary = problems switch
        {
            0 => "No problems found. Your inventory data looks healthy.",
            1 => "1 problem found. See the details below.",
            _ => $"{problems} problems found. See the details below."
        };

        return new DatabaseIntegrityReport(problems == 0, summary, findings, fileSize, lastWritten);
    }

    /// <summary>
    /// Writes a consistent copy of the database to <paramref name="destinationPath"/> using
    /// <c>VACUUM INTO</c>. An existing file at that path is replaced, because the engine refuses to.
    /// </summary>
    public static async Task BackupToAsync(
        KitchenInventoryDbContext db,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("A destination for the backup is required.", nameof(destinationPath));
        }
        if (!db.Database.IsSqlite())
        {
            throw new NotSupportedException("Backing up the database file is only supported for SQLite.");
        }

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        var escaped = fullPath.Replace("'", "''");
        // VACUUM INTO takes a string literal, so a parameter cannot be used here. The path is
        // quoted for SQLite, which is the only escaping the clause understands.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{escaped}';", cancellationToken);
#pragma warning restore EF1002
    }

    /// <summary>File size and last-write time of the database, or zeros for an in-memory database.</summary>
    private static (long SizeBytes, DateTime? LastWrittenUtc) DescribeFile(KitchenInventoryDbContext db)
    {
        try
        {
            var dataSource = db.Database.GetDbConnection().DataSource;
            if (!string.IsNullOrWhiteSpace(dataSource) && File.Exists(dataSource))
            {
                var info = new FileInfo(dataSource);
                return (info.Length, info.LastWriteTimeUtc);
            }
        }
        catch
        {
            // Purely informational; a provider that cannot answer must not fail the check.
        }
        return (0, null);
    }

    /// <summary>
    /// Runs a PRAGMA and returns the first column of every row it produces. Throwing here would
    /// turn "cannot check" into "the database is broken", which is the wrong conclusion, so a
    /// failure is returned as a single explanatory row instead.
    /// </summary>
    private static async Task<List<string>> ReadRowsAsync(
        KitchenInventoryDbContext db,
        string sql,
        CancellationToken cancellationToken)
    {
        var rows = new List<string>();
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(reader.FieldCount > 0 && !await reader.IsDBNullAsync(0, cancellationToken)
                    ? reader.GetValue(0)?.ToString() ?? string.Empty
                    : string.Empty);
            }
        }
        catch (Exception ex)
        {
            rows.Add($"could not be checked ({ex.Message})");
        }
        return rows;
    }
}
