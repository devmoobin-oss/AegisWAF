using System.Text;

namespace AegisWAF.Gateway;

internal static class InspectionTextDecoder
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decode(byte[] bytes) => Utf8.GetString(bytes);
}
