using AegisWAF.Gateway.Auth;

namespace AegisWAF.Tests;

public sealed class AuthThrottleTests
{
    [Fact]
    public void Limits_repeated_failures_for_normalized_ip_and_username()
    {
        var throttle = new AuthThrottle(maxFailures: 2, window: TimeSpan.FromMinutes(5));
        var now = DateTimeOffset.UtcNow;

        Assert.True(throttle.Allow("127.0.0.1", " Admin ", now).Allowed);
        throttle.RecordFailure("127.0.0.1", "admin", now);
        Assert.True(throttle.Allow("127.0.0.1", "ADMIN", now.AddSeconds(1)).Allowed);
        throttle.RecordFailure("127.0.0.1", "ADMIN", now.AddSeconds(1));

        var decision = throttle.Allow("127.0.0.1", "admin", now.AddSeconds(2));
        Assert.False(decision.Allowed);
        Assert.True(decision.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void Successful_login_resets_the_failure_bucket()
    {
        var throttle = new AuthThrottle(maxFailures: 1, window: TimeSpan.FromMinutes(5));
        var now = DateTimeOffset.UtcNow;
        throttle.RecordFailure("127.0.0.1", "admin", now);
        Assert.False(throttle.Allow("127.0.0.1", "admin", now).Allowed);

        throttle.RecordSuccess("127.0.0.1", "admin");
        Assert.True(throttle.Allow("127.0.0.1", "admin", now.AddSeconds(1)).Allowed);
    }

    [Fact]
    public void Expired_window_allows_login_again_and_state_is_bounded()
    {
        var throttle = new AuthThrottle(maxFailures: 1, window: TimeSpan.FromMinutes(1), maxEntries: 2);
        var now = DateTimeOffset.UtcNow;
        throttle.RecordFailure("1.1.1.1", "a", now);
        throttle.RecordFailure("2.2.2.2", "b", now);
        throttle.RecordFailure("3.3.3.3", "c", now);

        Assert.True(throttle.EntryCount <= 2);
        Assert.True(throttle.Allow("1.1.1.1", "a", now.AddMinutes(2)).Allowed);
    }
}
