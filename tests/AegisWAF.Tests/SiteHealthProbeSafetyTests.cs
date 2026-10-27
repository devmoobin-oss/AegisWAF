using System.Net;
using System.Net.Sockets;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthProbeSafetyTests
{
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
