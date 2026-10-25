using System.Windows;
using System.Windows.Controls;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class SitesView : UserControl, IPage
{
    private readonly ApiClient _api;
    private string? _editingId;
    public SitesView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        VersionText.Text = $"AegisWAF v{typeof(SitesView).Assembly.GetName().Version?.ToString(3)}";
        CanEdit = api.Session!.Level >= 4;
        AddSiteForm.IsEnabled = CanEdit;
        if (!CanEdit) FormHint.Text = "ต้องใช้สิทธิ์ SECURITY_ADMIN ขึ้นไปเพื่อเพิ่ม Site ระบบยังคงบังคับสิทธิ์ซ้ำที่ Gateway API";
    }
    public bool CanEdit { get; }
    public bool AutoRefresh => true;
    public Task RefreshAsync() => RefreshListAsync(true);

    private async Task RefreshListAsync(bool announce)
    {
        try
        {
            var sites = await _api.GetSitesAsync();
            Grid.ItemsSource = sites.Select(x => new SiteRow(x)).ToArray();
            if (announce) Msg.Text = "Routing is loaded from validated configuration. Changes are staged safely and require Gateway restart.";
        }
        catch (ApiException ex) { Msg.Text = Ui.Describe(ex); }
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnAddSite(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        var id = SiteIdBox.Text.Trim();
        var hosts = SplitValues(HostnamesBox.Text);
        var path = PathBox.Text.Trim();
        var upstreamText = UpstreamBox.Text.Trim();
        if (id.Length == 0 || hosts.Length == 0 || path.Length == 0 || upstreamText.Length == 0)
        { Show("กรุณากรอก Site ID, Hostname, Path prefix และ Upstream URL ให้ครบ", false); return; }
        if (!Uri.TryCreate(upstreamText, UriKind.Absolute, out var upstream) ||
            (upstream.Scheme != Uri.UriSchemeHttp && upstream.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(upstream.UserInfo))
        { Show("Upstream ต้องเป็น URL แบบ HTTP(S) และห้ามมี username/password", false); return; }

        var allowedHosts = SplitValues(AllowedHostsBox.Text);
        if (allowedHosts.Length == 0) allowedHosts = [upstream.Host];
        var port = upstream.Port > 0 ? upstream.Port : upstream.Scheme == Uri.UriSchemeHttps ? 443 : 80;
        AddSiteButton.IsEnabled = false;
        try
        {
            var request = new SiteCreateDto(id, hosts, SplitValues(PathBox.Text), upstream.ToString(),
                string.IsNullOrWhiteSpace(WafPolicyBox.Text) ? "default" : WafPolicyBox.Text.Trim(),
                allowedHosts, [port], [upstream.Scheme], true);
            if (_editingId is null)
                await _api.AddSiteAsync(request);
            else
                await _api.UpdateSiteAsync(_editingId, request);
            var verb = _editingId is null ? "เพิ่ม" : "แก้ไข";
            ResetForm();
            await RefreshListAsync(false);
            Show($"{verb} Site แล้ว — Restart Gateway เพื่อเปิดใช้งาน route ใหม่", true);
        }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
        finally { AddSiteButton.IsEnabled = CanEdit; }
    }

    private void OnEditSite(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || ((FrameworkElement)sender).DataContext is not SiteRow row) return;
        var site = row.Site;
        _editingId = site.SiteId;
        SiteIdBox.Text = site.SiteId;
        SiteIdBox.IsReadOnly = true;
        HostnamesBox.Text = string.Join(", ", site.Hostnames);
        PathBox.Text = string.Join(", ", site.PathPrefixes);
        var upstream = site.Upstreams.FirstOrDefault();
        UpstreamBox.Text = upstream?.Address ?? "";
        AllowedHostsBox.Text = upstream is null ? "" : new Uri(upstream.Address).Host;
        WafPolicyBox.Text = site.WafPolicy;
        AddSiteButton.Content = "บันทึกการแก้ไข";
        CancelEditButton.Visibility = Visibility.Visible;
        FormHint.Text = $"กำลังแก้ไข {site.SiteId} — เปลี่ยน Site ID ไม่ได้เพื่อป้องกัน route สับสน";
        SiteIdBox.Focus();
    }

    private async void OnDeleteSite(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || ((FrameworkElement)sender).DataContext is not SiteRow row) return;
        if (!Ui.Confirm(Window.GetWindow(this), $"ลบ Site {row.Site.SiteId}? การเปลี่ยนแปลงจะมีผลหลัง Restart Gateway")) return;
        try
        {
            await _api.DeleteSiteAsync(row.Site.SiteId);
            if (string.Equals(_editingId, row.Site.SiteId, StringComparison.OrdinalIgnoreCase)) ResetForm();
            await RefreshListAsync(false);
            Show("ลบ Site แล้ว — Restart Gateway เพื่อใช้การเปลี่ยนแปลง", true);
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
            var suffix = result.PendingRestart ? " — ต้อง Restart Gateway ก่อนจึงจะใช้งาน route ใหม่" : "";
            Show($"ตรวจสอบ {result.SiteId}: {result.Health}{suffix}", result.Health == "healthy");
        }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
        finally { button.IsEnabled = true; }
    }

    private void OnCancelEdit(object sender, RoutedEventArgs e) => ResetForm();

    private void ResetForm()
    {
        _editingId = null;
        SiteIdBox.Clear(); SiteIdBox.IsReadOnly = false; HostnamesBox.Clear(); PathBox.Text = "/";
        UpstreamBox.Clear(); AllowedHostsBox.Clear(); WafPolicyBox.Text = "default";
        AddSiteButton.Content = "เพิ่ม Site";
        CancelEditButton.Visibility = Visibility.Collapsed;
        FormHint.Text = "Production: upstream ต้องเป็น HTTPS และ Egress ต้องตรงกับปลายทาง";
    }

    private void Show(string text, bool ok)
    {
        Msg.Text = text;
        Msg.Foreground = Ui.Brush(ok ? "Green" : "Red");
    }

    private static string[] SplitValues(string value) => value.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed class SiteRow(SiteDto site)
    {
        public SiteDto Site => site;
        public string SiteId => site.SiteId;
        public string HostnamesText => string.Join(", ", site.Hostnames);
        public string UpstreamsText => string.Join(", ", site.Upstreams.Select(x => $"{x.Id} {x.Scheme}:{x.Port} ({x.Health})"));
        public string Health => site.Health;
        public string WafPolicy => site.WafPolicy;
    }
}
