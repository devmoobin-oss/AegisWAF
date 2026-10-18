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
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = DecodeForm(separator < 0 ? pair : pair[..separator]);
            var value = DecodeForm(separator < 0 ? string.Empty : pair[(separator + 1)..]);
            values.Add(InspectionValue.Create("form." + key + ".key", key, 65_536));
            values.Add(InspectionValue.Create("form." + key, value, 65_536));
            if (values.Count / 2 > limits.MaxMembers) return StructuredPayloadResult.Reject("form_limit");
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
        var parts = body.Split(marker, StringSplitOptions.RemoveEmptyEntries);
        var count = 0;
        foreach (var raw in parts)
        {
            if (raw is "--\r\n" or "--" || string.IsNullOrWhiteSpace(raw)) continue;
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
        }
        return StructuredPayloadResult.Clean(values);
    }

    private static string DecodeForm(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

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
