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
        Form.IsEnabled = CanEdit;
    }

    public bool CanEdit => true;
    public bool AutoRefresh => false;

    private void Show(string text, bool ok) { Msg.Text = text; Msg.Foreground = Ui.Brush(ok ? "Green" : "Red"); }

    public async Task RefreshAsync()
    {
        try { Grid.ItemsSource = await _api.GetIpPoliciesAsync(); }
        catch (ApiException ex) when (ex.Status != 0) { Show(Ui.Describe(ex), false); }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var policy = IpBox.Text.Trim();
        var parts = policy.Split('/', 2, StringSplitOptions.TrimEntries);
        var valid = IPAddress.TryParse(parts[0], out var ip) &&
            (parts.Length == 1 || (int.TryParse(parts[1], out var prefix) && prefix >= 0 && prefix <= ip.GetAddressBytes().Length * 8));
        if (!valid) { Show("IP/CIDR ไม่ถูกต้อง (เช่น 192.0.2.10 หรือ 192.0.2.0/24)", false); return; }
        try
        {
            await _api.SaveIpPolicyAsync(policy, ((ComboBoxItem)ActionBox.SelectedItem).Content.ToString()!, ReasonBox.Text.Trim());
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
