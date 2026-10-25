using System.Text.RegularExpressions;

namespace AegisWAF.Gateway.Auth;

internal sealed record LoginInput(string? Username, string? Password);
internal sealed record ChangePasswordInput(string? CurrentPassword, string? NewPassword);
internal sealed record CreateUserInput(string? Username, string? DisplayName, string? Password, string? Role);
internal sealed record UpdateUserInput(string? Role, bool? Enabled);
internal sealed record ResetPasswordInput(string? NewPassword);

internal static class AuthEndpoints
{
    private static readonly Regex UsernamePattern = new("^[a-z0-9._-]{3,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> RoleNames = ["VIEWER", "SECURITY_ANALYST", "SECURITY_OPERATOR", "SECURITY_ADMIN", Roles.SuperAdmin];
    private static readonly object Unauthorized = new { error = "Invalid username or password" };

    public static void MapAuth(this WebApplication app)
    {
        app.MapPost("/api/auth/login", Login).Require(0);
        app.MapPost("/api/auth/logout", Logout).Require(Roles.Viewer, allowMustChange: true);
        app.MapGet("/api/auth/session", (HttpContext ctx) => Results.Ok(SessionDto(ctx.Session()!))).Require(Roles.Viewer, allowMustChange: true);
        app.MapPost("/api/auth/change-password", ChangePassword).Require(Roles.Viewer, allowMustChange: true);

        app.MapGet("/api/users", async (AuthStore store, CancellationToken ct) =>
            Results.Ok((await store.ListUsersAsync(ct)).Select(UserDto))).Require(Roles.Super);
        app.MapPost("/api/users", CreateUser).Require(Roles.Super);
        app.MapPatch("/api/users/{id:guid}", UpdateUser).Require(Roles.Super);
        app.MapPost("/api/users/{id:guid}/reset-password", ResetPassword).Require(Roles.Super);
        app.MapPost("/api/users/{id:guid}/revoke-sessions", async (Guid id, AuthStore store, HttpContext ctx, CancellationToken ct) =>
        {
            var user = await store.FindByIdAsync(id, ct);
            if (user is null) return Results.NotFound();
            var n = await store.RevokeUserSessionsAsync(id, null, ct);
            await store.AuditAsync(ctx.Actor(), ctx.ClientIp(), "user.sessions.revoke", user.Username, "success", ct);
            return Results.Ok(new { revoked = n });
        }).Require(Roles.Super);
    }

    private static object SessionDto(SessionInfo s) => new { username = s.Username, role = s.Role, level = s.Level, csrfToken = s.CsrfToken, mustChangePassword = s.MustChange };
    private static object UserDto(UserRecord u) => new { id = u.Id, username = u.Username, displayName = u.DisplayName, role = u.Role, enabled = u.Enabled,
        locked = u.LockedUntil > DateTimeOffset.UtcNow, mustChangePassword = u.MustChange, createdAt = u.CreatedAt, lastLoginAt = u.LastLoginAt };

    private static async Task<IResult> Login(LoginInput input, AuthStore store, AuthThrottle throttle, DistributedAuthThrottle sharedThrottle, HttpContext ctx, CancellationToken ct)
    {
        var username = (input.Username ?? "").Trim().ToLowerInvariant();
        var password = input.Password ?? "";
        if (username.Length is < 1 or > 64 || password.Length is < 1 or > PasswordHasher.MaxLength) return Results.Json(Unauthorized, statusCode: 401);
        var ip = ctx.ClientIp();
        try
        {
            var gate = throttle.Allow(ip, username, DateTimeOffset.UtcNow);
            if (!gate.Allowed || !await sharedThrottle.AllowAsync(ip, username, ct))
            {
                await store.AuditAsync(username, ip, "auth.throttled", "login", "failure", ct);
                return Results.Json(Unauthorized, statusCode: 401);
            }
            var user = await store.FindByUsernameAsync(username, ct);
            var locked = user?.LockedUntil > DateTimeOffset.UtcNow;
            if (user is null || !user.Enabled || locked)
            {
                PasswordHasher.BurnTime(password);
                throttle.RecordFailure(ip, username, DateTimeOffset.UtcNow);
                await sharedThrottle.RecordFailureAsync(ip, username, ct);
                await store.AuditAsync(username.Length > 64 ? null : username, ip, "auth.login", user is null ? "unknown-user" : locked ? "account-locked" : "account-disabled", "failure", ct);
                return Results.Json(Unauthorized, statusCode: 401);
            }
            if (!PasswordHasher.Verify(password, user.PasswordHash))
            {
                throttle.RecordFailure(ip, username, DateTimeOffset.UtcNow);
                await sharedThrottle.RecordFailureAsync(ip, username, ct);
                await store.RecordLoginFailureAsync(user.Id, ct);
                await store.AuditAsync(user.Username, ip, "auth.login", "bad-password", "failure", ct);
                return Results.Json(Unauthorized, statusCode: 401);
            }
            throttle.RecordSuccess(ip, username);
            await sharedThrottle.RecordSuccessAsync(ip, username, ct);
            await store.RecordLoginSuccessAsync(user.Id, ct);
            var (token, csrf) = await store.CreateSessionAsync(user.Id, ip, ctx.Request.Headers.UserAgent.ToString(), ct);
            var secureCookie = AdminPerimeterPolicy.IsSecureRequest(ctx, ctx.RequestServices.GetRequiredService<IConfiguration>());
            ctx.Response.Cookies.Append(AdminGuard.CookieName, token, new CookieOptions
            {
                HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = secureCookie, Path = "/", IsEssential = true,
                MaxAge = TimeSpan.FromHours(store.Options.AbsoluteHours)
            });
            await store.AuditAsync(user.Username, ip, "auth.login", "session-created", "success", ct);
            return Results.Ok(new { username = user.Username, role = user.Role, level = user.Level, csrfToken = csrf, mustChangePassword = user.MustChange });
        }
        catch (Npgsql.NpgsqlException) { return Results.Problem("Authentication service unavailable", statusCode: 503); }
    }

    private static async Task<IResult> Logout(AuthStore store, HttpContext ctx, CancellationToken ct)
    {
        var s = ctx.Session()!;
        await store.RevokeSessionAsync(s.SessionId, ct);
        await store.AuditAsync(s.Username, ctx.ClientIp(), "auth.logout", "session-revoked", "success", ct);
        ctx.Response.Cookies.Delete(AdminGuard.CookieName, new CookieOptions
        {
            Path = "/", SameSite = SameSiteMode.Strict, HttpOnly = true,
            Secure = AdminPerimeterPolicy.IsSecureRequest(ctx, ctx.RequestServices.GetRequiredService<IConfiguration>())
        });
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(ChangePasswordInput input, AuthStore store, HttpContext ctx, CancellationToken ct)
    {
        var s = ctx.Session()!;
        var user = await store.FindByIdAsync(s.UserId, ct);
        if (user is null || string.IsNullOrEmpty(input.CurrentPassword) || input.CurrentPassword.Length > PasswordHasher.MaxLength
            || !PasswordHasher.Verify(input.CurrentPassword, user.PasswordHash))
        {
            await store.AuditAsync(s.Username, ctx.ClientIp(), "auth.password.change", "bad-current-password", "failure", ct);
            return Results.Json(new { error = "Current password is incorrect" }, statusCode: 403);
        }
        var problem = PasswordHasher.Validate(input.NewPassword, s.Username);
        if (problem is not null) return Results.BadRequest(new { error = problem });
        await store.SetPasswordAsync(user.Id, PasswordHasher.Hash(input.NewPassword!), false, ct);
        await store.RevokeUserSessionsAsync(user.Id, s.SessionId, ct);
        await store.AuditAsync(s.Username, ctx.ClientIp(), "auth.password.change", user.Username, "success", ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateUser(CreateUserInput input, AuthStore store, HttpContext ctx, CancellationToken ct)
    {
        var username = (input.Username ?? "").Trim().ToLowerInvariant();
        var display = (input.DisplayName ?? "").Trim();
        if (!UsernamePattern.IsMatch(username)) return Results.BadRequest(new { error = "Username must be 3-64 characters: a-z, 0-9, '.', '_' or '-'" });
        if (display.Length > 120) return Results.BadRequest(new { error = "Display name too long" });
        if (input.Role is null || !RoleNames.Contains(input.Role)) return Results.BadRequest(new { error = "Unknown role" });
        var problem = PasswordHasher.Validate(input.Password, username);
        if (problem is not null) return Results.BadRequest(new { error = problem });
        // Accounts created by an administrator must set their own password at first login.
        var id = await store.CreateUserAsync(username, display, PasswordHasher.Hash(input.Password!), input.Role, true, ct);
        if (id is null) return Results.Conflict(new { error = "Username already exists" });
        await store.AuditAsync(ctx.Actor(), ctx.ClientIp(), "user.create", $"{username}:{input.Role}", "success", ct);
        return Results.Created($"/api/users/{id}", new { id });
    }

    private static async Task<IResult> UpdateUser(Guid id, UpdateUserInput input, AuthStore store, HttpContext ctx, CancellationToken ct)
    {
        if (input.Role is null && input.Enabled is null) return Results.BadRequest(new { error = "Nothing to update" });
        if (input.Role is not null && !RoleNames.Contains(input.Role)) return Results.BadRequest(new { error = "Unknown role" });
        var target = await store.FindByIdAsync(id, ct);
        if (target is null) return Results.NotFound();
        var result = await store.UpdateUserAsync(id, input.Role, input.Enabled, ct);
        if (result is UpdateResult.Ok)
            await store.AuditAsync(ctx.Actor(), ctx.ClientIp(), "user.update", $"{target.Username}:role={input.Role};enabled={input.Enabled}", "success", ct);
        return result switch
        {
            UpdateResult.Ok => Results.NoContent(),
            UpdateResult.NotFound => Results.NotFound(),
            UpdateResult.LastSuperAdmin => Results.Conflict(new { error = "At least one enabled SUPER_ADMIN must remain" }),
            _ => Results.BadRequest(new { error = "Unknown role" })
        };
    }

    private static async Task<IResult> ResetPassword(Guid id, ResetPasswordInput input, AuthStore store, HttpContext ctx, CancellationToken ct)
    {
        var user = await store.FindByIdAsync(id, ct);
        if (user is null) return Results.NotFound();
        var problem = PasswordHasher.Validate(input.NewPassword, user.Username);
        if (problem is not null) return Results.BadRequest(new { error = problem });
        await store.SetPasswordAsync(id, PasswordHasher.Hash(input.NewPassword!), true, ct);
        await store.RevokeUserSessionsAsync(id, null, ct);
        await store.AuditAsync(ctx.Actor(), ctx.ClientIp(), "user.password.reset", user.Username, "success", ct);
        return Results.NoContent();
    }
}
