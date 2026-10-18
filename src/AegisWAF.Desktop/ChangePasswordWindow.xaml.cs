using System.Windows;
using AegisWAF.Client;

namespace AegisWAF.Desktop;

public partial class ChangePasswordWindow : Window
{
    private readonly ApiClient _api;

    public ChangePasswordWindow(ApiClient api, bool forced)
    {
        InitializeComponent();
        _api = api;
        Note.Text = forced ? "บัญชีนี้ต้องตั้งรหัสผ่านใหม่ก่อนใช้งาน" : "เซสชันอื่นของคุณจะถูกยกเลิกหลังเปลี่ยนรหัสผ่าน";
        if (forced) CancelButton.Content = "ออก";
        Loaded += (_, _) => CurrentBox.Focus();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (NewBox.Password != ConfirmBox.Password) { ErrorText.Text = "รหัสผ่านใหม่ไม่ตรงกัน"; return; }
        SaveButton.IsEnabled = false;
        try
        {
            await _api.ChangePasswordAsync(CurrentBox.Password, NewBox.Password);
            DialogResult = true;
        }
        catch (ApiException ex) { ErrorText.Text = ex.Message; }
        finally { SaveButton.IsEnabled = true; CurrentBox.Clear(); NewBox.Clear(); ConfirmBox.Clear(); }
    }
}
