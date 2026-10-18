using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using AegisWAF.Client;
using AegisWAF.Gateway;

namespace AegisWAF.Desktop;

/// <summary>First-run wizard: database → administrator → protected site → start. Replaces hand-written connection strings.</summary>
public partial class SetupWindow : Window
{
    private static readonly Regex UserPattern = new("^[a-z0-9._-]{3,64}$", RegexOptions.Compiled);
    private readonly Border[] _pills;
    private readonly TextBlock[] _pillTexts;
    private readonly FrameworkElement[] _steps;
    private int _step;
    private DbCheck? _db;

    /// <summary>Set when the wizard completes. <see cref="Api"/> is non-null when a new administrator was created and signed in.</summary>
    public GatewaySettings? Settings { get; private set; }
    public ApiClient? Api { get; private set; }

    public SetupWindow()
    {
        InitializeComponent();
        _pills = [Pill1, Pill2, Pill3, Pill4];
        _pillTexts = [PillText1, PillText2, PillText3, PillText4];
        _steps = [Step1, Step2, Step3, Step4];
        foreach (var t in new TextBox[] { DbHost, DbPort, DbUser, DbName }) t.TextChanged += (_, _) => InvalidateDb();
        DbPass.PasswordChanged += (_, _) => InvalidateDb();
        // Pre-fill from an older/partial configuration when the user chose "set up again".
        Show(0);
        OnPortChanged(null!, null!);
    }

    private bool HasAdmin => _db?.HasUsers == true;

    private DbParams CurrentDb => new(DbHost.Text.Trim(), int.TryParse(DbPort.Text, out var p) ? p : 5432, DbName.Text.Trim(), DbUser.Text.Trim(), DbPass.Password);

    private void InvalidateDb()
    {
        _db = null; CreateDbButton.Visibility = Visibility.Collapsed; DbHelp.Visibility = Visibility.Collapsed;
        if (DbResult is not null) DbResult.Text = "";
        if (NextButton is not null && _step == 0) NextButton.IsEnabled = false;
    }

    private void Show(int step)
    {
        _step = step;
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;
            var done = i < step; var active = i == step;
            _pills[i].Background = active ? Ui.Brush("BlueDark") : done ? (Brush)new BrushConverter().ConvertFromString("#0F4B3D")! : Ui.Brush("Hover");
            _pillTexts[i].Foreground = active || done ? Brushes.White : Ui.Brush("Muted");
            _pillTexts[i].FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
        BackButton.Visibility = step == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = step == 3 ? "เริ่มใช้งาน" : "ถัดไป";
        NextButton.IsEnabled = step != 0 || _db?.Ready == true;
        if (step == 1) { AdminExisting.Visibility = HasAdmin ? Visibility.Visible : Visibility.Collapsed; AdminNew.Visibility = HasAdmin ? Visibility.Collapsed : Visibility.Visible; }
        if (step == 3) BuildSummary();
    }

    private void SetResult(TextBlock target, string text, string brushKey) { target.Text = text; target.Foreground = Ui.Brush(brushKey); }

    // ---- step 1 -------------------------------------------------------------------------------------------
    private async void OnTestDb(object sender, RoutedEventArgs e) => await TestDbAsync();

    private async Task TestDbAsync()
    {
        TestDbButton.IsEnabled = false; CreateDbButton.Visibility = Visibility.Collapsed; DbHelp.Visibility = Visibility.Collapsed;
        SetResult(DbResult, "กำลังทดสอบ...", "Muted");
        try
        {
            var r = await GatewaySetup.CheckDatabaseAsync(CurrentDb);
            _db = r;
            SetResult(DbResult, (r.Ready ? "✓ " : "✗ ") + r.Message, r.Ready ? "Green" : "Red");
            CreateDbButton.Visibility = r.ServerReachable && !r.DatabaseExists ? Visibility.Visible : Visibility.Collapsed;
            DbHelp.Visibility = !r.ServerReachable && r.Message.Contains("ติดต่อ PostgreSQL", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
            NextButton.IsEnabled = r.Ready;
        }
        finally { TestDbButton.IsEnabled = true; }
    }

    private async void OnCreateDb(object sender, RoutedEventArgs e)
    {
        CreateDbButton.IsEnabled = false;
        try
        {
            await GatewaySetup.CreateDatabaseAsync(CurrentDb);
            await TestDbAsync();
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or ArgumentException)
        {
            SetResult(DbResult, "✗ สร้างฐานข้อมูลไม่สำเร็จ: " + ex.Message + " (ผู้ใช้นี้อาจไม่มีสิทธิ์สร้างฐานข้อมูล — ใช้ postgres หรือให้ผู้ดูแลสร้างให้)", "Red");
        }
        finally { CreateDbButton.IsEnabled = true; }
    }

    private void OnLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ---- step 2 -------------------------------------------------------------------------------------------
    private void OnAdminPassChanged(object sender, RoutedEventArgs e)
    {
        if (PassHint is null) return;
        if (AdminPass.Password.Length == 0) { SetResult(PassHint, "ตั้งรหัสผ่านอะไรก็ได้ที่คุณจำได้", "Muted"); return; }
        if (AdminPass2.Password.Length == 0) { SetResult(PassHint, "พิมพ์รหัสผ่านอีกครั้งในช่องยืนยัน", "Muted"); return; }
        if (AdminPass.Password != AdminPass2.Password) { SetResult(PassHint, "รหัสผ่านทั้งสองช่องยังไม่ตรงกัน", "Orange"); return; }
        SetResult(PassHint, "✓ รหัสผ่านตรงกัน", "Green");
    }

    private bool ValidateAdmin(out string error)
    {
        error = "";
        if (HasAdmin) return true;
        var user = AdminUser.Text.Trim().ToLowerInvariant();
        if (!UserPattern.IsMatch(user)) error = "ชื่อผู้ใช้ต้องยาว 3-64 ตัว ใช้ a-z ตัวเลข . _ - เท่านั้น";
        else if (Ui.PasswordProblem(AdminPass.Password) is { } p) error = p;
        else if (AdminPass.Password != AdminPass2.Password) error = "รหัสผ่านทั้งสองช่องไม่ตรงกัน";
        return error.Length == 0;
    }

    // ---- step 3 -------------------------------------------------------------------------------------------
    private async void OnCheckSite(object sender, RoutedEventArgs e)
    {
        CheckSiteButton.IsEnabled = false; SetResult(SiteResult, "กำลังตรวจสอบ...", "Muted");
        try { var r = await GatewaySetup.CheckUpstreamAsync(Upstream.Text); SetResult(SiteResult, (r.Reachable ? "✓ " : "⚠ ") + r.Message, r.Reachable ? "Green" : "Orange"); }
        finally { CheckSiteButton.IsEnabled = true; }
    }

    private void OnPortChanged(object sender, TextChangedEventArgs e)
    {
        if (PortHint is null) return;
        PortHint.Text = int.TryParse(ListenPort.Text, out var p) && p is >= 1 and <= 65535
            ? $"ผู้ใช้จะเข้าเว็บผ่านที่อยู่ http://127.0.0.1:{p}/ (รับเฉพาะคำขอจากเครื่องนี้ในรุ่นนี้)"
            : "พอร์ตต้องเป็นตัวเลข 1-65535 (แนะนำ 5080)";
    }

    private bool ValidateSite(out string error)
    {
        error = "";
        if (!Uri.TryCreate(Upstream.Text.Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) error = "ที่อยู่เว็บจริงไม่ถูกต้อง (ตัวอย่าง http://localhost:8080/)";
        else if (!int.TryParse(ListenPort.Text, out var p) || p is < 1 or > 65535) error = "พอร์ตต้องเป็นตัวเลข 1-65535";
        return error.Length == 0;
    }

    // ---- step 4 + navigation ------------------------------------------------------------------------------
    private void BuildSummary()
    {
        var d = CurrentDb;
        Summary.Text = $"• ฐานข้อมูล: {d.Database} บน {d.Host}:{d.Port} (ผู้ใช้ {d.Username})\n" +
                       (HasAdmin ? "• บัญชีผู้ดูแล: ใช้บัญชีเดิมที่มีอยู่ในฐานข้อมูล\n" : $"• บัญชีผู้ดูแล: {AdminUser.Text.Trim().ToLowerInvariant()} (สร้างใหม่)\n") +
                       $"• เว็บที่ปกป้อง: {Upstream.Text.Trim()}\n" +
                       $"• ผู้ใช้เข้าผ่าน: http://127.0.0.1:{ListenPort.Text.Trim()}/";
        StartResult.Text = "";
    }

    private void OnBack(object sender, RoutedEventArgs e) { if (_step > 0) Show(_step - 1); }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case 0:
                if (_db?.Ready != true) { await TestDbAsync(); if (_db?.Ready != true) return; }
                Show(1); break;
            case 1:
                if (!ValidateAdmin(out var e2)) { SetResult(PassHint, e2, "Red"); return; }
                Show(2); break;
            case 2:
                if (!ValidateSite(out var e3)) { SetResult(SiteResult, e3, "Red"); return; }
                Show(3); break;
            default:
                await FinishAsync(); break;
        }
    }

    private async Task FinishAsync()
    {
        NextButton.IsEnabled = BackButton.IsEnabled = CancelButton.IsEnabled = false;
        SetResult(StartResult, "กำลังเตรียมฐานข้อมูลและเริ่มระบบป้องกัน...", "Muted");
        var settings = GatewaySettings.Create(CurrentDb, int.Parse(ListenPort.Text), Upstream.Text.Trim());
        var user = AdminUser.Text.Trim().ToLowerInvariant();
        try
        {
            await App.Gateway.StartAsync(settings, HasAdmin ? null : AdminPass.Password);
            SettingsStore.Save(settings);
            Settings = settings;
            if (!HasAdmin)
            {
                SetResult(StartResult, "ระบบพร้อมแล้ว กำลังเข้าสู่ระบบ...", "Green");
                var api = new ApiClient(settings.ListenUrl);
                await api.LoginAsync(user, AdminPass.Password);
                Api = api;
            }
            AdminPass.Clear(); AdminPass2.Clear(); DbPass.Clear();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            await App.Gateway.StopAsync();
            var msg = ex.Message;
            if (msg.Contains("address already in use", StringComparison.OrdinalIgnoreCase) || msg.Contains("Only one usage", StringComparison.OrdinalIgnoreCase))
                msg = $"พอร์ต {settings.ListenPort} ถูกโปรแกรมอื่นใช้อยู่แล้ว — กลับไปขั้นที่ 3 แล้วเลือกพอร์ตอื่น";
            SetResult(StartResult, "✗ เริ่มระบบไม่สำเร็จ: " + msg, "Red");
            NextButton.IsEnabled = BackButton.IsEnabled = CancelButton.IsEnabled = true;
        }
    }
}
