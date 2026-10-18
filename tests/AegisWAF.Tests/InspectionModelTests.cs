using System.Text;
using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class InspectionModelTests
{
    [Fact]
    public void Invalid_utf8_is_rejected_instead_of_replaced_before_parser_evaluation()
    {
        Assert.Throws<DecoderFallbackException>(() => InspectionTextDecoder.Decode([0xC3, 0x28]));
    }

    [Fact]
    public void Inspection_value_is_bounded_and_decision_does_not_expose_raw_payload()
    {
        var value = InspectionValue.Create("query", "secret=very-long-payload", 6);
        var decision = InspectionDecision.Block("sqli-01", "SQL Injection", "A03", "query");

        Assert.Equal("query", value.Location);
        Assert.Equal(6, value.Text.Length);
        Assert.Equal(InspectionAction.Block, decision.Action);
        Assert.DoesNotContain("secret", decision.ToSafeString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspection_value_rejects_invalid_limits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InspectionValue.Create("body", "x", 0));
        Assert.Throws<ArgumentException>(() => InspectionValue.Create("", "x", 1));
    }
}
