using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Gateway;

internal sealed class PostgresHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString)) return HealthCheckResult.Unhealthy("PostgreSQL is not configured");
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 2, CommandTimeout = 2 };
            await using var db = new NpgsqlConnection(builder.ConnectionString);
            await db.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT 1", db);
            await cmd.ExecuteScalarAsync(cancellationToken);
            if (string.Equals(configuration["Waf:RateLimit:Store"], "postgres", StringComparison.OrdinalIgnoreCase))
            {
                await using var limiterTable = new NpgsqlCommand("SELECT to_regclass('public.waf_rate_limit_buckets')", db);
                var table = await limiterTable.ExecuteScalarAsync(cancellationToken);
                if (table is null or DBNull) return HealthCheckResult.Unhealthy("Shared rate-limit migration is not installed");
            }
            if (string.Equals(configuration["Auth:Throttle:Store"], "postgres", StringComparison.OrdinalIgnoreCase))
            {
                await using var authTable = new NpgsqlCommand("SELECT to_regclass('public.waf_auth_throttle_buckets')", db);
                var table = await authTable.ExecuteScalarAsync(cancellationToken);
                if (table is null or DBNull) return HealthCheckResult.Unhealthy("Shared authentication throttle migration is not installed");
            }
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable");
        }
    }
}

internal sealed class SiteRuntimeListenerHealthCheck(
    SiteRuntimeListenerStatus status,
    IConfiguration configuration) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("Waf:Sites:UseDatabase", true) ||
            string.IsNullOrWhiteSpace(configuration.GetConnectionString("Postgres")))
            return Task.FromResult(HealthCheckResult.Healthy("Site runtime listener is disabled"));

        var snapshot = status.Current;
        var result = snapshot.State switch
        {
            "connected" when snapshot.IsConnected && !snapshot.HasReloadFailure => HealthCheckResult.Healthy(),
            "connected" when snapshot.IsConnected => HealthCheckResult.Degraded("Site runtime snapshot reload is pending"),
            "stopped" => HealthCheckResult.Unhealthy("Site runtime listener is stopped"),
            _ => HealthCheckResult.Degraded("Site runtime listener is reconnecting")
        };
        return Task.FromResult(result);
    }
}

internal sealed class DataProtectionHealthCheck(IDataProtectionProvider provider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            const string probe = "aegiswaf-readiness-probe";
            var protector = provider.CreateProtector("AegisWAF.Readiness.v1");
            var roundTrip = protector.Unprotect(protector.Protect(probe));
            return Task.FromResult(roundTrip == probe
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Data Protection round-trip mismatch"));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Data Protection key ring is unavailable"));
        }
    }
}

internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            entries = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        status = entry.Value.Status.ToString(),
                        description = SafeDescription(entry.Key, entry.Value.Status)
                    },
                    StringComparer.Ordinal)
        };

        return JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions);
    }

    private static string? SafeDescription(string checkName, HealthStatus status)
    {
        return checkName switch
        {
            "site-runtime-listener" => status switch
            {
                HealthStatus.Healthy => "Site runtime listener is connected",
                HealthStatus.Degraded => "Site runtime listener is degraded",
                _ => "Site runtime listener is stopped"
            },
            "postgres" => status == HealthStatus.Healthy
                ? "PostgreSQL is available"
                : "PostgreSQL health check failed",
            "sites" => status == HealthStatus.Healthy
                ? "Site routing is healthy"
                : "Site routing has no available upstream destination",
            "data-protection" => status == HealthStatus.Healthy
                ? "Data Protection is available"
                : "Data Protection key ring is unavailable",
            _ => null
        };
    }
}
