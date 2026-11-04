using System.Net;
using System.Net.Sockets;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthProbeSafetyTests
{
    [Fact]
    public void Probe_display_address_omits_upstream_credentials()
    {
        var safe = SiteHealthProbe.SafeDisplayAddress(new Uri("https://health-user:health-password@example.test:8443/ready"));

        Assert.Equal("https://example.test:8443/ready", safe);
        Assert.DoesNotContain("health-user", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("health-password", safe, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_request_uri_omits_upstream_credentials()
    {
        var probe = SiteHealthProbe.BuildProbeUri(new Uri("https://health-user:health-password@example.test:8443/base"), "/ready");

        Assert.Equal("https://example.test:8443/ready", probe.AbsoluteUri);
        Assert.Empty(probe.UserInfo);
    }

    [Fact]
    public void Generic_failure_reason_never_echoes_exception_details()
    {
        var exception = new HttpRequestException(
            "https://user:secret@internal-db.example/password=secret: connection failed");

        var reason = SiteHealthProbe.SafeFailureReason(exception);

        Assert.Equal("request_failed", reason);
        Assert.DoesNotContain("internal-db", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Socket_failure_reason_contains_only_the_safe_socket_error_code()
    {
        var exception = new HttpRequestException(
            "internal host details",
            new SocketException((int)SocketError.ConnectionRefused));

        var reason = SiteHealthProbe.SafeFailureReason(exception);

        Assert.Equal("socket_ConnectionRefused", reason);
        Assert.DoesNotContain("internal", reason, StringComparison.OrdinalIgnoreCase);
    }
}
