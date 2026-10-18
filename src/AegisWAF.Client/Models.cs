namespace AegisWAF.Client;

public sealed record SessionDto(string Username, string Role, int Level, string CsrfToken, bool MustChangePassword);
public sealed record NameCount(string Name, long Count);
public sealed record TimelinePoint(string Time, long Requests, long Blocked);
public sealed record TopIp(string Ip, long Blocked);
public sealed record SecurityEventDto(DateTimeOffset Time, string Ip, string Method, string Path, string Rule, int Status)
{
    public string Action => Status == 429 ? "RATE LIMIT" : "BLOCK";
    public string Explanation => RuleText.Explain(Rule);
    public string LocalTime => Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public IReadOnlyList<string> OwaspCodes => OwaspCatalog.ForRule(Rule).Codes;
    public CoverageState Coverage => OwaspCatalog.ForRule(Rule).Coverage;
}
public sealed record OverviewDto(DateTimeOffset GeneratedAt, long TotalRequests, long BlockedRequests, long AllowedRequests, int ActiveRules,
    long DroppedEvents, List<NameCount> ThreatCategories, List<NameCount> Methods, List<TimelinePoint> Timeline, List<TopIp> TopIps,
    List<SecurityEventDto> RecentEvents);
public sealed record RuleDto(string Id, string Name, string Category, bool Enabled)
{
    public string Description => RuleText.Describe(Id);
    public IReadOnlyList<string> OwaspCodes => OwaspCatalog.ForRule(Id).Codes;
    public CoverageState Coverage => OwaspCatalog.ForRule(Id).Coverage;
}
public sealed record CustomRuleDto(string Id, string Name, string Pattern, int Priority, bool Enabled);
public sealed record OwaspCoverageDto(string Code, string Title, string Coverage, string Description, string Limitations, string[] RuleIds, string OfficialUrl);
public sealed record IpPolicyDto(string Ip, string Action, string Reason);
public sealed record SiteUpstreamDto(string Id, string Address, int Weight, string Health);
public sealed record SiteDto(string SiteId, bool Enabled, string[] Hostnames, string[] PathPrefixes, string WafPolicy, string Health, SiteUpstreamDto[] Upstreams);
public sealed record AuditEntryDto(DateTimeOffset OccurredAt, string Action, string Target, string? Actor, string? ClientIp, string Outcome)
{
    public string LocalTime => OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
public sealed record RuleTestResult(bool Matched, string? Rule, string Action, bool DryRun);
public sealed record UserDto(Guid Id, string Username, string DisplayName, string Role, bool Enabled, bool Locked, bool MustChangePassword,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt)
{
    public string LastLogin => LastLoginAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string Status => !Enabled ? "ปิดใช้งาน" : Locked ? "ล็อกชั่วคราว" : "เปิดใช้งาน";
}

public static class RoleNames
{
    public static readonly string[] All = ["VIEWER", "SECURITY_ANALYST", "SECURITY_OPERATOR", "SECURITY_ADMIN", "SUPER_ADMIN"];
}

/// <summary>Raised for any non-success API response. <see cref="Message"/> is the server's error text when available.</summary>
public sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
