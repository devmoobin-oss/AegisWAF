using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AegisWAF.Client;

namespace AegisWAF.Desktop.Views;

public partial class DashboardView : UserControl, IPage
{
    private static readonly string[] Palette = ["#FF5A6C", "#39AEFC", "#38D3B5", "#8266FA", "#E9B85B", "#BC87D7"];
    private readonly ApiClient _api;
    private OverviewDto? _last;

    public DashboardView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        var info = App.Info;
        var url = (info?.Url ?? api.BaseAddress)?.GetLeftPart(UriPartial.Authority) + "/";
        if (info is { Embedded: false })
        {
            BannerTitle.Text = "เชื่อมต่อกับ Gateway ที่รันอยู่เครื่องอื่น";
            BannerSite.Text = "";
            BannerUrl.Text = "ที่อยู่ Gateway: " + url;
        }
        else
        {
            BannerSite.Text = "เว็บที่ปกป้อง: " + (info?.Upstream ?? "—");
            BannerUrl.Text = "ให้ผู้ใช้เข้าเว็บผ่านที่อยู่: " + url;
        }
        BannerUrl.Foreground = Ui.Brush("Blue");
        _url = url;
    }

    private readonly string _url;

    private void OnCopyUrl(object sender, RoutedEventArgs e) { try { Clipboard.SetText(_url); SelfTestResult.Text = "คัดลอก " + _url + " แล้ว"; SelfTestResult.Foreground = Ui.Brush("Muted"); } catch (System.Runtime.InteropServices.COMException) { } }

    private void OnOpenUrl(object sender, RoutedEventArgs e) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_url) { UseShellExecute = true });

    private async void OnSelfTest(object sender, RoutedEventArgs e)
    {
        SelfTestButton.IsEnabled = false;
        SelfTestResult.Text = "กำลังส่งคำขอโจมตีจำลอง..."; SelfTestResult.Foreground = Ui.Brush("Muted");
        try
        {
            var (blocked, status) = await _api.RunSelfTestAsync();
            if (blocked) { SelfTestResult.Text = "✓ ระบบป้องกันทำงาน — คำขอโจมตีจำลองถูกบล็อก (HTTP 403) และไม่ถึงเว็บของคุณ"; SelfTestResult.Foreground = Ui.Brush("Green"); }
            else { SelfTestResult.Text = $"⚠ คำขอโจมตีจำลองไม่ถูกบล็อก (HTTP {status}) — ตรวจสอบว่ากฎ SQL Injection เปิดอยู่ที่เมนู กฎการป้องกัน หรือ IP นี้อยู่ในรายการอนุญาต"; SelfTestResult.Foreground = Ui.Brush("Orange"); }
            await RefreshAsync();
        }
        catch (ApiException ex) { SelfTestResult.Text = "✗ " + Ui.Describe(ex); SelfTestResult.Foreground = Ui.Brush("Red"); }
        finally { SelfTestButton.IsEnabled = true; }
    }

    public bool AutoRefresh => true;

    private sealed record IpRow(int Rank, string Ip, long Blocked, string Share);

    public async Task RefreshAsync()
    {
        try { Render(await _api.GetOverviewAsync()); }
        catch (ApiException ex) when (ex.Status != 0) { KStatus.Text = "ERROR"; KDropped.Text = Ui.Describe(ex); }
    }

    private static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private void Render(OverviewDto d)
    {
        _last = d;
        EmptyGuide.Visibility = d.TotalRequests == 0 ? Visibility.Visible : Visibility.Collapsed;
        KTotal.Text = d.TotalRequests.ToString("N0"); KBlocked.Text = d.BlockedRequests.ToString("N0"); KAllowed.Text = d.AllowedRequests.ToString("N0");
        KRules.Text = d.ActiveRules.ToString("N0"); KStatus.Text = "ONLINE"; KDropped.Text = "Dropped events: " + d.DroppedEvents.ToString("N0");
        KBlockedPct.Text = Ui.Pct(d.BlockedRequests, d.TotalRequests); KAllowedPct.Text = Ui.Pct(d.AllowedRequests, d.TotalRequests);

        DrawChart(d.Timeline);
        DrawDonut(d.ThreatCategories, d.BlockedRequests);

        MethodList.Children.Clear();
        var mSum = d.Methods.Sum(m => m.Count); var mMax = Math.Max(1, d.Methods.Select(m => m.Count).DefaultIfEmpty(1).Max());
        if (d.Methods.Count == 0) MethodList.Children.Add(new TextBlock { Text = "ยังไม่มีข้อมูลคำขอ", Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 20, 0, 0) });
        var i = 0;
        foreach (var m in d.Methods.Take(7))
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            row.Children.Add(new TextBlock { Text = m.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var track = new Border { Background = B("#12263D"), Height = 12, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0) };
            var bar = new Border { Background = B(Palette[(i + 1) % Palette.Length]), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) };
            var frac = (double)m.Count / mMax;
            track.Child = bar;
            track.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * frac;
            Grid.SetColumn(track, 1); row.Children.Add(track);
            var pct = new TextBlock { Text = Ui.Pct(m.Count, mSum), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, Foreground = B("#C3D3E6") };
            Grid.SetColumn(pct, 2); row.Children.Add(pct);
            MethodList.Children.Add(row); i++;
        }

        EventsGrid.ItemsSource = d.RecentEvents.Take(8).ToList();
        EventsGrid.Visibility = d.RecentEvents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EventsEmpty.Visibility = d.RecentEvents.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        var ips = d.TopIps.Select((x, n) => new IpRow(n + 1, x.Ip, x.Blocked, Ui.Pct(x.Blocked, d.BlockedRequests))).ToList();
        IpGrid.ItemsSource = ips;
        IpGrid.Visibility = ips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        IpEmpty.Visibility = ips.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnChartResized(object sender, SizeChangedEventArgs e) { if (_last is not null) DrawChart(_last.Timeline); }

    private static double NiceStep(double v)
    {
        var p = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(v, 1))));
        foreach (var m in new[] { 1, 2, 5, 10 }) if (m * p >= v) return m * p;
        return 10 * p;
    }

    private void DrawChart(List<TimelinePoint> pts)
    {
        Chart.Children.Clear();
        double w = Chart.ActualWidth, h = Chart.ActualHeight;
        if (w < 80 || h < 60 || pts.Count < 2) return;
        const double pl = 46, pr = 10, pt = 8, pb = 26;
        var peak = pts.Max(p => p.Requests);
        var step = NiceStep(Math.Max(peak, 4) / 4.0); var max = step * 4;
        double X(int i) => pl + i * (w - pl - pr) / (pts.Count - 1);
        double Y(double v) => pt + (h - pt - pb) * (1 - v / max);

        for (var k = 0; k <= 4; k++)
        {
            var y = Y(k * step);
            Chart.Children.Add(new Line { X1 = pl, X2 = w - pr, Y1 = y, Y2 = y, Stroke = B("#16304E"), StrokeThickness = 1 });
            var label = new TextBlock { Text = Ui.Compact((long)(k * step)), FontSize = 11, Foreground = B("#8AA3C0"), Width = pl - 8, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(label, 0); Canvas.SetTop(label, y - 8); Chart.Children.Add(label);
        }
        var every = w < 520 ? 4 : 2;
        for (var i = 0; i < pts.Count; i += every)
        {
            var label = new TextBlock { Text = pts[i].Time, FontSize = 11, Foreground = B("#8AA3C0"), Width = 44, TextAlignment = TextAlignment.Center };
            Canvas.SetLeft(label, X(i) - 22); Canvas.SetTop(label, h - pb + 6); Chart.Children.Add(label);
        }

        void Series(Func<TimelinePoint, long> sel, string color)
        {
            var line = new PointCollection(pts.Select((p, i) => new Point(X(i), Y(sel(p)))));
            var area = new PointCollection(line) { new Point(X(pts.Count - 1), Y(0)), new Point(X(0), Y(0)) };
            var c = (Color)ColorConverter.ConvertFromString(color);
            var fill = new LinearGradientBrush(Color.FromArgb(110, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B), 90);
            Chart.Children.Add(new Polygon { Points = area, Fill = fill });
            Chart.Children.Add(new Polyline { Points = line, Stroke = new SolidColorBrush(c), StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Round });
        }
        Series(p => p.Requests, "#2F9BFF");
        Series(p => p.Blocked, "#FF4D5E");

        if (peak == 0)
        {
            var t = new TextBlock { Text = "ยังไม่มีทราฟฟิกใน 24 ชั่วโมงที่ผ่านมา", Foreground = B("#7E97B3"), FontSize = 13 };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, (w - t.DesiredSize.Width) / 2); Canvas.SetTop(t, h / 2 - 20); Chart.Children.Add(t);
        }
    }

    private void DrawDonut(List<NameCount> cats, long blocked)
    {
        Donut.Children.Clear(); ThreatList.Children.Clear();
        DonutTotal.Text = blocked.ToString("N0");
        const double size = 176, thick = 28, r = (size - thick) / 2, c = size / 2;
        var sum = cats.Sum(x => x.Count);
        if (sum == 0)
        {
            Donut.Children.Add(new Ellipse { Width = size - thick, Height = size - thick, Stroke = B("#22374F"), StrokeThickness = thick, Margin = new Thickness(thick / 2) });
            ThreatList.Children.Add(new TextBlock { Text = "ยังไม่มีเหตุการณ์โจมตี", Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 60, 0, 0), TextWrapping = TextWrapping.Wrap });
            return;
        }
        double angle = -90; var idx = 0;
        foreach (var cat in cats)
        {
            var color = Palette[idx % Palette.Length];
            var sweep = 360.0 * cat.Count / sum;
            if (cats.Count == 1)
                Donut.Children.Add(new Ellipse { Width = size - thick, Height = size - thick, Stroke = B(color), StrokeThickness = thick, Margin = new Thickness(thick / 2) });
            else
            {
                Point P(double a) => new(c + r * Math.Cos(a * Math.PI / 180), c + r * Math.Sin(a * Math.PI / 180));
                var fig = new PathFigure { StartPoint = P(angle), IsClosed = false };
                fig.Segments.Add(new ArcSegment(P(angle + sweep - 0.4), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
                Donut.Children.Add(new Path { Stroke = B(color), StrokeThickness = thick, Data = new PathGeometry([fig]) });
            }
            angle += sweep;
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = B(color), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top });
            name.Children.Add(new TextBlock { Text = cat.Name, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 120, Foreground = B("#C0D0E3") });
            row.Children.Add(name);
            var val = new TextBlock { Text = $"{cat.Count:N0}  ({Ui.Pct(cat.Count, sum)})", FontSize = 12, Foreground = B("#8AA3C0"), TextAlignment = TextAlignment.Right, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(val, 1); row.Children.Add(val);
            ThreatList.Children.Add(row); idx++;
        }
    }
}
