using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class AuditIntegrityAlertTests
{
    [Fact]
    public void First_invalid_id_is_alertable()
    {
        var state = new AuditIntegrityAlertState();

        Assert.True(state.TryClaim(42));
    }

    [Fact]
    public void Same_invalid_id_is_suppressed_after_first_alert()
    {
        var state = new AuditIntegrityAlertState();

        Assert.True(state.TryClaim(42));
        Assert.False(state.TryClaim(42));
    }

    [Fact]
    public void New_invalid_id_is_alertable_after_an_earlier_failure()
    {
        var state = new AuditIntegrityAlertState();

        Assert.True(state.TryClaim(42));
        Assert.True(state.TryClaim(99));
    }

    [Fact]
    public async Task Concurrent_checks_emit_at_most_one_alert_for_an_id()
    {
        var state = new AuditIntegrityAlertState();
        var claims = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => state.TryClaim(42))));

        Assert.Equal(1, claims.Count(claimed => claimed));
    }

    [Fact]
    public void A_failed_delivery_can_be_retried()
    {
        var state = new AuditIntegrityAlertState();

        Assert.True(state.TryClaim(41));
        Assert.True(state.TryClaim(42));
        state.Release(42);
        Assert.True(state.TryClaim(42));
        Assert.False(state.TryClaim(41));
    }
}
