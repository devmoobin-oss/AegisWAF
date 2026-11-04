using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class CommandCenterView : UserControl, IPage
{
    private readonly ApiClient _api;
    private readonly DispatcherTimer _liveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private List<CommandCenterTrafficDto> _all = [];
    private bool _refreshing;
    private bool _initialized;

    public CommandCenterView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        _initialized = true;
        Loaded += async (_, _) => { _liveTimer.Start(); await RefreshAsync(); };
        Unloaded += (_, _) => _liveTimer.Stop();
        _liveTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public bool AutoRefresh => true;

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var decision = (DecisionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var summary = await _api.GetCommandCenterSummaryAsync();
            _all = await _api.GetCommandCenterTrafficAsync(new CommandCenterTrafficQuery(100, decision, Search: SearchBox.Text));
            ActiveText.Text = summary.ActiveConnections.ToString("N0");
            RequestsText.Text = summary.RequestsPerMinute.ToString("N0");
            AllowedText.Text = summary.Allowed.ToString("N0");
            BlockedText.Text = summary.Blocked.ToString("N0");
            RateText.Text = summary.RateLimited.ToString("N0");
            ErrorsText.Text = summary.UpstreamErrors.ToString("N0");
            LiveStatus.Text = $"LIVE • {summary.BufferedEvents:N0} buffered";
            LiveStatus.Foreground = Ui.Brush("Green"); LiveDot.Fill = Ui.Brush("Green");
            ApplyFilter();
        }
        catch (ApiException ex)
        {
            LiveStatus.Text = ex.Status == 403 ? "NO PERMISSION" : "GATEWAY OFFLINE";
            LiveStatus.Foreground = Ui.Brush(ex.Status == 403 ? "Orange" : "Red"); LiveDot.Fill = LiveStatus.Foreground;
            NoticeText.Text = Ui.Describe(ex);
        }
        finally { _refreshing = false; }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        // SelectionChanged can fire while InitializeComponent is still creating named controls.
        if (!_initialized) return;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var term = SearchBox.Text.Trim();
        var rows = term.Length == 0 ? _all : _all.Where(x =>
            x.ClientIp.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.Path.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (x.SiteId?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (x.Hostname?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (x.Rule?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        TrafficGrid.ItemsSource = rows;
        CountText.Text = $"{rows.Count:N0} events";
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
