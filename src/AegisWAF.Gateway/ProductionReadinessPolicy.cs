using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace AegisWAF.Gateway;

/// <summary>
/// Fail-closed configuration gate for deployments that explicitly opt into production enforcement.
/// The desktop profile remains usable with local defaults; a production deployment must set
/// <c>Waf:Deployment:Enforce=true</c> and satisfy every check below.
/// </summary>
public static class ProductionReadinessPolicy
{
    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        if (!configuration.GetValue("Waf:Deployment:Enforce", false)) return [];

        var errors = new List<string>();
        errors.AddRange(AdminPerimeterPolicy.ValidateConfiguration(configuration));
        Require(configuration.GetValue("Database:RequireLeastPrivilege", false),
            "Database:RequireLeastPrivilege must be true", errors);
        Require(!configuration.GetValue("Database:AutoMigrate", true),
            "Database:AutoMigrate must be false; run reviewed migrations separately", errors);
        Require(configuration.GetValue("Admin:RequireHttps", true),
            "Admin:RequireHttps must be true", errors);
        Require(configuration.GetValue("Auth:SessionIdleMinutes", 30) is >= 1 and <= 1_440,
            "Auth:SessionIdleMinutes must be between 1 and 1440 minutes", errors);
        Require(configuration.GetValue("Auth:SessionAbsoluteHours", 8) is >= 1 and <= 168,
            "Auth:SessionAbsoluteHours must be between 1 and 168 hours", errors);
        Require(configuration.GetValue("Auth:MaxFailedAttempts", 5) is >= 1 and <= 100,
            "Auth:MaxFailedAttempts must be between 1 and 100", errors);
        Require(configuration.GetValue("Auth:LockoutMinutes", 15) is >= 1 and <= 1_440,
            "Auth:LockoutMinutes must be between 1 and 1440 minutes", errors);
        var keyRingPath = configuration["Security:DataProtection:KeyRingPath"];
        Require(!string.IsNullOrWhiteSpace(keyRingPath) && Path.IsPathFullyQualified(keyRingPath),
            "Security:DataProtection:KeyRingPath must be an absolute persistent path", errors);
        if (!OperatingSystem.IsWindows())
        {
            Require(!string.IsNullOrWhiteSpace(configuration["Security:DataProtection:CertificatePath"]) &&
                    Path.IsPathFullyQualified(configuration["Security:DataProtection:CertificatePath"]!),
                "Security:DataProtection:CertificatePath must be an absolute path on non-Windows Production hosts", errors);
            Require(!string.IsNullOrWhiteSpace(configuration["Security:DataProtection:CertificatePassword"]),
                "Security:DataProtection:CertificatePassword is required on non-Windows Production hosts", errors);
        }
        Require(!configuration.GetValue("Waf:AllowProtocolUpgrade", false),
            "Waf:AllowProtocolUpgrade must be false unless a dedicated inspected route is deployed", errors);
        Require(!configuration.GetValue("Waf:AllowUnsafeMethods", false),
            "Waf:AllowUnsafeMethods must be false", errors);
        Require(configuration.GetValue("Waf:InspectBinaryRequests", false),
            "Waf:InspectBinaryRequests must be true to inspect content-type spoofing", errors);
        Require(configuration.GetValue("Waf:InspectResponses", true),
            "Waf:InspectResponses must be true for response leak inspection", errors);
        Require(configuration.GetValue("Waf:InspectBinaryResponses", false),
            "Waf:InspectBinaryResponses must be true to detect content-type spoofing", errors);
        if (configuration.GetValue("Waf:AnomalyScoring:Enabled", false))
            Require(configuration.GetValue("Waf:AnomalyScoring:Threshold", 7) is >= 3 and <= 100,
                "Waf:AnomalyScoring:Threshold must be between 3 and 100 when anomaly scoring is enabled", errors);
        Require(configuration.GetValue("Waf:RateLimit:Requests", 0) is >= 1 and <= 1_000_000,
            "Waf:RateLimit:Requests must be between 1 and 1000000", errors);
        Require(configuration.GetValue("Waf:RateLimit:WindowSeconds", 0) is >= 1 and <= 3600,
            "Waf:RateLimit:WindowSeconds must be between 1 and 3600 seconds", errors);
        Require(configuration.GetValue("Waf:RateLimit:MaxEntries", 0) is >= 100 and <= 1_000_000,
            "Waf:RateLimit:MaxEntries must be between 100 and 1000000", errors);
        Require(configuration.GetValue("Waf:RateLimit:GlobalRequests", 0) is >= 1 and <= 10_000_000,
            "Waf:RateLimit:GlobalRequests must be between 1 and 10000000 for botnet-wide protection", errors);
        Require(string.Equals(configuration["Waf:RateLimit:Store"], "postgres", StringComparison.OrdinalIgnoreCase),
            "Waf:RateLimit:Store must be postgres for multi-instance shared enforcement", errors);
        Require(string.Equals(configuration["Auth:Throttle:Store"], "postgres", StringComparison.OrdinalIgnoreCase),
            "Auth:Throttle:Store must be postgres for distributed login protection", errors);
        Require(configuration.GetValue("Auth:Throttle:MaxFailures", 0) is >= 1 and <= 1_000,
            "Auth:Throttle:MaxFailures must be between 1 and 1000", errors);
        Require(configuration.GetValue("Auth:Throttle:WindowMinutes", 0) is >= 1 and <= 1_440,
            "Auth:Throttle:WindowMinutes must be between 1 and 1440 minutes", errors);
        Require(configuration.GetValue("Auth:Throttle:MaxEntries", 0) is >= 100 and <= 1_000_000,
            "Auth:Throttle:MaxEntries must be between 100 and 1000000", errors);
        Require(!string.IsNullOrWhiteSpace(configuration.GetConnectionString("Postgres")),
            "ConnectionStrings:Postgres is required for the shared rate limiter", errors);
        Require(configuration.GetValue("Waf:Events:StructuredLogging", false),
            "Waf:Events:StructuredLogging must be true for SIEM ingestion", errors);
        Require(configuration.GetValue("Waf:Ssrf:ResolveDns", false),
            "Waf:Ssrf:ResolveDns must be true for DNS rebinding-aware SSRF checks", errors);
        Require(string.Equals(configuration["Waf:ResponseOverflowAction"], "block", StringComparison.OrdinalIgnoreCase),
            "Waf:ResponseOverflowAction must be block so oversized responses fail closed", errors);
        var bodyCap = configuration.GetValue("Waf:MaxBodyInspectBytes", 0);
        var responseCap = configuration.GetValue("Waf:MaxResponseInspectBytes", 0);
        Require(bodyCap is >= 1 and <= 16 * 1024 * 1024,
            "Waf:MaxBodyInspectBytes must be between 1 byte and 16 MiB", errors);
        Require(responseCap is >= 1 and <= 32 * 1024 * 1024,
            "Waf:MaxResponseInspectBytes must be between 1 byte and 32 MiB", errors);
        var requestBodyCap = configuration.GetValue<long?>("Waf:Limits:MaxRequestBodyBytes", null);
        Require(requestBodyCap is >= 1 and <= 16 * 1024 * 1024,
            "Waf:Limits:MaxRequestBodyBytes must be between 1 byte and 16 MiB", errors);
        Require(requestBodyCap <= bodyCap,
            "Waf:Limits:MaxRequestBodyBytes must not exceed Waf:MaxBodyInspectBytes", errors);
        var requestHeadersTimeout = configuration.GetValue("Waf:Limits:RequestHeadersTimeoutSeconds", 10);
        Require(requestHeadersTimeout is >= 1 and <= 120,
            "Waf:Limits:RequestHeadersTimeoutSeconds must be between 1 and 120 seconds", errors);
        var keepAlive = configuration.GetValue("Waf:Limits:KeepAliveSeconds", 120);
        Require(keepAlive is >= 5 and <= 600,
            "Waf:Limits:KeepAliveSeconds must be between 5 and 600 seconds", errors);
        var requestLineBytes = configuration.GetValue("Waf:Limits:MaxRequestLineBytes", 16 * 1024);
        Require(requestLineBytes is >= 1024 and <= 64 * 1024,
            "Waf:Limits:MaxRequestLineBytes must be between 1 KiB and 64 KiB", errors);
        var requestHeadersBytes = configuration.GetValue("Waf:Limits:MaxRequestHeadersBytes", 64 * 1024);
        Require(requestHeadersBytes is >= 4 * 1024 and <= 256 * 1024,
            "Waf:Limits:MaxRequestHeadersBytes must be between 4 KiB and 256 KiB", errors);
        var maxConcurrentConnections = configuration.GetValue("Waf:Limits:MaxConcurrentConnections", 10_000L);
        Require(maxConcurrentConnections is >= 1 and <= 1_000_000,
            "Waf:Limits:MaxConcurrentConnections must be between 1 and 1000000", errors);
        var minRequestBodyRate = configuration.GetValue("Waf:Limits:MinRequestBodyBytesPerSecond", 240d);
        Require(minRequestBodyRate is >= 1 and <= 1_000_000,
            "Waf:Limits:MinRequestBodyBytesPerSecond must be between 1 and 1000000", errors);
        var requestBodyRateGrace = configuration.GetValue("Waf:Limits:RequestBodyDataRateGraceSeconds", 10);
        Require(requestBodyRateGrace is >= 1 and <= 120,
            "Waf:Limits:RequestBodyDataRateGraceSeconds must be between 1 and 120 seconds", errors);
        var minResponseRate = configuration.GetValue("Waf:Limits:MinResponseBytesPerSecond", 240d);
        Require(minResponseRate is >= 1 and <= 1_000_000,
            "Waf:Limits:MinResponseBytesPerSecond must be between 1 and 1000000", errors);
        var responseRateGrace = configuration.GetValue("Waf:Limits:ResponseDataRateGraceSeconds", 10);
        Require(responseRateGrace is >= 1 and <= 120,
            "Waf:Limits:ResponseDataRateGraceSeconds must be between 1 and 120 seconds", errors);

        var inspection = InspectionLimits.From(configuration);
        Require(configuration.GetValue("Waf:Inspection:MaxCompressedBytes", 131_072) is >= 1 and <= 16 * 1024 * 1024,
            "Waf:Inspection:MaxCompressedBytes must be between 1 byte and 16 MiB", errors);
        Require(configuration.GetValue("Waf:Inspection:MaxDecompressedBytes", 1_048_576) is >= 1 and <= 16 * 1024 * 1024,
            "Waf:Inspection:MaxDecompressedBytes must be between 1 byte and 16 MiB", errors);
        Require(configuration.GetValue("Waf:Inspection:MaxExpansionRatio", 100) is >= 1 and <= 1_000,
            "Waf:Inspection:MaxExpansionRatio must be between 1 and 1000", errors);
        Require(configuration.GetValue("Waf:Inspection:MaxNestingDepth", 32) is >= 1 and <= 256,
            "Waf:Inspection:MaxNestingDepth must be between 1 and 256", errors);
        Require(configuration.GetValue("Waf:Inspection:MaxMembers", 10_000) is >= 1 and <= 1_000_000,
            "Waf:Inspection:MaxMembers must be between 1 and 1000000", errors);
        Require(configuration.GetValue("Waf:Inspection:MaxMultipartParts", 100) is >= 1 and <= 10_000,
            "Waf:Inspection:MaxMultipartParts must be between 1 and 10000", errors);
        Require(configuration.GetValue("Waf:Inspection:TimeoutMilliseconds", 250) is >= 1 and <= 10_000,
            "Waf:Inspection:TimeoutMilliseconds must be between 1 and 10000", errors);
        Require(inspection.MaxDecompressedBytes >= inspection.MaxCompressedBytes,
            "Waf:Inspection:MaxDecompressedBytes must not be smaller than MaxCompressedBytes", errors);

        var hasExplicitSites = configuration.GetSection("Sites").GetChildren().Any();
        if (!hasExplicitSites)
        {
            var egress = UpstreamEgressOptions.From(configuration);
            Require(egress.AllowedHosts.Count > 0,
                "Waf:Egress:AllowedHosts must contain at least one explicit upstream host", errors);
            Require(egress.AllowedHosts.All(IsExplicitHost),
                "Waf:Egress:AllowedHosts must contain only explicit hostnames or IP literals; wildcards are not allowed", errors);
            Require(egress.AllowedPorts.Count > 0 && egress.AllowedPorts.All(port => port is >= 1 and <= 65535),
                "Waf:Egress:AllowedPorts must contain valid explicit ports", errors);
            Require(egress.AllowedSchemes.Count > 0 && egress.AllowedSchemes.All(s => string.Equals(s, "https", StringComparison.OrdinalIgnoreCase)),
                "Waf:Egress:AllowedSchemes must be explicit HTTPS in Production", errors);
            Require(!egress.AllowPrivateExceptions,
                "Waf:Egress:AllowPrivateExceptions must be false; private destinations need an explicit reviewed exception model", errors);
        }

        var maxConnectionsPerServer = configuration.GetValue("Waf:Egress:MaxConnectionsPerServer", 256);
        Require(maxConnectionsPerServer is >= 1 and <= 10_000,
            "Waf:Egress:MaxConnectionsPerServer must be between 1 and 10000", errors);

        var connection = configuration.GetConnectionString("Postgres") ?? string.Empty;
        if (connection.Contains("Username=postgres", StringComparison.OrdinalIgnoreCase) ||
            connection.Contains("User ID=postgres", StringComparison.OrdinalIgnoreCase))
            errors.Add("ConnectionStrings:Postgres must use a dedicated least-privilege role, not postgres");

        if (configuration.GetValue("Admin:AllowRemote", false) &&
            string.Equals(configuration["AllowedHosts"], "*", StringComparison.Ordinal))
            errors.Add("AllowedHosts must be explicit when Admin:AllowRemote is true");

        foreach (var cluster in configuration.GetSection("ReverseProxy:Clusters").GetChildren())
        {
            Require(!cluster.GetValue("HttpClient:DangerousAcceptAnyServerCertificate", false),
                $"ReverseProxy cluster {cluster.Key} must not enable DangerousAcceptAnyServerCertificate", errors);
            if (cluster.GetSection("HttpClient:WebProxy").Exists())
                errors.Add($"ReverseProxy cluster {cluster.Key} HttpClient:WebProxy must not be configured in Production; use the reviewed infrastructure egress path");
            Require(!cluster.GetValue("HttpClient:UseProxy", false),
                $"ReverseProxy cluster {cluster.Key} HttpClient:UseProxy must be false in Production", errors);
            Require(!cluster.GetValue("HttpClient:AllowAutoRedirect", false),
                $"ReverseProxy cluster {cluster.Key} HttpClient:AllowAutoRedirect must be false in Production", errors);
            var clusterMaxConnectionsText = cluster["HttpClient:MaxConnectionsPerServer"];
            if (!string.IsNullOrWhiteSpace(clusterMaxConnectionsText) &&
                (!int.TryParse(clusterMaxConnectionsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clusterMaxConnections) ||
                 clusterMaxConnections is < 1 or > 10_000))
                errors.Add($"ReverseProxy cluster {cluster.Key} HttpClient:MaxConnectionsPerServer must be between 1 and 10000");
            var activityTimeoutText = cluster["HttpRequest:ActivityTimeout"];
            if (!string.IsNullOrWhiteSpace(activityTimeoutText))
            {
                if (!TimeSpan.TryParse(activityTimeoutText, CultureInfo.InvariantCulture, out var activityTimeout) ||
                    activityTimeout < TimeSpan.FromSeconds(1) || activityTimeout > TimeSpan.FromMinutes(5))
                    errors.Add($"ReverseProxy cluster {cluster.Key} HttpRequest:ActivityTimeout must be between 1 second and 5 minutes");
            }
        foreach (var destination in cluster.GetSection("Destinations").GetChildren())
        {
            var address = destination["Address"];
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"ReverseProxy destination {cluster.Key}/{destination.Key} must be an absolute HTTP(S) URL");
                continue;
            }
            if (!string.IsNullOrEmpty(uri.UserInfo))
                errors.Add($"ReverseProxy destination {cluster.Key}/{destination.Key} must not contain credentials in the URL");
        }
        }

        return errors;
    }

    public static void ThrowIfInvalid(IConfiguration configuration)
    {
        var errors = Validate(configuration);
        if (errors.Count > 0)
            throw new InvalidOperationException("Production configuration rejected:\n - " + string.Join("\n - ", errors));
    }

    private static void Require(bool condition, string message, ICollection<string> errors)
    {
        if (!condition) errors.Add(message);
    }

    private static bool IsExplicitHost(string value)
    {
        var host = value.Trim().TrimEnd('.');
        return host.Length > 0 && !string.Equals(host, "*", StringComparison.Ordinal) &&
               Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }
}
