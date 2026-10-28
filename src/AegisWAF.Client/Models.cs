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
public sealed record SiteUpstreamDto(string Id, string Address, string Scheme, int Port, int Weight, string Health);
public sealed record SiteDto(string SiteId, bool Enabled, string[] Hostnames, string[] PathPrefixes, string WafPolicy, string Health, SiteUpstreamDto[] Upstreams,
    string HealthPath = "/", int HealthIntervalSeconds = 10, int HealthTimeoutSeconds = 3, int HealthPassiveReactivationSeconds = 30,
    int RateRequests = 300, int RateWindowSeconds = 60, int RateGlobalRequests = 10_000,
    bool Fresh = false, string Source = "yarp-runtime-state", DateTimeOffset? LastProbeAt = null,
    long Version = 1, string Etag = "\"1\"", long? ActiveVersion = null, string ActivationStatus = "active", bool PendingRestart = false,
    bool AllowPrivateExceptions = false);
public sealed record SiteUpstreamInputDto(string Id, string Address, int Weight = 100);
public sealed record SiteHealthPolicyDto(string Path = "/", int IntervalSeconds = 10, int TimeoutSeconds = 3, int PassiveReactivationSeconds = 30);
public sealed record SiteRateLimitPolicyDto(int Requests = 300, int WindowSeconds = 60, int GlobalRequests = 10_000);
public sealed record SiteCreateDto(string Id, string[] Hostnames, string[] PathPrefixes, string UpstreamAddress, string WafPolicy, string[] AllowedHosts, int[] AllowedPorts, string[] AllowedSchemes, bool Enabled = true,
    SiteUpstreamInputDto[]? Upstreams = null, SiteHealthPolicyDto? Health = null, SiteRateLimitPolicyDto? RateLimit = null, long? ExpectedVersion = null,
    bool AllowPrivateExceptions = false);
public sealed record SiteHealthUpstreamDto(string Id, string Address, string Health, string? Reason = null);
public sealed record SiteHealthCheckDto(string SiteId, string Health, bool PendingRestart, DateTimeOffset CheckedAt, SiteHealthUpstreamDto[] Upstreams,
    bool Fresh = false, string Source = "yarp-runtime-state", DateTimeOffset? LastProbeAt = null,
    long Version = 1, long? ActiveVersion = null, string ActivationStatus = "active", string Etag = "\"1\"",
    bool? HistoryPersisted = null, string? HistoryRetention = null);
public sealed record SiteHealthObservationDto(string UpstreamId, string Health, string? Reason, DateTimeOffset CheckedAt);
public sealed record SiteHealthHistoryDto(string SiteId, string Retention, int MaxObservationsPerSite, SiteHealthObservationDto[] Observations);
public sealed record SiteHealthTrendDto(string SiteId, int WindowSize, int Total, int Healthy, int Unhealthy, int Unknown,
    double HealthyPercent, int ConsecutiveUnhealthy, string AlertState, DateTimeOffset? LastCheckedAt,
    DateTimeOffset? AcknowledgedUntil = null, string? AcknowledgedBy = null, bool AlertSuppressed = false,
    string RecoveryState = "none", bool IsStale = false, string? EffectiveAlertState = null);
public sealed record SiteHealthComparisonDto(string SiteId, int Total, int Healthy, int Unhealthy, int Unknown,
    double HealthyPercent, int ConsecutiveUnhealthy, string AlertState, bool IsStale,
    DateTimeOffset? LastCheckedAt, DateTimeOffset? AcknowledgedUntil, string? AcknowledgedBy, bool AlertSuppressed,
    string RecoveryState = "none", string? EffectiveAlertState = null);
public sealed record SiteHealthSummaryDto(int WindowSize, int TotalSites, int CriticalSites, int DegradedSites,
    int HealthySites, int NoDataSites, int StaleSites, SiteHealthComparisonDto[] Sites,
    int ExpiredAcknowledgementsRemoved = 0, DateTimeOffset? GeneratedAt = null, int QueryDurationMs = 0,
    int QueryBudgetMs = 2_000, bool WithinBudget = true, string Status = "ready",
    long SummaryFailureCount = 0, long SummaryConsecutiveFailures = 0, int RecoveredSites = 0,
    int StaleCriticalSites = 0, int StaleDegradedSites = 0, int ReturnedSites = 0, bool Truncated = false);
public sealed record AuditEntryDto(DateTimeOffset OccurredAt, string Action, string Target, string? Actor, string? ClientIp, string Outcome)
{
    public string LocalTime => OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
public sealed record CommandCenterSummaryDto(DateTimeOffset GeneratedAt, long ActiveConnections, long RequestsPerMinute,
    long Allowed, long Blocked, long RateLimited, long UpstreamErrors, int BufferedEvents, long DroppedEvents);
public sealed record CommandCenterTrafficQuery(int Limit = 100, string? Decision = null, string? SiteId = null, string? ClientIp = null, string? Search = null);
public sealed record CommandCenterTrafficDto(DateTimeOffset Time, string TraceId, string ClientIp, string? SiteId, string? Hostname,
    string Method, string Path, string? UpstreamId, string? UpstreamAddress, string Decision, int StatusCode, string? Rule,
    string[] OwaspCodes, long DurationMs)
{
    public string LocalTime => Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public bool PassedSecurity => Decision == "ALLOWED";
    public string SiteHostDisplay => string.Join(" / ", new[] { SiteId ?? "UNMATCHED", Hostname }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string RequestDisplay => Method + " " + Path;
    public string UpstreamDisplay => string.Join(" / ", new[] { UpstreamId, UpstreamAddress }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } value ? value : "NOT FORWARDED";
    public string DurationDisplay => DurationMs.ToString("N0") + " ms";
    public string SecurityDisplay => string.Join(" • ", new[] { Rule, OwaspCodes.Length == 0 ? null : string.Join(", ", OwaspCodes) }.Where(x => !string.IsNullOrWhiteSpace(x)));
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
