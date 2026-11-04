using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeListenerStatusTests
{
    [Fact]
    public void Tracks_connection_retry_recovery_and_reload_failure_without_exposing_error_text()
    {
        var status = new SiteRuntimeListenerStatus();

        status.MarkConnecting();
        status.MarkDisconnected(new InvalidOperationException("password=secret; host=internal-db"));
        status.MarkConnecting();
        status.MarkConnected();
        status.MarkReloadFailure(new InvalidDataException("private persisted site details"));
        Assert.True(status.Current.HasReloadFailure);
        status.MarkReloaded();

        var snapshot = status.Current;

        Assert.Equal("connected", snapshot.State);
        Assert.True(snapshot.IsConnected);
        Assert.Equal(2, snapshot.ConnectionAttempts);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.NotNull(snapshot.LastConnectedAt);
        Assert.NotNull(snapshot.LastDisconnectedAt);
        Assert.NotNull(snapshot.LastReloadAt);
        Assert.Equal(nameof(InvalidDataException), snapshot.LastErrorType);
        Assert.False(snapshot.HasReloadFailure);
        Assert.DoesNotContain("secret", snapshot.LastErrorType!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", snapshot.LastErrorType!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disabled_and_stopped_states_are_explicit_and_not_reported_as_connected()
    {
        var status = new SiteRuntimeListenerStatus();

        status.MarkDisabled();
        Assert.Equal("disabled", status.Current.State);
        Assert.False(status.Current.IsConnected);

        status.MarkStopped();
        Assert.Equal("stopped", status.Current.State);
        Assert.False(status.Current.IsConnected);
    }
}
