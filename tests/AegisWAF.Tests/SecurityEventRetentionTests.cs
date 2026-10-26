using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public sealed class SecurityEventRetentionTests
{
    [Fact]
    public void Retention_options_have_bounded_operator_defaults_and_configured_values()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:Events:RetentionHours"] = "48",
            ["Waf:Events:CleanupIntervalSeconds"] = "600"
        }).Build();

        var options = SecurityEventRetentionOptions.From(configuration);

        Assert.Equal(48, options.RetentionHours);
        Assert.Equal(600, options.CleanupIntervalSeconds);
    }

    [Fact]
    public void Retention_options_clamp_only_for_runtime_safety()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Waf:Events:RetentionHours"] = "0",
            ["Waf:Events:CleanupIntervalSeconds"] = "1"
        }).Build();

        var options = SecurityEventRetentionOptions.From(configuration);

        Assert.Equal(1, options.RetentionHours);
        Assert.Equal(60, options.CleanupIntervalSeconds);
    }
}
