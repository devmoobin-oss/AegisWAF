using System.Windows;
using System.Windows.Controls;
using AegisWAF.Client;
using AegisWAF.Gateway;

namespace AegisWAF.Desktop.Views;

public partial class SettingsView : UserControl, IPage
{
    private readonly Action _restart, _reset;
    private readonly GatewaySettings? _current = SettingsStore.Load();

    public SettingsView(ApiClient api, Action restart, Action reset)
    {
        InitializeComponent();
        _restart = restart; _reset = reset;
        var info = App.Info;
        About.Text = $"AegisWAF v{typeof(SettingsView).Assembly.GetName().Version?.ToString(3)} • Connected to {info?.Url.GetLeftPart(UriPartial.Authority)} • User {api.Session?.Username} ({api.Session?.Role})";
        if (info is not { Embedded: true } || _current is null)
        {
            LocalPanel.Visibility = Visibility.Collapsed;
            RemoteNote.Visibility = Visibility.Visible;
            RemoteText.Text = $"Connected to a Gateway on another machine ({info?.Url.GetLeftPart(UriPartial.Authority)}). Database and listener settings must be changed on that machine. Protected websites are managed in Multi-Site Gateway.";
            return;
        }
        DbHost.Text = _current.Host; DbPort.Text = _current.Port.ToString(); DbName.Text = _current.Database; DbUser.Text = _current.Username;
        ListenPort.Text = _current.ListenPort.ToString();
    }

    public bool AutoRefresh => false;
    public Task RefreshAsync() => Task.CompletedTask;

    private static void Set(TextBlock t, string text, string brushKey) { t.Text = text; t.Foreground = Ui.Brush(brushKey); }

    private DbParams Db => new(DbHost.Text.Trim(), int.TryParse(DbPort.Text, out var p) ? p : 5432, DbName.Text.Trim(), DbUser.Text.Trim(),
        DbPass.Password.Length > 0 ? DbPass.Password : _current!.Password);

    private async void OnTestDb(object sender, RoutedEventArgs e)
    {
        Set(DbResult, "Testing database connection...", "Muted");
        var r = await GatewaySetup.CheckDatabaseAsync(Db);
        Set(DbResult, (r.Ready ? "✓ " : "✗ ") + r.Message, r.Ready ? "Green" : "Red");
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ListenPort.Text, out var port) || port is < 1 or > 65535)
        { Set(SaveResult, "Listener port must be a number between 1 and 65535", "Red"); return; }
        SaveButton.IsEnabled = false;
        try
        {
            Set(SaveResult, "Checking database connection...", "Muted");
            var check = await GatewaySetup.CheckDatabaseAsync(Db);
            if (!check.Ready) { Set(SaveResult, "✗ Not saved: " + check.Message, "Red"); return; }
            // The protected website is intentionally preserved here for legacy startup compatibility;
            // all new routing changes belong to Multi-Site Gateway.
            SettingsStore.Save(GatewaySettings.Create(Db, port, _current!.UpstreamUrl));
            Set(SaveResult, "Saved. Restarting Gateway — please sign in again.", "Green");
            await App.Gateway.StopAsync();
            _restart();
        }
        finally { SaveButton.IsEnabled = true; }
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (Ui.Confirm(Window.GetWindow(this), "Reset local settings and start setup again? Database data, users and rules will not be deleted.")) _reset();
    }
}
