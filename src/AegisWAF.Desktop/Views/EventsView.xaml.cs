using System.Net;
using System.Windows;
using System.Windows.Controls;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class EventsView : UserControl, IPage
{
    private readonly ApiClient _api;
    private List<SecurityEventDto> _all = [];

    public EventsView(ApiClient api) { InitializeComponent(); _api = api; }

    public bool CanBlock => true;

    private async void OnBlockIp(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not SecurityEventDto ev) return;
        var self = IPAddress.TryParse(ev.Ip, out var ip) && IPAddress.IsLoopback(ip);
        var msg = $"บล็อก IP {ev.Ip} ถาวร?{Environment.NewLine}คำขอทุกอย่างจาก IP นี้จะถูกปฏิเสธ (ยกเลิกได้ที่เมนู IP ที่บล็อก / อนุญาต)";
        if (self) msg += Environment.NewLine + Environment.NewLine + "⚠ นี่คือเครื่องของคุณเอง — ถ้าบล็อก คุณจะเข้าเว็บผ่านระบบนี้จากเครื่องนี้ไม่ได้";
        if (!Ui.Confirm(Window.GetWindow(this), msg)) return;
        try { await _api.SaveIpPolicyAsync(ev.Ip, "block", "บล็อกจากหน้าเหตุการณ์: " + ev.Rule); CountText.Text = $"บล็อก {ev.Ip} แล้ว"; }
        catch (ApiException ex) { CountText.Text = Ui.Describe(ex); }
    }

    public bool AutoRefresh => true;

    public async Task RefreshAsync()
    {
        try { _all = (await _api.GetOverviewAsync()).RecentEvents; Apply(); }
        catch (ApiException ex) when (ex.Status != 0) { CountText.Text = Ui.Describe(ex); }
    }

    private void OnFilter(object sender, TextChangedEventArgs e) => Apply();

    private void Apply()
    {
        var t = Filter.Text.Trim();
        var list = t.Length == 0 ? _all : _all.Where(x => new[] { x.Rule, x.Ip, x.Path, x.Method }.Any(v => v.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
        Grid.ItemsSource = list;
        CountText.Text = list.Count == 0 ? (t.Length > 0 ? "ไม่พบเหตุการณ์ที่ตรงกัน" : "ยังไม่มีเหตุการณ์") : $"{list.Count} รายการ";
    }
}
