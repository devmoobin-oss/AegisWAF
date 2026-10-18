using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace AegisWAF.Gateway.Infrastructure;

/// <summary>
/// Applies the SQL scripts embedded from /database in filename order. Each script runs in its own
/// transaction and is recorded in schema_migrations with a checksum; a modified applied script is a hard error.
/// A session-level advisory lock serialises concurrent nodes.
/// </summary>
internal static class Migrator
{
    private const long LockKey = 7000;

    public static async Task<IReadOnlyList<string>> ApplyAsync(string connectionString, CancellationToken ct = default)
    {
        var scripts = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.StartsWith("db/", StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        var applied = new List<string>();
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync(ct);
        await Exec(db, null, $"SELECT pg_advisory_lock({LockKey})", ct);
        try
        {
            await Exec(db, null, "CREATE TABLE IF NOT EXISTS schema_migrations (version text PRIMARY KEY, checksum text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now())", ct);
            var known = new Dictionary<string, string>();
            await using (var cmd = new NpgsqlCommand("SELECT version,checksum FROM schema_migrations", db))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct)) known[r.GetString(0)] = r.GetString(1);

            foreach (var name in scripts)
            {
                var version = name[3..];
                string sql;
                await using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!)
                using (var reader = new StreamReader(s, Encoding.UTF8)) sql = await reader.ReadToEndAsync(ct);
                var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql.ReplaceLineEndings("\n"))));
                if (known.TryGetValue(version, out var existing))
                {
                    if (existing != checksum) throw new InvalidOperationException($"Migration {version} was modified after being applied");
                    continue;
                }
                await using var tx = await db.BeginTransactionAsync(ct);
                await Exec(db, tx, sql, ct);
                await using (var ins = new NpgsqlCommand("INSERT INTO schema_migrations(version,checksum) VALUES (@v,@c)", db, tx))
                {
                    ins.Parameters.AddWithValue("v", version); ins.Parameters.AddWithValue("c", checksum);
                    await ins.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                applied.Add(version);
            }
        }
        finally { await Exec(db, null, $"SELECT pg_advisory_unlock({LockKey})", ct); }
        return applied;
    }

    private static async Task Exec(NpgsqlConnection db, NpgsqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, db, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
