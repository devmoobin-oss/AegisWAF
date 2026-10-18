namespace AegisWAF.Gateway;

internal enum InspectionAction
{
    Allow,
    Block,
    Reject
}

internal sealed record InspectionValue(string Location, string Text)
{
    private const int MaxValueLength = 65_536;

    public static InspectionValue Create(string location, string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("Inspection location is required", nameof(location));
        if (maxLength is < 1 or > MaxValueLength) throw new ArgumentOutOfRangeException(nameof(maxLength));
        text ??= string.Empty;
        return new InspectionValue(location, text.Length <= maxLength ? text : text[..maxLength]);
    }
}

internal sealed record InspectionDecision(
    InspectionAction Action,
    string? RuleId,
    string? Category,
    string? OwaspCode,
    string Location,
    string? ReasonCode = null)
{
    public static InspectionDecision Allow(string location) => new(InspectionAction.Allow, null, null, null, location);

    public static InspectionDecision Block(string ruleId, string category, string? owaspCode, string location) =>
        new(InspectionAction.Block, ruleId, category, owaspCode, location, "signature_match");

    public static InspectionDecision Reject(string reasonCode, string location) =>
        new(InspectionAction.Reject, null, null, null, location, reasonCode);

    public string ToSafeString() =>
        $"action={Action};rule={RuleId ?? "none"};category={Category ?? "none"};owasp={OwaspCode ?? "none"};location={Location};reason={ReasonCode ?? "none"}";
}

internal sealed record InspectionLimits(
    int MaxCompressedBytes,
    int MaxDecompressedBytes,
    int MaxExpansionRatio,
    int MaxNestingDepth,
    int MaxMembers,
    int MaxMultipartParts,
    int TimeoutMilliseconds)
{
    public static InspectionLimits From(IConfiguration cfg) => new(
        Clamp(cfg.GetValue("Waf:Inspection:MaxCompressedBytes", 131_072), 1, 16 * 1024 * 1024),
        Clamp(cfg.GetValue("Waf:Inspection:MaxDecompressedBytes", 1_048_576), 1, 16 * 1024 * 1024),
        Clamp(cfg.GetValue("Waf:Inspection:MaxExpansionRatio", 100), 1, 1_000),
        Clamp(cfg.GetValue("Waf:Inspection:MaxNestingDepth", 32), 1, 256),
        Clamp(cfg.GetValue("Waf:Inspection:MaxMembers", 10_000), 1, 1_000_000),
        Clamp(cfg.GetValue("Waf:Inspection:MaxMultipartParts", 100), 1, 10_000),
        Clamp(cfg.GetValue("Waf:Inspection:TimeoutMilliseconds", 250), 1, 10_000));

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
}
