using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class SitesView : UserControl, IPage
{
    private readonly ApiClient _api;
    private readonly SiteHealthSummaryRecoveryState _summaryRecovery = new();
    private readonly SiteHealthSummaryRefreshLifecycle _refreshLifecycle = new();
    private bool _initialized;
    private string? _editingId;
    private long? _editingVersion;
    public SitesView(ApiClient api)
    {
        InitializeComponent();
        _initialized = true;
        _api = api;
        VersionText.Text = $"AegisWAF v{typeof(SitesView).Assembly.GetName().Version?.ToString(3)}";
        CanEdit = api.Session!.Level >= 4;
        AddSiteForm.IsEnabled = CanEdit;
        if (!CanEdit) FormHint.Text = "ต้องใช้สิทธิ์ SECURITY_ADMIN ขึ้นไปเพื่อเพิ่ม Site ระบบยังคงบังคับสิทธิ์ซ้ำที่ Gateway API";
    }
    public bool CanEdit { get; }
    public bool AutoRefresh => true;
    public Task RefreshAsync() => RefreshListAsync(true, RefreshCancellationReason.Superseded);

    private async Task RefreshListAsync(bool announce, RefreshCancellationReason cancellationReason = RefreshCancellationReason.Superseded)
    {
        var ct = _refreshLifecycle.Begin(cancellationReason);
        var generation = _refreshLifecycle.CurrentGeneration;
        _summaryRecovery.BeginRefresh();
        SummaryText.Text = "กำลังโหลดภาพรวม health trend...";
        RecoveryDetailsExpander.Visibility = Visibility.Collapsed;
        RecoveryEventsText.Text = "";
        RecoveryDiagnosticsText.Text = "";
        CopyRecoveryDiagnosticsButton.IsEnabled = false;
        CopyRecoveryTimelineButton.IsEnabled = false;
        CancellationReasonFilter.IsEnabled = false;
        RetrySummaryButton.Visibility = Visibility.Collapsed;
        RetrySummaryButton.IsEnabled = false;
        try
        {
            var sites = await _api.GetSitesAsync(ct);
            if (ct.IsCancellationRequested || !_refreshLifecycle.IsCurrentGeneration(generation)) return;
            var freshSummary = await TryGetSummaryAsync(ct);
            if (ct.IsCancellationRequested || !_refreshLifecycle.IsCurrentGeneration(generation)) return;
            var summary = freshSummary ?? _summaryRecovery.LastSuccessfulSummary;
            var trends = summary?.Sites.ToDictionary(x => x.SiteId, x => ToTrend(x, summary.WindowSize), StringComparer.OrdinalIgnoreCase);
            var rows = sites.Select(site => new SiteRow(site, trends is not null && trends.TryGetValue(site.SiteId, out var trend) ? trend : null)).ToArray();
            if (ct.IsCancellationRequested || !_refreshLifecycle.IsCurrentGeneration(generation)) return;
            Grid.ItemsSource = rows;
            RenderSummary(summary, freshSummary is not null);
            if (freshSummary is null)
            {
                RetrySummaryButton.Visibility = Visibility.Visible;
                RetrySummaryButton.IsEnabled = true;
            }
            if (announce)
            {
                Msg.ClearValue(TextBlock.ForegroundProperty);
                Msg.Text = "Routing is loaded from validated configuration. เพิ่ม/แก้ไข/ลบ Site แล้ว route ใช้งานทันที";
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (ApiException ex)
        {
            if (ct.IsCancellationRequested || !_refreshLifecycle.IsCurrentGeneration(generation)) return;
            SummaryText.Text = "Health trend summary unavailable — รายการ Site ยังคงใช้งานได้";
            RetrySummaryButton.Visibility = Visibility.Visible;
            RetrySummaryButton.IsEnabled = true;
            Show(Ui.Describe(ex), false);
        }
        finally
        {
            _refreshLifecycle.Complete(ct);
        }
    }

    private async Task<SiteHealthSummaryDto?> TryGetSummaryAsync(CancellationToken ct)
    {
        try
        {
            var summary = await _api.GetSiteHealthSummaryAsync(ct: ct);
            ct.ThrowIfCancellationRequested();
            _summaryRecovery.RecordSuccess(summary);
            return summary;
        }
        catch (ApiException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (ApiException)
        {
            _summaryRecovery.RecordFailure();
            return null;
        }
    }

    private void RenderSummary(SiteHealthSummaryDto? summary, bool fresh)
    {
        var now = DateTimeOffset.UtcNow;
        var age = _summaryRecovery.LastSuccessfulSummaryAge(now);
        var ageLabel = age is { } value ? $" · age {(int)value.TotalSeconds}s" : "";
        var staleAlertLabel = summary is null || summary.StaleSites == 0
            ? "ข้อมูลเก่า 0"
            : $"ข้อมูลเก่า {summary.StaleSites} (เดิมวิกฤติ {summary.StaleCriticalSites} · เดิมเฝ้าระวัง {summary.StaleDegradedSites})";
        SummaryText.Text = summary is null
            ? "Health trend summary unavailable — รายการ Site ยังคงใช้งานได้"
            : $"ภาพรวม {summary.TotalSites} Site · health trend: วิกฤติปัจจุบัน {summary.CriticalSites} · เฝ้าระวังปัจจุบัน {summary.DegradedSites} · กลับมาปกติ {summary.RecoveredSites} · ปกติ {summary.HealthySites} · ไม่มีข้อมูล {summary.NoDataSites} · {staleAlertLabel} · สถานะ runtime ดูในแถว Site · query {summary.QueryDurationMs} ms ({(summary.WithinBudget ? "พร้อม" : "ช้า")}){ageLabel}";
        if (summary is not null && _summaryRecovery.IsFreshnessStale(now))
            SummaryText.Text = $"ข้อมูล summary เกินอายุ {_summaryRecovery.LastSuccessfulSummaryAge(now)!.Value.TotalMinutes:0.#} นาที · {SummaryText.Text}";
        if (!fresh && summary is not null)
        {
            var staleLabel = _summaryRecovery.IsStale ? "ข้อมูลล่าสุดอาจ stale" : "ข้อมูลล่าสุดอาจเก่า";
            SummaryText.Text = $"{staleLabel} · ล้มเหลวต่อเนื่อง {_summaryRecovery.ConsecutiveFailures} ครั้ง · {SummaryText.Text}";
        }
        RecoveryDiagnosticsText.Text = _summaryRecovery.BuildDiagnostics(now, _refreshLifecycle.LastCancellationReason);
        CancellationReasonFilter.IsEnabled = true;
        RenderRecoveryEvents();
    }

    private void RenderRecoveryEvents()
    {
        var filter = SelectedCancellationReason();
        var cancellations = _refreshLifecycle.GetCancellationHistory(filter)
            .TakeLast(3)
            .Select(item => $"#{item.Sequence} cancel:{ToReasonLabel(item.Reason)}");
        var recovery = _summaryRecovery.Events
            .TakeLast(3)
            .Select(item => item.Display);
        var events = cancellations.Concat(recovery).ToArray();
        var hasHistory = events.Length > 0 || _summaryRecovery.Events.Count > 0 || _refreshLifecycle.GetCancellationHistory().Count > 0;
        RecoveryDetailsExpander.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        CopyRecoveryDiagnosticsButton.IsEnabled = _summaryRecovery.DiagnosticsAvailable && hasHistory;
        CopyRecoveryTimelineButton.IsEnabled = _summaryRecovery.DiagnosticsAvailable && hasHistory;
        RecoveryEventsText.Text = events.Length == 0
            ? "ไม่พบเหตุการณ์สำหรับตัวกรองนี้"
            : $"เหตุการณ์ recovery ล่าสุด: {string.Join(" · ", events)}";
    }

    private void OnCancellationReasonFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized) RenderRecoveryEvents();
    }

    private RefreshCancellationReason SelectedCancellationReason() => CancellationReasonFilter.SelectedItem is ComboBoxItem { Tag: string tag }
        ? tag switch
        {
            "superseded" => RefreshCancellationReason.Superseded,
            "unloaded" => RefreshCancellationReason.Unloaded,
            "retry" => RefreshCancellationReason.Retry,
            _ => RefreshCancellationReason.None
        }
        : RefreshCancellationReason.None;

    private static string ToReasonLabel(RefreshCancellationReason reason) => reason switch
    {
        RefreshCancellationReason.Superseded => "superseded",
        RefreshCancellationReason.Unloaded => "unloaded",
        RefreshCancellationReason.Retry => "retry",
        _ => "none"
    };

    private static SiteHealthTrendDto ToTrend(SiteHealthComparisonDto item, int windowSize) => new(
        item.SiteId, windowSize, item.Total, item.Healthy, item.Unhealthy, item.Unknown, item.HealthyPercent,
        item.ConsecutiveUnhealthy, item.AlertState, item.LastCheckedAt, item.AcknowledgedUntil,
        item.AcknowledgedBy, item.AlertSuppressed, item.RecoveryState, item.IsStale, item.EffectiveAlertState);
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnRetrySummary(object sender, RoutedEventArgs e) => await RefreshListAsync(false, RefreshCancellationReason.Retry);

    private void OnCopyRecoveryDiagnostics(object sender, RoutedEventArgs e)
    {
        if (!_summaryRecovery.DiagnosticsAvailable) return;
        try
        {
            Clipboard.SetText(RecoveryDiagnosticsText.Text);
            Show("คัดลอก diagnostics ที่ไม่เปิดเผยข้อมูลลับแล้ว", true);
        }
        catch (Exception)
        {
            Show("ไม่สามารถคัดลอก diagnostics ได้", false);
        }
    }

    private void OnCopyRecoveryTimeline(object sender, RoutedEventArgs e)
    {
        if (!_summaryRecovery.DiagnosticsAvailable) return;
        var timeline = SiteHealthSummaryTimelineExporter.Export(
            _refreshLifecycle.GetCancellationHistory(SelectedCancellationReason()),
            _summaryRecovery.Events);
        try
        {
            Clipboard.SetText(timeline);
            Show("คัดลอก recovery timeline ที่ redacted แล้ว", true);
        }
        catch (Exception)
        {
            Show("ไม่สามารถคัดลอก recovery timeline ได้", false);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshLifecycle.Invalidate(RefreshCancellationReason.Unloaded);
        RecoveryDetailsExpander.Visibility = Visibility.Collapsed;
        RetrySummaryButton.IsEnabled = false;
        CopyRecoveryDiagnosticsButton.IsEnabled = false;
        CopyRecoveryTimelineButton.IsEnabled = false;
    }

    private async void OnAddSite(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        var id = SiteIdBox.Text.Trim();
        var hosts = SplitValues(HostnamesBox.Text);
        var path = PathBox.Text.Trim();
        var upstreamText = UpstreamBox.Text.Trim();
        if (id.Length == 0 || hosts.Length == 0 || path.Length == 0 || upstreamText.Length == 0)
        { Show("กรุณากรอก Site ID, Hostname, Path prefix และ Upstream URL ให้ครบ", false); return; }
        var inputError = SiteFormInputValidator.Validate(id, hosts, path, upstreamText);
        if (inputError is not null) { Show(inputError, false); return; }
        if (!Uri.TryCreate(upstreamText, UriKind.Absolute, out var upstream) ||
            (upstream.Scheme != Uri.UriSchemeHttp && upstream.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(upstream.UserInfo))
        { Show("Upstream ต้องเป็น URL แบบ HTTP(S) และห้ามมี username/password", false); return; }

        var allowedHosts = SplitValues(AllowedHostsBox.Text);
        if (!TryParseAdvancedSettings(out var advancedUpstreams, out var health, out var rateLimit, out var advancedError))
        { Show(advancedError, false); return; }
        var egressUris = (advancedUpstreams ?? [new SiteUpstreamInputDto("primary", upstream.ToString(), 100)])
            .Select(x => new Uri(x.Address, UriKind.Absolute)).ToArray();
        if (allowedHosts.Length == 0) allowedHosts = egressUris.Select(x => x.Host).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var allowedPorts = egressUris.Select(x => x.Port).Distinct().ToArray();
        var allowedSchemes = egressUris.Select(x => x.Scheme).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        AddSiteButton.IsEnabled = false;
        try
        {
            var request = new SiteCreateDto(id, hosts, SplitValues(PathBox.Text), upstream.ToString(),
                string.IsNullOrWhiteSpace(WafPolicyBox.Text) ? "default" : WafPolicyBox.Text.Trim(),
                allowedHosts, allowedPorts, allowedSchemes, true, advancedUpstreams, health, rateLimit, _editingVersion,
                AllowPrivateExceptionsBox.IsChecked == true);
            if (_editingId is null)
                await _api.AddSiteAsync(request);
            else
                await _api.UpdateSiteAsync(_editingId, request);
            var verb = _editingId is null ? "เพิ่ม" : "แก้ไข";
            ResetForm();
            await RefreshListAsync(false);
            Show($"{verb} Site แล้ว — route ใหม่ใช้งานทันที", true);
        }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
        finally { AddSiteButton.IsEnabled = CanEdit; }
    }

    private void OnEditSite(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || ((FrameworkElement)sender).DataContext is not SiteRow row) return;
        var site = row.Site;
        _editingId = site.SiteId;
        _editingVersion = site.Version;
        SiteIdBox.Text = site.SiteId;
        SiteIdBox.IsReadOnly = true;
        HostnamesBox.Text = string.Join(", ", site.Hostnames);
        PathBox.Text = string.Join(", ", site.PathPrefixes);
        var upstream = site.Upstreams.FirstOrDefault();
        UpstreamBox.Text = upstream?.Address ?? "";
        AllowedHostsBox.Text = upstream is null ? "" : new Uri(upstream.Address).Host;
        WafPolicyBox.Text = site.WafPolicy;
        UpstreamsBox.Text = string.Join(";", site.Upstreams.Select(x => $"{x.Id}={x.Address}@{x.Weight}"));
        HealthPathBox.Text = site.HealthPath;
        RateRequestsBox.Text = $"{site.RateRequests}/{site.RateWindowSeconds}";
        RateGlobalBox.Text = site.RateGlobalRequests.ToString();
        AllowPrivateExceptionsBox.IsChecked = site.AllowPrivateExceptions;
        AddSiteButton.Content = "บันทึกการแก้ไข";
        CancelEditButton.Visibility = Visibility.Visible;
        FormHint.Text = $"กำลังแก้ไข {site.SiteId} — เปลี่ยน Site ID ไม่ได้เพื่อป้องกัน route สับสน";
        SiteIdBox.Focus();
    }

    private async void OnDeleteSite(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || ((FrameworkElement)sender).DataContext is not SiteRow row) return;
        if (!Ui.Confirm(Window.GetWindow(this), $"ลบ Site {row.Site.SiteId}? route จะหยุดใช้งานทันที")) return;
        try
        {
            await _api.DeleteSiteAsync(row.Site.SiteId, row.Site.Version);
            if (string.Equals(_editingId, row.Site.SiteId, StringComparison.OrdinalIgnoreCase)) ResetForm();
            await RefreshListAsync(false);
            Show("ลบ Site แล้ว — route หยุดใช้งานทันที", true);
        }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
    }

    private async void OnHealthCheck(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not SiteRow row) return;
        var button = (Button)sender;
        button.IsEnabled = false;
        try
        {
            var result = await _api.CheckSiteHealthAsync(row.Site.SiteId);
            await RefreshListAsync(false);
            var suffix = result.PendingRestart ? " — route ยังไม่ active โปรดตรวจสอบสถานะ Gateway" : "";
            var reasons = string.Join(", ", result.Upstreams
                .Where(x => !string.IsNullOrWhiteSpace(x.Reason))
                .Select(x => $"{x.Id}: {x.Reason}"));
            var reasonSuffix = reasons.Length == 0 ? "" : $" — เหตุผล: {reasons}";
            Show($"ตรวจสอบ {result.SiteId}: {result.Health}{reasonSuffix}{suffix}", result.Health == "healthy");
        }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
        finally { button.IsEnabled = true; }
    }

    private async void OnAcknowledgeAlert(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not SiteRow row || !row.CanAcknowledge) return;
        var button = (Button)sender;
        button.IsEnabled = false;
        try
        {
            await _api.AcknowledgeSiteHealthAlertAsync(row.Site.SiteId);
            await RefreshListAsync(false);
            Show($"รับทราบ alert ของ {row.Site.SiteId} เป็นเวลา 1 ชั่วโมงแล้ว", true);
        }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
        finally { button.IsEnabled = row.CanAcknowledge; }
    }

    private void OnCancelEdit(object sender, RoutedEventArgs e) => ResetForm();

    private void ResetForm()
    {
        _editingId = null;
        _editingVersion = null;
        SiteIdBox.Clear(); SiteIdBox.IsReadOnly = false; HostnamesBox.Clear(); PathBox.Text = "/";
        UpstreamBox.Clear(); AllowedHostsBox.Clear(); WafPolicyBox.Text = "default";
        UpstreamsBox.Clear(); HealthPathBox.Text = "/"; RateRequestsBox.Text = "300/60"; RateGlobalBox.Text = "10000";
        AllowPrivateExceptionsBox.IsChecked = false;
        AddSiteButton.Content = "เพิ่ม Site";
        CancelEditButton.Visibility = Visibility.Collapsed;
        FormHint.Text = "ตัวอย่างสำหรับเครื่องทดสอบ: Hostname ใช้ affiliateiq.localhost (ไม่ใส่พอร์ต) และ Upstream ใช้ http://localhost:4000/ — Hostname ต้องไม่ซ้ำกับ Site อื่น";
    }

    private void Show(string text, bool ok)
    {
        Msg.Text = text;
        Msg.Foreground = Ui.Brush(ok ? "Green" : "Red");
    }

    private static string[] SplitValues(string value) => value.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private bool TryParseAdvancedSettings(out SiteUpstreamInputDto[]? upstreams, out SiteHealthPolicyDto health, out SiteRateLimitPolicyDto rateLimit, out string error)
    {
        upstreams = null;
        health = new SiteHealthPolicyDto();
        rateLimit = new SiteRateLimitPolicyDto();
        error = "";
        var advanced = UpstreamsBox.Text.Trim();
        if (advanced.Length > 0)
        {
            var parsed = new List<SiteUpstreamInputDto>();
            foreach (var item in advanced.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = item.IndexOf('=');
                if (separator <= 0) { error = "Advanced upstreams ต้องอยู่ในรูป id=url@weight"; return false; }
                var id = item[..separator].Trim();
                var addressAndWeight = item[(separator + 1)..].Trim();
                var at = addressAndWeight.LastIndexOf('@');
                var address = at > 0 ? addressAndWeight[..at] : addressAndWeight;
                var weightText = at > 0 ? addressAndWeight[(at + 1)..] : "100";
                if (!int.TryParse(weightText, out var weight) || weight is < 1 or > 10_000)
                { error = "Upstream weight ต้องอยู่ระหว่าง 1 ถึง 10000"; return false; }
                parsed.Add(new SiteUpstreamInputDto(id, address, weight));
            }
            upstreams = parsed.ToArray();
        }

        var rateParts = SplitValues(RateRequestsBox.Text.Replace('/', ','));
        if (rateParts.Length != 2 || !int.TryParse(rateParts[0], out var requests) || !int.TryParse(rateParts[1], out var window) || !int.TryParse(RateGlobalBox.Text, out var global))
        { error = "Rate limit ต้องเป็น requests/window และ global เป็นตัวเลข"; return false; }
        health = new SiteHealthPolicyDto(string.IsNullOrWhiteSpace(HealthPathBox.Text) ? "/" : HealthPathBox.Text.Trim());
        rateLimit = new SiteRateLimitPolicyDto(requests, window, global);
        return true;
    }

    private sealed class SiteRow(SiteDto site, SiteHealthTrendDto? trend)
    {
        public SiteDto Site => site;
        public string SiteId => site.SiteId;
        public string HostnamesText => string.Join(", ", site.Hostnames);
        public string UpstreamsText => string.Join(", ", site.Upstreams.Select(x => $"{x.Id} {x.Scheme}:{x.Port} ({x.Health})"));
        public string Health => site.Health;
        public string TrendText => trend is null ? "—" : $"{trend.HealthyPercent:0.#}% / {trend.Total}";
        private string DisplayState => SiteHealthDisplayState.Resolve(site.Health, trend?.AlertState, trend?.RecoveryState, site.PendingRestart, trend?.IsStale == true, trend?.EffectiveAlertState);
        public bool CanAcknowledge => trend is { AlertSuppressed: false } && DisplayState is "critical" or "degraded";
        public string AlertText => DisplayState switch
        {
            "recovered" => "กลับมาปกติ · มีประวัติเก่า",
            "critical" when trend?.AlertSuppressed == true => "วิกฤติ · รับทราบแล้ว",
            "degraded" when trend?.AlertSuppressed == true => "เฝ้าระวัง · รับทราบแล้ว",
            "critical" => "วิกฤติ",
            "degraded" => "เฝ้าระวัง",
            "healthy" => "ปกติ",
            "unhealthy" => "ไม่พร้อม",
            "pending-restart" => "รอเปิดใช้",
            "stale" => "ข้อมูลเก่า · รอตรวจสอบ",
            _ => "ไม่มีข้อมูล"
        };
        public Brush AlertBrush => DisplayState switch
        {
            "critical" => Ui.Brush("Red"),
            "degraded" => Ui.Brush("Orange"),
            "healthy" or "recovered" => Ui.Brush("Green"),
            "unhealthy" => Ui.Brush("Red"),
            "stale" => Ui.Brush("Muted"),
            _ => Ui.Brush("Muted")
        };
        public string WafPolicy => site.WafPolicy;
    }
}
