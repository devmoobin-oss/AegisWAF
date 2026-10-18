using System.Windows;
using System.Windows.Controls;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class SitesView : UserControl, IPage
{
    private readonly ApiClient _api;
    public SitesView(ApiClient api) { InitializeComponent(); _api = api; }
    public bool AutoRefresh => true;
    public async Task RefreshAsync()
    {
        try
        {
            var sites = await _api.GetSitesAsync();
            Grid.ItemsSource = sites.Select(x => new SiteRow(x)).ToArray();
            Msg.Text = "Routing is loaded from validated configuration. Restart Gateway after configuration changes.";
        }
        catch (ApiException ex) { Msg.Text = Ui.Describe(ex); }
    }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private sealed class SiteRow(SiteDto site)
    {
        public string SiteId => site.SiteId;
        public string HostnamesText => string.Join(", ", site.Hostnames);
        public string UpstreamsText => string.Join(", ", site.Upstreams.Select(x => $"{x.Id} {x.Scheme}:{x.Port} ({x.Health})"));
        public string Health => site.Health;
        public string WafPolicy => site.WafPolicy;
    }
}
