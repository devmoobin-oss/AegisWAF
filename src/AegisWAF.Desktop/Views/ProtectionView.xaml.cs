using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

/// <summary>
/// "What does the system protect against?" — rendered from <see cref="ProtectionCatalog"/>, whose every example is verified against a
/// real gateway by the test suite. The "ลองทดสอบ" buttons run the live rule sandbox so the user can see the result on their own gateway.
/// </summary>
public partial class ProtectionView : UserControl, IPage
{
    private readonly ApiClient _api;
    private const bool _canTest = true;

    public ProtectionView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        Build();
    }

    public bool AutoRefresh => false;
    public Task RefreshAsync() => Task.CompletedTask;

    private static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private static (string Text, string Bg, string Fg) Chip(Coverage c) => c switch
    {
        Coverage.Covered => ("✓ ป้องกันได้", "#0F4B3D", "#2FE0A3"),
        Coverage.Partial => ("◐ ป้องกันได้บางส่วน", "#4A3410", "#FFB830"),
        _ => ("✗ ยังไม่ป้องกัน", "#4C2029", "#FF6878")
    };

    private static Border MakeChip(string text, string bg, string fg) => new()
    {
        Background = B(bg), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, Foreground = B(fg), FontWeight = FontWeights.SemiBold, FontSize = 12 }
    };

    private static TextBlock Text(string text, string? brushKey = null, double size = 13, Thickness? margin = null, FontWeight? weight = null) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, LineHeight = size * 1.7, Margin = margin ?? new Thickness(0),
        Foreground = brushKey is null ? Ui.Brush("Text") : Ui.Brush(brushKey), FontWeight = weight ?? FontWeights.Normal
    };

    private void Build()
    {
        var items = ProtectionCatalog.Items;
        Root.Children.Add(SummaryCard(items));
        foreach (var level in new[] { Coverage.Covered, Coverage.Partial, Coverage.NotCovered })
        {
            var group = items.Where(i => i.Coverage == level).ToList();
            if (group.Count == 0) continue;
            var (label, bg, fg) = Chip(level);
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 10, 0, 10) };
            heading.Children.Add(MakeChip(label, bg, fg));
            heading.Children.Add(new TextBlock { Text = $"  {group.Count} หัวข้อ", Foreground = Ui.Brush("Muted"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
            Root.Children.Add(heading);
            foreach (var item in group) Root.Children.Add(ItemCard(item));
        }
    }

    private Border SummaryCard(IReadOnlyList<ProtectionItem> items)
    {
        int Count(Coverage c) => items.Count(i => i.Coverage == c);
        var panel = new StackPanel();
        panel.Children.Add(Text("สรุปสั้นๆ", "Text", 17, weight: FontWeights.SemiBold));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 12) };
        foreach (var c in new[] { Coverage.Covered, Coverage.Partial, Coverage.NotCovered })
        {
            var (label, bg, fg) = Chip(c);
            var chip = MakeChip($"{label}  {Count(c)}", bg, fg); chip.Margin = new Thickness(0, 0, 10, 0); chips.Children.Add(chip);
        }
        panel.Children.Add(chips);
        panel.Children.Add(Text("ระบบนี้เป็นการป้องกันชั้นแรกแบบเรียบง่าย ตรวจข้อความที่ส่งมาด้วยลายเซ็น (เช่น sleep( , <script, ../) แล้วปฏิเสธคำขอที่ตรง รวมถึงบล็อก IP และจำกัดความถี่ ไม่ใช่ WAF เต็มรูปแบบ และยังไม่ผ่านการทดสอบเจาะระบบโดยผู้เชี่ยวชาญอิสระ", "Muted", 13));
        var warn = new Border
        {
            Background = B("#2A2410"), BorderBrush = B("#6B4D14"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 12, 0, 0),
            Child = Text("⚠ จุดที่ควรรู้ที่สุด: ระบบตรวจข้อความ \"ดิบ\" โดยไม่ถอดรหัส %xx เบราว์เซอร์เข้ารหัสช่องว่างเป็น %20 และ < เป็น %3C ทำให้การโจมตีผ่าน URL จากเบราว์เซอร์จริงจำนวนมากหลุด และ header, cookie, text/plain, multipart และคำขอแบบ chunked ไม่ถูกตรวจเลย ดูตัวอย่างจริงในแต่ละหัวข้อด้านล่าง", "Orange", 12.5)
        };
        panel.Children.Add(warn);
        panel.Children.Add(Text("ทุกตัวอย่างในหน้านี้ถูกรันกับ Gateway จริงโดยชุดเทสต์อัตโนมัติ (เมื่อโค้ดเปลี่ยน เทสต์จะล้มจนกว่าหน้านี้จะแก้ให้ตรง) รายละเอียดฉบับเต็ม: docs\\PROTECTION.md ใช้ปุ่ม ลองทดสอบ เพื่อดูผลบน Gateway ของคุณเอง", "Muted", 12, new Thickness(0, 12, 0, 0)));
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Child = panel, Margin = new Thickness(0, 0, 0, 6) };
    }

    private Border ItemCard(ProtectionItem item)
    {
        var (label, bg, fg) = Chip(item.Coverage);
        var panel = new StackPanel();
        var head = new DockPanel();
        var chip = MakeChip(label, bg, fg); DockPanel.SetDock(chip, Dock.Right); head.Children.Add(chip);
        head.Children.Add(Text(item.Title, "Text", 15.5, weight: FontWeights.SemiBold));
        panel.Children.Add(head);

        panel.Children.Add(Text(item.Coverage == Coverage.NotCovered ? "สถานะ" : "ตรวจพบ / ทำได้", "Muted", 11.5, new Thickness(0, 12, 0, 2)));
        panel.Children.Add(Text(item.Summary, "Text", 13));
        panel.Children.Add(Text(item.Coverage == Coverage.NotCovered ? "ควรทำอย่างไร" : "ข้อจำกัด / หลุดได้เมื่อ", "Muted", 11.5, new Thickness(0, 10, 0, 2)));
        panel.Children.Add(Text(item.Limits, item.Coverage == Coverage.Covered ? "Text" : "Orange", 13));

        if (item.Examples.Count > 0)
        {
            var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var ex in item.Examples) list.Children.Add(ExampleRow(ex));
            panel.Children.Add(new Expander
            {
                Header = $"ดูตัวอย่าง ({item.Examples.Count})", Foreground = Ui.Brush("Blue"), Margin = new Thickness(0, 12, 0, 0), Content = list
            });
        }
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Child = panel, Margin = new Thickness(0, 0, 0, 12) };
    }

    private UIElement ExampleRow(ProtectionExample ex)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(Text(ex.Label, "Text", 12.5));
        var code = ex.Request.Body is { Length: <= 140 } b ? $"{ex.Request.Method} {ex.Request.Target}   {b}" :
                   ex.Request.Headers is { Count: > 0 } h ? $"{ex.Request.Method} {ex.Request.Target}   {string.Join("; ", h.Select(kv => kv.Key + ": " + kv.Value))}" :
                   ex.Request.Body is not null ? $"{ex.Request.Method} {ex.Request.Target}   ({ex.Request.ContentType}, {ex.Request.Body.Length:N0} ตัวอักษร)" :
                   $"{ex.Request.Method} {ex.Request.Target}";
        if (ex.Request.Chunked) code += "   [chunked]";
        var codeBox = new TextBlock { Text = code, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Foreground = Ui.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        left.Children.Add(codeBox);
        grid.Children.Add(left);

        var result = ex.ExpectBlocked ? MakeChip("ถูกบล็อก", "#4C2029", "#FF6878") : MakeChip("หลุด (ผ่านไปถึงเว็บ)", "#4A3410", "#FFB830");
        result.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(result, 1); grid.Children.Add(result);

        if (ex.SandboxText is not null)
        {
            var status = new TextBlock { FontSize = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 230 };
            var button = new Button { Content = "ลองทดสอบ", Style = (Style)Application.Current.FindResource("Ghost"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(12, 0, 0, 0), IsEnabled = _canTest,
                ToolTip = _canTest ? "ส่งข้อความนี้เข้า Rule Sandbox ของ Gateway (ไม่ส่งไปเว็บจริง)" : "ต้องใช้สิทธิ์นักวิเคราะห์ (SECURITY_ANALYST) ขึ้นไป" };
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false; status.Text = "กำลังทดสอบ..."; status.Foreground = Ui.Brush("Muted");
                try
                {
                    var r = await _api.TestRuleAsync(ex.SandboxText);
                    var matches = r.Matched == ex.ExpectBlocked;
                    status.Text = (r.Matched ? "Gateway ของคุณ: บล็อก" : "Gateway ของคุณ: ปล่อยผ่าน") + (matches ? " ✓ ตรงกับที่ระบุ" : " ⚠ ต่างจากที่ระบุ (กฎอาจถูกปิดหรือแก้ไข)");
                    status.Foreground = Ui.Brush(matches ? "Green" : "Orange");
                }
                catch (ApiException e) { status.Text = Ui.Describe(e); status.Foreground = Ui.Brush("Red"); }
                finally { button.IsEnabled = _canTest; }
            };
            var holder = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            holder.Children.Add(button); holder.Children.Add(status);
            Grid.SetColumn(holder, 2); grid.Children.Add(holder);
        }
        return new Border { BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 2), Child = grid };
    }
}
