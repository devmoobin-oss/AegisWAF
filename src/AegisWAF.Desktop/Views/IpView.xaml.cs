using System.Net;
using System.Windows;
using System.Windows.Controls;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class IpView : UserControl, IPage
{
    private readonly ApiClient _api;

    public IpView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        // Controls mirror the server's rule (SECURITY_OPERATOR+); the server enforces it regardless.
        CanEdit = api.Session!.Level >= 3;
        Form.IsEnabled = CanEdit;
        if (!Form.IsEnabled) Msg.Text = "สิทธิ์ของคุณดูข้อมูลได้อย่างเดียว (ต้องใช้ SECURITY_OPERATOR ขึ้นไปเพื่อแก้ไข)";
    }

    public bool CanEdit { get; }
    public bool AutoRefresh => false;

    private void Show(string text, bool ok) { Msg.Text = text; Msg.Foreground = Ui.Brush(ok ? "Green" : "Red"); }

    public async Task RefreshAsync()
    {
        try { Grid.ItemsSource = await _api.GetIpPoliciesAsync(); }
        catch (ApiException ex) when (ex.Status != 0) { Show(Ui.Describe(ex), false); }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (!IPAddress.TryParse(IpBox.Text.Trim(), out var ip)) { Show("IP ไม่ถูกต้อง (รองรับ IPv4 / IPv6 เดี่ยว)", false); return; }
        try
        {
            await _api.SaveIpPolicyAsync(ip.ToString(), ((ComboBoxItem)ActionBox.SelectedItem).Content.ToString()!, ReasonBox.Text.Trim());
            Show("บันทึกแล้ว", true); IpBox.Clear(); ReasonBox.Clear();
            await RefreshAsync();
        }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not IpPolicyDto row) return;
        if (!Ui.Confirm(Window.GetWindow(this), $"ลบนโยบายของ {row.Ip}?")) return;
        try { await _api.DeleteIpPolicyAsync(row.Ip); Show("ลบแล้ว", true); await RefreshAsync(); }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
    }
}
