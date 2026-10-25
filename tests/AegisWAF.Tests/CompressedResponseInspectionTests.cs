using System.IO.Compression;
using System.Text;
using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class CompressedResponseInspectionTests
{
    private static readonly InspectionLimits Limits = new(65536, 262144, 100, 32, 10000, 100, 250);

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData("deflate")]
    public void Compressed_html_is_decoded_before_inspection_and_secrets_still_block(string encoding)
    {
        var inspector = new ResponseInspector();
        Assert.Null(inspector.InspectEncoded(Compress("<html>สวัสดี</html>", encoding), encoding, Limits));
        Assert.Equal("Response Leak — private key", inspector.InspectEncoded(
            Compress("-----BEGIN PRIVATE KEY-----", encoding), encoding, Limits));
    }

    [Fact]
    public void Malformed_and_oversized_compressed_responses_fail_closed()
    {
        var inspector = new ResponseInspector();
        Assert.Equal("Response Decode — decompression_malformed", inspector.InspectEncoded([1, 2, 3], "gzip", Limits));
        Assert.Equal("Response Decode — decompressed_limit", inspector.InspectEncoded(
            Compress(new string('a', 4000), "gzip"), "gzip", Limits with { MaxDecompressedBytes = 64 }));
        Assert.Equal("Response Decode — decompression_unsupported", inspector.InspectEncoded([1, 2, 3], "unknown", Limits));
    }

    private static byte[] Compress(string text, string encoding)
    {
        using var output = new MemoryStream();
        using (Stream compressor = encoding switch
        {
            "gzip" => new GZipStream(output, CompressionMode.Compress, true),
            "br" => new BrotliStream(output, CompressionMode.Compress, true),
            _ => new DeflateStream(output, CompressionMode.Compress, true)
        }) compressor.Write(Encoding.UTF8.GetBytes(text));
        return output.ToArray();
    }
}
