using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Npgsql;
using AegisWAF.Gateway.Auth;
using AegisWAF.Gateway.Infrastructure;
using AegisWAF.Client;
using Yarp.ReverseProxy;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Gateway;

/// <summary>Builds the gateway host. Shared by the standalone exe (Program.cs) and the in-process host inside the desktop app.</summary>
public static class GatewayApp
{
    // Request headers that commonly carry injection payloads. Inspecting only these keeps false positives down.
    private static readonly string[] InspectedHeaders =
        ["User-Agent", "Referer", "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "Origin", "X-Api-Version", "X-Requested-With", "Forwarded"];

    /// <summary>True for textual content types whose body is worth running signatures over (json, form, text, xml, multipart).</summary>
    private static bool IsInspectableBody(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return true; // no declared type: inspect to be safe
        var c = contentType.AsSpan();
        return c.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            || c.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            || c.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || c.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    /// <param name="contentRoot">Explicit content root (embedded use). Null = working directory, falling back to the exe directory for wwwroot.</param>
    /// <param name="overrides">Highest-precedence configuration (applied after files and environment variables).</param>
    public static WebApplicationBuilder CreateBuilder(string[] args, string? contentRoot = null, IDictionary<string, string?>? overrides = null)
    {
        // A published exe may be launched from any directory; resolve wwwroot next to the binary when the working directory has none.
        var useBaseDir = contentRoot is null && !Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")) && Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        var options = new WebApplicationOptions { Args = args, ContentRootPath = contentRoot ?? (useBaseDir ? AppContext.BaseDirectory : null) };
        var builder = WebApplication.CreateBuilder(options);
        // Windows may add an EventLog provider that throws when the process cannot write the event source.
        // A logging sink failure must never take down EventWriter or the request pipeline; console JSON/message
        // output is also the portable contract consumed by the SIEM/agent in production.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        if (builder.Environment.IsEnvironment("Testing")) builder.Logging.ClearProviders();
        // Lowest precedence: a working default so the proxy has a route even when no appsettings.json is shipped.
        builder.Configuration.Sources.Insert(0, new Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource { InitialData = new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:route1:ClusterId"] = "appcluster",
            ["ReverseProxy:Routes:route1:Match:Path"] = "{**catch-all}",
            ["ReverseProxy:Clusters:appcluster:Destinations:app1:Address"] = "http://localhost:8080/"
        } });
        // Optional machine-local overrides (connection string etc.) so a double-clicked exe needs no environment variables.
        // Environment variables are added after this file and still take precedence.
        builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables();
        if (overrides is not null) builder.Configuration.AddInMemoryCollection(overrides);
        var siteSnapshot = SiteConfigurationLoader.Load(builder.Configuration);
        builder.Configuration.AddInMemoryCollection(SiteProxyConfiguration.Build(siteSnapshot, builder.Configuration));
        ProductionReadinessPolicy.ThrowIfInvalid(builder.Configuration);
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxRequestLineSize = builder.Configuration.GetValue("Waf:Limits:MaxRequestLineBytes", 16 * 1024);
            server.Limits.MaxRequestHeadersTotalSize = builder.Configuration.GetValue("Waf:Limits:MaxRequestHeadersBytes", 64 * 1024);
            server.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long?>("Waf:Limits:MaxRequestBodyBytes", 1_048_576);
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Waf:Limits:KeepAliveSeconds", 120));
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Waf:Limits:RequestHeadersTimeoutSeconds", 10));
        });
        return builder;
    }

    public static void AddServices(WebApplicationBuilder builder)
    {
    var sites = SiteConfigurationLoader.Load(builder.Configuration);
    builder.Services.AddSingleton(sites);
    builder.Services.AddSingleton<SiteRegistry>();
    builder.Services.AddSingleton<SiteHealthRegistry>();
    var reverseProxy = builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
    if (builder.Configuration.GetValue("Waf:Deployment:Enforce", false))
    {
        var globalEgress = UpstreamEgressOptions.From(builder.Configuration);
        reverseProxy.ConfigureHttpClient((context, handler) =>
        {
            var metadata = context.NewMetadata;
            var hosts = metadata is not null && metadata.TryGetValue("EgressHosts", out var hostText)
                ? hostText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : globalEgress.AllowedHosts.ToArray();
            var ports = metadata is not null && metadata.TryGetValue("EgressPorts", out var portText)
                ? portText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray() : globalEgress.AllowedPorts.ToArray();
            var schemes = metadata is not null && metadata.TryGetValue("EgressSchemes", out var schemeText)
                ? schemeText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : globalEgress.AllowedSchemes.ToArray();
            var policy = new UpstreamEgressPolicy(new UpstreamEgressOptions(hosts, ports, schemes, false), new SystemUpstreamAddressResolver());
            handler.ConnectCallback = (socketContext, ct) => EgressConnectionGuard.ConnectAsync(policy, socketContext.DnsEndPoint, ct);
        });
    }
    builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
        .AddCheck<SiteRoutingHealthCheck>("sites", tags: ["ready"]);
    builder.Services.AddSingleton<SecurityRules>();
    builder.Services.AddSingleton<SitePolicyResolver>();
    builder.Services.AddSingleton<ResponseInspector>();
    builder.Services.AddHostedService<CustomRuleLoader>();
    builder.Services.AddHostedService<RuleStateLoader>();
    builder.Services.AddSingleton<TrafficCounters>();
    builder.Services.AddSingleton<SecurityEvents>();
    builder.Services.AddSingleton<SlidingRateLimiter>();
    builder.Services.AddSingleton<UpstreamEgressPolicy>(sp =>
        new UpstreamEgressPolicy(UpstreamEgressOptions.From(sp.GetRequiredService<IConfiguration>()), new SystemUpstreamAddressResolver()));
    if (builder.Configuration.GetValue("Waf:Deployment:Enforce", false))
        builder.Services.AddHostedService<ProductionEgressStartupValidator>();
    builder.Services.AddSingleton<IpAccessPolicy>();
    builder.Services.AddHostedService<IpPolicyLoader>();
    builder.Services.AddHostedService<EventWriter>();
    builder.Services.AddSingleton<AuthStore>();
    builder.Services.AddSingleton<DistributedAuthThrottle>();
    builder.Services.AddSingleton(sp =>
    {
        var cfg = sp.GetRequiredService<IConfiguration>();
        return new AuthThrottle(cfg.GetValue("Auth:Throttle:MaxFailures", 10),
            TimeSpan.FromMinutes(cfg.GetValue("Auth:Throttle:WindowMinutes", 15)),
            cfg.GetValue("Auth:Throttle:MaxEntries", 10_000));
    });
    }

    /// <summary>Middleware and endpoints. Must run after <c>builder.Build()</c> and after <c>Bootstrap.RunAsync</c>.</summary>
    public static void MapPipeline(WebApplication app)
    {
    app.Use(async (ctx, next) =>
    {
        var configuration = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var framingError = RequestSanitizer.Validate(ctx.Request, configuration);
        if (framingError is not null)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { error = "Malformed request framing" });
            return;
        }
        await next();
    });
    // Harden management responses. GUI and APIs are loopback-only in this single-process prototype.
    app.Use(async (ctx, next) =>
    {
        AdminSecurityHeaders.Apply(ctx);
        if ((ctx.Request.Path == "/" || ctx.Request.Path == "/index.html" || ctx.Request.Path.StartsWithSegments("/assets")) &&
            !ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Admin:AllowRemote", false) && !AdminGuard.IsLocalClient(ctx))
        { ctx.Response.StatusCode = 403; return; }
        await next();
    });
    // Static files must run BEFORE routing: StaticFileMiddleware yields to any matched endpoint, and the YARP catch-all matches everything.
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseRouting();
    // Admin API: session cookie + RBAC + CSRF, deny-by-default, local host only (see AdminGuard). Needs the routed endpoint.
    app.Use(AdminGuard.Invoke);
    app.Use(async (ctx, next) =>
    {
        if (IsManagementPath(ctx.Request.Path))
        {
            await next();
            return;
        }

        var registry = ctx.RequestServices.GetRequiredService<SiteRegistry>();
        var match = registry.Resolve(ctx.Request.Host.Value, ctx.Request.Path.Value);
        if (match is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status421MisdirectedRequest;
            await ctx.Response.WriteAsJsonAsync(new { title = "Unknown site", status = 421 });
            return;
        }
        ctx.Items[SiteContext.ItemKey] = new SiteContext(match.Site, match.Hostname, match.PathPrefix);
        await next();
    });
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    { Predicate = _ => false });
    app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    { Predicate = check => check.Tags.Contains("ready") });
    app.MapGet("/api/overview", (TrafficCounters stats, SecurityEvents events, SecurityRules rules) =>
        Results.Ok(new { generatedAt = DateTimeOffset.UtcNow, totalRequests = stats.Total, blockedRequests = stats.Blocked,
            allowedRequests = stats.Allowed, activeRules = rules.Count, droppedEvents = events.Dropped,
            threatCategories = stats.Categories.OrderByDescending(x => x.Value).Select(x => new { name = x.Key, count = x.Value }),
            methods = stats.Methods.OrderByDescending(x => x.Value).Select(x => new { name = x.Key, count = x.Value }),
            timeline = stats.Timeline(), topIps = stats.TopIps(), recentEvents = events.Recent() })).Require(Roles.Viewer);
    app.MapGet("/api/owasp-coverage", () => Results.Ok(OwaspCatalog.All.Select(x => new
    {
        code = x.Code, title = x.Title, coverage = x.Coverage.ToString(), description = x.Description,
        limitations = x.Limitations, ruleIds = OwaspCatalog.RuleIdsForCode(x.Code), officialUrl = OwaspCatalog.OfficialUrl
    }))).Require(Roles.Viewer);
    app.MapGet("/api/sites", (SiteRegistry registry, SiteHealthRegistry legacyHealth, IProxyStateLookup proxy) =>
    {
        var result = registry.Current.Sites.Select(site =>
        {
            proxy.TryGetCluster("site-" + site.Id, out var cluster);
            var upstreams = site.Upstreams.Select(upstream =>
            {
                var yarp = cluster is null ? null : cluster.DestinationsState.AllDestinations.FirstOrDefault(x => x.DestinationId == upstream.Id);
                var status = yarp is null
                    ? legacyHealth.Get(site.Id, upstream.Id)?.Healthy == false ? "unhealthy" : "unknown"
                    : SiteProxyHealth.Status(yarp.Health.Active.ToString(), yarp.Health.Passive.ToString());
                return new { id = upstream.Id, scheme = upstream.Address.Scheme, port = upstream.Address.Port, weight = upstream.Weight, health = status };
            }).ToArray();
            var siteHealth = upstreams.Length == 0 ? "invalid" : upstreams.All(x => x.health == "unhealthy") ? "unhealthy" : upstreams.Any(x => x.health == "healthy") ? "healthy" : "unknown";
            return new { siteId = site.Id, enabled = site.Enabled, hostnames = site.Hostnames, pathPrefixes = site.PathPrefixes, wafPolicy = site.WafPolicy, health = siteHealth, upstreams };
        });
        return Results.Ok(result);
    }).Require(Roles.Viewer);
    app.MapAuth();
    app.MapGet("/api/ip-policies", (IpAccessPolicy policies) => Results.Ok(policies.List())).Require(Roles.Viewer);
    app.MapPut("/api/ip-policies", async (IpPolicyInput input, IpAccessPolicy policies, IConfiguration cfg, HttpContext ctx, CancellationToken ct) =>
    {
        if (!IPAddress.TryParse(input.Ip, out var address) || (input.Action != "allow" && input.Action != "block")) return Results.BadRequest(new { error = "Provide a valid single IP and allow/block action" });
        var cs = cfg.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("Database unavailable", statusCode:503);
        try {
            await using var db = new NpgsqlConnection(cs); await db.OpenAsync(ct);
            await using var tx = await db.BeginTransactionAsync(ct);
            await using (var cmd = new NpgsqlCommand("INSERT INTO waf_ip_policies(ip,action,reason) VALUES (@ip,@action,@reason) ON CONFLICT(ip) DO UPDATE SET action=excluded.action,reason=excluded.reason,updated_at=now()",db,tx)) {
                cmd.Parameters.AddWithValue("ip",address.ToString());cmd.Parameters.AddWithValue("action",input.Action);cmd.Parameters.AddWithValue("reason",input.Reason??"");await cmd.ExecuteNonQueryAsync(ct);
            }
            await Audit.Write(db,tx,"ip-policy.upsert",address.ToString(),ctx.RequestAborted,ctx.Actor(),ctx.ClientIp());
            await tx.CommitAsync(ct); policies.Upsert(address.ToString(),input.Action,input.Reason??""); return Results.NoContent();
        } catch (Exception) { return Results.Problem("IP policy could not be persisted",statusCode:503); }
    }).Require(Roles.Operator);
    app.MapDelete("/api/ip-policies/{ip}", async (string ip, IpAccessPolicy policies, IConfiguration cfg, HttpContext ctx, CancellationToken ct) =>
    {
        if (!IPAddress.TryParse(ip,out var address)) return Results.BadRequest();
        try { await using var db=new NpgsqlConnection(cfg.GetConnectionString("Postgres"));await db.OpenAsync(ct);
            await using var tx=await db.BeginTransactionAsync(ct);
            await using(var cmd=new NpgsqlCommand("DELETE FROM waf_ip_policies WHERE ip=@ip",db,tx)){cmd.Parameters.AddWithValue("ip",address.ToString());await cmd.ExecuteNonQueryAsync(ct);}
            await Audit.Write(db,tx,"ip-policy.delete",address.ToString(),ct,ctx.Actor(),ctx.ClientIp());await tx.CommitAsync(ct);
            policies.Remove(address.ToString());return Results.NoContent();
        } catch(Exception) { return Results.Problem("Database unavailable",statusCode:503); }
    }).Require(Roles.Operator);
    app.MapGet("/api/audit", async (IConfiguration cfg, CancellationToken ct) =>
    {
        try { var result=new List<object>(); await using var db=new NpgsqlConnection(cfg.GetConnectionString("Postgres"));await db.OpenAsync(ct);
            await using var cmd=new NpgsqlCommand("SELECT occurred_at,action,target,actor,client_ip,outcome FROM waf_audit ORDER BY id DESC LIMIT 100",db);
            await using var reader=await cmd.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))result.Add(new { occurredAt=reader.GetDateTime(0),action=reader.GetString(1),target=reader.GetString(2),
                actor=reader.IsDBNull(3)?null:reader.GetString(3),clientIp=reader.IsDBNull(4)?null:reader.GetString(4),outcome=reader.GetString(5) });return Results.Ok(result);
        } catch(Exception) { return Results.Problem("Audit database unavailable",statusCode:503); }
    }).Require(Roles.Analyst);
    app.MapGet("/api/audit/integrity", async (IConfiguration cfg, ILoggerFactory loggerFactory, CancellationToken ct) =>
    {
        try
        {
            await using var db = new NpgsqlConnection(cfg.GetConnectionString("Postgres"));
            await db.OpenAsync(ct);
            var result = await Audit.VerifyAsync(db, ct);
            if (!result.Valid && result.BadId is long badId)
            {
                var alert = SecurityEventLogFormatter.FormatAuditIntegrityFailure(result.Checked, badId);
                loggerFactory.CreateLogger("AuditIntegrity").LogCritical("AEGISWAF_AUDIT_INTEGRITY_FAILURE {AuditIntegrityEvent}", alert);
            }
            return Results.Ok(new { valid = result.Valid, checkedEntries = result.Checked, firstInvalidId = result.BadId });
        }
        catch (Exception) { return Results.Problem("Audit integrity check unavailable", statusCode: 503); }
    }).Require(Roles.Analyst);
    // Dry-run inspection: never proxies, persists, or increments counters.
    app.MapPost("/api/rules/test", (RuleTestInput input, SecurityRules rules) =>
    {
        if (input.Sample is null || input.Sample.Length > 8192)
            return Results.BadRequest(new { error = "Sample must be at most 8192 characters" });
        var matched = rules.Match(input.Sample);
        return Results.Ok(new { matched = matched is not null, rule = matched, action = matched is null ? "allow" : "block", dryRun = true });
    }).Require(Roles.Analyst);
    app.MapGet("/api/config/proxy", (IConfiguration cfg) => Results.Ok(new {
        routes = cfg.GetSection("ReverseProxy:Routes").GetChildren().Select(x => new { id=x.Key, clusterId=x["ClusterId"], path=x["Match:Path"] }),
        clusters = cfg.GetSection("ReverseProxy:Clusters").GetChildren().Select(x => new { id=x.Key, destinations=x.GetSection("Destinations").GetChildren().Select(d=>d.Key) })
    })).Require(Roles.Viewer);
    app.MapGet("/api/custom-rules", (SecurityRules rules) => Results.Ok(rules.CustomList())).Require(Roles.Viewer);
    app.MapPut("/api/custom-rules/{id}", async (string id, CustomRuleInput input, SecurityRules rules, IConfiguration cfg, HttpContext ctx, CancellationToken ct) =>
    {
        if (id.Length is < 1 or > 64 || !Regex.IsMatch(id, "^[a-zA-Z0-9_-]+$")) return Results.BadRequest(new {error="Invalid rule id"});
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 || string.IsNullOrWhiteSpace(input.Pattern) || input.Pattern.Length > 512 || input.Priority is < 1 or > 10000)
            return Results.BadRequest(new {error="Invalid rule parameters"});
        try { _ = new Regex(input.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(20)); }
        catch (ArgumentException) { return Results.BadRequest(new {error="Invalid or unsupported regular expression"}); }
        try {
            await using var db = new NpgsqlConnection(cfg.GetConnectionString("Postgres")); await db.OpenAsync(ct);
            await using var tx = await db.BeginTransactionAsync(ct);
            await using(var cmd = new NpgsqlCommand("INSERT INTO waf_custom_rules(id,name,pattern,priority,enabled) VALUES (@id,@n,@p,@priority,@e) ON CONFLICT(id) DO UPDATE SET name=excluded.name,pattern=excluded.pattern,priority=excluded.priority,enabled=excluded.enabled,updated_at=now()",db,tx)) {
                cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("n",input.Name);cmd.Parameters.AddWithValue("p",input.Pattern);cmd.Parameters.AddWithValue("priority",input.Priority);cmd.Parameters.AddWithValue("e",input.Enabled);await cmd.ExecuteNonQueryAsync(ct);
            }
            await Audit.Write(db,tx,"custom-rule.upsert",id,ct,ctx.Actor(),ctx.ClientIp());await tx.CommitAsync(ct);
            rules.UpsertCustom(id,input);return Results.NoContent();
        } catch(Exception) { return Results.Problem("Could not persist custom rule",statusCode:503); }
    }).Require(Roles.SecurityAdmin);
    app.MapDelete("/api/custom-rules/{id}", async (string id, SecurityRules rules, IConfiguration cfg, HttpContext ctx, CancellationToken ct) =>
    {
        try {await using var db=new NpgsqlConnection(cfg.GetConnectionString("Postgres"));await db.OpenAsync(ct);
            await using var tx=await db.BeginTransactionAsync(ct);
            await using(var cmd=new NpgsqlCommand("DELETE FROM waf_custom_rules WHERE id=@id",db,tx)) {cmd.Parameters.AddWithValue("id",id);await cmd.ExecuteNonQueryAsync(ct);}
            await Audit.Write(db,tx,"custom-rule.delete",id,ct,ctx.Actor(),ctx.ClientIp());await tx.CommitAsync(ct);rules.RemoveCustom(id);return Results.NoContent();
        }catch(Exception){return Results.Problem("Could not delete custom rule",statusCode:503);}
    }).Require(Roles.SecurityAdmin);
    app.MapGet("/api/rules", (SecurityRules rules) => Results.Ok(rules.List())).Require(Roles.Viewer);
    app.MapPut("/api/rules/{id}/enabled", async (string id, RuleToggle toggle, SecurityRules rules, IConfiguration cfg, HttpContext ctx, CancellationToken ct) =>
    {
        if (!rules.Exists(id)) return Results.NotFound();
        var cs = cfg.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("PostgreSQL not configured", statusCode: 503);
        try
        {
            await using var db = new NpgsqlConnection(cs);
            await db.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("INSERT INTO waf_rule_states(rule_id,enabled,updated_at) VALUES (@id,@enabled,now()) ON CONFLICT (rule_id) DO UPDATE SET enabled=excluded.enabled,updated_at=now()", db);
            cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("enabled", toggle.Enabled);
            await cmd.ExecuteNonQueryAsync(ct);
            await Audit.Write(db,null,"rule.toggle",id+":"+toggle.Enabled,ct,ctx.Actor(),ctx.ClientIp());
            rules.Toggle(id, toggle.Enabled);
            return Results.NoContent();
        }
        catch (Exception) { return Results.Problem("Could not persist rule state. Check database migration.", statusCode: 503); }
    }).Require(Roles.SecurityAdmin);
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/health") ||
            ctx.Request.Path == "/" || ctx.Request.Path == "/index.html" || ctx.Request.Path.StartsWithSegments("/assets"))
        { await next(); return; }
        var stats = ctx.RequestServices.GetRequiredService<TrafficCounters>();
        var rules = ctx.RequestServices.GetRequiredService<SecurityRules>();
        var policyResolver = ctx.RequestServices.GetRequiredService<SitePolicyResolver>();
        var events = ctx.RequestServices.GetRequiredService<SecurityEvents>();
        var limiter = ctx.RequestServices.GetRequiredService<SlidingRateLimiter>();
        var policies = ctx.RequestServices.GetRequiredService<IpAccessPolicy>();
        var ip = ClientAddress.Resolve(ctx, ctx.RequestServices.GetRequiredService<IConfiguration>());
        var path = ctx.Request.Path.Value ?? "/";
        var method = ctx.Request.Method;
        var siteContext = SiteContext.Get(ctx);
        var sitePolicy = siteContext is null ? SiteWafPolicy.Global : policyResolver.Resolve(siteContext.Site);
        var watch = Stopwatch.StartNew();
        string? threat = null;
        async Task<string?> InspectValue(string value, bool checkOversize = false, InspectionBudget? budget = null)
        {
            var ssrf = await SsrfGuard.CheckAsync(value,
                ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Waf:Ssrf:ResolveDns", false),
                ctx.RequestAborted);
            return ssrf.Blocked ? ssrf.Reason : rules.Match(value, checkOversize, budget, sitePolicy);
        }
        if (policies.Check(ip) == "block") threat = "IP Blocklist";
        else if (!await limiter.AllowAsync(siteContext?.Site.Id ?? "legacy", ip, ctx.Request.Path.Value ?? "/", siteContext?.Site.RateLimit, ctx.RequestAborted)) threat = "Rate Limit";
        else
        {
            // 1) Request line: decoded path + raw query (normalised inside Match to defeat %xx / double-encoding).
            threat = await InspectValue(path + ctx.Request.QueryString.Value, checkOversize: true);
            // 2) Each query parameter on its own (ASP.NET has already %-decoded these — catches what the concat misses).
            if (threat is null)
                foreach (var q in ctx.Request.Query)
                { threat = await InspectValue(q.Key + "=" + q.Value); if (threat != null) break; }
            // 3) Attack-tool fingerprint, then headers and cookies that commonly carry payloads.
            if (threat is null) threat = rules.MatchUserAgent(ctx.Request.Headers.UserAgent.ToString(), sitePolicy);
            if (threat is null)
                foreach (var name in InspectedHeaders)
                    if (ctx.Request.Headers.TryGetValue(name, out var hv) && (threat = await InspectValue(hv.ToString())) != null) break;
            if (threat is null)
                foreach (var cookie in ctx.Request.Cookies)
                { threat = await InspectValue(cookie.Key + "=" + cookie.Value); if (threat != null) break; }
            // 4) Body of any textual content type (json, form, text, xml, multipart), including chunked (no Content-Length).
            var hasBody = ctx.Request.ContentLength is > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding");
            var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
            var inspectBinaryRequests = config.GetValue("Waf:InspectBinaryRequests", false);
            var textualBody = IsInspectableBody(ctx.Request.ContentType);
            if (threat is null && hasBody && (textualBody || inspectBinaryRequests))
            {
                    var maxBodyBytes = config.GetValue("Waf:MaxBodyInspectBytes", 131072);
                    var encoding = ctx.Request.Headers.ContentEncoding.ToString().Trim();
                    var limits = InspectionLimits.From(config) with { MaxDecompressedBytes = Math.Min(InspectionLimits.From(config).MaxDecompressedBytes, maxBodyBytes) };
                    var maxRawBytes = string.IsNullOrWhiteSpace(encoding) || encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)
                        ? maxBodyBytes : limits.MaxCompressedBytes;
                    if (ctx.Request.ContentLength > maxRawBytes) threat = "Inspection Body Limit"; // fail closed on oversized bodies
                else
                {
                        var budget = new InspectionBudget(limits.TimeoutMilliseconds);
                        using var inspectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
                        inspectionTimeout.CancelAfter(limits.TimeoutMilliseconds);
                        ctx.Request.EnableBuffering(bufferThreshold: 32768, bufferLimit: maxRawBytes);
                    try
                    {
                            using var raw = new MemoryStream();
                            await ctx.Request.Body.CopyToAsync(raw, inspectionTimeout.Token);
                        ctx.Request.Body.Position = 0;
                            var decoded = RequestBodyInspector.Decode(raw.ToArray(), encoding, limits);
                            if (budget.Expired)
                                threat = "Inspection Timeout";
                            else if (!decoded.Success)
                                threat = decoded.ReasonCode is "compressed_limit" or "decompressed_limit" or "decompression_ratio"
                                    ? "Inspection Body Limit" : "Malformed Structured Body";
                            else
                            {
                                var body = string.Empty;
                                try
                                {
                                    body = !textualBody && inspectBinaryRequests
                                        ? Encoding.Latin1.GetString(decoded.Bytes)
                                        : InspectionTextDecoder.Decode(decoded.Bytes);
                                }
                                catch (DecoderFallbackException) { threat = "Malformed Structured Body"; }
                                if (threat is null && budget.Expired) threat = "Inspection Timeout";
                                if (threat is null) threat = await InspectValue(body, budget: budget);
                                if (threat is null && budget.Expired) threat = "Inspection Timeout";
                                if (threat is null && textualBody)
                                {
                                    var parsed = StructuredPayloadParser.Parse(ctx.Request.ContentType, body, limits);
                                    if (budget.Expired)
                                        threat = "Inspection Timeout";
                                    else if (!parsed.Success)
                                        threat = parsed.ReasonCode is "json_limit" or "xml_limit" or "form_limit" or "multipart_limit"
                                            ? "Inspection Body Limit" : "Malformed Structured Body";
                                    else
                                        foreach (var value in parsed.Values)
                                        {
                                            threat = await InspectValue(value.Text, budget: budget);
                                            if (threat is not null) break;
                                            if (budget.Expired) { threat = "Inspection Timeout"; break; }
                                        }
                                }
                            }
                    }
                    catch (OperationCanceledException) when (!ctx.RequestAborted.IsCancellationRequested) { threat = "Inspection Timeout"; }
                    catch (IOException) { threat = "Inspection Body Limit"; }
                    catch (InvalidDataException) { threat = "Inspection Body Limit"; }
                }
            }
        }
        if (threat != null)
        {
            stats.Record(method, ip, threat);
            events.TryWrite(new SecurityEvent(DateTimeOffset.UtcNow, ip, method, path, threat, 403, ctx.TraceIdentifier,
                siteContext?.Site.Id, siteContext?.Hostname));
            ctx.Response.StatusCode = threat == "Rate Limit" ? 429 : 403;
            if (ctx.Response.StatusCode == 429) ctx.Response.Headers.RetryAfter = "60";
            await ctx.Response.WriteAsJsonAsync(new { title = "Request rejected", status = ctx.Response.StatusCode });
            return;
        }
        // Request looked clean. Optionally inspect the upstream RESPONSE for leaked secrets before it reaches the client.
        var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var inspector = ctx.RequestServices.GetService<ResponseInspector>();
        if (inspector is null || !cfg.GetValue("Waf:InspectResponses", true))
        {
            stats.Record(method, ip, null);
            RequestSanitizer.RemoveUntrustedForwardedHeaders(ctx, cfg);
            await next();
            return;
        }
        var cap = cfg.GetValue("Waf:MaxResponseInspectBytes", 262144);
        var inspectBinary = cfg.GetValue("Waf:InspectBinaryResponses", false);
        var original = ctx.Response.Body;
        var failClosedOverflow = string.Equals(cfg["Waf:ResponseOverflowAction"], "block", StringComparison.OrdinalIgnoreCase);
        var capture = new ResponseBufferStream(original, cap, failClosedOverflow);
        ctx.Response.Body = capture;
        RequestSanitizer.RemoveUntrustedForwardedHeaders(ctx, cfg);
        try { await next(); }
        finally { ctx.Response.Body = original; }
        // Only responses captured whole (under the cap) and of a textual type can be inspected; anything else streamed through already.
        string? leak = null;
        if (capture.Overflowed && failClosedOverflow)
        {
            stats.Record(method, ip, "Response Oversize");
            events.TryWrite(new SecurityEvent(DateTimeOffset.UtcNow, ip, method, path, "Response Oversize", 502, ctx.TraceIdentifier,
                siteContext?.Site.Id, siteContext?.Hostname));
            if (ctx.Response.HasStarted)
            {
                // Headers may already have been committed by the proxy before the body crosses the cap.
                // Abort the stream rather than returning an apparent 2xx with a truncated response.
                ctx.Abort();
                return;
            }
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.Clear();
                ctx.Response.StatusCode = 502;
                await ctx.Response.WriteAsJsonAsync(new { title = "Upstream response exceeded inspection limit", status = 502 });
            }
            return;
        }
        if (!capture.Overflowed && (ResponseInspector.IsInspectable(ctx.Response.ContentType) || inspectBinary))
        {
            try
            {
                var bytes = capture.CapturedBuffer.AsSpan(0, capture.CapturedLength);
                leak = ResponseInspector.IsInspectable(ctx.Response.ContentType)
                    ? inspector.Inspect(InspectionTextDecoder.Decode(bytes.ToArray()))
                    : inspector.InspectBytes(bytes);
            }
            catch (DecoderFallbackException)
            {
                leak = "Malformed Response UTF-8";
            }
        }
        if (leak != null)
        {
            stats.Record(method, ip, leak);
            events.TryWrite(new SecurityEvent(DateTimeOffset.UtcNow, ip, method, path, leak, 502, ctx.TraceIdentifier,
                siteContext?.Site.Id, siteContext?.Hostname));
            if (ctx.Response.HasStarted)
            {
                ctx.Abort();
                return;
            }
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.Clear();
                ctx.Response.StatusCode = 502;
                await ctx.Response.WriteAsJsonAsync(new { title = "Upstream response blocked", status = 502 });
            }
            return;
        }
        stats.Record(method, ip, null);
        if (!capture.Overflowed && capture.CapturedLength > 0)
            await original.WriteAsync(capture.CapturedBuffer.AsMemory(0, capture.CapturedLength));
    });
    app.Use(async (ctx, next) =>
    {
        var cfg = ctx.RequestServices.GetRequiredService<IConfiguration>();
        if (!cfg.GetValue("Waf:Deployment:Enforce", false) ||
            ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/health") ||
            ctx.Request.Path == "/" || ctx.Request.Path == "/index.html" || ctx.Request.Path.StartsWithSegments("/assets"))
        {
            await next();
            return;
        }

        var policy = ctx.RequestServices.GetRequiredService<UpstreamEgressPolicy>();
        var siteContext = SiteContext.Get(ctx);
        var destinations = siteContext is not null
            ? siteContext.Site.Upstreams.Select(x => (x.Id, x.Address, (SiteEgressPolicy?)siteContext.Site.Egress))
            : cfg.GetSection("ReverseProxy:Clusters").GetChildren()
                .SelectMany(cluster => cluster.GetSection("Destinations").GetChildren())
                .Where(destination => Uri.TryCreate(destination["Address"], UriKind.Absolute, out _))
                .Select(destination => (destination.Key, new Uri(destination["Address"]!, UriKind.Absolute), (SiteEgressPolicy?)null));
        foreach (var destination in destinations)
        {
            var decision = destination.Item3 is null
                ? await policy.CheckAsync(destination.Item2, ctx.RequestAborted)
                : await policy.CheckAsync(destination.Item2, destination.Item3, ctx.RequestAborted);
            if (!decision.Allowed)
            {
                ctx.RequestServices.GetRequiredService<SecurityEvents>().TryWrite(new SecurityEvent(
                    DateTimeOffset.UtcNow, ClientAddress.Resolve(ctx, cfg), ctx.Request.Method, ctx.Request.Path,
                    "SSRF: upstream egress " + decision.ReasonCode, 503, ctx.TraceIdentifier,
                    siteContext?.Site.Id, siteContext?.Hostname, destination.Item1));
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsJsonAsync(new { title = "Upstream egress policy rejected the destination", status = 503 });
                return;
            }
        }
        await next();
    });
    app.MapReverseProxy(proxyPipeline => proxyPipeline.UsePassiveHealthChecks().UseLoadBalancing());
    }

    private static bool IsManagementPath(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/health") ||
        path == "/" || path == "/index.html" || path.StartsWithSegments("/assets");
}
