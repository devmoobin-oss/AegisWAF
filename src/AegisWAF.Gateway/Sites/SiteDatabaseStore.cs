using Npgsql;

namespace AegisWAF.Gateway.Sites;

/// <summary>PostgreSQL persistence for multi-site configuration. JSON files are not used for CRUD.</summary>
public sealed class SiteDatabaseStore(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("Postgres") ?? "";

    public async Task<IReadOnlyList<GatewaySite>> ListAsync(CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        const string sql = """
            SELECT site_id, enabled, hostnames, path_prefixes, upstream_ids, upstream_addresses,
                   upstream_weights, waf_policy, egress_allowed_hosts, egress_allowed_ports,
                   egress_allowed_schemes
            FROM waf_sites ORDER BY site_id
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<GatewaySite>();
        while (await reader.ReadAsync(ct)) result.Add(ReadSite(reader));
        return result;
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
        }
        await tx.CommitAsync(ct);
    }

    public async Task<bool> InsertAsync(GatewaySite site, string? actor, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(InsertSql, db, tx);
            AddParameters(cmd, site);
            cmd.Parameters["updated_by"].Value = (object?)actor ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
    }

    public async Task<bool> UpdateAsync(GatewaySite site, string? actor, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        const string sql = """
            UPDATE waf_sites SET enabled=@enabled, hostnames=@hostnames, path_prefixes=@path_prefixes,
                upstream_ids=@upstream_ids, upstream_addresses=@upstream_addresses,
                upstream_weights=@upstream_weights, waf_policy=@waf_policy,
                egress_allowed_hosts=@egress_allowed_hosts, egress_allowed_ports=@egress_allowed_ports,
                egress_allowed_schemes=@egress_allowed_schemes, updated_at=now(), updated_by=@updated_by
            WHERE site_id=@site_id
            """;
        await using var cmd = new NpgsqlCommand(sql, db);
        AddParameters(cmd, site);
        cmd.Parameters["updated_by"].Value = (object?)actor ?? DBNull.Value;
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM waf_sites WHERE site_id=@site_id", db);
        cmd.Parameters.AddWithValue("site_id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public static SiteConfigurationSnapshot? TryLoadSnapshot(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            using var db = new NpgsqlConnection(connectionString);
            db.Open();
            using var cmd = new NpgsqlCommand("SELECT site_id, enabled, hostnames, path_prefixes, upstream_ids, upstream_addresses, upstream_weights, waf_policy, egress_allowed_hosts, egress_allowed_ports, egress_allowed_schemes FROM waf_sites ORDER BY site_id", db);
            using var reader = cmd.ExecuteReader();
            var sites = new List<GatewaySite>();
            while (reader.Read()) sites.Add(ReadSite(reader));
            return sites.Count == 0 ? null : new SiteConfigurationSnapshot(sites, false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null; // First start: migration runs after the builder is created.
        }
        catch (NpgsqlException)
        {
            return null; // Bootstrap remains responsible for the authoritative startup error.
        }
    }

    public static GatewaySite ToGatewaySite(SiteManagementRequest request)
    {
        var address = new Uri(request.UpstreamAddress, UriKind.Absolute);
        return new GatewaySite(
            request.Id, request.Enabled, request.Hostnames, request.PathPrefixes,
            [new SiteUpstream("primary", address, 100)], request.WafPolicy,
            new SiteRateLimitPolicy(),
            new SiteEgressPolicy(request.AllowedHosts, request.AllowedPorts, request.AllowedSchemes),
            new SiteHealthPolicy(NormalizeHealthPath(address)));
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) throw new InvalidOperationException("PostgreSQL is not configured");
        var db = new NpgsqlConnection(_connectionString);
        try { await db.OpenAsync(ct); return db; }
        catch { await db.DisposeAsync(); throw; }
    }

    private const string InsertSql = """
        INSERT INTO waf_sites(site_id, enabled, hostnames, path_prefixes, upstream_ids, upstream_addresses,
            upstream_weights, waf_policy, egress_allowed_hosts, egress_allowed_ports, egress_allowed_schemes, updated_by)
        VALUES (@site_id, @enabled, @hostnames, @path_prefixes, @upstream_ids, @upstream_addresses,
            @upstream_weights, @waf_policy, @egress_allowed_hosts, @egress_allowed_ports, @egress_allowed_schemes, @updated_by)
        """;

    private static void AddParameters(NpgsqlCommand cmd, GatewaySite site)
    {
        cmd.Parameters.AddWithValue("site_id", site.Id);
        cmd.Parameters.AddWithValue("enabled", site.Enabled);
        cmd.Parameters.AddWithValue("hostnames", site.Hostnames.ToArray());
        cmd.Parameters.AddWithValue("path_prefixes", site.PathPrefixes.ToArray());
        cmd.Parameters.AddWithValue("upstream_ids", site.Upstreams.Select(x => x.Id).ToArray());
        cmd.Parameters.AddWithValue("upstream_addresses", site.Upstreams.Select(x => x.Address.ToString()).ToArray());
        cmd.Parameters.AddWithValue("upstream_weights", site.Upstreams.Select(x => x.Weight).ToArray());
        cmd.Parameters.AddWithValue("waf_policy", site.WafPolicy);
        cmd.Parameters.AddWithValue("egress_allowed_hosts", site.Egress.AllowedHosts.ToArray());
        cmd.Parameters.AddWithValue("egress_allowed_ports", site.Egress.AllowedPorts.ToArray());
        cmd.Parameters.AddWithValue("egress_allowed_schemes", site.Egress.AllowedSchemes.ToArray());
        if (!cmd.Parameters.Contains("updated_by")) cmd.Parameters.AddWithValue("updated_by", DBNull.Value);
    }

    private static GatewaySite ReadSite(NpgsqlDataReader reader)
    {
        var ids = (string[])reader[4];
        var addresses = (string[])reader[5];
        var weights = (int[])reader[6];
        var upstreams = ids.Select((id, i) => new SiteUpstream(id, new Uri(addresses[i]), weights[i])).ToArray();
        return new GatewaySite(
            reader.GetString(0), reader.GetBoolean(1), (string[])reader[2], (string[])reader[3], upstreams,
            reader.GetString(7), new SiteRateLimitPolicy(),
            new SiteEgressPolicy((string[])reader[8], (int[])reader[9], (string[])reader[10]),
            new SiteHealthPolicy(NormalizeHealthPath(upstreams[0].Address)));
    }

    private static string NormalizeHealthPath(Uri address)
    {
        var path = address.AbsolutePath.TrimEnd('/');
        return path.Length == 0 ? "/" : path;
    }
}
