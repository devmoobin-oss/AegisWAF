using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AegisWAF.Client;
using AegisWAF.Desktop.Views;

namespace AegisWAF.Desktop;

public partial class MainWindow : Window
{
    private readonly ApiClient _api;
    private readonly Dictionary<RadioButton, (string Title, string Hint, Func<UserControl> Create)> _pages = new();
    private readonly Dictionary<RadioButton, UserControl> _instances = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private RadioButton? _current;
    private bool _refreshing;

    /// <summary>True when the user logged out (or the session expired) rather than closing the window.</summary>
    public bool ReturnToLogin { get; private set; }
    /// <summary>The user chose to discard all saved settings; the app returns to the setup wizard.</summary>
    public bool ResetRequested { get; private set; }

    public MainWindow(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        var s = api.Session!;
        UserText.Text = s.Username;
        RoleText.Text = s.Role;
        AvatarText.Text = s.Username.Length > 0 ? s.Username[..1].ToUpperInvariant() : "?";
        VersionText.Text = "v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.10.0") + " · OWASP mapped";

        _pages[NavDashboard] = ("ภาพรวม", "สถานะการป้องกันและการโจมตีล่าสุดในที่เดียว", () => new DashboardView(api));
        _pages[NavEvents] = ("เหตุการณ์ที่ถูกบล็อก", "รายการคำขอที่ระบบปฏิเสธ พร้อมคำอธิบาย — กด \"บล็อก IP นี้\" เพื่อกันผู้โจมตีถาวร", () => new EventsView(api));
        _pages[NavRules] = ("กฎการป้องกัน", "เปิด/ปิดกฎที่ใช้ตรวจ เพิ่มกฎของคุณเอง และทดลองกฎโดยไม่กระทบเว็บจริง", () => new RulesView(api));
        _pages[NavProtect] = ("ระบบป้องกันอะไรได้บ้าง", "สิ่งที่ป้องกันได้ ป้องกันได้บางส่วน และยังไม่ป้องกัน — พร้อมตัวอย่างจริงที่ลองได้", () => new ProtectionView(api));
        _pages[NavIps] = ("IP ที่บล็อก / อนุญาต", "กำหนดว่า IP ไหนถูกกันออกตลอด หรือได้รับการยกเว้นจากการตรวจ", () => new IpView(api));
        _pages[NavUsers] = ("ผู้ใช้งานระบบ", "จัดการบัญชีและสิทธิ์ของคนที่เข้าใช้โปรแกรมนี้", () => new UsersView(api));
        _pages[NavAudit] = ("ประวัติการใช้งาน", "ใครเข้าสู่ระบบและเปลี่ยนแปลงอะไรบ้าง", () => new AuditView(api));
        _pages[NavSettings] = ("ตั้งค่า", "ฐานข้อมูล เว็บที่ปกป้อง และพอร์ต — แก้ไขได้ที่นี่", () => new SettingsView(api, () => { ReturnToLogin = true; Close(); }, () => { ResetRequested = true; Close(); }));

        _pages[NavOwasp] = ("OWASP Top 10", "Coverage ของระบบป้องกันตาม OWASP Top 10:2021 — badge เป็น mapping ไม่ใช่ certification", () => new OwaspCoverageView(api));

        // Navigation mirrors server-side authorisation; the server remains the enforcement point.
        NavUsers.Visibility = s.Level >= 5 ? Visibility.Visible : Visibility.Collapsed;
        NavAudit.Visibility = s.Level >= 2 ? Visibility.Visible : Visibility.Collapsed;

        api.SessionExpired += () => Dispatcher.BeginInvoke(() =>
        {
            if (ReturnToLogin) return;
            ReturnToLogin = true;
            MessageBox.Show("เซสชันหมดอายุหรือถูกยกเลิก กรุณาเข้าสู่ระบบใหม่", "AegisWAF", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        });
        _timer.Tick += async (_, _) => await RefreshCurrentAsync(auto: true);
        Loaded += async (_, _) => { Navigate(NavDashboard); _timer.Start(); await Task.CompletedTask; };
        Closed += (_, _) => _timer.Stop();
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && Host is null) return;
        Navigate((RadioButton)sender);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        var target = e.Key switch
        {
            Key.D1 => NavDashboard,
            Key.D2 => NavEvents,
            Key.D3 => NavRules,
            Key.D4 => NavOwasp,
            Key.D5 => NavProtect,
            _ => null
        };
        if (target is null || target.Visibility != Visibility.Visible) return;
        target.IsChecked = true;
        target.Focus();
        e.Handled = true;
    }

    private void Navigate(RadioButton nav)
    {
        if (!_pages.TryGetValue(nav, out var page)) return;
        _current = nav;
        if (!_instances.TryGetValue(nav, out var view)) _instances[nav] = view = page.Create();
        PageTitle.Text = page.Title; PageHint.Text = page.Hint;
        Host.Content = view;
        _ = RefreshCurrentAsync(auto: false);
    }

    private async Task RefreshCurrentAsync(bool auto)
    {
        if (_current is null || _refreshing || !_instances.TryGetValue(_current, out var view) || view is not IPage page) return;
        if (auto && !page.AutoRefresh) return;
        _refreshing = true;
        try
        {
            await page.RefreshAsync();
            SetStatus(true, "System Online");
        }
        catch (ApiException ex) when (ex.Status == 0) { SetStatus(false, "Gateway Offline"); }
        catch (ApiException) { SetStatus(true, "System Online"); } // page reports its own HTTP errors
        finally { _refreshing = false; }
    }

    private void SetStatus(bool ok, string text)
    {
        StatusText.Text = text;
        var brush = Ui.Brush(ok ? "Green" : "Red");
        StatusDot.Fill = brush; StatusText.Foreground = brush;
    }

    private void OnChangePassword(object sender, RoutedEventArgs e) => new ChangePasswordWindow(_api, forced: false) { Owner = this }.ShowDialog();

    private async void OnLogout(object sender, RoutedEventArgs e)
    {
        try { await _api.LogoutAsync(); } catch (ApiException) { /* session already gone */ }
        ReturnToLogin = true;
        Close();
    }
}
