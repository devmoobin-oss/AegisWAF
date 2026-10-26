using AegisWAF.Gateway;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class ProductionReadinessPolicyTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> SecureValues() => new()
    {
        ["Waf:Deployment:Enforce"] = "true",
        ["Database:RequireLeastPrivilege"] = "true",
        ["Database:AutoMigrate"] = "false",
        ["Admin:RequireHttps"] = "true",
        ["Admin:AllowRemote"] = "false",
        ["Admin:ListenerPort"] = "8443",
        ["Gateway:ListenerPort"] = "8080",
        ["Waf:AllowProtocolUpgrade"] = "false",
        ["Waf:AllowUnsafeMethods"] = "false",
        ["Waf:InspectBinaryRequests"] = "true",
        ["Waf:InspectBinaryResponses"] = "true",
        ["Waf:ResponseOverflowAction"] = "block",
        ["Waf:MaxBodyInspectBytes"] = "131072",
        ["Waf:MaxResponseInspectBytes"] = "262144",
        ["Waf:Limits:MaxRequestBodyBytes"] = "131072",
        ["Waf:Events:StructuredLogging"] = "true",
        ["Waf:Ssrf:ResolveDns"] = "true",
        ["Waf:RateLimit:Requests"] = "300",
        ["Waf:RateLimit:WindowSeconds"] = "60",
        ["Waf:RateLimit:MaxEntries"] = "10000",
        ["Waf:RateLimit:GlobalRequests"] = "10000",
        ["Waf:RateLimit:Store"] = "postgres",
        ["Auth:Throttle:Store"] = "postgres",
        ["Auth:Throttle:MaxFailures"] = "10",
        ["Auth:Throttle:WindowMinutes"] = "15",
        ["Auth:Throttle:MaxEntries"] = "10000",
        ["ConnectionStrings:Postgres"] = "Host=db;Database=aegiswaf;Username=aegiswaf_runtime",
        ["Security:DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "AegisWAF", "keys"),
        ["Waf:Inspection:MaxCompressedBytes"] = "131072",
        ["Waf:Inspection:MaxDecompressedBytes"] = "1048576",
        ["Waf:Inspection:MaxExpansionRatio"] = "100",
        ["Waf:Inspection:MaxNestingDepth"] = "32",
        ["Waf:Inspection:MaxMembers"] = "10000",
        ["Waf:Inspection:MaxMultipartParts"] = "100",
        ["Waf:Inspection:TimeoutMilliseconds"] = "250",
        ["Waf:Egress:AllowedHosts:0"] = "origin.example.test",
        ["Waf:Egress:AllowedPorts:0"] = "443",
        ["Waf:Egress:AllowedSchemes:0"] = "https",
        ["Waf:Egress:AllowPrivateExceptions"] = "false"
    };

    private static Dictionary<string, string?> SecureValuesForCurrentPlatform()
    {
        var values = SecureValues();
        if (!OperatingSystem.IsWindows())
        {
            values["Security:DataProtection:CertificatePath"] = "/etc/aegiswaf/certs/data-protection.pfx";
            values["Security:DataProtection:CertificatePassword"] = "test-only-password";
        }

        return values;
    }

    [Fact]
    public void Explicit_production_enforcement_accepts_secure_configuration()
    {
        Assert.Empty(ProductionReadinessPolicy.Validate(Config(SecureValuesForCurrentPlatform())));
    }

    [Fact]
    public void Production_requires_an_absolute_data_protection_key_ring_path()
    {
        var values = SecureValuesForCurrentPlatform();
        values.Remove("Security:DataProtection:KeyRingPath");

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("DataProtection:KeyRingPath", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_disabled_binary_request_inspection()
    {
        var values = SecureValuesForCurrentPlatform();
        values["Waf:InspectBinaryRequests"] = "false";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("InspectBinaryRequests", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_production_enforcement_rejects_insecure_defaults()
    {
        var values = SecureValuesForCurrentPlatform();
        values["Database:RequireLeastPrivilege"] = "false";
        values["Database:AutoMigrate"] = "true";
        values["Waf:AllowUnsafeMethods"] = "true";
        values["ConnectionStrings:Postgres"] = "Host=db;Database=aegiswaf;Username=postgres";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("RequireLeastPrivilege", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("AutoMigrate", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("AllowUnsafeMethods", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("dedicated least-privilege", StringComparison.Ordinal));
    }

    [Fact]
    public void Desktop_defaults_are_not_blocked_until_production_enforcement_is_enabled()
    {
        var errors = ProductionReadinessPolicy.Validate(new ConfigurationBuilder().Build());
        Assert.Empty(errors);
    }

    [Fact]
    public void Production_rejects_proxy_destinations_with_embedded_credentials_or_bad_scheme()
    {
        var values = SecureValuesForCurrentPlatform();
        values["ReverseProxy:Clusters:app:Destinations:one:Address"] = "ftp://user:pass@example.test/";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("absolute HTTP(S)", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_accepts_explicit_http_proxy_destination_without_credentials()
    {
        var values = SecureValuesForCurrentPlatform();
        values["ReverseProxy:Clusters:app:Destinations:one:Address"] = "https://origin.example.test/";

        Assert.Empty(ProductionReadinessPolicy.Validate(Config(values)));
    }

    [Fact]
    public void Production_rejects_unbounded_inspection_buffers()
    {
        var values = SecureValues();
        values["Waf:MaxResponseInspectBytes"] = "999999999";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("MaxResponseInspectBytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_request_body_limit_above_the_inspection_cap()
    {
        var values = SecureValues();
        values["Waf:Limits:MaxRequestBodyBytes"] = "262144";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("MaxRequestBodyBytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unsafe_connection_timeout_limits()
    {
        var values = SecureValues();
        values["Waf:Limits:RequestHeadersTimeoutSeconds"] = "0";
        values["Waf:Limits:KeepAliveSeconds"] = "999999";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("RequestHeadersTimeoutSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("KeepAliveSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unbounded_proxy_activity_timeout()
    {
        var values = SecureValues();
        values["ReverseProxy:Clusters:app:HttpRequest:ActivityTimeout"] = "00:05:01";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("ActivityTimeout", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unsafe_outbound_connection_capacity()
    {
        var values = SecureValues();
        values["Waf:Egress:MaxConnectionsPerServer"] = "0";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("MaxConnectionsPerServer", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unreviewed_outbound_web_proxy()
    {
        var values = SecureValues();
        values["ReverseProxy:Clusters:app:HttpClient:WebProxy:Address"] = "http://proxy.example.test:8080";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("WebProxy", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_environment_proxy_usage()
    {
        var values = SecureValues();
        values["ReverseProxy:Clusters:app:HttpClient:UseProxy"] = "true";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("UseProxy", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_automatic_upstream_redirects()
    {
        var values = SecureValues();
        values["ReverseProxy:Clusters:app:HttpClient:AllowAutoRedirect"] = "true";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("AllowAutoRedirect", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unsafe_cluster_connection_capacity()
    {
        var values = SecureValues();
        values["ReverseProxy:Clusters:app:HttpClient:MaxConnectionsPerServer"] = "0";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("MaxConnectionsPerServer", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unbounded_request_line_and_header_limits()
    {
        var values = SecureValues();
        values["Waf:Limits:MaxRequestLineBytes"] = "0";
        values["Waf:Limits:MaxRequestHeadersBytes"] = "999999999";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("MaxRequestLineBytes", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("MaxRequestHeadersBytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unsafe_concurrent_connection_capacity()
    {
        var values = SecureValues();
        values["Waf:Limits:MaxConcurrentConnections"] = "0";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("MaxConcurrentConnections", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unsafe_request_body_data_rate_limits()
    {
        var values = SecureValues();
        values["Waf:Limits:MinRequestBodyBytesPerSecond"] = "0";
        values["Waf:Limits:RequestBodyDataRateGraceSeconds"] = "0";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("MinRequestBodyBytesPerSecond", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("RequestBodyDataRateGraceSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unsafe_response_data_rate_limits()
    {
        var values = SecureValues();
        values["Waf:Limits:MinResponseBytesPerSecond"] = "0";
        values["Waf:Limits:ResponseDataRateGraceSeconds"] = "121";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("MinResponseBytesPerSecond", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("ResponseDataRateGraceSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_rate_limit_values_outside_runtime_bounds()
    {
        var values = SecureValues();
        values["Waf:RateLimit:Requests"] = "1000001";
        values["Waf:RateLimit:WindowSeconds"] = "3601";
        values["Waf:RateLimit:MaxEntries"] = "1000001";
        values["Waf:RateLimit:GlobalRequests"] = "10000001";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("RateLimit:Requests", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("RateLimit:WindowSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("RateLimit:MaxEntries", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("RateLimit:GlobalRequests", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_auth_throttle_values_outside_runtime_bounds()
    {
        var values = SecureValues();
        values["Auth:Throttle:MaxFailures"] = "1001";
        values["Auth:Throttle:WindowMinutes"] = "1441";
        values["Auth:Throttle:MaxEntries"] = "1000001";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("Auth:Throttle:MaxFailures", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Auth:Throttle:WindowMinutes", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Auth:Throttle:MaxEntries", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unsafe_session_and_lockout_windows()
    {
        var values = SecureValues();
        values["Auth:SessionIdleMinutes"] = "0";
        values["Auth:SessionAbsoluteHours"] = "169";
        values["Auth:MaxFailedAttempts"] = "0";
        values["Auth:LockoutMinutes"] = "1441";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("SessionIdleMinutes", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("SessionAbsoluteHours", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("MaxFailedAttempts", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("LockoutMinutes", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_anomaly_threshold_outside_runtime_bounds_when_enabled()
    {
        var values = SecureValues();
        values["Waf:AnomalyScoring:Enabled"] = "true";
        values["Waf:AnomalyScoring:Threshold"] = "101";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("AnomalyScoring:Threshold", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_disabled_structured_security_logging()
    {
        var values = SecureValues();
        values["Waf:Events:StructuredLogging"] = "false";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("StructuredLogging", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_disabled_response_inspection()
    {
        var values = SecureValues();
        values["Waf:InspectResponses"] = "false";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("InspectResponses", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_disabled_binary_response_inspection()
    {
        var values = SecureValues();
        values["Waf:InspectBinaryResponses"] = "false";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("InspectBinaryResponses", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unbounded_or_unsafe_inspection_limits()
    {
        var values = SecureValues();
        values["Waf:Inspection:MaxDecompressedBytes"] = "999999999";
        values["Waf:Inspection:MaxExpansionRatio"] = "0";
        values["Waf:Inspection:MaxNestingDepth"] = "999999";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("MaxDecompressedBytes", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("MaxExpansionRatio", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("MaxNestingDepth", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_requires_explicit_https_egress_allowlist()
    {
        var values = SecureValues();
        values.Remove("Waf:Egress:AllowedHosts:0");
        values["Waf:Egress:AllowedSchemes:0"] = "http";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("AllowedHosts", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("AllowedSchemes", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_wildcard_or_malformed_global_egress_hosts()
    {
        var values = SecureValues();
        values["Waf:Egress:AllowedHosts:0"] = "*";
        values["Waf:Egress:AllowedHosts:1"] = "not a host";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("AllowedHosts", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_dangerous_tls_certificate_bypass()
    {
        var values = SecureValues();
        values["ReverseProxy:Clusters:app:HttpClient:DangerousAcceptAnyServerCertificate"] = "true";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("DangerousAcceptAnyServerCertificate", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_unsafe_security_event_delivery_limits()
    {
        var values = SecureValues();
        values["Waf:Events:QueueCapacity"] = "99";
        values["Waf:Events:BatchSize"] = "1001";
        values["Waf:Events:FlushMilliseconds"] = "9";
        values["Waf:Events:RetentionHours"] = "0";
        values["Waf:Events:CleanupIntervalSeconds"] = "59";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("Events:QueueCapacity", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Events:BatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Events:FlushMilliseconds", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Events:RetentionHours", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Events:CleanupIntervalSeconds", StringComparison.Ordinal));
    }
}
