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
        About.Text = $"AegisWAF v{typeof(SettingsView).Assembly.GetName().Version?.ToString(3)} • เชื่อมต่อกับ {info?.Url.GetLeftPart(UriPartial.Authority)} • ผู้ใช้ {api.Session?.Username} ({api.Session?.Role})";
        if (info is not { Embedded: true } || _current is null)
        {
            LocalPanel.Visibility = Visibility.Collapsed; RemoteNote.Visibility = Visibility.Visible;
            RemoteText.Text = $"ตอนนี้เชื่อมต่อกับ Gateway ที่รันอยู่เครื่องอื่น ({info?.Url.GetLeftPart(UriPartial.Authority)}) — การตั้งค่าฐานข้อมูลและเว็บที่ปกป้องต้องแก้ที่เครื่องนั้น\n" +
                              "หากต้องการใช้ระบบในเครื่องนี้ ให้ออกจากระบบ แล้วเลือก กลับไปใช้ระบบในเครื่องนี้ ที่หน้าเข้าสู่ระบบ";
            return;
        }
        DbHost.Text = _current.Host; DbPort.Text = _current.Port.ToString(); DbName.Text = _current.Database; DbUser.Text = _current.Username;
        Upstream.Text = _current.UpstreamUrl; ListenPort.Text = _current.ListenPort.ToString();
    }

    public bool AutoRefresh => false;
    public Task RefreshAsync() => Task.CompletedTask;

    private static void Set(TextBlock t, string text, string brushKey) { t.Text = text; t.Foreground = Ui.Brush(brushKey); }

    private DbParams Db => new(DbHost.Text.Trim(), int.TryParse(DbPort.Text, out var p) ? p : 5432, DbName.Text.Trim(), DbUser.Text.Trim(),
        DbPass.Password.Length > 0 ? DbPass.Password : _current!.Password);

    private async void OnTestDb(object sender, RoutedEventArgs e)
    {
        Set(DbResult, "กำลังทดสอบ...", "Muted");
        var r = await GatewaySetup.CheckDatabaseAsync(Db);
        Set(DbResult, (r.Ready ? "✓ " : "✗ ") + r.Message, r.Ready ? "Green" : "Red");
    }

    private async void OnCheckSite(object sender, RoutedEventArgs e)
    {
        Set(SiteResult, "กำลังตรวจสอบ...", "Muted");
        var r = await GatewaySetup.CheckUpstreamAsync(Upstream.Text);
        Set(SiteResult, (r.Reachable ? "✓ " : "⚠ ") + r.Message, r.Reachable ? "Green" : "Orange");
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(Upstream.Text.Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) { Set(SaveResult, "ที่อยู่เว็บจริงไม่ถูกต้อง (ตัวอย่าง http://localhost:8080/)", "Red"); return; }
        if (!int.TryParse(ListenPort.Text, out var port) || port is < 1 or > 65535) { Set(SaveResult, "พอร์ตต้องเป็นตัวเลข 1-65535", "Red"); return; }
        SaveButton.IsEnabled = false;
        try
        {
            Set(SaveResult, "กำลังตรวจสอบฐานข้อมูล...", "Muted");
            var check = await GatewaySetup.CheckDatabaseAsync(Db);
            if (!check.Ready) { Set(SaveResult, "✗ ยังไม่บันทึก: " + check.Message, "Red"); return; }
            SettingsStore.Save(GatewaySettings.Create(Db, port, Upstream.Text.Trim()));
            Set(SaveResult, "บันทึกแล้ว กำลังเริ่มระบบใหม่ — กรุณาเข้าสู่ระบบอีกครั้ง", "Green");
            await App.Gateway.StopAsync();
            _restart();
        }
        finally { SaveButton.IsEnabled = true; }
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (Ui.Confirm(Window.GetWindow(this), "ลบการตั้งค่าที่จำไว้ในเครื่องนี้และเริ่มตั้งค่าใหม่ตั้งแต่ต้น?\n(ข้อมูลในฐานข้อมูลไม่ถูกลบ)")) _reset();
    }
}
