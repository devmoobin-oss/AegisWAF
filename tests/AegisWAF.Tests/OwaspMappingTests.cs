using AegisWAF.Client;

namespace AegisWAF.Tests;

public sealed class OwaspMappingTests
{
    [Fact]
    public void Catalog_contains_each_owasp_top_10_code_once()
    {
        var codes = OwaspCatalog.All.Select(x => x.Code).ToArray();
        Assert.Equal(10, codes.Length);
        Assert.Equal(10, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"A{i:00}"), codes);
    }

    [Theory]
    [InlineData("sqli-01", "A03")]
    [InlineData("xss-01", "A03")]
    [InlineData("ssrf-01", "A10")]
    [InlineData("scanner-01", "A06")]
    [InlineData("anomaly-01", "A03")]
    public void Built_in_rules_expose_explicit_owasp_mapping(string ruleId, string expectedCode)
    {
        var metadata = OwaspCatalog.ForRule(ruleId);
        Assert.Contains(expectedCode, metadata.Codes);
        Assert.NotEmpty(metadata.Description);
    }

    [Theory]
    [InlineData("A02")]
    [InlineData("A04")]
    [InlineData("A06")]
    public void Non_runtime_categories_are_not_claimed_as_fully_covered(string code)
    {
        Assert.NotEqual(CoverageState.Covered, OwaspCatalog.ForCode(code).Coverage);
    }

    [Fact]
    public void Unknown_rules_return_explicit_not_covered_metadata()
    {
        var metadata = OwaspCatalog.ForRule("unknown-rule");
        Assert.Equal(CoverageState.NotCovered, metadata.Coverage);
        Assert.NotEmpty(metadata.Limitations);
    }
}
