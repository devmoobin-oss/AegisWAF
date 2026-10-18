using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class InspectionBudgetTests
{
    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 1, true)]
    [InlineData(1, 0, true)]
    public void Inspection_budget_has_a_fail_closed_boundary(long elapsedMilliseconds, int timeoutMilliseconds, bool expected)
    {
        Assert.Equal(expected, InspectionBudget.HasExpired(elapsedMilliseconds, timeoutMilliseconds));
    }
}
