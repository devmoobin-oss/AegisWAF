using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using System.Security.Cryptography;

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
