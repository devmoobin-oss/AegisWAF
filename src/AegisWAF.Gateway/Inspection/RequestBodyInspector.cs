using System.IO.Compression;

namespace AegisWAF.Gateway;

internal sealed record BodyDecodeResult(bool Success, byte[] Bytes, string? ReasonCode = null)
{
    public static BodyDecodeResult Reject(string reason) => new(false, [], reason);
}

internal static class RequestBodyInspector
{
    public static BodyDecodeResult Decode(ReadOnlySpan<byte> compressed, string? contentEncoding, InspectionLimits limits)
    {
        if (compressed.Length > limits.MaxCompressedBytes) return BodyDecodeResult.Reject("compressed_limit");
        if (string.IsNullOrWhiteSpace(contentEncoding) || contentEncoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
            return compressed.Length <= limits.MaxDecompressedBytes
                ? new(true, compressed.ToArray())
                : BodyDecodeResult.Reject("decompressed_limit");
        if (!contentEncoding.Equals("gzip", StringComparison.OrdinalIgnoreCase) &&
            !contentEncoding.Equals("deflate", StringComparison.OrdinalIgnoreCase) &&
            !contentEncoding.Equals("br", StringComparison.OrdinalIgnoreCase))
            return BodyDecodeResult.Reject("decompression_unsupported");

        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var decoder = CreateDecoder(input, contentEncoding);
            using var output = new MemoryStream(Math.Min(limits.MaxDecompressedBytes, 64 * 1024));
            var buffer = new byte[8192];
            int read;
            while ((read = decoder.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > limits.MaxDecompressedBytes) return BodyDecodeResult.Reject("decompressed_limit");
                output.Write(buffer, 0, read);
                if (output.Length > (long)Math.Max(1, compressed.Length) * limits.MaxExpansionRatio)
                    return BodyDecodeResult.Reject("decompression_ratio");
            }
            return new(true, output.ToArray());
        }
        catch (InvalidDataException) { return BodyDecodeResult.Reject("decompression_malformed"); }
        catch (IOException) { return BodyDecodeResult.Reject("decompression_malformed"); }
    }

    private static Stream CreateDecoder(Stream input, string encoding) => encoding.ToLowerInvariant() switch
    {
        "gzip" => new GZipStream(input, CompressionMode.Decompress),
        "deflate" => new DeflateStream(input, CompressionMode.Decompress),
        "br" => new BrotliStream(input, CompressionMode.Decompress),
        _ => throw new InvalidDataException("Unsupported encoding")
    };
}
