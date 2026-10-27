using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using AegisWAF.Gateway.Traffic;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;

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
        var siteSnapshot = LoadSiteSnapshot(builder.Configuration);
        builder.Services.AddSingleton(siteSnapshot);
        var runtimeProvider = new SiteRuntimeConfigurationProvider(SiteProxyConfiguration.Build(siteSnapshot, builder.Configuration));
        builder.Services.AddSingleton(runtimeProvider);
        builder.Configuration.Sources.Add(new SiteRuntimeConfigurationSource(runtimeProvider));
        ProductionReadinessPolicy.ThrowIfInvalid(builder.Configuration);
        var keyRingPath = builder.Configuration["Security:DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            var dataProtection = builder.Services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(keyRingPath)));
            if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
            else
            {
                var certificatePath = builder.Configuration["Security:DataProtection:CertificatePath"];
                var certificatePassword = builder.Configuration["Security:DataProtection:CertificatePassword"];
                if (!string.IsNullOrWhiteSpace(certificatePath) && !string.IsNullOrWhiteSpace(certificatePassword))
                    dataProtection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certificatePassword));
            }
        }
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxRequestLineSize = builder.Configuration.GetValue("Waf:Limits:MaxRequestLineBytes", 16 * 1024);
            server.Limits.MaxRequestHeadersTotalSize = builder.Configuration.GetValue("Waf:Limits:MaxRequestHeadersBytes", 64 * 1024);
            server.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long?>("Waf:Limits:MaxRequestBodyBytes", 1_048_576);
            server.Limits.MaxConcurrentConnections = builder.Configuration.GetValue<long?>("Waf:Limits:MaxConcurrentConnections", 10_000);
            server.Limits.MinRequestBodyDataRate = new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(
                builder.Configuration.GetValue("Waf:Limits:MinRequestBodyBytesPerSecond", 240d),
                TimeSpan.FromSeconds(builder.Configuration.GetValue("Waf:Limits:RequestBodyDataRateGraceSeconds", 10)));
            server.Limits.MinResponseDataRate = new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(
                builder.Configuration.GetValue("Waf:Limits:MinResponseBytesPerSecond", 240d),
                TimeSpan.FromSeconds(builder.Configuration.GetValue("Waf:Limits:ResponseDataRateGraceSeconds", 10)));
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Waf:Limits:KeepAliveSeconds", 120));
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Waf:Limits:RequestHeadersTimeoutSeconds", 10));
        });
        return builder;
    }

    public static void AddServices(WebApplicationBuilder builder)
    {
    builder.Services.AddSingleton<SiteRegistry>();
    builder.Services.AddSingleton<SiteRuntimeCoordinator>();
    builder.Services.AddSingleton<SiteDatabaseStore>();
    builder.Services.AddSingleton<SiteRuntimeListenerStatus>();
    builder.Services.AddHostedService<SiteRuntimeChangeListener>();
    builder.Services.AddSingleton<SiteHealthProbe>();
        builder.Services.AddSingleton<SiteHealthHistory>();
    builder.Services.AddSingleton<SiteHealthSummaryFailureTelemetry>();
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
            handler.MaxConnectionsPerServer = builder.Configuration.GetValue("Waf:Egress:MaxConnectionsPerServer", 256);
            handler.UseProxy = false;
            handler.AllowAutoRedirect = false;
            handler.ConnectCallback = (socketContext, ct) => EgressConnectionGuard.ConnectAsync(policy, socketContext.DnsEndPoint, ct);
        });
    }

    var healthChecks = builder.Services.AddHealthChecks()
        .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
        .AddCheck<SiteRuntimeListenerHealthCheck>("site-runtime-listener", tags: ["ready"])
        .AddCheck<SiteRoutingHealthCheck>("sites", tags: ["ready"]);
    if (builder.Configuration.GetValue("Waf:Deployment:Enforce", false))
        healthChecks.AddCheck<DataProtectionHealthCheck>("data-protection", tags: ["ready"]);
    builder.Services.AddSingleton<SecurityRules>();
    builder.Services.AddSingleton<SitePolicyResolver>();
    builder.Services.AddSingleton<ResponseInspector>();
    builder.Services.AddHostedService<CustomRuleLoader>();
    builder.Services.AddHostedService<RuleStateLoader>();
    builder.Services.AddSingleton<TrafficCounters>();
    builder.Services.AddSingleton<TrafficTelemetry>();
    builder.Services.AddSingleton<SecurityEvents>();
    builder.Services.AddSingleton(sp => new SecurityEventRetentionStatus(
        SecurityEventRetentionOptions.From(sp.GetRequiredService<IConfiguration>())));
    builder.Services.AddSingleton<AuditIntegrityAlertState>();
    builder.Services.AddSingleton<SlidingRateLimiter>();
    builder.Services.AddSingleton<UpstreamEgressPolicy>(sp =>
        new UpstreamEgressPolicy(UpstreamEgressOptions.From(sp.GetRequiredService<IConfiguration>()), new SystemUpstreamAddressResolver()));
    if (builder.Configuration.GetValue("Waf:Deployment:Enforce", false))
        builder.Services.AddHostedService<ProductionEgressStartupValidator>();
    builder.Services.AddSingleton<IpAccessPolicy>();
    builder.Services.AddHostedService<IpPolicyLoader>();
    builder.Services.AddHostedService<EventWriter>();
    builder.Services.AddHostedService<SecurityEventRetentionWorker>();
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

    private static async Task<IResult> DeleteIpPolicy(string? ip, IpAccessPolicy policies, IConfiguration cfg, HttpContext ctx, CancellationToken ct)
    {
        if (!Cidr.TryNormalize(ip ?? "", out var policy)) return Results.BadRequest(new { error = "Provide a valid IP or CIDR" });
        try
        {
            await using var db = new NpgsqlConnection(cfg.GetConnectionString("Postgres"));
            await db.OpenAsync(ct);
            await using var tx = await db.BeginTransactionAsync(ct);
            await using (var cmd = new NpgsqlCommand("DELETE FROM waf_ip_policies WHERE ip=@ip", db, tx))
            {
                cmd.Parameters.AddWithValue("ip", policy);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await Audit.Write(db, tx, "ip-policy.delete", policy, ct, ctx.Actor(), ctx.ClientIp());
            await tx.CommitAsync(ct);
            policies.Remove(policy);
            return Results.NoContent();
        }
        catch (Exception) { return Results.Problem("Database unavailable", statusCode: 503); }
    }

    private static SiteConfigurationSnapshot LoadSiteSnapshot(IConfiguration configuration)
    {
        var snapshot = configuration.GetValue("Waf:Sites:UseDatabase", true)
            ? SiteDatabaseStore.TryLoadSnapshot(configuration.GetConnectionString("Postgres"))
            : null;
        snapshot ??= SiteConfigurationLoader.Load(configuration);
        var errors = SiteConfigurationValidator.ValidateSnapshot(snapshot, configuration);
        if (errors.Count > 0)
            throw new InvalidOperationException("Site snapshot rejected:\n - " + string.Join("\n - ", errors));
        return snapshot;
    }

    private static IResult SiteDatabaseUnavailable(HttpContext ctx, string operation, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Site database unavailable",
            detail: $"The Site configuration could not be {operation}.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["traceId"] = ctx.TraceIdentifier
            });

    private static IResult SiteMutationBusy(HttpContext ctx) =>
        Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Site mutation busy",
            detail: "Another Site change is still in progress. Retry the operation after it completes.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "site_mutation_busy",
                ["traceId"] = ctx.TraceIdentifier
            });

    private static IResult SiteRuntimeActivationFailed(HttpContext ctx, string operation) =>
        Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Site runtime activation failed",
            detail: $"The Site was {operation} in PostgreSQL, but the active runtime route could not be refreshed. Check the Site configuration and listener diagnostics.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "site_runtime_activation_failed",
                ["traceId"] = ctx.TraceIdentifier
            });

    private static IResult SiteHostnameConflict(HttpContext ctx) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Site hostname conflict",
            detail: "The Site could not be saved because one or more hostnames are already assigned to another Site.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "site_hostname_conflict",
                ["traceId"] = ctx.TraceIdentifier
            });

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
    app.Use(async (ctx, next) =>
    {
        var configuration = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var perimeterError = AdminPerimeterPolicy.ValidateRequest(ctx, configuration);
        if (perimeterError is not null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsJsonAsync(new { error = "Not found" });
            return;
        }
        await next();
    });
    // Harden management responses. GUI and APIs are loopback-only in this single-process prototype.
    app.Use(async (ctx, next) =>
    {
        var configuration = ctx.RequestServices.GetRequiredService<IConfiguration>();
        AdminSecurityHeaders.Apply(ctx, configuration);
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

        var telemetry = ctx.RequestServices.GetRequiredService<TrafficTelemetry>();
        using var scope = telemetry.BeginRequest();
        var watch = Stopwatch.StartNew();
        try { await next(); }
        finally
        {
            var decision = ctx.Items.TryGetValue(TrafficTelemetry.DecisionItem, out var decisionValue) && decisionValue is TrafficDecision explicitDecision
                ? explicitDecision
                : ctx.Response.StatusCode switch
                {
                    StatusCodes.Status429TooManyRequests => TrafficDecision.RateLimited,
                    >= 500 => TrafficDecision.UpstreamError,
                    _ => TrafficDecision.Allowed
                };
            var rule = ctx.Items.TryGetValue(TrafficTelemetry.RuleItem, out var ruleValue) ? ruleValue as string : null;
            var site = SiteContext.Get(ctx);
            // Keep hostname-level attribution for diagnostics when the host is known but
            // the path is outside that site's configured prefix (for example /favicon.ico).
            var telemetrySite = site is not null
                ? new SiteMatchResult(site.Site, site.Hostname, site.PathPrefix)
                : SiteMatcher.MatchHostname(ctx.RequestServices.GetRequiredService<SiteRegistry>().Current, ctx.Request.Host.Value);
            var proxied = ctx.Features.Get<Yarp.ReverseProxy.Model.IReverseProxyFeature>()?.ProxiedDestination;
            var upstreamId = proxied?.DestinationId;
            var upstreamAddress = proxied?.Model.Config.Address;
            telemetry.Record(new TrafficRecord(
                DateTimeOffset.UtcNow,
                ctx.TraceIdentifier,
                ClientAddress.Resolve(ctx, ctx.RequestServices.GetRequiredService<IConfiguration>()),
                telemetrySite?.Site.Id,
                TrafficTelemetry.ResolveHostname(telemetrySite?.Hostname, ctx.Request.Host),
                ctx.Request.Method,
                TrafficTelemetry.SanitizePath(ctx.Request.Path, ctx.Request.QueryString),
                upstreamId,
                upstreamAddress,
                decision,
                ctx.Response.StatusCode,
                rule,
                rule is null ? [] : OwaspCatalog.ForRule(rule).Codes,
                watch.ElapsedMilliseconds));
        }
    });
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
    { Predicate = _ => false, ResponseWriter = HealthResponseWriter.WriteAsync });
    app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    { Predicate = check => check.Tags.Contains("ready"), ResponseWriter = HealthResponseWriter.WriteAsync });
    app.MapGet("/api/overview", (TrafficCounters stats, SecurityEvents events, SecurityRules rules, SiteRuntimeListenerStatus listenerStatus, SecurityEventRetentionStatus retentionStatus) =>
         Results.Ok(new { generatedAt = DateTimeOffset.UtcNow, totalRequests = stats.Total, blockedRequests = stats.Blocked,
             allowedRequests = stats.Allowed, activeRules = rules.Count, droppedEvents = events.Dropped,
             eventDelivery = new { queueCapacity = events.QueueCapacity, queueDepth = events.QueueDepth, persisted = events.Persisted, persistenceFailures = events.PersistenceFailures },
            securityEventRetention = retentionStatus.Current,
            siteRuntimeListener = listenerStatus.Current,
            threatCategories = stats.Categories.OrderByDescending(x => x.Value).Select(x => new { name = x.Key, count = x.Value }),
            methods = stats.Methods.OrderByDescending(x => x.Value).Select(x => new { name = x.Key, count = x.Value }),
            timeline = stats.Timeline(), topIps = stats.TopIps(), recentEvents = events.Recent() })).Require(Roles.Viewer);
    app.MapGet("/api/command-center/summary", (TrafficTelemetry telemetry, HttpContext ctx) =>
    {
        ctx.Response.Headers.CacheControl = "no-store";
        var summary = telemetry.GetSummary();
        return Results.Ok(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            activeConnections = summary.ActiveConnections,
            requestsPerMinute = summary.RequestsPerMinute,
            allowed = summary.Allowed,
            blocked = summary.Blocked,
            rateLimited = summary.RateLimited,
            upstreamErrors = summary.UpstreamErrors,
            bufferedEvents = summary.BufferedEvents,
            droppedEvents = summary.DroppedEvents
        });
    }).Require(Roles.Analyst);
    app.MapGet("/api/command-center/traffic", (string? decision, int? limit, string? siteId, string? ip, string? q, TrafficTelemetry telemetry, HttpContext ctx) =>
    {
        ctx.Response.Headers.CacheControl = "no-store";
        var requestedLimit = limit ?? 100;
        if (requestedLimit is < 1 or > 500) return Results.BadRequest(new { error = "limit must be between 1 and 500" });
        TrafficDecision? parsedDecision = null;
        if (!string.IsNullOrWhiteSpace(decision))
        {
            if (!Enum.TryParse<TrafficDecision>(decision, true, out var parsed))
                return Results.BadRequest(new { error = "decision is invalid" });
            parsedDecision = parsed;
        }
        var records = telemetry.Query(new TrafficQuery(requestedLimit, parsedDecision, siteId, ip, q));
        return Results.Ok(records.Select(x => new
        {
            time = x.Timestamp,
            traceId = x.TraceId,
            clientIp = x.ClientIp,
            siteId = x.SiteId,
            hostname = x.Hostname,
            method = x.Method,
            path = x.Path,
            upstreamId = x.UpstreamId,
            upstreamAddress = x.UpstreamAddress,
            decision = x.Decision.ToString().ToUpperInvariant(),
            statusCode = x.StatusCode,
            rule = x.Rule,
            owaspCodes = x.OwaspCodes,
            durationMs = x.DurationMs
        }));
    }).Require(Roles.Analyst);
    app.MapGet("/api/owasp-coverage", () => Results.Ok(OwaspCatalog.All.Select(x => new
    {
        code = x.Code, title = x.Title, coverage = x.Coverage.ToString(), description = x.Description,
        limitations = x.Limitations, ruleIds = OwaspCatalog.RuleIdsForCode(x.Code), officialUrl = OwaspCatalog.OfficialUrl
    }))).Require(Roles.Viewer);
    app.MapGet("/api/sites", async (SiteRegistry registry, SiteHealthRegistry legacyHealth, IProxyStateLookup proxy, SiteDatabaseStore store, HttpContext ctx, CancellationToken ct) =>
    {
        using var readBudget = SiteHealthReadBudget.Create(ct);
        var readCt = readBudget.Token;
        var activeSites = registry.Current.Sites;
        IReadOnlyList<GatewaySite> storedSites;
        try { storedSites = await store.ListAsync(readCt); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "listed", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
        catch (OperationCanceledException) when (SiteHealthReadBudget.IsBudgetCancellation(ct, readCt))
        { return SiteDatabaseUnavailable(ctx, "list-timeout", "site_health_read_timeout"); }
        var result = storedSites.Select(site =>
        {
            var active = activeSites.FirstOrDefault(x => x.Id.Equals(site.Id, StringComparison.OrdinalIgnoreCase));
            var pendingRestart = active is null || active.Version != site.Version || !SiteConfigurationEditor.AreEquivalent(active, site);
            if (pendingRestart)
            {
                var pendingUpstreams = site.Upstreams.Select(upstream => new
                {
                    id = upstream.Id,
                    address = upstream.Address.ToString(),
                    scheme = upstream.Address.Scheme,
                    port = upstream.Address.Port,
                    weight = upstream.Weight,
                    health = "pending-restart"
                }).ToArray();
                return new
                {
                    siteId = site.Id,
                    enabled = site.Enabled,
                    hostnames = site.Hostnames,
                    pathPrefixes = site.PathPrefixes,
                    wafPolicy = site.WafPolicy,
                    healthPath = site.Health?.Path ?? "/",
                    healthIntervalSeconds = site.Health?.IntervalSeconds ?? 10,
                    healthTimeoutSeconds = site.Health?.TimeoutSeconds ?? 3,
                    healthPassiveReactivationSeconds = site.Health?.PassiveReactivationSeconds ?? 30,
                    rateRequests = site.RateLimit.Requests,
                    rateWindowSeconds = site.RateLimit.WindowSeconds,
                    rateGlobalRequests = site.RateLimit.GlobalRequests,
                    allowPrivateExceptions = site.Egress.AllowPrivateExceptions,
                    health = "pending-restart",
                    fresh = false,
                    source = "pending-restart",
                    lastProbeAt = (DateTimeOffset?)null,
                    version = site.Version,
                    etag = SiteConcurrency.ToEtag(site.Version),
                    activeVersion = active?.Version,
                    activationStatus = "pending-restart",
                    pendingRestart = true,
                    upstreams = pendingUpstreams
                };
            }

            proxy.TryGetCluster("site-" + site.Id, out var cluster);
            var upstreams = site.Upstreams.Select(upstream =>
            {
                var yarp = cluster is null ? null : cluster.DestinationsState.AllDestinations.FirstOrDefault(x => x.DestinationId == upstream.Id);
                var status = yarp is null
                    ? legacyHealth.Get(site.Id, upstream.Id)?.Healthy == false ? "unhealthy" : "unknown"
                    : SiteProxyHealth.Status(yarp.Health.Active.ToString(), yarp.Health.Passive.ToString());
                return new { id = upstream.Id, address = upstream.Address.ToString(), scheme = upstream.Address.Scheme, port = upstream.Address.Port, weight = upstream.Weight, health = status };
            }).ToArray();
            var siteHealth = upstreams.Length == 0 ? "invalid" : upstreams.All(x => x.health == "unhealthy") ? "unhealthy" : upstreams.Any(x => x.health == "healthy") ? "healthy" : "unknown";
            return new
            {
                siteId = site.Id,
                enabled = site.Enabled,
                hostnames = site.Hostnames,
                pathPrefixes = site.PathPrefixes,
                wafPolicy = site.WafPolicy,
                healthPath = site.Health?.Path ?? "/",
                healthIntervalSeconds = site.Health?.IntervalSeconds ?? 10,
                healthTimeoutSeconds = site.Health?.TimeoutSeconds ?? 3,
                healthPassiveReactivationSeconds = site.Health?.PassiveReactivationSeconds ?? 30,
                rateRequests = site.RateLimit.Requests,
                rateWindowSeconds = site.RateLimit.WindowSeconds,
                rateGlobalRequests = site.RateLimit.GlobalRequests,
                allowPrivateExceptions = site.Egress.AllowPrivateExceptions,
                health = siteHealth,
                fresh = false,
                source = "yarp-runtime-state",
                lastProbeAt = (DateTimeOffset?)null,
                version = site.Version,
                etag = SiteConcurrency.ToEtag(site.Version),
                activeVersion = (long?)active!.Version,
                activationStatus = "active",
                pendingRestart = false,
                upstreams
            };
        });
        return Results.Ok(result);
    }).Require(Roles.Viewer);
    app.MapPost("/api/sites/{id}/health", async (string id, SiteRegistry registry, SiteHealthRegistry legacyHealth, IProxyStateLookup proxy, SiteDatabaseStore store, SiteHealthProbe probe, SiteHealthHistory history, HttpContext ctx, CancellationToken ct) =>
    {
        using var readBudget = SiteHealthReadBudget.Create(ct);
        var readCt = readBudget.Token;
        IReadOnlyList<GatewaySite> storedSites;
        try { storedSites = await store.ListAsync(readCt); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "health-checked", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
        catch (OperationCanceledException) when (SiteHealthReadBudget.IsBudgetCancellation(ct, readCt))
        { return SiteDatabaseUnavailable(ctx, "health-check-timeout", "site_health_read_timeout"); }
        var site = storedSites.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (site is null) return Results.NotFound(new { error = $"Site '{id}' was not found" });
        var active = registry.Current.Sites.FirstOrDefault(x => x.Id.Equals(site.Id, StringComparison.OrdinalIgnoreCase));
        if (active is null || active.Version != site.Version || !SiteConfigurationEditor.AreEquivalent(active, site))
            return Results.Ok(new { siteId = site.Id, health = "pending-restart", pendingRestart = true, activationStatus = "pending-restart", version = site.Version, activeVersion = active?.Version, etag = SiteConcurrency.ToEtag(site.Version), fresh = false, source = "pending-restart", checkedAt = DateTimeOffset.UtcNow, upstreams = site.Upstreams.Select(x => new { id = x.Id, address = x.Address.ToString(), health = "pending-restart", reason = (string?)null }) });

        var probeResults = await probe.ProbeAsync(site, ct);
        history.Record(site.Id, probeResults);
        var historyPersisted = true;
        using var writeBudget = SiteHealthWriteBudget.Create(ct);
        var writeCt = writeBudget.Token;
        try
        {
            await store.RecordHealthHistoryAsync(site.Id, probeResults.Select(x => new SiteHealthObservation(x.UpstreamId, x.Health, x.Reason, x.CheckedAt)).ToArray(), writeCt);
        }
        catch (OperationCanceledException) when (SiteHealthWriteBudget.IsBudgetCancellation(ct, writeCt))
        {
            historyPersisted = false;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            historyPersisted = false;
        }
        var checkedAt = probeResults.Count == 0 ? DateTimeOffset.UtcNow : probeResults.Max(x => x.CheckedAt);
        var upstreams = probeResults.Select(result => new { id = result.UpstreamId, address = result.Address, health = result.Health, reason = result.Reason }).ToArray();
        var health = upstreams.Length == 0 ? "invalid" : upstreams.All(x => x.health == "unhealthy") ? "unhealthy" : upstreams.Any(x => x.health == "healthy") ? "healthy" : "unknown";
        return Results.Ok(new { siteId = site.Id, health, pendingRestart = false, activationStatus = "active", version = site.Version, activeVersion = active!.Version, etag = SiteConcurrency.ToEtag(site.Version), fresh = true, source = "active-probe", checkedAt, lastProbeAt = checkedAt, historyRetention = historyPersisted ? "postgresql" : "process-memory", historyPersisted, upstreams });
    }).Require(Roles.Viewer);
    app.MapGet("/api/sites/{id}/health/history", async (string id, SiteDatabaseStore store, int? limit, HttpContext ctx, CancellationToken ct) =>
    {
        using var readBudget = SiteHealthReadBudget.Create(ct);
        var readCt = readBudget.Token;
        IReadOnlyList<GatewaySite> storedSites;
        try { storedSites = await store.ListAsync(readCt); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "health-history-listed", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
        catch (OperationCanceledException) when (SiteHealthReadBudget.IsBudgetCancellation(ct, readCt))
        { return SiteDatabaseUnavailable(ctx, "health-history-timeout", "site_health_read_timeout"); }
        var site = storedSites.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (site is null) return Results.NotFound(new { error = $"Site '{id}' was not found" });
        IReadOnlyList<SiteHealthObservation> observations;
        try { observations = await store.ListHealthHistoryAsync(site.Id, limit ?? 20, readCt); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "health-history-listed", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
        catch (OperationCanceledException) when (SiteHealthReadBudget.IsBudgetCancellation(ct, readCt))
        { return SiteDatabaseUnavailable(ctx, "health-history-timeout", "site_health_read_timeout"); }
        return Results.Ok(new
        {
            siteId = site.Id,
            retention = "postgresql",
            maxObservationsPerSite = 64,
            observations = observations.Select(x => new { upstreamId = x.UpstreamId, health = x.Health, reason = x.Reason, checkedAt = x.CheckedAt })
        });
    }).Require(Roles.Viewer);
    app.MapGet("/api/sites/{id}/health/trend", async (string id, SiteDatabaseStore store, int? window, HttpContext ctx, CancellationToken ct) =>
    {
        using var readBudget = SiteHealthReadBudget.Create(ct);
        var readCt = readBudget.Token;
        IReadOnlyList<GatewaySite> storedSites;
        try { storedSites = await store.ListAsync(readCt); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "health-trend-listed", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
        catch (OperationCanceledException) when (SiteHealthReadBudget.IsBudgetCancellation(ct, readCt))
        { return SiteDatabaseUnavailable(ctx, "health-trend-timeout", "site_health_read_timeout"); }
        var site = storedSites.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (site is null) return Results.NotFound(new { error = $"Site '{id}' was not found" });
        var boundedWindow = Math.Clamp(window ?? 20, 1, SiteHealthTrendCalculator.MaxWindow);
        IReadOnlyList<SiteHealthObservation> observations;
        try { observations = await store.ListHealthHistoryAsync(site.Id, boundedWindow, readCt); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "health-trend-listed", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
        catch (OperationCanceledException) when (SiteHealthReadBudget.IsBudgetCancellation(ct, readCt))
        { return SiteDatabaseUnavailable(ctx, "health-trend-timeout", "site_health_read_timeout"); }
        var trend = SiteHealthTrendCalculator.Calculate(observations, boundedWindow, site.Upstreams.Count);
        var trendNow = DateTimeOffset.UtcNow;
        var trendIsStale = HealthAlertLifecycle.IsStale(trend, trendNow);
        SiteHealthAlertAcknowledgement? acknowledgement = null;
        if (HealthAlertLifecycle.IsActionable(trend, trendNow))
        {
            try { acknowledgement = await store.GetHealthAlertAcknowledgementAsync(site.Id, trend.AlertState, readCt); }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
            { return SiteDatabaseUnavailable(ctx, "health-trend-listed", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
            catch (OperationCanceledException) when (SiteHealthReadBudget.IsBudgetCancellation(ct, readCt))
            { return SiteDatabaseUnavailable(ctx, "health-trend-timeout", "site_health_read_timeout"); }
        }
        var suppressed = HealthAlertLifecycle.IsActionable(trend, trendNow) && acknowledgement is not null && HealthAlertLifecycle.IsSuppressed(trend.AlertState, acknowledgement.AlertState, acknowledgement.AcknowledgedUntil, trendNow);
        return Results.Ok(new
        {
            siteId = site.Id,
            windowSize = trend.WindowSize,
            total = trend.Total,
            healthy = trend.Healthy,
            unhealthy = trend.Unhealthy,
            unknown = trend.Unknown,
            healthyPercent = trend.HealthyPercent,
            consecutiveUnhealthy = trend.ConsecutiveUnhealthy,
            alertState = trend.AlertState,
            recoveryState = trend.RecoveryState,
            lastCheckedAt = trend.LastCheckedAt,
            isStale = trendIsStale,
            acknowledgedUntil = acknowledgement?.AcknowledgedUntil,
            acknowledgedBy = acknowledgement?.AcknowledgedBy,
            alertSuppressed = suppressed
        });
    }).Require(Roles.Viewer);
    app.MapPost("/api/sites/{id}/health/alerts/acknowledge", async (string id, SiteHealthAlertAcknowledgeRequest? input, SiteDatabaseStore store, HttpContext ctx, CancellationToken ct) =>
    {
        using var readBudget = SiteHealthReadBudget.Create(ct);
        var readCt = readBudget.Token;
        input ??= new SiteHealthAlertAcknowledgeRequest();
        if (!HealthAlertLifecycle.TryValidateCooldown(input.CooldownSeconds, out var cooldownError))
            return Results.BadRequest(new { error = "invalid_cooldown", detail = cooldownError });
        IReadOnlyList<GatewaySite> storedSites;
        try { storedSites = await store.ListAsync(readCt); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "health-alert-acknowledged", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
        catch (OperationCanceledException) when (SiteHealthReadBudget.IsBudgetCancellation(ct, readCt))
        { return SiteDatabaseUnavailable(ctx, "health-alert-read-timeout", "site_health_read_timeout"); }
        var site = storedSites.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (site is null) return Results.NotFound(new { error = $"Site '{id}' was not found" });
        IReadOnlyList<SiteHealthObservation> observations;
        try { observations = await store.ListHealthHistoryAsync(site.Id, SiteHealthTrendCalculator.MaxWindow, readCt); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "health-alert-acknowledged", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
        catch (OperationCanceledException) when (SiteHealthReadBudget.IsBudgetCancellation(ct, readCt))
        { return SiteDatabaseUnavailable(ctx, "health-alert-read-timeout", "site_health_read_timeout"); }
        var trend = SiteHealthTrendCalculator.Calculate(observations, SiteHealthTrendCalculator.MaxWindow, site.Upstreams.Count);
        var trendNow = DateTimeOffset.UtcNow;
        var trendIsStale = HealthAlertLifecycle.IsStale(trend, trendNow);
        if (!HealthAlertLifecycle.IsActionable(trend, trendNow))
            return Results.Conflict(new { error = "health_alert_not_actionable", alertState = trend.AlertState, recoveryState = trend.RecoveryState, isStale = trendIsStale });
        IAsyncDisposable? mutationLease = null;
        CancellationTokenSource? writeBudget = null;
        try
        {
            mutationLease = await store.AcquireSiteMutationLockAsync(ct);
            writeBudget = SiteHealthWriteBudget.Create(ct);
            var acknowledgement = await store.AcknowledgeHealthAlertAsync(site.Id, trend.AlertState, input.CooldownSeconds, ctx.Actor() ?? "unknown", ctx.ClientIp(), writeBudget.Token);
            return Results.Ok(new
            {
                siteId = site.Id,
                windowSize = trend.WindowSize,
                total = trend.Total,
                healthy = trend.Healthy,
                unhealthy = trend.Unhealthy,
                unknown = trend.Unknown,
                healthyPercent = trend.HealthyPercent,
                consecutiveUnhealthy = trend.ConsecutiveUnhealthy,
                alertState = trend.AlertState,
                recoveryState = trend.RecoveryState,
                lastCheckedAt = trend.LastCheckedAt,
                acknowledgedUntil = acknowledgement.AcknowledgedUntil,
                acknowledgedBy = acknowledgement.AcknowledgedBy,
                alertSuppressed = true
            });
        }
        catch (OperationCanceledException) when (writeBudget is not null && SiteHealthWriteBudget.IsBudgetCancellation(ct, writeBudget.Token))
        { return SiteDatabaseUnavailable(ctx, "health-alert-write-timeout", "site_health_write_timeout"); }
        catch (SiteMutationLockTimeoutException)
        { return SiteMutationBusy(ctx); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        { return SiteDatabaseUnavailable(ctx, "health-alert-acknowledged", "site_database_unavailable"); }
        finally
        {
            writeBudget?.Dispose();
            if (mutationLease is not null) await mutationLease.DisposeAsync();
        }
    }).Require(Roles.Operator);
    app.MapGet("/api/sites/health/summary", async (SiteDatabaseStore store, SiteHealthSummaryFailureTelemetry summaryTelemetry, int? window, int? limit, HttpContext ctx, CancellationToken ct) =>
    {
        var summaryGeneratedAt = DateTimeOffset.UtcNow;
        var summaryTimer = Stopwatch.StartNew();
        const int summaryQueryBudgetMs = SiteHealthSummaryBudget.DefaultMilliseconds;
        var boundedWindow = Math.Clamp(window ?? 20, 1, SiteHealthTrendCalculator.MaxWindow);
        var boundedLimit = Math.Clamp(limit ?? 100, 1, 100);
        using var summaryBudget = SiteHealthSummaryBudget.Create(ct, summaryQueryBudgetMs);
        var summaryCt = summaryBudget.Token;
        IReadOnlyList<GatewaySite> storedSites;
        try { storedSites = (await store.ListAsync(summaryCt)).Take(boundedLimit).ToArray(); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        {
            var failure = summaryTelemetry.RecordFailure();
            ctx.Response.Headers["X-Aegis-Health-Summary-Failures"] = failure.ConsecutiveFailures.ToString();
            return SiteDatabaseUnavailable(ctx, "health-summary-listed", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable");
        }
        catch (OperationCanceledException) when (SiteHealthSummaryBudget.IsBudgetCancellation(ct, summaryCt))
        {
            var failure = summaryTelemetry.RecordFailure();
            ctx.Response.Headers["X-Aegis-Health-Summary-Failures"] = failure.ConsecutiveFailures.ToString();
            return SiteDatabaseUnavailable(ctx, "health-summary-timeout", "site_health_summary_timeout");
        }
        var siteIds = storedSites.Select(x => x.Id).ToArray();
        IReadOnlyDictionary<string, IReadOnlyList<SiteHealthObservation>> histories;
        IReadOnlyDictionary<string, IReadOnlyList<SiteHealthAlertAcknowledgement>> acknowledgements;
        int expiredAcknowledgementsRemoved;
        try
        {
            expiredAcknowledgementsRemoved = await store.CleanupExpiredHealthAlertAcknowledgementsAsync(DateTimeOffset.UtcNow, summaryCt);
            histories = await store.ListHealthHistoryBySiteAsync(siteIds, boundedWindow, summaryCt);
            acknowledgements = await store.ListHealthAlertAcknowledgementsAsync(siteIds, summaryCt);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        {
            var failure = summaryTelemetry.RecordFailure();
            ctx.Response.Headers["X-Aegis-Health-Summary-Failures"] = failure.ConsecutiveFailures.ToString();
            return SiteDatabaseUnavailable(ctx, "health-summary-listed", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable");
        }
        catch (OperationCanceledException) when (SiteHealthSummaryBudget.IsBudgetCancellation(ct, summaryCt))
        {
            var failure = summaryTelemetry.RecordFailure();
            ctx.Response.Headers["X-Aegis-Health-Summary-Failures"] = failure.ConsecutiveFailures.ToString();
            return SiteDatabaseUnavailable(ctx, "health-summary-timeout", "site_health_summary_timeout");
        }
        var inputs = storedSites.Select(site =>
            (site.Id, histories.TryGetValue(site.Id, out var observations) ? observations : (IReadOnlyList<SiteHealthObservation>)[]));
        var expectedUpstreamCounts = storedSites.ToDictionary(x => x.Id, x => x.Upstreams.Count, StringComparer.OrdinalIgnoreCase);
        var comparison = SiteHealthComparisonCalculator.Calculate(inputs, boundedWindow, boundedLimit, DateTimeOffset.UtcNow, expectedUpstreamCounts);
        var alertCounts = SiteHealthSummaryAlertCounts.Calculate(comparison);
        var now = DateTimeOffset.UtcNow;
        summaryTimer.Stop();
        var queryDurationMs = (int)Math.Min(int.MaxValue, summaryTimer.ElapsedMilliseconds);
        var withinBudget = queryDurationMs <= summaryQueryBudgetMs;
        var telemetry = summaryTelemetry.RecordSuccess();
        var projected = comparison.Select(item =>
        {
            var acknowledgement = acknowledgements.TryGetValue(item.SiteId, out var states)
                ? states.FirstOrDefault(x => string.Equals(x.AlertState, item.Trend.AlertState, StringComparison.OrdinalIgnoreCase))
                : null;
            var suppressed = HealthAlertLifecycle.IsActionable(item) && acknowledgement is not null && HealthAlertLifecycle.IsSuppressed(item.Trend.AlertState, acknowledgement.AlertState, acknowledgement.AcknowledgedUntil, now);
            return new
            {
                siteId = item.SiteId,
                total = item.Trend.Total,
                healthy = item.Trend.Healthy,
                unhealthy = item.Trend.Unhealthy,
                unknown = item.Trend.Unknown,
                healthyPercent = item.Trend.HealthyPercent,
                consecutiveUnhealthy = item.Trend.ConsecutiveUnhealthy,
                alertState = item.Trend.AlertState,
                recoveryState = item.Trend.RecoveryState,
                isStale = item.IsStale,
                lastCheckedAt = item.Trend.LastCheckedAt,
                acknowledgedUntil = acknowledgement?.AcknowledgedUntil,
                acknowledgedBy = acknowledgement?.AcknowledgedBy,
                alertSuppressed = suppressed
            };
        }).ToArray();
        return Results.Ok(new
        {
            windowSize = boundedWindow,
            totalSites = projected.Length,
            criticalSites = alertCounts.CriticalSites,
            degradedSites = alertCounts.DegradedSites,
            healthySites = alertCounts.HealthySites,
            noDataSites = alertCounts.NoDataSites,
            recoveredSites = alertCounts.RecoveredSites,
            staleSites = alertCounts.StaleSites,
            staleCriticalSites = alertCounts.StaleCriticalSites,
            staleDegradedSites = alertCounts.StaleDegradedSites,
            expiredAcknowledgementsRemoved,
            generatedAt = summaryGeneratedAt,
            queryDurationMs,
            queryBudgetMs = summaryQueryBudgetMs,
            withinBudget,
            status = withinBudget ? "ready" : "slow",
            summaryFailureCount = telemetry.TotalFailures,
            summaryConsecutiveFailures = telemetry.ConsecutiveFailures,
            sites = projected
        });
    }).Require(Roles.Viewer);
    app.MapPost("/api/sites", async (SiteManagementRequest input, SiteRegistry registry, SiteDatabaseStore store, SiteRuntimeCoordinator runtime, IConfiguration configuration, HttpContext ctx, CancellationToken ct) =>
    {
        if (!SiteConfigurationEditor.TryNormalize(input, out var site, out var inputError))
            return Results.BadRequest(new { error = inputError });
        var candidate = SiteDatabaseStore.ToGatewaySite(site);
        try
        {
            await using var mutationLock = await store.AcquireSiteMutationLockAsync(ct);
            using var validationBudget = SiteMutationReadBudget.Create(ct);
            var validationCt = validationBudget.Token;
            IReadOnlyList<GatewaySite> storedSitesForValidation;
            try { storedSitesForValidation = await store.ListAsync(validationCt); }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
            { return SiteDatabaseUnavailable(ctx, "validated", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
            catch (OperationCanceledException) when (SiteMutationReadBudget.IsBudgetCancellation(ct, validationCt))
            { return SiteDatabaseUnavailable(ctx, "validation-timeout", "site_mutation_read_timeout"); }
            var proposedSites = storedSitesForValidation.Append(candidate).ToArray();
            var validationErrors = SiteConfigurationValidator.ValidateSnapshot(
                new SiteConfigurationSnapshot(proposedSites, false), configuration);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { error = "Site configuration rejected: " + string.Join("; ", validationErrors), code = "site_configuration_invalid", errors = validationErrors });
            var inserted = await store.InsertAsync(candidate, ctx.Actor(), ctx.ClientIp(), ct);
            if (!inserted) return Results.Conflict(new { error = $"Site ID '{site.Id}' already exists" });
            try
            {
                await runtime.ActivateFromStoreAsync(store, ct);
                return Results.Ok(new { siteId = site.Id, active = true, requiresRestart = false, message = "Site saved to PostgreSQL and route activated immediately." });
            }
            catch (SiteRuntimeActivationException)
            { return SiteRuntimeActivationFailed(ctx, "saved"); }
        }
        catch (SiteHostnameConflictException)
        { return SiteHostnameConflict(ctx); }
        catch (SiteMutationLockTimeoutException)
        { return SiteMutationBusy(ctx); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "saved", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
    }).Require(Roles.SecurityAdmin);
    app.MapPut("/api/sites/{id}", async (string id, SiteManagementRequest input, SiteRegistry registry, SiteDatabaseStore store, SiteRuntimeCoordinator runtime, IConfiguration configuration, HttpContext ctx, CancellationToken ct) =>
    {
        if (!SiteConfigurationEditor.TryNormalize(input with { Id = id }, out var site, out var inputError))
            return Results.BadRequest(new { error = inputError });
        var candidate = SiteDatabaseStore.ToGatewaySite(site);
        try
        {
            await using var mutationLock = await store.AcquireSiteMutationLockAsync(ct);
            using var validationBudget = SiteMutationReadBudget.Create(ct);
            var validationCt = validationBudget.Token;
            IReadOnlyList<GatewaySite> storedSitesForValidation;
            try { storedSitesForValidation = await store.ListAsync(validationCt); }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
            { return SiteDatabaseUnavailable(ctx, "validated", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
            catch (OperationCanceledException) when (SiteMutationReadBudget.IsBudgetCancellation(ct, validationCt))
            { return SiteDatabaseUnavailable(ctx, "validation-timeout", "site_mutation_read_timeout"); }
            var proposedSites = storedSitesForValidation
                .Where(existing => !existing.Id.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase))
                .Append(candidate)
                .ToArray();
            var validationErrors = SiteConfigurationValidator.ValidateSnapshot(
                new SiteConfigurationSnapshot(proposedSites, false), configuration);
            if (validationErrors.Count > 0)
                return Results.BadRequest(new { error = "Site configuration rejected: " + string.Join("; ", validationErrors), code = "site_configuration_invalid", errors = validationErrors });
            var expectedVersion = input.ExpectedVersion;
            if (ctx.Request.Headers.IfMatch.Count > 0)
            {
                var header = ctx.Request.Headers.IfMatch.ToString();
                if (!SiteConcurrency.TryParseEtag(header, out var headerVersion))
                    return Results.BadRequest(new { error = "If-Match must contain a strong Site ETag", code = "site_etag_invalid" });
                if (expectedVersion.HasValue && expectedVersion.Value != headerVersion)
                    return Results.BadRequest(new { error = "Expected version and If-Match do not match", code = "site_version_mismatch" });
                expectedVersion = headerVersion;
            }
            if (!expectedVersion.HasValue)
                return Results.Conflict(new { error = "A current Site version or If-Match ETag is required", code = "site_version_required" });
            var updated = await store.UpdateWithConcurrencyAsync(candidate, expectedVersion.Value, ctx.Actor(), ctx.ClientIp(), ct);
            if (updated.Status == SiteUpdateStatus.Updated)
            {
                var version = updated.CurrentVersion!.Value;
                ctx.Response.Headers.ETag = SiteConcurrency.ToEtag(version);
                try
                {
                    await runtime.ActivateFromStoreAsync(store, ct);
                    return Results.Ok(new { siteId = site.Id, version, etag = SiteConcurrency.ToEtag(version), active = true, requiresRestart = false, message = "Site updated in PostgreSQL and route activated immediately." });
                }
                catch (SiteRuntimeActivationException)
                { return SiteRuntimeActivationFailed(ctx, "updated"); }
            }
            if (updated.Status == SiteUpdateStatus.Conflict)
            {
                var version = updated.CurrentVersion!.Value;
                ctx.Response.Headers.ETag = SiteConcurrency.ToEtag(version);
                return Results.Conflict(new { error = "Site changed since it was loaded; refresh before saving", code = "site_version_conflict", currentVersion = version, etag = SiteConcurrency.ToEtag(version) });
            }
            return Results.NotFound(new { error = $"Site '{id}' was not found" });
        }
        catch (SiteHostnameConflictException)
        { return SiteHostnameConflict(ctx); }
        catch (SiteMutationLockTimeoutException)
        { return SiteMutationBusy(ctx); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "updated", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
    }).Require(Roles.SecurityAdmin);
    app.MapDelete("/api/sites/{id}", async (string id, SiteDatabaseStore store, SiteRuntimeCoordinator runtime, HttpContext ctx, CancellationToken ct) =>
    {
        if (!SiteConcurrency.TryParseEtag(ctx.Request.Headers.IfMatch.ToString(), out var expectedVersion))
            return Results.Conflict(new { error = "If-Match with the current Site ETag is required before delete", code = "site_version_required" });
        try
        {
            await using var mutationLock = await store.AcquireSiteMutationLockAsync(ct);
            var result = await store.DeleteWithConcurrencyAsync(id.Trim().ToLowerInvariant(), expectedVersion, ctx.Actor(), ctx.ClientIp(), ct);
            if (result.Status == SiteDeleteStatus.Conflict)
            {
                var version = result.CurrentVersion!.Value;
                ctx.Response.Headers.ETag = SiteConcurrency.ToEtag(version);
                return Results.Conflict(new { error = "Site changed since it was loaded; refresh before deleting", code = "site_version_conflict", currentVersion = version, etag = SiteConcurrency.ToEtag(version) });
            }
            if (result.Status == SiteDeleteStatus.NotFound) return Results.NotFound(new { error = $"Site '{id}' was not found" });
            try
            {
                await runtime.ActivateFromStoreAsync(store, ct);
                return Results.Ok(new { siteId = id, active = true, requiresRestart = false, message = "Site deleted from PostgreSQL and route deactivated immediately." });
            }
            catch (SiteRuntimeActivationException)
            { return SiteRuntimeActivationFailed(ctx, "deleted"); }
        }
        catch (SiteMutationLockTimeoutException)
        { return SiteMutationBusy(ctx); }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or InvalidDataException)
        { return SiteDatabaseUnavailable(ctx, "deleted", ex is InvalidDataException ? "site_persistence_invalid" : "site_database_unavailable"); }
    }).Require(Roles.SecurityAdmin);
    app.MapAuth();
    app.MapGet("/api/ip-policies", (IpAccessPolicy policies) => Results.Ok(policies.List())).Require(Roles.Viewer);
    app.MapPut("/api/ip-policies", async (IpPolicyInput input, IpAccessPolicy policies, IConfiguration cfg, HttpContext ctx, CancellationToken ct) =>
    {
        if (!Cidr.TryNormalize(input.Ip, out var policy) || (input.Action != "allow" && input.Action != "block")) return Results.BadRequest(new { error = "Provide a valid IP or CIDR and allow/block action" });
        var cs = cfg.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("Database unavailable", statusCode:503);
        try {
            await using var db = new NpgsqlConnection(cs); await db.OpenAsync(ct);
            await using var tx = await db.BeginTransactionAsync(ct);
            await using (var cmd = new NpgsqlCommand("INSERT INTO waf_ip_policies(ip,action,reason) VALUES (@ip,@action,@reason) ON CONFLICT(ip) DO UPDATE SET action=excluded.action,reason=excluded.reason,updated_at=now()",db,tx)) {
                cmd.Parameters.AddWithValue("ip",policy);cmd.Parameters.AddWithValue("action",input.Action);cmd.Parameters.AddWithValue("reason",input.Reason??"");await cmd.ExecuteNonQueryAsync(ct);
            }
            await Audit.Write(db,tx,"ip-policy.upsert",policy,ctx.RequestAborted,ctx.Actor(),ctx.ClientIp());
            await tx.CommitAsync(ct); policies.Upsert(policy,input.Action,input.Reason??""); return Results.NoContent();
        } catch (Exception) { return Results.Problem("IP policy could not be persisted",statusCode:503); }
    }).Require(Roles.Operator);
    app.MapDelete("/api/ip-policies/{ip}", DeleteIpPolicy).Require(Roles.Operator);
    app.MapDelete("/api/ip-policies", (HttpContext ctx, IpAccessPolicy policies, IConfiguration cfg, CancellationToken ct) =>
        DeleteIpPolicy(ctx.Request.Query["ip"].ToString(), policies, cfg, ctx, ct)).Require(Roles.Operator);
    app.MapGet("/api/audit", async (IConfiguration cfg, CancellationToken ct) =>
    {
        try { var result=new List<object>(); await using var db=new NpgsqlConnection(cfg.GetConnectionString("Postgres"));await db.OpenAsync(ct);
            await using var cmd=new NpgsqlCommand("SELECT occurred_at,action,target,actor,client_ip,outcome FROM waf_audit ORDER BY id DESC LIMIT 100",db);
            await using var reader=await cmd.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))result.Add(new { occurredAt=reader.GetDateTime(0),action=reader.GetString(1),target=reader.GetString(2),
                actor=reader.IsDBNull(3)?null:reader.GetString(3),clientIp=reader.IsDBNull(4)?null:reader.GetString(4),outcome=reader.GetString(5) });return Results.Ok(result);
        } catch(Exception) { return Results.Problem("Audit database unavailable",statusCode:503); }
    }).Require(Roles.Analyst);
    app.MapGet("/api/audit/integrity", async (IConfiguration cfg, ILoggerFactory loggerFactory, SecurityEvents events, AuditIntegrityAlertState alerts, HttpContext ctx, CancellationToken ct) =>
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
                if (alerts.TryClaim(badId))
                {
                    var eventRecord = new SecurityEvent(
                        DateTimeOffset.UtcNow,
                        "system",
                        ctx.Request.Method,
                        ctx.Request.Path,
                        $"Audit Integrity Failure (firstInvalidId={badId})",
                        StatusCodes.Status500InternalServerError,
                        ctx.TraceIdentifier);
                    if (!events.TryWrite(eventRecord)) alerts.Release(badId);
                }
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
            // 4) Body of textual content types and bounded text-like opaque bodies, including chunked (no Content-Length).
            var hasBody = ctx.Request.ContentLength is > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding");
            var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
            var inspectBinaryRequests = config.GetValue("Waf:InspectBinaryRequests", false);
            var structuredBody = IsInspectableBody(ctx.Request.ContentType);
            var opaqueBody = ContentInspection.IsOpaqueContentType(ctx.Request.ContentType);
            var textualBody = structuredBody;
            if (threat is null && hasBody && (structuredBody || opaqueBody || inspectBinaryRequests))
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
                                    if (!textualBody && !inspectBinaryRequests && !ContentInspection.IsTextLike(decoded.Bytes))
                                    {
                                        body = string.Empty;
                                    }
                                    else
                                    {
                                        body = !textualBody && inspectBinaryRequests
                                        ? Encoding.Latin1.GetString(decoded.Bytes)
                                        : InspectionTextDecoder.Decode(decoded.Bytes);
                                    }
                                }
                                catch (DecoderFallbackException) { threat = "Malformed Structured Body"; }
                                if (threat is null && budget.Expired) threat = "Inspection Timeout";
                                if (threat is null && (textualBody || inspectBinaryRequests || body.Length > 0))
                                    threat = await InspectValue(body, budget: budget);
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
            TrafficTelemetry.Mark(ctx, threat == "Rate Limit" ? TrafficDecision.RateLimited : TrafficDecision.Blocked, threat);
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
            TrafficTelemetry.Mark(ctx, TrafficDecision.UpstreamError, "Response Oversize");
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
        if (!capture.Overflowed && (ResponseInspector.IsInspectable(ctx.Response.ContentType, capture.CapturedBuffer.AsSpan(0, capture.CapturedLength)) || inspectBinary))
        {
            try
            {
                var bytes = capture.CapturedBuffer.AsSpan(0, capture.CapturedLength);
                var limits = InspectionLimits.From(cfg) with { MaxCompressedBytes = Math.Clamp(cap, 1, 16 * 1024 * 1024) };
                leak = inspector.InspectEncoded(bytes, ctx.Response.Headers.ContentEncoding.ToString(), limits,
                    textual: ResponseInspector.IsInspectable(ctx.Response.ContentType));
            }
            catch (DecoderFallbackException)
            {
                leak = "Malformed Response UTF-8";
            }
        }
        if (leak != null)
        {
            TrafficTelemetry.Mark(ctx, TrafficDecision.UpstreamError, leak);
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
                TrafficTelemetry.Mark(ctx, TrafficDecision.UpstreamError, "SSRF: upstream egress " + decision.ReasonCode);
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
