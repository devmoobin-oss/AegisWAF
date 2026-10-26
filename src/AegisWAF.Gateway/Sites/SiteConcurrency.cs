using System.Globalization;

namespace AegisWAF.Gateway.Sites;

public static class SiteConcurrency
{
    public static string ToEtag(long version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    public static bool TryParseEtag(string? value, out long version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Length < 3 || text[0] != '\"' || text[^1] != '\"') return false;
        return long.TryParse(text[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0;
    }
}
