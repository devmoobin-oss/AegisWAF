using System.IO.Compression;
using System.Text;
using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class RequestBodyInspectorTests
{
    private static readonly InspectionLimits Limits = new(1024, 4096, 20, 8, 20, 4, 250);

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public void Supported_compression_is_decoded_within_limits(string encoding)
    {
        var plain = Encoding.UTF8.GetBytes("{\"message\":\"hello\"}");
        var compressed = Compress(plain, encoding);

        var result = RequestBodyInspector.Decode(compressed, encoding, Limits);

        Assert.True(result.Success);
        Assert.Equal(plain, result.Bytes);
    }

    [Fact]
    public void Expansion_ratio_and_decompressed_size_are_bounded()
    {
        var compressed = Compress(Encoding.UTF8.GetBytes(new string('A', 4000)), "gzip");

        var ratio = RequestBodyInspector.Decode(compressed, "gzip", Limits with { MaxExpansionRatio = 2 });
        var size = RequestBodyInspector.Decode(compressed, "gzip", Limits with { MaxDecompressedBytes = 64 });

        Assert.False(ratio.Success);
        Assert.Equal("decompression_ratio", ratio.ReasonCode);
        Assert.False(size.Success);
        Assert.Equal("decompressed_limit", size.ReasonCode);
    }

    [Fact]
    public void Malformed_or_unsupported_encoding_fails_closed()
    {
        var malformed = RequestBodyInspector.Decode([1, 2, 3], "gzip", Limits);
        var unsupported = RequestBodyInspector.Decode([1, 2, 3], "compress", Limits);

        Assert.False(malformed.Success);
        Assert.Equal("decompression_malformed", malformed.ReasonCode);
        Assert.False(unsupported.Success);
        Assert.Equal("decompression_unsupported", unsupported.ReasonCode);
    }

    private static byte[] Compress(byte[] input, string encoding)
    {
        using var output = new MemoryStream();
        using (Stream compressor = encoding switch
        {
            "gzip" => new GZipStream(output, CompressionMode.Compress, leaveOpen: true),
            "deflate" => new DeflateStream(output, CompressionMode.Compress, leaveOpen: true),
            "br" => new BrotliStream(output, CompressionMode.Compress, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        }) compressor.Write(input);
        return output.ToArray();
    }
}
