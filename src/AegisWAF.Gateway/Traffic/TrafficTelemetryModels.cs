using Microsoft.AspNetCore.Http;

namespace AegisWAF.Gateway.Traffic;

internal enum TrafficDecision
{
    Allowed,
    Blocked,
    RateLimited,
    UpstreamError,
    Unknown
}

internal sealed record TrafficRecord(
    DateTimeOffset Timestamp,
    string TraceId,
    string ClientIp,
    string? SiteId,
    string? Hostname,
    string Method,
    string Path,
    string? UpstreamId,
    string? UpstreamAddress,
    TrafficDecision Decision,
    int StatusCode,
    string? Rule,
    IReadOnlyList<string> OwaspCodes,
    long DurationMs);

internal sealed record TrafficQuery(
    int Limit = 100,
    TrafficDecision? Decision = null,
    string? SiteId = null,
    string? ClientIp = null,
    string? Search = null);

internal sealed record TrafficTelemetrySummary(
    long ActiveConnections,
    long RequestsPerMinute,
    long Allowed,
    long Blocked,
    long RateLimited,
    long UpstreamErrors,
    int BufferedEvents,
    long DroppedEvents);

internal static class TrafficTelemetryPath
{
    private const int MaxPathLength = 4096;

    public static string Sanitize(PathString path, QueryString query)
    {
        var value = path.Value ?? "/";
        if (value.Length <= MaxPathLength) return value;
        return value[..MaxPathLength];
    }
}

