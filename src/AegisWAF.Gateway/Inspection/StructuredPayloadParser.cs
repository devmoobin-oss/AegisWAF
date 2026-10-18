using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace AegisWAF.Gateway;

internal sealed record StructuredPayloadResult(bool Success, IReadOnlyList<InspectionValue> Values, string? ReasonCode = null)
{
    public static StructuredPayloadResult Clean(IReadOnlyList<InspectionValue> values) => new(true, values);
    public static StructuredPayloadResult Reject(string reason) => new(false, [], reason);
}

internal static class StructuredPayloadParser
{
    private static readonly Encoding FormUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static StructuredPayloadResult Parse(string? contentType, string body, InspectionLimits limits)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return StructuredPayloadResult.Clean([InspectionValue.Create("body", body, 65_536)]);
        try
        {
            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) || contentType.Contains("+json", StringComparison.OrdinalIgnoreCase))
                return ParseJson(body, limits);
            if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                return ParseForm(body, limits);
            if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                return ParseXml(body, limits);
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                return ParseMultipart(contentType, body, limits);
            return StructuredPayloadResult.Clean([InspectionValue.Create("body", body, 65_536)]);
        }
        catch (JsonException) { return StructuredPayloadResult.Reject("json_malformed"); }
        catch (XmlException) { return StructuredPayloadResult.Reject("xml_malformed"); }
        catch (PayloadLimitException ex) { return StructuredPayloadResult.Reject(ex.ReasonCode); }
        catch (InvalidOperationException) { return StructuredPayloadResult.Reject("payload_malformed"); }
    }

    private static StructuredPayloadResult ParseJson(string body, InspectionLimits limits)
    {
        using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 0, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        var values = new List<InspectionValue>();
        var members = 0;
        WalkJson(doc.RootElement, "body", 0);
        return StructuredPayloadResult.Clean(values);

        void WalkJson(JsonElement element, string path, int depth)
        {
            if (++members > limits.MaxMembers || depth > limits.MaxNestingDepth) throw new PayloadLimitException("json_limit");
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var next = path + "." + property.Name;
                        values.Add(InspectionValue.Create(next + ".key", property.Name, 65_536));
                        WalkJson(property.Value, next, depth + 1);
                    }
                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray()) WalkJson(item, path + "[" + index++ + "]", depth + 1);
                    break;
                case JsonValueKind.String:
                    values.Add(InspectionValue.Create(path, element.GetString() ?? string.Empty, 65_536));
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    values.Add(InspectionValue.Create(path, element.ToString(), 65_536));
                    break;
            }
        }
    }

    private static StructuredPayloadResult ParseForm(string body, InspectionLimits limits)
    {
        var values = new List<InspectionValue>();
        var start = 0;
        var pairs = 0;
        while (start <= body.Length)
        {
            var end = body.IndexOf('&', start);
            if (end < 0) end = body.Length;
            if (end > start)
            {
                if (++pairs > limits.MaxMembers) return StructuredPayloadResult.Reject("form_limit");
                var length = end - start;
                var separator = body.IndexOf('=', start, length);
                var keyText = separator < 0 ? body[start..end] : body[start..separator];
                var valueText = separator < 0 ? string.Empty : body[(separator + 1)..end];
                if (!TryDecodeForm(keyText, out var key) || !TryDecodeForm(valueText, out var value))
                    return StructuredPayloadResult.Reject("form_malformed");
            values.Add(InspectionValue.Create("form." + key + ".key", key, 65_536));
            values.Add(InspectionValue.Create("form." + key, value, 65_536));
            }
            if (end == body.Length) break;
            start = end + 1;
        }
        return StructuredPayloadResult.Clean(values);
    }

    private static StructuredPayloadResult ParseXml(string body, InspectionLimits limits)
    {
        if (body.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(body, @"(?is)<!DOCTYPE\b[^>]*(?:SYSTEM|PUBLIC)\b"))
            return StructuredPayloadResult.Reject("xml_dtd_or_entity");
        var values = new List<InspectionValue>();
        using var reader = XmlReader.Create(new StringReader(body), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = limits.MaxDecompressedBytes,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreWhitespace = false
        });
        var depth = 0; var members = 0;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                depth++;
                if (depth > limits.MaxNestingDepth || ++members > limits.MaxMembers) return StructuredPayloadResult.Reject("xml_limit");
                if (reader.HasAttributes)
                    while (reader.MoveToNextAttribute()) values.Add(InspectionValue.Create("xml." + reader.Name, reader.Value, 65_536));
                reader.MoveToElement();
            }
            else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                values.Add(InspectionValue.Create("xml.text", reader.Value, 65_536));
            else if (reader.NodeType == XmlNodeType.EndElement)
                depth--;
        }
        return StructuredPayloadResult.Clean(values);
    }

    private static StructuredPayloadResult ParseMultipart(string contentType, string body, InspectionLimits limits)
    {
        var boundary = ExtractBoundary(contentType);
        if (string.IsNullOrWhiteSpace(boundary)) return StructuredPayloadResult.Reject("multipart_boundary");
        var values = new List<InspectionValue>();
        var marker = "--" + boundary;
        var count = 0;
        var cursor = 0;
        while (cursor < body.Length)
        {
            var markerStart = body.IndexOf(marker, cursor, StringComparison.Ordinal);
            if (markerStart < 0) break;
            var partStart = markerStart + marker.Length;
            var nextMarker = body.IndexOf(marker, partStart, StringComparison.Ordinal);
            var raw = nextMarker < 0 ? body[partStart..] : body[partStart..nextMarker];
            if (raw.StartsWith("--", StringComparison.Ordinal)) break;
            if (raw is "--\r\n" or "--" || string.IsNullOrWhiteSpace(raw))
            {
                if (nextMarker < 0) break;
                cursor = nextMarker;
                continue;
            }
            if (++count > limits.MaxMultipartParts) return StructuredPayloadResult.Reject("multipart_limit");
            var part = raw.Trim('\r', '\n', '-');
            var headerEnd = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0) return StructuredPayloadResult.Reject("multipart_malformed");
            var headers = part[..headerEnd];
            var value = part[(headerEnd + 4)..].TrimEnd('\r', '\n');
            var name = HeaderParameter(headers, "name") ?? "part" + count;
            var filename = HeaderParameter(headers, "filename");
            values.Add(InspectionValue.Create("multipart." + name + ".key", name, 65_536));
            if (filename is not null) values.Add(InspectionValue.Create("multipart." + name + ".filename", filename, 65_536));
            values.Add(InspectionValue.Create("multipart." + name, value, 65_536));
            if (nextMarker < 0) break;
            cursor = nextMarker;
        }
        return StructuredPayloadResult.Clean(values);
    }

    private static bool TryDecodeForm(string value, out string decoded)
    {
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%')
            {
                if (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2]))
                {
                    decoded = string.Empty;
                    return false;
                }
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }

            if (value[i] == '+')
            {
                bytes.Add((byte)' ');
                continue;
            }

            var charLength = char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]) ? 2 : 1;
            bytes.AddRange(FormUtf8.GetBytes(value.AsSpan(i, charLength).ToArray()));
            i += charLength - 1;
        }

        try
        {
            decoded = FormUtf8.GetString(bytes.ToArray());
            return true;
        }
        catch (DecoderFallbackException)
        {
            decoded = string.Empty;
            return false;
        }
    }

    private static string? ExtractBoundary(string contentType)
    {
        var marker = "boundary=";
        var index = contentType.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var value = contentType[(index + marker.Length)..].Trim();
        return value.Trim('"');
    }

    private static string? HeaderParameter(string headers, string parameter)
    {
        var pattern = $@"(?i)(?:^|;)\s*{parameter}\s*=\s*""(?<v>[^"" ]*)""|(?:^|;)\s*{parameter}\s*=\s*(?<v>[^;\r\n]+)";
        var match = System.Text.RegularExpressions.Regex.Match(headers, pattern);
        return match.Success ? match.Groups["v"].Value : null;
    }
}

internal sealed class PayloadLimitException(string reasonCode) : Exception(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}
