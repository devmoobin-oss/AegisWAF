using System.Windows;
using System.Windows.Controls;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class UsersView : UserControl, IPage
{
    private readonly ApiClient _api;

    public UsersView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        NRole.ItemsSource = RoleNames.All;
        NRole.SelectedItem = "VIEWER";
    }

    public bool AutoRefresh => false;

    private void Show(string text, bool ok) { Msg.Text = text; Msg.Foreground = Ui.Brush(ok ? "Green" : "Red"); }

    public async Task RefreshAsync()
    {
        try { Grid.ItemsSource = await _api.GetUsersAsync(); }
        catch (ApiException ex) when (ex.Status != 0) { Show(Ui.Describe(ex) + " (หน้านี้ใช้ได้เฉพาะ SUPER_ADMIN)", false); }
    }

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        try
        {
            await _api.CreateUserAsync(NName.Text.Trim(), NDisplay.Text.Trim(), NPass.Password, (string)NRole.SelectedItem);
            Show($"สร้างผู้ใช้ {NName.Text.Trim().ToLowerInvariant()} แล้ว (ต้องเปลี่ยนรหัสผ่านเมื่อเข้าสู่ระบบครั้งแรก)", true);
            NName.Clear(); NDisplay.Clear(); NPass.Clear();
            await RefreshAsync();
        }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
    }

    private async void OnRoleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || ((FrameworkElement)sender).DataContext is not UserDto user) return;
        var role = (string)e.AddedItems[0]!;
        if (role == user.Role) return; // initial binding, not a user change
        try { await _api.UpdateUserAsync(user.Id, role, null); Show($"เปลี่ยน Role ของ {user.Username} เป็น {role} แล้ว", true); }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
        await RefreshAsync();
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not UserDto user) return;
        var enable = !user.Enabled;
        if (!Ui.Confirm(Window.GetWindow(this), $"{(enable ? "เปิด" : "ปิด")}ใช้งานบัญชี {user.Username}?")) return;
        try { await _api.UpdateUserAsync(user.Id, null, enable); Show($"{(enable ? "เปิด" : "ปิด")}ใช้งาน {user.Username} แล้ว", true); }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
        await RefreshAsync();
    }

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not UserDto user) return;
        var dlg = new PromptWindow(Window.GetWindow(this)!, "รีเซ็ตรหัสผ่าน", $"รหัสผ่านชั่วคราวใหม่ของ {user.Username} — ผู้ใช้ต้องเปลี่ยนเมื่อเข้าสู่ระบบ และเซสชันเดิมทั้งหมดจะถูกยกเลิก");
        if (dlg.ShowDialog() != true) return;
        try { await _api.ResetPasswordAsync(user.Id, dlg.Value); Show($"รีเซ็ตรหัสผ่านของ {user.Username} แล้ว", true); }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
    }

    private async void OnRevoke(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not UserDto user) return;
        if (!Ui.Confirm(Window.GetWindow(this), $"ยกเลิกทุกเซสชันของ {user.Username}?")) return;
        try { await _api.RevokeSessionsAsync(user.Id); Show($"ยกเลิกเซสชันของ {user.Username} แล้ว", true); }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
    }
}
