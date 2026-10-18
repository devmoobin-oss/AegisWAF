using AegisWAF.Gateway;
using AegisWAF.Gateway.Auth;

namespace AegisWAF.Tests;

public sealed class DistributedAuthThrottleCapacityTests
{
    [Theory]
    [InlineData(2, 0, 2, 3, false)]
    [InlineData(1, 1, 2, 2, true)]
    [InlineData(2, 1, 2, 2, false)]
    [InlineData(1, 0, 2, 3, true)]
    public void Capacity_policy_counts_only_missing_identity_keys(
        long totalRows, int existingKeys, int candidateKeys, int maxEntries, bool expected)
    {
        Assert.Equal(expected, AuthThrottleCapacityPolicy.Allows(totalRows, existingKeys, candidateKeys, maxEntries));
    }
}
