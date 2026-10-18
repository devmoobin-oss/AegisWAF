using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

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
