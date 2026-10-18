using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class ResponseBufferStreamTests
{
    [Fact]
    public async Task Fail_closed_overflow_does_not_forward_uninspected_bytes()
    {
        await using var target = new MemoryStream();
        await using var buffer = new ResponseBufferStream(target, 4, failClosedOverflow: true);

        await buffer.WriteAsync(new byte[] { 1, 2, 3, 4, 5 });

        Assert.True(buffer.Overflowed);
        Assert.Empty(target.ToArray());
    }

    [Fact]
    public async Task Explicit_stream_mode_preserves_legacy_large_response_behavior()
    {
        await using var target = new MemoryStream();
        await using var buffer = new ResponseBufferStream(target, 4, failClosedOverflow: false);

        await buffer.WriteAsync(new byte[] { 1, 2, 3, 4, 5 });

        Assert.True(buffer.Overflowed);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, target.ToArray());
    }
}
