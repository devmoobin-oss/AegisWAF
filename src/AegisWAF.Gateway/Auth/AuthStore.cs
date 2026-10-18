using System.Security.Cryptography;
using Npgsql;

namespace AegisWAF.Gateway.Auth;

internal sealed record AdminAccess(int MinLevel, bool AllowMustChange = false);
internal sealed record SessionInfo(Guid SessionId, Guid UserId, string Username, string Role, int Level, string CsrfToken, bool MustChange);
internal sealed record UserRecord(Guid Id, string Username, string DisplayName, string PasswordHash, bool Enabled,
    DateTimeOffset? LockedUntil, bool MustChange, string Role, int Level, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);
internal sealed record AuthOptions(int IdleMinutes, int AbsoluteHours, int MaxFailedAttempts, int LockoutMinutes)
{
    public static AuthOptions From(IConfiguration c) => new(
        c.GetValue("Auth:SessionIdleMinutes", 30), c.GetValue("Auth:SessionAbsoluteHours", 8),
        c.GetValue("Auth:MaxFailedAttempts", 5), c.GetValue("Auth:LockoutMinutes", 15));
}
internal enum UpdateResult { Ok, NotFound, LastSuperAdmin, InvalidRole }

internal static class Roles
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const int Viewer = 1, Analyst = 2, Operator = 3, SecurityAdmin = 4, Super = 5;
}

/// <summary>PostgreSQL persistence for users, sessions and authentication audit. All SQL is parameterised.</summary>
internal sealed class AuthStore(IConfiguration cfg)
{
    public AuthOptions Options { get; } = AuthOptions.From(cfg);
    private const string UserSelect = @"SELECT u.id,u.username,u.display_name,u.password_hash,u.enabled,u.locked_until,u.must_change_password,
        r.name,r.level,u.created_at,u.last_login_at FROM users u
        JOIN LATERAL (SELECT ro.name,ro.level FROM user_roles ur JOIN roles ro ON ro.id=ur.role_id WHERE ur.user_id=u.id ORDER BY ro.level DESC LIMIT 1) r ON true ";

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var db = new NpgsqlConnection(cfg.GetConnectionString("Postgres"));
        await db.OpenAsync(ct);
        return db;
    }

    private static UserRecord ReadUser(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4),
        r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5), r.GetBoolean(6), r.GetString(7), r.GetInt16(8),
        r.GetFieldValue<DateTimeOffset>(9), r.IsDBNull(10) ? null : r.GetFieldValue<DateTimeOffset>(10));

    public async Task<long> CountUsersAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM users", db);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<UserRecord?> FindByUsernameAsync(string username, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(UserSelect + "WHERE u.username=@u", db);
        cmd.Parameters.AddWithValue("u", username);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadUser(r) : null;
    }

    public async Task<UserRecord?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(UserSelect + "WHERE u.id=@id", db);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? ReadUser(r) : null;
    }

    public async Task<List<UserRecord>> ListUsersAsync(CancellationToken ct)
    {
        var list = new List<UserRecord>();
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(UserSelect + "ORDER BY u.username", db);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(ReadUser(r));
        return list;
    }

    /// <summary>Returns the new user id, or null when the username is taken or the role is unknown.</summary>
    public async Task<Guid?> CreateUserAsync(string username, string displayName, string passwordHash, string role, bool mustChange, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        try
        {
            Guid id;
            await using (var cmd = new NpgsqlCommand("INSERT INTO users(username,display_name,password_hash,must_change_password) VALUES (@u,@d,@h,@m) RETURNING id", db, tx))
            {
                cmd.Parameters.AddWithValue("u", username); cmd.Parameters.AddWithValue("d", displayName);
                cmd.Parameters.AddWithValue("h", passwordHash); cmd.Parameters.AddWithValue("m", mustChange);
                id = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
            }
            await using (var cmd = new NpgsqlCommand("INSERT INTO user_roles(user_id,role_id) SELECT @id,id FROM roles WHERE name=@r", db, tx))
            {
                cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("r", role);
                if (await cmd.ExecuteNonQueryAsync(ct) != 1) { await tx.RollbackAsync(ct); return null; }
            }
            await tx.CommitAsync(ct);
            return id;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation) { return null; }
    }

    public async Task<UpdateResult> UpdateUserAsync(Guid id, string? role, bool? enabled, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using (var lk = new NpgsqlCommand("SELECT pg_advisory_xact_lock(7001)", db, tx)) await lk.ExecuteNonQueryAsync(ct);
        string? current;
        await using (var cmd = new NpgsqlCommand("SELECT ro.name FROM user_roles ur JOIN roles ro ON ro.id=ur.role_id WHERE ur.user_id=@id ORDER BY ro.level DESC LIMIT 1", db, tx))
        {
            cmd.Parameters.AddWithValue("id", id);
            current = (string?)await cmd.ExecuteScalarAsync(ct);
        }
        if (current is null) return UpdateResult.NotFound;
        var losingSuper = current == Roles.SuperAdmin && ((role is not null && role != Roles.SuperAdmin) || enabled == false);
        if (losingSuper)
        {
            await using var cmd = new NpgsqlCommand(@"SELECT count(*) FROM users u JOIN user_roles ur ON ur.user_id=u.id JOIN roles ro ON ro.id=ur.role_id
                WHERE ro.name='SUPER_ADMIN' AND u.enabled AND u.id<>@id", db, tx);
            cmd.Parameters.AddWithValue("id", id);
            if ((long)(await cmd.ExecuteScalarAsync(ct))! == 0) return UpdateResult.LastSuperAdmin;
        }
        if (role is not null)
        {
            await using (var del = new NpgsqlCommand("DELETE FROM user_roles WHERE user_id=@id", db, tx)) { del.Parameters.AddWithValue("id", id); await del.ExecuteNonQueryAsync(ct); }
            await using var ins = new NpgsqlCommand("INSERT INTO user_roles(user_id,role_id) SELECT @id,id FROM roles WHERE name=@r", db, tx);
            ins.Parameters.AddWithValue("id", id); ins.Parameters.AddWithValue("r", role);
            if (await ins.ExecuteNonQueryAsync(ct) != 1) return UpdateResult.InvalidRole; // tx disposed => rolled back
        }
        if (enabled is not null)
        {
            await using var upd = new NpgsqlCommand("UPDATE users SET enabled=@e,updated_at=now() WHERE id=@id", db, tx);
            upd.Parameters.AddWithValue("e", enabled.Value); upd.Parameters.AddWithValue("id", id);
            await upd.ExecuteNonQueryAsync(ct);
            if (!enabled.Value)
            {
                await using var rev = new NpgsqlCommand("UPDATE sessions SET revoked_at=now() WHERE user_id=@id AND revoked_at IS NULL", db, tx);
                rev.Parameters.AddWithValue("id", id); await rev.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
        return UpdateResult.Ok;
    }

    public async Task SetPasswordAsync(Guid id, string hash, bool mustChange, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("UPDATE users SET password_hash=@h,must_change_password=@m,failed_attempts=0,locked_until=NULL,updated_at=now() WHERE id=@id", db);
        cmd.Parameters.AddWithValue("h", hash); cmd.Parameters.AddWithValue("m", mustChange); cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Atomically counts a failure and locks the account once the threshold is reached.</summary>
    public async Task RecordLoginFailureAsync(Guid id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"UPDATE users SET
            failed_attempts = CASE WHEN failed_attempts+1 >= @max THEN 0 ELSE failed_attempts+1 END,
            locked_until = CASE WHEN failed_attempts+1 >= @max THEN now() + make_interval(mins => @mins) ELSE locked_until END
            WHERE id=@id", db);
        cmd.Parameters.AddWithValue("max", Options.MaxFailedAttempts); cmd.Parameters.AddWithValue("mins", Options.LockoutMinutes); cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordLoginSuccessAsync(Guid id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("UPDATE users SET failed_attempts=0,locked_until=NULL,last_login_at=now() WHERE id=@id", db);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] HashToken(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    public async Task<(string Token, string Csrf)> CreateSessionAsync(Guid userId, string ip, string userAgent, CancellationToken ct)
    {
        var token = NewToken(); var csrf = NewToken();
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"INSERT INTO sessions(user_id,token_hash,csrf_token,expires_at,client_ip,user_agent)
            VALUES (@u,@t,@c,now()+make_interval(hours => @h),@ip,@ua)", db);
        cmd.Parameters.AddWithValue("u", userId); cmd.Parameters.AddWithValue("t", HashToken(token)); cmd.Parameters.AddWithValue("c", csrf);
        cmd.Parameters.AddWithValue("h", Options.AbsoluteHours); cmd.Parameters.AddWithValue("ip", ip);
        cmd.Parameters.AddWithValue("ua", userAgent.Length > 300 ? userAgent[..300] : userAgent);
        await cmd.ExecuteNonQueryAsync(ct);
        return (token, csrf);
    }

    public async Task<SessionInfo?> ValidateSessionAsync(string token, CancellationToken ct)
    {
        if (token.Length is < 20 or > 100) return null;
        await using var db = await OpenAsync(ct);
        SessionInfo? info; DateTimeOffset lastSeen;
        await using (var cmd = new NpgsqlCommand(@"SELECT s.id,u.id,u.username,r.name,r.level,s.csrf_token,u.must_change_password,s.last_seen_at
            FROM sessions s JOIN users u ON u.id=s.user_id
            JOIN LATERAL (SELECT ro.name,ro.level FROM user_roles ur JOIN roles ro ON ro.id=ur.role_id WHERE ur.user_id=u.id ORDER BY ro.level DESC LIMIT 1) r ON true
            WHERE s.token_hash=@h AND s.revoked_at IS NULL AND s.expires_at>now() AND s.last_seen_at>now()-make_interval(mins => @idle) AND u.enabled", db))
        {
            cmd.Parameters.AddWithValue("h", HashToken(token)); cmd.Parameters.AddWithValue("idle", Options.IdleMinutes);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            info = new SessionInfo(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetInt16(4), r.GetString(5), r.GetBoolean(6));
            lastSeen = r.GetFieldValue<DateTimeOffset>(7);
        }
        if (DateTimeOffset.UtcNow - lastSeen > TimeSpan.FromSeconds(60))
        {
            await using var upd = new NpgsqlCommand("UPDATE sessions SET last_seen_at=now() WHERE id=@id", db);
            upd.Parameters.AddWithValue("id", info.SessionId);
            await upd.ExecuteNonQueryAsync(ct);
        }
        return info;
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("UPDATE sessions SET revoked_at=now() WHERE id=@id AND revoked_at IS NULL", db);
        cmd.Parameters.AddWithValue("id", sessionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> RevokeUserSessionsAsync(Guid userId, Guid? except, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("UPDATE sessions SET revoked_at=now() WHERE user_id=@u AND revoked_at IS NULL AND (@x::uuid IS NULL OR id<>@x::uuid)", db);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.Add(new NpgsqlParameter("x", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)except ?? DBNull.Value });
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AuditAsync(string? actor, string? ip, string action, string target, string outcome, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await Audit.Write(db, null, action, target, ct, actor, ip, outcome);
    }
}
