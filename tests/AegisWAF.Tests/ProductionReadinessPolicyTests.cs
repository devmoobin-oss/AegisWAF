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
        ["Auth:Throttle:MaxEntries"] = "10000",
        ["ConnectionStrings:Postgres"] = "Host=db;Database=aegiswaf;Username=aegiswaf_runtime",
        ["Security:DataProtection:KeyRingPath"] = "C:\\ProgramData\\AegisWAF\\keys",
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

    [Fact]
    public void Explicit_production_enforcement_accepts_secure_configuration()
    {
        Assert.Empty(ProductionReadinessPolicy.Validate(Config(SecureValues())));
    }

    [Fact]
    public void Production_requires_an_absolute_data_protection_key_ring_path()
    {
        var values = SecureValues();
        values.Remove("Security:DataProtection:KeyRingPath");

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("DataProtection:KeyRingPath", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_rejects_disabled_binary_request_inspection()
    {
        var values = SecureValues();
        values["Waf:InspectBinaryRequests"] = "false";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("InspectBinaryRequests", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_production_enforcement_rejects_insecure_defaults()
    {
        var values = SecureValues();
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
        var values = SecureValues();
        values["ReverseProxy:Clusters:app:Destinations:one:Address"] = "ftp://user:pass@example.test/";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("absolute HTTP(S)", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_accepts_explicit_http_proxy_destination_without_credentials()
    {
        var values = SecureValues();
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
    public void Production_rejects_dangerous_tls_certificate_bypass()
    {
        var values = SecureValues();
        values["ReverseProxy:Clusters:app:HttpClient:DangerousAcceptAnyServerCertificate"] = "true";

        var errors = ProductionReadinessPolicy.Validate(Config(values));

        Assert.Contains(errors, e => e.Contains("DangerousAcceptAnyServerCertificate", StringComparison.Ordinal));
    }
}
