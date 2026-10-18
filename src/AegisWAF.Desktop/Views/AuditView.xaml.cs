using System.Windows.Controls;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class AuditView : UserControl, IPage
{
    private readonly ApiClient _api;

    public AuditView(ApiClient api) { InitializeComponent(); _api = api; }

    public bool AutoRefresh => false;

    public async Task RefreshAsync()
    {
        try { Grid.ItemsSource = await _api.GetAuditAsync(); }
        catch (ApiException ex) when (ex.Status != 0) { Note.Text = Ui.Describe(ex) + " (ต้องใช้สิทธิ์ SECURITY_ANALYST ขึ้นไป)"; }
    }
}
