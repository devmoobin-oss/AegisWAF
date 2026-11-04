using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AegisWAF.Client;

/// <summary>Typed client for the AegisWAF admin API. There is no login: every request is sent as-is.</summary>
public sealed class ApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaxApiErrorMessageLength = 512;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public ApiClient(Uri baseAddress)
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
        _http = new HttpClient(handler) { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(15) };
        _ownsHttp = true;
    }

    /// <summary>For tests / hosting: the supplied client must keep cookies between calls.</summary>
    public ApiClient(HttpClient http) { _http = http; }

    public Uri? BaseAddress => _http.BaseAddress;

    public Task<OverviewDto> GetOverviewAsync(CancellationToken ct = default) => SendAsync<OverviewDto>(HttpMethod.Get, "api/overview", null, ct);
    public Task<List<RuleDto>> GetRulesAsync(CancellationToken ct = default) => SendAsync<List<RuleDto>>(HttpMethod.Get, "api/rules", null, ct);
    public Task<List<OwaspCoverageDto>> GetOwaspCoverageAsync(CancellationToken ct = default) => SendAsync<List<OwaspCoverageDto>>(HttpMethod.Get, "api/owasp-coverage", null, ct);
    public Task SetRuleEnabledAsync(string id, bool enabled, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/rules/{Uri.EscapeDataString(id)}/enabled", new { enabled }, ct);
    public Task<RuleTestResult> TestRuleAsync(string sample, CancellationToken ct = default) =>
        SendAsync<RuleTestResult>(HttpMethod.Post, "api/rules/test", new { sample }, ct);

    public Task<List<CustomRuleDto>> GetCustomRulesAsync(CancellationToken ct = default) => SendAsync<List<CustomRuleDto>>(HttpMethod.Get, "api/custom-rules", null, ct);
    public Task SaveCustomRuleAsync(CustomRuleDto rule, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/custom-rules/{Uri.EscapeDataString(rule.Id)}", new { rule.Name, rule.Pattern, rule.Priority, rule.Enabled }, ct);
    public Task DeleteCustomRuleAsync(string id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/custom-rules/{Uri.EscapeDataString(id)}", null, ct);

    public Task<List<IpPolicyDto>> GetIpPoliciesAsync(CancellationToken ct = default) => SendAsync<List<IpPolicyDto>>(HttpMethod.Get, "api/ip-policies", null, ct);
    public Task<List<SiteDto>> GetSitesAsync(CancellationToken ct = default) => SendAsync<List<SiteDto>>(HttpMethod.Get, "api/sites", null, ct);
    public Task AddSiteAsync(SiteCreateDto site, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/sites", site, ct);
    public Task UpdateSiteAsync(string id, SiteCreateDto site, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/sites/{Uri.EscapeDataString(id)}", site, ct,
            ifMatch: site.ExpectedVersion is long version ? $"\"{version}\"" : null);
    public Task DeleteSiteAsync(string id, long? expectedVersion = null, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/sites/{Uri.EscapeDataString(id)}", null, ct,
            ifMatch: expectedVersion is long version ? $"\"{version}\"" : null);
    public Task<SiteHealthCheckDto> CheckSiteHealthAsync(string id, CancellationToken ct = default) =>
        SendAsync<SiteHealthCheckDto>(HttpMethod.Post, $"api/sites/{Uri.EscapeDataString(id)}/health", null, ct);
    public Task<SiteHealthHistoryDto> GetSiteHealthHistoryAsync(string id, int limit = 20, CancellationToken ct = default) =>
        SendAsync<SiteHealthHistoryDto>(HttpMethod.Get, $"api/sites/{Uri.EscapeDataString(id)}/health/history?limit={Math.Clamp(limit, 1, 100)}", null, ct);

    public Task<SiteHealthTrendDto> GetSiteHealthTrendAsync(string id, int window = 20, CancellationToken ct = default) =>
        SendAsync<SiteHealthTrendDto>(HttpMethod.Get, $"api/sites/{Uri.EscapeDataString(id)}/health/trend?window={Math.Clamp(window, 1, 64)}", null, ct);
    public Task<SiteHealthTrendDto> AcknowledgeSiteHealthAlertAsync(string id, int cooldownSeconds = 3_600, CancellationToken ct = default) =>
        SendAsync<SiteHealthTrendDto>(HttpMethod.Post, $"api/sites/{Uri.EscapeDataString(id)}/health/alerts/acknowledge", new { cooldownSeconds }, ct);
    public Task<SiteHealthSummaryDto> GetSiteHealthSummaryAsync(int window = 20, int limit = 100, CancellationToken ct = default) =>
        SendAsync<SiteHealthSummaryDto>(HttpMethod.Get, $"api/sites/health/summary?window={Math.Clamp(window, 1, 64)}&limit={Math.Clamp(limit, 1, 100)}", null, ct);
    public Task SaveIpPolicyAsync(string ip, string action, string reason, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "api/ip-policies", new { ip, action, reason }, ct);
    public Task DeleteIpPolicyAsync(string ip, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, ip.Contains('/') ? $"api/ip-policies?ip={Uri.EscapeDataString(ip)}" : $"api/ip-policies/{Uri.EscapeDataString(ip)}", null, ct);

    public Task<List<AuditEntryDto>> GetAuditAsync(CancellationToken ct = default) => SendAsync<List<AuditEntryDto>>(HttpMethod.Get, "api/audit", null, ct);
    public Task<CommandCenterSummaryDto> GetCommandCenterSummaryAsync(CancellationToken ct = default) =>
        SendAsync<CommandCenterSummaryDto>(HttpMethod.Get, "api/command-center/summary", null, ct);
    public Task<List<CommandCenterTrafficDto>> GetCommandCenterTrafficAsync(CommandCenterTrafficQuery? query = null, CancellationToken ct = default)
    {
        query ??= new CommandCenterTrafficQuery();
        var parameters = new List<string> { "limit=" + query.Limit };
        if (!string.IsNullOrWhiteSpace(query.Decision)) parameters.Add("decision=" + Uri.EscapeDataString(query.Decision));
        if (!string.IsNullOrWhiteSpace(query.SiteId)) parameters.Add("siteId=" + Uri.EscapeDataString(query.SiteId));
        if (!string.IsNullOrWhiteSpace(query.ClientIp)) parameters.Add("ip=" + Uri.EscapeDataString(query.ClientIp));
        if (!string.IsNullOrWhiteSpace(query.Search)) parameters.Add("q=" + Uri.EscapeDataString(query.Search));
        return SendAsync<List<CommandCenterTrafficDto>>(HttpMethod.Get, "api/command-center/traffic?" + string.Join('&', parameters), null, ct);
    }

    /// <summary>
    /// Sends one harmless, clearly labelled SQL-injection-style request through the gateway's protected path and reports
    /// whether it was blocked (HTTP 403). It never reaches the protected site when the protection works.
    /// </summary>
    public async Task<(bool Blocked, int Status)> RunSelfTestAsync(CancellationToken ct = default)
    {
        try
        {
            // UNION SELECT is matched by the sqli-01 rule; the dashboard tells the user to keep that rule on.
            using var resp = await _http.GetAsync("aegis-selftest?q=1%20union%20select%201", ct);
            return (resp.StatusCode == HttpStatusCode.Forbidden, (int)resp.StatusCode);
        }
        catch (HttpRequestException ex) { throw new ApiException(0, "เชื่อมต่อ Gateway ไม่ได้: " + ex.Message); }
    }

    /// <summary>True when /health answers 200; never throws.</summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try { return (await _http.GetAsync("health", ct)).IsSuccessStatusCode; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var resp = await RawAsync(method, url, body, ct);
        return (await resp.Content.ReadFromJsonAsync<T>(Json, ct)) ?? throw new ApiException((int)resp.StatusCode, "Empty response");
    }

    private async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken ct, string? ifMatch = null)
    {
        using var _ = await RawAsync(method, url, body, ct, ifMatch);
    }

    private async Task<HttpResponseMessage> RawAsync(HttpMethod method, string url, object? body, CancellationToken ct, string? ifMatch = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        if (!string.IsNullOrWhiteSpace(ifMatch)) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new ApiException(0, "เชื่อมต่อ Gateway ไม่ได้: " + ex.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new ApiException(0, "Gateway ไม่ตอบสนอง (timeout)"); }
        if (resp.IsSuccessStatusCode) return resp;

        var message = $"HTTP {(int)resp.StatusCode}";
        string? code = null;
        try
        {
            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            if (doc.ValueKind == JsonValueKind.Object)
            {
                code = ReadString(doc, "code");
                if (doc.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(error.GetString()))
                    message = error.GetString()!;
                else
                {
                    var title = ReadString(doc, "title");
                    var detail = ReadString(doc, "detail");
                    message = title is not null && detail is not null && !title.Equals(detail, StringComparison.Ordinal)
                        ? title + ": " + detail
                        : title ?? detail ?? message;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException) { /* non-JSON body */ }
        var status = (int)resp.StatusCode;
        resp.Dispose();
        throw new ApiException(status, BoundErrorMessage(message), code);
    }

    private static string? ReadString(JsonElement document, string property) =>
        document.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static string BoundErrorMessage(string message)
    {
        var trimmed = message.Trim();
        return trimmed.Length <= MaxApiErrorMessageLength
            ? trimmed
            : trimmed[..MaxApiErrorMessageLength] + "…";
    }

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
