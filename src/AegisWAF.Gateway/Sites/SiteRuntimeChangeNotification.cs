using System.Text.Json;

namespace AegisWAF.Gateway.Sites;

public sealed record SiteRuntimeChange(string Schema, string Operation, string SiteId);

/// <summary>Small redacted PostgreSQL wake-up contract for database-backed Site changes.</summary>
public static class SiteRuntimeChangeNotification
{
    public const string Channel = "aegiswaf_site_config_changed";
    public const string Schema = "aegiswaf.site.v1";
    public const int MaxPayloadLength = 512;

    private static readonly HashSet<string> Operations =
        ["site.create", "site.update", "site.delete"];

    public static string CreatePayload(string operation, string siteId)
    {
        Validate(operation, siteId);
        var payload = JsonSerializer.Serialize(new { schema = Schema, operation, siteId });
        if (payload.Length > MaxPayloadLength) throw new ArgumentException("Site change notification payload is too large.", nameof(siteId));
        return payload;
    }

    public static bool TryParse(string? payload, out SiteRuntimeChange change)
    {
        change = new SiteRuntimeChange("", "", "");
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxPayloadLength) return false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schema", out var schema) ||
                !root.TryGetProperty("operation", out var operation) ||
                !root.TryGetProperty("siteId", out var siteId) ||
                schema.ValueKind != JsonValueKind.String ||
                operation.ValueKind != JsonValueKind.String ||
                siteId.ValueKind != JsonValueKind.String) return false;
            var schemaValue = schema.GetString();
            var operationValue = operation.GetString();
            var siteIdValue = siteId.GetString();
            if (!string.Equals(schemaValue, Schema, StringComparison.Ordinal) || operationValue is null || siteIdValue is null) return false;
            Validate(operationValue, siteIdValue);
            change = new SiteRuntimeChange(Schema, operationValue, siteIdValue);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static void Validate(string operation, string siteId)
    {
        if (!Operations.Contains(operation)) throw new ArgumentException("Unsupported Site change operation.", nameof(operation));
        if (string.IsNullOrWhiteSpace(siteId) || siteId.Length > 128) throw new ArgumentException("Site ID is empty or too long.", nameof(siteId));
    }
}
