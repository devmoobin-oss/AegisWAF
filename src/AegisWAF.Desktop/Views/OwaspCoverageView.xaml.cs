using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class OwaspCoverageView : UserControl, IPage
{
    private readonly ApiClient _api;
    private List<OwaspCoverageDto> _items = [];
    private ComboBox? _filter;

    public OwaspCoverageView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
    }

    public bool AutoRefresh => false;

    public async Task RefreshAsync()
    {
        try
        {
            _items = await _api.GetOwaspCoverageAsync();
            Render();
        }
        catch (ApiException ex)
        {
            Root.Children.Clear();
            Root.Children.Add(Card(new TextBlock { Text = "ไม่สามารถโหลด OWASP coverage ได้: " + ex.Message, Foreground = Ui.Brush("Red"), TextWrapping = TextWrapping.Wrap }));
        }
    }

    private void Render()
    {
        Root.Children.Clear();
        Root.Children.Add(Card(new StackPanel
        {
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = "\uE72E", FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = Ui.Brush("Cyan"), FontSize = 22, Margin = new Thickness(0, 0, 8, 0) }, new TextBlock { Text = "OWASP Top 10 Coverage", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Text") } } },
                new TextBlock { Text = "แผนที่ความครอบคลุมของกลไกป้องกัน AegisWAF ตาม OWASP Top 10:2021", Foreground = Ui.Brush("Muted"), FontSize = 13, Margin = new Thickness(0, 6, 0, 12), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "ไอคอนโล่บอกหมวดที่กฎช่วยป้องกัน — ไม่ใช่การรับรองความปลอดภัยครบถ้วน และไม่ทดแทน secure coding หรือ outbound allowlist", Foreground = Ui.Brush("Orange"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap }
            }
        }));

        _filter = new ComboBox { Width = 260, Height = 38, Margin = new Thickness(0, 8, 0, 14), ToolTip = "กรองหมวด OWASP" };
        _filter.Items.Add(new ComboBoxItem { Content = "ทุกหมวด OWASP", Tag = "all", IsSelected = true });
        foreach (var item in _items) _filter.Items.Add(new ComboBoxItem { Content = $"{item.Code} — {item.Title}", Tag = item.Code });
        _filter.SelectionChanged += (_, _) => RenderCards();
        Root.Children.Add(_filter);
        RenderCards();
    }

    private void RenderCards()
    {
        if (_filter is null) return;
        while (Root.Children.Count > 2) Root.Children.RemoveAt(2);
        var selected = (_filter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 16) };
        foreach (var item in _items.Where(x => selected == "all" || x.Code == selected)) grid.Children.Add(CoverageCard(item));
        Root.Children.Add(grid);
    }

    private Border CoverageCard(OwaspCoverageDto item)
    {
        var covered = item.Coverage.Equals("Covered", StringComparison.OrdinalIgnoreCase);
        var partial = item.Coverage.Equals("Partial", StringComparison.OrdinalIgnoreCase);
        var color = covered ? "Green" : partial ? "Orange" : "Red";
        var badge = new Border { Background = B(color == "Green" ? "#0F4B3D" : color == "Orange" ? "#4A3410" : "#4C2029"), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = "\uE72E", FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = Ui.Brush(color), FontSize = 13, Margin = new Thickness(0, 0, 5, 0) }, new TextBlock { Text = $"{item.Code} · {item.Coverage}", Foreground = Ui.Brush(color), FontSize = 11.5, FontWeight = FontWeights.SemiBold } } } };
        var panel = new StackPanel();
        panel.Children.Add(badge);
        panel.Children.Add(new TextBlock { Text = item.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Text"), Margin = new Thickness(0, 12, 0, 6), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Text(item.Description, "Text"));
        panel.Children.Add(Text("ข้อจำกัด: " + item.Limitations, "Muted"));
        panel.Children.Add(Text("Rules: " + (item.RuleIds.Length == 0 ? "ยังไม่มี rule mapping" : string.Join(", ", item.RuleIds)), "Blue"));
        return Card(panel);
    }

    private static TextBlock Text(string value, string brush) => new() { Text = value, Foreground = Ui.Brush(brush), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 4, 0, 0) };

    private static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private static Border Card(UIElement child) => new() { Style = (Style)Application.Current.FindResource("Card"), Child = child, Margin = new Thickness(0, 0, 12, 12) };
}
