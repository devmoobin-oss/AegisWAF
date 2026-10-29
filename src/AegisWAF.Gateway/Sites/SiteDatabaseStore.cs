using Npgsql;

namespace AegisWAF.Gateway.Sites;

/// <summary>PostgreSQL persistence for multi-site configuration. JSON files are not used for CRUD.</summary>
public sealed class SiteDatabaseStore(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("Postgres") ?? "";
    private readonly int _maxSites = SiteConfigurationValidator.ResolveMaxSites(configuration);
    private const string SiteMutationLockKey = "aegiswaf.site_mutation.v1";

    private const string SelectSql = """
        SELECT site_id, enabled, hostnames, path_prefixes, upstream_ids, upstream_addresses,
               upstream_weights, waf_policy, egress_allowed_hosts, egress_allowed_ports,
               egress_allowed_schemes, egress_allow_private_exceptions, health_path, health_interval_seconds, health_timeout_seconds,
               health_passive_reactivation_seconds, rate_requests, rate_window_seconds, rate_global_requests, row_version
        FROM waf_sites
        """;

    public async Task<IReadOnlyList<GatewaySite>> ListAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectSql + " ORDER BY site_id LIMIT @max_sites_plus_one", db);
        cmd.Parameters.AddWithValue("max_sites_plus_one", _maxSites + 1);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<GatewaySite>(Math.Min(_maxSites, 256));
        while (await reader.ReadAsync(ct))
        {
            if (result.Count >= _maxSites)
                throw new InvalidDataException($"Site configuration contains more than {_maxSites} Sites");
            result.Add(ReadSite(reader));
        }
        return result;
    }

    public async Task<SiteSummaryPage> ListEnabledSummaryPageAsync(int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT site_id, enabled, hostnames, path_prefixes, upstream_ids, upstream_addresses,
                   upstream_weights, waf_policy, egress_allowed_hosts, egress_allowed_ports,
                   egress_allowed_schemes, egress_allow_private_exceptions, health_path, health_interval_seconds, health_timeout_seconds,
                   health_passive_reactivation_seconds, rate_requests, rate_window_seconds, rate_global_requests, row_version,
                   COUNT(*) OVER() AS total_sites
            FROM waf_sites
            WHERE enabled
            ORDER BY site_id
            LIMIT @limit
            """, db);
        cmd.Parameters.AddWithValue("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<GatewaySite>(limit);
        long totalSites = 0;
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadSite(reader));
            totalSites = reader.GetInt64(20);
        }
        if (totalSites > _maxSites)
            throw new InvalidDataException($"Site configuration contains more than {_maxSites} Sites");
        return new SiteSummaryPage(result, totalSites);
    }

    public async Task<GatewaySite?> FindByIdAsync(string siteId, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectSql + " WHERE site_id=@site_id", db);
        cmd.Parameters.AddWithValue("site_id", siteId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSite(reader) : null;
    }

    /// <summary>
    /// Holds one PostgreSQL session advisory lock for the complete Site mutation workflow.
    /// The caller must keep the lease alive through validation, persistence, and activation
    /// so separate Gateway instances cannot validate against the same stale snapshot.
    /// </summary>
    public async Task<IAsyncDisposable> AcquireSiteMutationLockAsync(CancellationToken ct)
    {
        var db = await OpenAsync(ct);
        using var budget = SiteMutationLockPolicy.CreateBudget(ct);
        try
        {
            while (true)
            {
                await using var command = new NpgsqlCommand(
                    "SELECT pg_try_advisory_lock(hashtextextended(@lock_key, 0))", db);
                command.Parameters.AddWithValue("lock_key", SiteMutationLockKey);
                if (Convert.ToBoolean(await command.ExecuteScalarAsync(budget.Token)))
                    return new SiteMutationLockLease(db);
                await Task.Delay(SiteMutationLockPolicy.PollInterval, budget.Token);
            }
        }
        catch (OperationCanceledException) when (SiteMutationLockPolicy.IsBudgetCancellation(ct, budget.Token))
        {
            await db.DisposeAsync();
            throw new SiteMutationLockTimeoutException();
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    public async Task RecordHealthHistoryAsync(string siteId, IReadOnlyList<SiteHealthObservation> observations, CancellationToken ct)
    {
        if (observations.Count == 0) return;
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using var batch = new NpgsqlBatch(db, tx);
        foreach (var observation in observations)
        {
            var insert = new NpgsqlBatchCommand("""
                INSERT INTO waf_site_health_observations(site_id, upstream_id, health, reason, checked_at)
                VALUES (@site_id, @upstream_id, @health, @reason, @checked_at)
                """);
            insert.Parameters.AddWithValue("site_id", siteId);
            insert.Parameters.AddWithValue("upstream_id", observation.UpstreamId);
            insert.Parameters.AddWithValue("health", observation.Health);
            insert.Parameters.AddWithValue("reason", (object?)observation.Reason ?? DBNull.Value);
            insert.Parameters.AddWithValue("checked_at", observation.CheckedAt);
            batch.BatchCommands.Add(insert);
        }
        await batch.ExecuteNonQueryAsync(ct);
        await using (var trim = new NpgsqlCommand("""
            DELETE FROM waf_site_health_observations
            WHERE site_id=@site_id
              AND observation_id NOT IN (
                  SELECT observation_id FROM waf_site_health_observations
                  WHERE site_id=@site_id
                  ORDER BY checked_at DESC, observation_id DESC
                  LIMIT 64)
            """, db, tx))
        {
            trim.Parameters.AddWithValue("site_id", siteId);
            await trim.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<SiteHealthObservation>> ListHealthHistoryAsync(string siteId, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT upstream_id, health, reason, checked_at
            FROM waf_site_health_observations
            WHERE site_id=@site_id
            ORDER BY checked_at DESC, observation_id DESC
            LIMIT @limit
            """, db);
        cmd.Parameters.AddWithValue("site_id", siteId);
        cmd.Parameters.AddWithValue("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<SiteHealthObservation>();
        while (await reader.ReadAsync(ct))
            result.Add(new SiteHealthObservation(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        return result;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<SiteHealthObservation>>> ListHealthHistoryBySiteAsync(
        IReadOnlyList<string> siteIds, int limit, CancellationToken ct)
    {
        if (siteIds.Count == 0) return new Dictionary<string, IReadOnlyList<SiteHealthObservation>>(StringComparer.OrdinalIgnoreCase);
        limit = Math.Clamp(limit, 1, SiteHealthTrendCalculator.MaxWindow);
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT site_id, upstream_id, health, reason, checked_at
            FROM (
                SELECT site_id, upstream_id, health, reason, checked_at,
                       ROW_NUMBER() OVER (PARTITION BY site_id ORDER BY checked_at DESC, observation_id DESC) AS row_number
                FROM waf_site_health_observations
                WHERE site_id = ANY(@site_ids)
            ) observations
            WHERE row_number <= @limit
            ORDER BY site_id, checked_at DESC
            """, db);
        cmd.Parameters.AddWithValue("site_ids", siteIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray());
        cmd.Parameters.AddWithValue("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, List<SiteHealthObservation>>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
        {
            var siteId = reader.GetString(0);
            if (!result.TryGetValue(siteId, out var observations)) result[siteId] = observations = [];
            observations.Add(new SiteHealthObservation(reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)));
        }
        return result.ToDictionary(x => x.Key, x => (IReadOnlyList<SiteHealthObservation>)x.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<SiteHealthAlertAcknowledgement?> GetHealthAlertAcknowledgementAsync(string siteId, string alertState, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT acknowledged_at, acknowledged_until, acknowledged_by
            FROM waf_site_health_alerts
            WHERE site_id=@site_id AND alert_state=@alert_state
            """, db);
        cmd.Parameters.AddWithValue("site_id", siteId);
        cmd.Parameters.AddWithValue("alert_state", alertState);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new SiteHealthAlertAcknowledgement(siteId, alertState, reader.GetFieldValue<DateTimeOffset>(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2));
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<SiteHealthAlertAcknowledgement>>> ListHealthAlertAcknowledgementsAsync(
        IReadOnlyList<string> siteIds, CancellationToken ct)
    {
        if (siteIds.Count == 0) return new Dictionary<string, IReadOnlyList<SiteHealthAlertAcknowledgement>>(StringComparer.OrdinalIgnoreCase);
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT site_id, alert_state, acknowledged_at, acknowledged_until, acknowledged_by
            FROM waf_site_health_alerts
            WHERE site_id = ANY(@site_ids)
            ORDER BY site_id, alert_state
            """, db);
        cmd.Parameters.AddWithValue("site_ids", siteIds.Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, List<SiteHealthAlertAcknowledgement>>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
        {
            var siteId = reader.GetString(0);
            if (!result.TryGetValue(siteId, out var states)) result[siteId] = states = [];
            states.Add(new SiteHealthAlertAcknowledgement(siteId, reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4)));
        }
        return result.ToDictionary(x => x.Key, x => (IReadOnlyList<SiteHealthAlertAcknowledgement>)x.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<int> CleanupExpiredHealthAlertAcknowledgementsAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM waf_site_health_alerts WHERE acknowledged_until <= @now", db);
        cmd.Parameters.AddWithValue("now", now);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<SiteHealthAlertAcknowledgement> AcknowledgeHealthAlertAsync(
        string siteId, string alertState, int cooldownSeconds, string actor, string? clientIp, CancellationToken ct)
    {
        if (!HealthAlertLifecycle.IsActionable(alertState)) throw new ArgumentException("Only degraded or critical alerts can be acknowledged", nameof(alertState));
        if (!HealthAlertLifecycle.TryValidateCooldown(cooldownSeconds, out var error)) throw new ArgumentOutOfRangeException(nameof(cooldownSeconds), error);
        var acknowledgedAt = DateTimeOffset.UtcNow;
        var acknowledgedUntil = acknowledgedAt.AddSeconds(cooldownSeconds);
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO waf_site_health_alerts(site_id, alert_state, acknowledged_at, acknowledged_until, acknowledged_by, updated_at)
            VALUES (@site_id, @alert_state, @acknowledged_at, @acknowledged_until, @acknowledged_by, now())
            ON CONFLICT (site_id, alert_state) DO UPDATE SET
                acknowledged_at=EXCLUDED.acknowledged_at,
                acknowledged_until=EXCLUDED.acknowledged_until,
                acknowledged_by=EXCLUDED.acknowledged_by,
                updated_at=now()
            """, db, tx);
        cmd.Parameters.AddWithValue("site_id", siteId);
        cmd.Parameters.AddWithValue("alert_state", alertState);
        cmd.Parameters.AddWithValue("acknowledged_at", acknowledgedAt);
        cmd.Parameters.AddWithValue("acknowledged_until", acknowledgedUntil);
        cmd.Parameters.AddWithValue("acknowledged_by", actor);
        await cmd.ExecuteNonQueryAsync(ct);
        await Audit.Write(db, tx, "site.health.alert.acknowledge", $"{siteId}:{alertState}", ct, actor, clientIp);
        await tx.CommitAsync(ct);
        return new SiteHealthAlertAcknowledgement(siteId, alertState, acknowledgedAt, acknowledgedUntil, actor);
    }

    public async Task EnsureSeededAsync(IReadOnlyList<GatewaySite> sites, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        foreach (var site in sites)
        {
            await using var cmd = new NpgsqlCommand(InsertSql + " ON CONFLICT (site_id) DO NOTHING", db, tx);
            AddParameters(cmd, site);
            await cmd.ExecuteNonQueryAsync(ct);
            await ReplaceHostnamesAsync(db, tx, site, ct);
        }
        await tx.CommitAsync(ct);
    }

    public Task<bool> InsertAsync(GatewaySite site, string? actor, CancellationToken ct) =>
        InsertAsync(site, actor, null, ct);

    public async Task<bool> InsertAsync(GatewaySite site, string? actor, string? clientIp, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(InsertSql, db, tx);
            AddParameters(cmd, site);
            cmd.Parameters["updated_by"].Value = (object?)actor ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
            await ReplaceHostnamesAsync(db, tx, site, ct);
            await Audit.Write(db, tx, "site.create", site.Id, ct, actor, clientIp);
            await NotifySiteChangedAsync(db, tx, "site.create", site.Id, ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(ct);
            if (string.Equals(ex.ConstraintName, "ux_waf_site_hostnames_active_hostname", StringComparison.Ordinal))
                throw new SiteHostnameConflictException(site.Id, ex);
            return false;
        }
    }

    public async Task<bool> UpdateAsync(GatewaySite site, string? actor, CancellationToken ct)
    {
        var result = await UpdateWithConcurrencyAsync(site, site.Version, actor, null, ct);
        return result.Status == SiteUpdateStatus.Updated;
    }

    public async Task<SiteUpdateResult> UpdateWithConcurrencyAsync(GatewaySite site, long expectedVersion, string? actor, string? clientIp, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        const string sql = """
            UPDATE waf_sites SET enabled=@enabled, hostnames=@hostnames, path_prefixes=@path_prefixes,
                upstream_ids=@upstream_ids, upstream_addresses=@upstream_addresses,
                upstream_weights=@upstream_weights, waf_policy=@waf_policy,
                egress_allowed_hosts=@egress_allowed_hosts, egress_allowed_ports=@egress_allowed_ports,
                egress_allowed_schemes=@egress_allowed_schemes, egress_allow_private_exceptions=@egress_allow_private_exceptions,
                health_path=@health_path,
                health_interval_seconds=@health_interval_seconds, health_timeout_seconds=@health_timeout_seconds,
                health_passive_reactivation_seconds=@health_passive_reactivation_seconds,
                rate_requests=@rate_requests, rate_window_seconds=@rate_window_seconds,
                rate_global_requests=@rate_global_requests, row_version=row_version + 1,
                updated_at=now(), updated_by=@updated_by
            WHERE site_id=@site_id AND row_version=@expected_version
            RETURNING row_version
            """;
        await using var cmd = new NpgsqlCommand(sql, db, tx);
        AddParameters(cmd, site);
        cmd.Parameters.AddWithValue("expected_version", expectedVersion);
        cmd.Parameters["updated_by"].Value = (object?)actor ?? DBNull.Value;
        try
        {
            var scalar = await cmd.ExecuteScalarAsync(ct);
            if (scalar is null || scalar is DBNull)
            {
                await using var current = new NpgsqlCommand("SELECT row_version FROM waf_sites WHERE site_id=@site_id", db, tx);
                current.Parameters.AddWithValue("site_id", site.Id);
                var currentValue = await current.ExecuteScalarAsync(ct);
                await tx.RollbackAsync(ct);
                return currentValue is null or DBNull
                    ? new SiteUpdateResult(SiteUpdateStatus.NotFound)
                    : new SiteUpdateResult(SiteUpdateStatus.Conflict, Convert.ToInt64(currentValue));
            }
            var newVersion = Convert.ToInt64(scalar);
            await ReplaceHostnamesAsync(db, tx, site, ct);
            await Audit.Write(db, tx, "site.update", site.Id, ct, actor, clientIp);
            await NotifySiteChangedAsync(db, tx, "site.update", site.Id, ct);
            await tx.CommitAsync(ct);
            return new SiteUpdateResult(SiteUpdateStatus.Updated, newVersion);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation &&
                                           string.Equals(ex.ConstraintName, "ux_waf_site_hostnames_active_hostname", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(ct);
            throw new SiteHostnameConflictException(site.Id, ex);
        }
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct) =>
        DeleteAsync(id, null, null, ct);

    public async Task<bool> DeleteAsync(string id, string? actor, string? clientIp, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM waf_sites WHERE site_id=@site_id", db, tx);
        cmd.Parameters.AddWithValue("site_id", id);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
        await Audit.Write(db, tx, "site.delete", id, ct, actor, clientIp);
        await NotifySiteChangedAsync(db, tx, "site.delete", id, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<SiteDeleteResult> DeleteWithConcurrencyAsync(string id, long expectedVersion, string? actor, string? clientIp, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM waf_sites WHERE site_id=@site_id AND row_version=@expected_version", db, tx);
        cmd.Parameters.AddWithValue("site_id", id);
        cmd.Parameters.AddWithValue("expected_version", expectedVersion);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
        {
            await using var current = new NpgsqlCommand("SELECT row_version FROM waf_sites WHERE site_id=@site_id", db, tx);
            current.Parameters.AddWithValue("site_id", id);
            var currentValue = await current.ExecuteScalarAsync(ct);
            await tx.RollbackAsync(ct);
            return currentValue is null or DBNull
                ? new SiteDeleteResult(SiteDeleteStatus.NotFound)
                : new SiteDeleteResult(SiteDeleteStatus.Conflict, Convert.ToInt64(currentValue));
        }
        await Audit.Write(db, tx, "site.delete", id, ct, actor, clientIp);
        await NotifySiteChangedAsync(db, tx, "site.delete", id, ct);
        await tx.CommitAsync(ct);
        return new SiteDeleteResult(SiteDeleteStatus.Deleted);
    }

    public static SiteConfigurationSnapshot? TryLoadSnapshot(string? connectionString, int maxSites = SiteConfigurationValidator.DefaultMaxSites)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        maxSites = Math.Clamp(maxSites, 1, SiteConfigurationValidator.MaximumAllowedSites);
        try
        {
            using var db = new NpgsqlConnection(connectionString);
            db.Open();
            using var cmd = new NpgsqlCommand(SelectSql + " ORDER BY site_id LIMIT @max_sites_plus_one", db);
            cmd.Parameters.AddWithValue("max_sites_plus_one", maxSites + 1);
            using var reader = cmd.ExecuteReader();
            var sites = new List<GatewaySite>(Math.Min(maxSites, 256));
            while (reader.Read())
            {
                if (sites.Count >= maxSites)
                    throw new InvalidDataException($"Site configuration contains more than {maxSites} Sites");
                sites.Add(ReadSite(reader));
            }
            return new SiteConfigurationSnapshot(sites, false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null; // First start: migration runs after the builder is created.
        }
    }

    public static GatewaySite ToGatewaySite(SiteManagementRequest request)
    {
        var managementUpstreams = request.Upstreams is { Length: > 0 }
            ? request.Upstreams
            : [new SiteUpstreamManagementRequest("primary", request.UpstreamAddress, 100)];
        var upstreams = managementUpstreams
            .Select(x => new SiteUpstream(x.Id, new Uri(x.Address, UriKind.Absolute), x.Weight))
            .ToArray();
        var health = request.Health ?? new SiteHealthPolicy(NormalizeHealthPath(upstreams[0].Address));
        var rateLimit = request.RateLimit ?? new SiteRateLimitPolicy();
        return new GatewaySite(
            request.Id, request.Enabled, request.Hostnames, request.PathPrefixes,
            upstreams, request.WafPolicy,
            rateLimit,
            new SiteEgressPolicy(request.AllowedHosts, request.AllowedPorts, request.AllowedSchemes, request.AllowPrivateExceptions),
            health);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) throw new InvalidOperationException("PostgreSQL is not configured");
        var db = new NpgsqlConnection(_connectionString);
        try { await db.OpenAsync(ct); return db; }
        catch { await db.DisposeAsync(); throw; }
    }

    private static async Task NotifySiteChangedAsync(NpgsqlConnection db, NpgsqlTransaction tx, string operation, string siteId, CancellationToken ct)
    {
        var payload = SiteRuntimeChangeNotification.CreatePayload(operation, siteId);
        await using var notify = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", db, tx);
        notify.Parameters.AddWithValue("channel", SiteRuntimeChangeNotification.Channel);
        notify.Parameters.AddWithValue("payload", payload);
        await notify.ExecuteNonQueryAsync(ct);
    }

    private const string InsertSql = """
        INSERT INTO waf_sites(site_id, enabled, hostnames, path_prefixes, upstream_ids, upstream_addresses,
            upstream_weights, waf_policy, egress_allowed_hosts, egress_allowed_ports, egress_allowed_schemes,
            egress_allow_private_exceptions, health_path, health_interval_seconds, health_timeout_seconds, health_passive_reactivation_seconds,
            rate_requests, rate_window_seconds, rate_global_requests, row_version, updated_by)
        VALUES (@site_id, @enabled, @hostnames, @path_prefixes, @upstream_ids, @upstream_addresses,
            @upstream_weights, @waf_policy, @egress_allowed_hosts, @egress_allowed_ports, @egress_allowed_schemes,
            @egress_allow_private_exceptions, @health_path, @health_interval_seconds, @health_timeout_seconds, @health_passive_reactivation_seconds,
            @rate_requests, @rate_window_seconds, @rate_global_requests, @row_version, @updated_by)
        """;

    private static void AddParameters(NpgsqlCommand cmd, GatewaySite site)
    {
        var row = SitePersistenceCodec.ToRow(site);
        cmd.Parameters.AddWithValue("site_id", row.SiteId);
        cmd.Parameters.AddWithValue("enabled", row.Enabled);
        cmd.Parameters.AddWithValue("hostnames", row.Hostnames);
        cmd.Parameters.AddWithValue("path_prefixes", row.PathPrefixes);
        cmd.Parameters.AddWithValue("upstream_ids", row.UpstreamIds);
        cmd.Parameters.AddWithValue("upstream_addresses", row.UpstreamAddresses);
        cmd.Parameters.AddWithValue("upstream_weights", row.UpstreamWeights);
        cmd.Parameters.AddWithValue("waf_policy", row.WafPolicy);
        cmd.Parameters.AddWithValue("egress_allowed_hosts", row.EgressAllowedHosts);
        cmd.Parameters.AddWithValue("egress_allowed_ports", row.EgressAllowedPorts);
        cmd.Parameters.AddWithValue("egress_allowed_schemes", row.EgressAllowedSchemes);
        cmd.Parameters.AddWithValue("egress_allow_private_exceptions", row.EgressAllowPrivateExceptions);
        cmd.Parameters.AddWithValue("health_path", row.HealthPath);
        cmd.Parameters.AddWithValue("health_interval_seconds", row.HealthIntervalSeconds);
        cmd.Parameters.AddWithValue("health_timeout_seconds", row.HealthTimeoutSeconds);
        cmd.Parameters.AddWithValue("health_passive_reactivation_seconds", row.HealthPassiveReactivationSeconds);
        cmd.Parameters.AddWithValue("rate_requests", row.RateRequests);
        cmd.Parameters.AddWithValue("rate_window_seconds", row.RateWindowSeconds);
        cmd.Parameters.AddWithValue("rate_global_requests", row.RateGlobalRequests);
        cmd.Parameters.AddWithValue("row_version", row.Version);
        if (!cmd.Parameters.Contains("updated_by")) cmd.Parameters.AddWithValue("updated_by", DBNull.Value);
    }

    private static async Task ReplaceHostnamesAsync(NpgsqlConnection db, NpgsqlTransaction tx, GatewaySite site, CancellationToken ct)
    {
        await using (var delete = new NpgsqlCommand("DELETE FROM waf_site_hostnames WHERE site_id=@site_id", db, tx))
        {
            delete.Parameters.AddWithValue("site_id", site.Id);
            await delete.ExecuteNonQueryAsync(ct);
        }
        foreach (var hostname in site.Hostnames.Select(SiteConfigurationValidator.NormalizeHostname).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO waf_site_hostnames(site_id, hostname, active) VALUES (@site_id, @hostname, @active)", db, tx);
            insert.Parameters.AddWithValue("site_id", site.Id);
            insert.Parameters.AddWithValue("hostname", hostname);
            insert.Parameters.AddWithValue("active", site.Enabled);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    private static GatewaySite ReadSite(NpgsqlDataReader reader)
    {
        var row = new SitePersistenceRow(
            reader.GetString(0),
            reader.GetBoolean(1),
            reader.GetFieldValue<string[]>(2),
            reader.GetFieldValue<string[]>(3),
            reader.GetFieldValue<string[]>(4),
            reader.GetFieldValue<string[]>(5),
            reader.GetFieldValue<int[]>(6),
            reader.GetString(7),
            reader.GetFieldValue<string[]>(8),
            reader.GetFieldValue<int[]>(9),
            reader.GetFieldValue<string[]>(10),
            reader.GetBoolean(11),
            reader.GetString(12),
            reader.GetInt32(13),
            reader.GetInt32(14),
            reader.GetInt32(15),
            reader.GetInt32(16),
            reader.GetInt32(17),
            reader.GetInt32(18),
            reader.GetInt64(19));
        return SitePersistenceCodec.FromRow(row);
    }

    private static string NormalizeHealthPath(Uri address)
    {
        var path = address.AbsolutePath.TrimEnd('/');
        return path.Length == 0 ? "/" : path;
    }
}

file sealed class SiteMutationLockLease(NpgsqlConnection connection) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => connection.DisposeAsync();
}

public sealed class SiteHostnameConflictException(string siteId, Exception inner)
    : InvalidOperationException($"Site '{siteId}' conflicts with an active hostname assigned to another Site", inner);

public enum SiteUpdateStatus { Updated, NotFound, Conflict }

public sealed record SiteUpdateResult(SiteUpdateStatus Status, long? CurrentVersion = null);

public enum SiteDeleteStatus { Deleted, NotFound, Conflict }

public sealed record SiteDeleteResult(SiteDeleteStatus Status, long? CurrentVersion = null);

public sealed record SiteSummaryPage(IReadOnlyList<GatewaySite> Sites, long TotalSites);
