using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class RulesView : UserControl, IPage
{
    private static readonly Regex IdPattern = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);
    private readonly ApiClient _api;

    public RulesView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        CustomForm.IsEnabled = CanEdit;
        TestButton.IsEnabled = Sample.IsEnabled = true;
    }

    public bool CanEdit => true;
    public bool AutoRefresh => false;

    private void Show(string text, bool ok) { Msg.Text = text; Msg.Foreground = Ui.Brush(ok ? "Green" : "Red"); }

    public async Task RefreshAsync()
    {
        try
        {
            RulesGrid.ItemsSource = await _api.GetRulesAsync();
            CustomGrid.ItemsSource = await _api.GetCustomRulesAsync();
        }
        catch (ApiException ex) when (ex.Status != 0) { Show(Ui.Describe(ex), false); }
    }

    private async void OnToggleRule(object sender, RoutedEventArgs e)
    {
        var box = (CheckBox)sender;
        if (box.DataContext is not RuleDto rule) return;
        var want = box.IsChecked == true;
        try { await _api.SetRuleEnabledAsync(rule.Id, want); Show($"{rule.Id}: {(want ? "เปิด" : "ปิด")}ใช้งานแล้ว", true); await RefreshAsync(); }
        catch (ApiException ex) { box.IsChecked = !want; Show(Ui.Describe(ex), false); }
    }

    private async void OnSaveCustom(object sender, RoutedEventArgs e)
    {
        var id = CId.Text.Trim();
        if (!IdPattern.IsMatch(id)) { Show("Rule ID ต้องเป็น a-z, 0-9, _ หรือ - (1-64 ตัวอักษร)", false); return; }
        if (!int.TryParse(CPriority.Text, out var pr) || pr is < 1 or > 10000) { Show("Priority ต้องเป็นจำนวนเต็ม 1-10000", false); return; }
        if (CName.Text.Trim().Length == 0 || CPattern.Text.Length == 0) { Show("กรอกชื่อกฎและ Regex pattern", false); return; }
        try
        {
            await _api.SaveCustomRuleAsync(new CustomRuleDto(id, CName.Text.Trim(), CPattern.Text, pr, true));
            Show("บันทึกกฎแล้ว", true); CId.Clear(); CName.Clear(); CPattern.Clear();
            await RefreshAsync();
        }
        catch (ApiException ex) { Show(ex.Status == 400 ? "Regex ไม่ถูกต้องหรือไม่รองรับ (NonBacktracking) หรือพารามิเตอร์ไม่ถูกต้อง" : Ui.Describe(ex), false); }
    }

    private async void OnDeleteCustom(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not CustomRuleDto rule) return;
        if (!Ui.Confirm(Window.GetWindow(this), $"ลบกฎ {rule.Id}?")) return;
        try { await _api.DeleteCustomRuleAsync(rule.Id); Show("ลบแล้ว", true); await RefreshAsync(); }
        catch (ApiException ex) { Show(Ui.Describe(ex), false); }
    }

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        try
        {
            var r = await _api.TestRuleAsync(Sample.Text);
            TestResult.Text = r.Matched ? $"BLOCK — {r.Rule}" : "ALLOW — ไม่พบกฎที่ตรงกัน";
            TestResult.Foreground = Ui.Brush(r.Matched ? "Red" : "Green");
        }
        catch (ApiException ex) { TestResult.Text = Ui.Describe(ex); TestResult.Foreground = Ui.Brush("Red"); }
    }
}
