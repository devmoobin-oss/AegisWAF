using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
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
                new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = "\uE72E", FontFamily = (FontFamily)FindResource("IconFont"), Foreground = Ui.Brush("Blue"), FontSize = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = "OWASP Top 10 Coverage", FontSize = 20, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("SignFont"), Foreground = Ui.Brush("Text") } } },
                new TextBlock { Text = "แผนที่ความครอบคลุมของกลไกป้องกัน AegisWAF ตาม OWASP Top 10:2021", Foreground = Ui.Brush("Muted"), FontSize = 13, Margin = new Thickness(0, 6, 0, 12), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "วงสถานีบอกระดับที่กฎของ AegisWAF ช่วยได้ในแต่ละหมวด (เต็ม = ครอบคลุม, ครึ่ง = บางส่วน, กลวง = อยู่นอกขอบเขต WAF) — ไม่ใช่การรับรองความปลอดภัยครบถ้วน และไม่ทดแทน secure coding หรือ outbound allowlist", Foreground = Ui.Brush("Orange"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap }
            }
        }));

        _filter = new ComboBox { Width = 340, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 16), ToolTip = "กรองหมวด OWASP" };
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
        // One ruled list, one row per category: a status station, code + title, then detail and rule chips.
        var list = new StackPanel();
        var rows = _items.Where(x => selected == "all" || x.Code == selected).ToList();
        for (var i = 0; i < rows.Count; i++) list.Children.Add(Row(rows[i], last: i == rows.Count - 1));
        Root.Children.Add(new Border { Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(24, 6, 24, 6), Child = list, Margin = new Thickness(0, 0, 12, 16) });
    }

    /// <summary>Station ring: filled = on the line (covered), half = partly, hollow = not on the line. No red: a gap is a fact, not an alarm.</summary>
    private static UIElement Station(string coverage)
    {
        var covered = coverage.Equals("Covered", StringComparison.OrdinalIgnoreCase);
        var partial = coverage.Equals("Partial", StringComparison.OrdinalIgnoreCase);
        var g = new Grid { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        var ink = covered ? Ui.Brush("LineGreen") : partial ? Ui.Brush("Amber") : Ui.Brush("Slate");
        g.Children.Add(new Ellipse { Stroke = ink, StrokeThickness = 3, Fill = covered ? ink : Brushes.Transparent });
        if (partial)
        {
            var half = new Path { Fill = ink, Data = Geometry.Parse("M11,1 A10,10 0 0 0 11,21 Z") };
            g.Children.Add(half);
        }
        return g;
    }

    private static string CoverageText(string c) => c.ToLowerInvariant() switch
    {
        "covered" => "ครอบคลุม",
        "partial" => "ครอบคลุมบางส่วน",
        _ => "ไม่อยู่ในสาย (นอกขอบเขต WAF)"
    };

    private UIElement Row(OwaspCoverageDto item, bool last)
    {
        var grid = new Grid { Margin = new Thickness(0, 16, 0, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Station(item.Coverage));

        var body = new StackPanel();
        var head = new DockPanel();
        var state = new TextBlock { Text = CoverageText(item.Coverage), Foreground = Ui.Brush("Muted"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(state, Dock.Right); head.Children.Add(state);
        var title = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap, FontFamily = (FontFamily)Application.Current.FindResource("SignFont") };
        title.Inlines.Add(new System.Windows.Documents.Run(item.Code + "   ") { FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Muted") });
        title.Inlines.Add(new System.Windows.Documents.Run(item.Title) { FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Text") });
        head.Children.Add(title);
        body.Children.Add(head);
        body.Children.Add(Text(item.Description, "Text"));
        body.Children.Add(Text("ข้อจำกัด: " + item.Limitations, "Muted"));
        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (item.RuleIds.Length == 0) chips.Children.Add(new TextBlock { Text = "ยังไม่มีกฎที่แมปกับหมวดนี้", Foreground = Ui.Brush("Faint"), FontSize = 12.5 });
        foreach (var id in item.RuleIds)
            chips.Children.Add(new Border { BorderBrush = Ui.Brush("Line2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock { Text = id, FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"), FontSize = 12, Foreground = Ui.Brush("Blue") } });
        body.Children.Add(chips);
        Grid.SetColumn(body, 1); grid.Children.Add(body);
        return new Border { BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1), Child = grid };
    }

    private static TextBlock Text(string value, string brush) => new() { Text = value, Foreground = Ui.Brush(brush), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 4, 0, 0) };

    private static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private static Border Card(UIElement child) => new() { Style = (Style)Application.Current.FindResource("Card"), Child = child, Margin = new Thickness(0, 0, 12, 12) };
}
