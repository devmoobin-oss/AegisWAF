using AegisWAF.Client;

namespace AegisWAF.Tests;

public class RuleTextTests
{
    [Theory]
    [InlineData("SQL Injection")]
    [InlineData("XSS")]
    [InlineData("Path Traversal")]
    [InlineData("Rate Limit")]
    [InlineData("IP Blocklist")]
    [InlineData("Inspection Timeout")]
    public void Every_detection_the_gateway_can_emit_has_a_plain_language_explanation(string rule) =>
        Assert.NotEqual(rule, RuleText.Explain(rule));

    [Fact]
    public void Custom_rule_events_name_the_rule_and_unknown_names_pass_through()
    {
        Assert.Contains("my-rule", RuleText.Explain("Custom: my-rule"));
        Assert.Equal("Something New", RuleText.Explain("Something New"));
    }

    [Theory]
    [InlineData("sqli-01")]
    [InlineData("xss-01")]
    [InlineData("path-01")]
    public void Built_in_rules_are_described(string id) => Assert.NotEmpty(RuleText.Describe(id));

    [Fact]
    public void Event_and_rule_dtos_expose_the_text_for_the_ui()
    {
        var ev = new SecurityEventDto(DateTimeOffset.UtcNow, "1.2.3.4", "GET", "/x", "SQL Injection", 403);
        Assert.Equal("BLOCK", ev.Action);
        Assert.Contains("SQL Injection", ev.Explanation);
        Assert.Equal("RATE LIMIT", (ev with { Status = 429 }).Action);
        Assert.NotEmpty(new RuleDto("sqli-01", "n", "c", true).Description);
    }
}
