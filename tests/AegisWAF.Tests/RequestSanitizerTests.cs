using Microsoft.AspNetCore.Http;
using AegisWAF.Gateway;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Tests;

public class RequestSanitizerTests
{
    private static IConfiguration Config(bool allowUpgrade = false) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Waf:AllowProtocolUpgrade"] = allowUpgrade.ToString() }).Build();
    [Fact]
    public void Rejects_ambiguous_content_length_and_transfer_encoding()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["Content-Length"] = "12";
        request.Headers.TransferEncoding = "chunked";
        Assert.Equal("Ambiguous message framing", RequestSanitizer.Validate(request, Config()));
    }

    [Fact]
    public void Rejects_duplicate_content_length_and_unknown_transfer_encoding()
    {
        var duplicate = new DefaultHttpContext().Request;
        duplicate.Headers["Content-Length"] = "12, 12";
        Assert.Equal("Duplicate Content-Length", RequestSanitizer.Validate(duplicate, Config()));

        var unknown = new DefaultHttpContext().Request;
        unknown.Headers.TransferEncoding = "gzip";
        Assert.Equal("Unsupported Transfer-Encoding", RequestSanitizer.Validate(unknown, Config()));
    }

    [Fact]
    public void Allows_single_length_or_chunked_framing()
    {
        var length = new DefaultHttpContext().Request;
        length.Headers["Content-Length"] = "12";
        Assert.Null(RequestSanitizer.Validate(length, Config()));

        var chunked = new DefaultHttpContext().Request;
        chunked.Headers.TransferEncoding = "chunked";
        Assert.Null(RequestSanitizer.Validate(chunked, Config()));
    }

    [Fact]
    public void Allows_supported_compression_but_rejects_unknown_encoding_and_protocol_upgrade_by_default()
    {
        var compressed = new DefaultHttpContext().Request;
        compressed.Headers.ContentEncoding = "gzip";
        Assert.Null(RequestSanitizer.Validate(compressed, Config()));

        compressed.Headers.ContentEncoding = "compress";
        Assert.Equal("Unsupported request content encoding", RequestSanitizer.Validate(compressed, Config()));

        var upgrade = new DefaultHttpContext().Request;
        upgrade.Headers.Upgrade = "websocket";
        Assert.Equal("Protocol upgrade is disabled", RequestSanitizer.Validate(upgrade, Config()));
        Assert.Null(RequestSanitizer.Validate(upgrade, Config(true)));
    }

    [Fact]
    public void Rejects_unsafe_or_unlisted_methods()
    {
        var trace = new DefaultHttpContext().Request;
        trace.Method = HttpMethods.Trace;
        Assert.Equal("Unsafe HTTP method is disabled", RequestSanitizer.Validate(trace, Config()));

        var custom = new DefaultHttpContext().Request;
        custom.Method = "PURGE";
        Assert.Equal("HTTP method is not allowed", RequestSanitizer.Validate(custom, Config()));
    }

    [Fact]
    public void Removes_forwarded_headers_from_untrusted_peers()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.10");
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.1";
        context.Request.Headers["Forwarded"] = "for=10.0.0.1";
        RequestSanitizer.RemoveUntrustedForwardedHeaders(context, Config());
        Assert.False(context.Request.Headers.ContainsKey("X-Forwarded-For"));
        Assert.False(context.Request.Headers.ContainsKey("Forwarded"));
    }
}
