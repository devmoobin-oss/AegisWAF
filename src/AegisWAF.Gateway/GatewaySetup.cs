using System.Text.RegularExpressions;
using Npgsql;

namespace AegisWAF.Gateway;

/// <summary>PostgreSQL connection settings as separate fields (so users never write a connection string by hand).</summary>
public sealed record DbParams(string Host, int Port, string Database, string Username, string Password)
{
    public string ToConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = Host, Port = Port, Database = Database, Username = Username, Password = Password, Pooling = true, Timeout = 5
    }.ConnectionString;

    public static DbParams FromConnectionString(string cs)
    {
        var b = new NpgsqlConnectionStringBuilder(cs);
        return new DbParams(b.Host ?? "localhost", b.Port, b.Database ?? "aegiswaf", b.Username ?? "postgres", b.Password ?? "");
    }
}

public sealed record DbCheck(bool ServerReachable, bool DatabaseExists, bool HasUsers, string Message)
{
    /// <summary>Server reachable, credentials accepted and the database exists.</summary>
    public bool Ready => ServerReachable && DatabaseExists;
}

public sealed record UpstreamCheck(bool Reachable, string Message);

/// <summary>Pre-flight checks used by the first-run wizard and the settings page. Messages are user-facing (Thai).</summary>
public static class GatewaySetup
{
    private static readonly Regex DbName = new("^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValidDatabaseName(string name) => DbName.IsMatch(name);

    public static async Task<DbCheck> CheckDatabaseAsync(DbParams p, CancellationToken ct = default)
    {
        if (!IsValidDatabaseName(p.Database)) return new(false, false, false, "ชื่อฐานข้อมูลต้องเป็นตัวอักษรอังกฤษ ตัวเลข หรือ _ (ขึ้นต้นด้วยตัวอักษร)");
        try
        {
            await using var db = new NpgsqlConnection(p.ToConnectionString());
            await db.OpenAsync(ct).ConfigureAwait(false);
            var hasUsers = false;
            await using (var cmd = new NpgsqlCommand("SELECT to_regclass('public.users') IS NOT NULL", db))
                if ((bool)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!)
                    await using (var count = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM users)", db))
                        hasUsers = (bool)(await count.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            return new(true, true, hasUsers, hasUsers ? "เชื่อมต่อสำเร็จ — พบบัญชีผู้ใช้อยู่แล้วในฐานข้อมูลนี้" : "เชื่อมต่อสำเร็จ");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
        {
            // Credentials/host are fine (the server answered), only the database is missing: confirm via the maintenance database.
            var maint = await TryMaintenanceAsync(p, ct).ConfigureAwait(false);
            return maint is null
                ? new(true, false, false, $"เชื่อมต่อ PostgreSQL ได้ แต่ยังไม่มีฐานข้อมูล \"{p.Database}\" — กด \"สร้างฐานข้อมูลให้\"")
                : new(false, false, false, maint);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification)
        {
            return new(false, false, false, "ชื่อผู้ใช้หรือรหัสผ่านของ PostgreSQL ไม่ถูกต้อง");
        }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException or InvalidOperationException)
        {
            return new(false, false, false, Describe(ex, p));
        }
    }

    /// <summary>Creates the database if it does not exist. The name is validated and quoted; it is never concatenated unchecked.</summary>
    public static async Task CreateDatabaseAsync(DbParams p, CancellationToken ct = default)
    {
        if (!IsValidDatabaseName(p.Database)) throw new ArgumentException("Invalid database name", nameof(p));
        var maint = p with { Database = "postgres" };
        await using var db = new NpgsqlConnection(maint.ToConnectionString());
        await db.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{p.Database}\"", db);
        try { await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase) { /* already there */ }
    }

    public static async Task<UpstreamCheck> CheckUpstreamAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            return new(false, "ที่อยู่เว็บไม่ถูกต้อง (ตัวอย่าง http://localhost:8080/)");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var resp = await http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return new(true, $"เว็บตอบกลับแล้ว (HTTP {(int)resp.StatusCode})");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new(false, "ติดต่อเว็บปลายทางไม่ได้ในตอนนี้ — ตรวจสอบว่าเว็บเปิดอยู่ (ตั้งค่าต่อได้ แต่ผู้ใช้จะได้ข้อผิดพลาด 502 จนกว่าเว็บจะเปิด)");
        }
    }

    private static async Task<string?> TryMaintenanceAsync(DbParams p, CancellationToken ct)
    {
        try
        {
            await using var db = new NpgsqlConnection((p with { Database = "postgres" }).ToConnectionString());
            await db.OpenAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification)
        { return "ชื่อผู้ใช้หรือรหัสผ่านของ PostgreSQL ไม่ถูกต้อง"; }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        { return Describe(ex, p); }
    }

    private static string Describe(Exception ex, DbParams p)
    {
        var inner = ex.InnerException ?? ex;
        if (inner is System.Net.Sockets.SocketException or TimeoutException || ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return $"ติดต่อ PostgreSQL ที่ {p.Host}:{p.Port} ไม่ได้ — ตรวจสอบว่าติดตั้ง PostgreSQL แล้วและบริการกำลังทำงาน (หรือพอร์ตถูกต้อง)";
        return "เชื่อมต่อฐานข้อมูลไม่สำเร็จ: " + ex.Message;
    }
}
