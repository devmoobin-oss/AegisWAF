using System.Text;

namespace AegisWAF.Gateway;

/// <summary>Classifies opaque payloads conservatively before they enter text-based inspection.</summary>
internal static class ContentInspection
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool IsOpaqueContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase);

    public static bool IsTextLike(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return false;
        var controls = 0;
        foreach (var value in bytes)
        {
            if (value == 0 || value < 0x09 || value > 0x0D && value < 0x20) controls++;
            if (controls > Math.Max(0, bytes.Length / 100)) return false;
        }

        try { _ = StrictUtf8.GetString(bytes); return true; }
        catch (DecoderFallbackException) { return false; }
    }
}
