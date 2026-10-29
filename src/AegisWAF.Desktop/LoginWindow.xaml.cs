using System.Windows;
using AegisWAF.Client;

namespace AegisWAF.Desktop;

/// <summary>Username + password only. The in-app gateway starts in the background from the saved settings.</summary>
public partial class LoginWindow : Window
{
    private readonly GatewaySettings? _settings = SettingsStore.Load();
    private bool _remote;

    public ApiClient? Api { get; private set; }
    public ConnectionInfo? Info { get; private set; }
    /// <summary>The user asked to discard the saved settings and run the setup wizard again.</summary>
    public bool ResetRequested { get; private set; }

    public LoginWindow(string? suggestedUser = null)
    {
        InitializeComponent();
        UserBox.Text = suggestedUser ?? "";
        Loaded += async (_, _) =>
        {
            (UserBox.Text.Length == 0 ? UserBox : (UIElement)PassBox).Focus();
            await StartEmbeddedAsync();
        };
    }

    private void Status(string text, string brushKey, bool busy = false)
    {
        StatusText.Text = text; StatusDot.Fill = Ui.Brush(brushKey);
        LoginButton.IsEnabled = !busy && brushKey == "Green";
    }

    private async Task StartEmbeddedAsync()
    {
        ErrorText.Text = ""; RetryButton.Visibility = Visibility.Collapsed;
        if (_remote) { Status("โหมดเชื่อมต่อ Gateway เครื่องอื่น", "Green"); return; }
        if (_settings is null) { Status("ยังไม่ได้ตั้งค่า — กด ตั้งค่าใหม่", "Red"); return; }
        if (App.Gateway.Running) { Status($"ระบบป้องกันทำงานอยู่ที่ {_settings.ListenUrl.GetLeftPart(UriPartial.Authority)}", "Green"); return; }
        Status("กำลังเริ่มระบบป้องกัน...", "Orange", busy: true);
        try
        {
            await App.Gateway.StartAsync(_settings);
            Status($"ระบบป้องกันพร้อมแล้วที่ {_settings.ListenUrl.GetLeftPart(UriPartial.Authority)}", "Green");
        }
        catch (Exception ex)
        {
            await App.Gateway.StopAsync();
            Status("เริ่มระบบป้องกันไม่สำเร็จ", "Red");
            ErrorText.Text = GatewayStartupFailure.Describe(ex.Message, _settings.ListenPort);
            RetryButton.Visibility = Visibility.Visible;
        }
    }

    private async void OnRetry(object sender, RoutedEventArgs e) => await StartEmbeddedAsync();

    private async void OnToggleRemote(object sender, RoutedEventArgs e)
    {
        _remote = !_remote;
        RemotePanel.Visibility = _remote ? Visibility.Visible : Visibility.Collapsed;
        RemoteLink.Content = _remote ? "กลับไปใช้ระบบในเครื่องนี้" : "ใช้ Gateway ที่รันอยู่เครื่องอื่น";
        ErrorText.Text = "";
        if (_remote) { await App.Gateway.StopAsync(); Status("โหมดเชื่อมต่อ Gateway เครื่องอื่น", "Green"); }
        else await StartEmbeddedAsync();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (!Ui.Confirm(this, "ลบการตั้งค่าที่บันทึกไว้และเริ่มตั้งค่าใหม่ตั้งแต่ต้น?\n(ข้อมูลในฐานข้อมูลไม่ถูกลบ)")) return;
        ResetRequested = true;
        DialogResult = false;
    }

    private async void OnLogin(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        Uri url;
        if (_remote)
        {
            if (!Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) { ErrorText.Text = "ที่อยู่ Gateway ไม่ถูกต้อง (ตัวอย่าง http://192.168.1.10:5080)"; return; }
            url = u;
        }
        else url = _settings!.ListenUrl;

        LoginButton.IsEnabled = false;
        var api = new ApiClient(url);
        try
        {
            await api.LoginAsync(UserBox.Text.Trim(), PassBox.Password);
            Api = api;
            Info = new ConnectionInfo(!_remote, url, _remote ? null : _settings!.UpstreamUrl);
            DialogResult = true;
        }
        catch (ApiException ex)
        {
            api.Dispose();
            ErrorText.Text = ex.Status switch
            {
                401 => "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง (ถ้าใส่ผิดหลายครั้ง บัญชีจะถูกล็อก 15 นาที)",
                403 => "Gateway ปฏิเสธการเชื่อมต่อ (อนุญาตเฉพาะการเข้าจากเครื่องเดียวกัน)",
                _ => ex.Message
            };
        }
        finally { PassBox.Clear(); LoginButton.IsEnabled = true; }
    }
}
