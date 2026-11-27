using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Views;

/// <summary>
/// 功 能： 轻量电量曲线图（纯 XAML Shapes 绘制，不引图表库）。
/// 2026/10/06 15:53:36 xliu
/// 数据：若干系列，每系列一组 (时间, 百分比) 点。
/// </summary>
public sealed class BatteryChartControl : UserControl
{
    public sealed class Series
    {
        public required string Name;
        public required Color Color;
        public required IReadOnlyList<(DateTimeOffset Time, int Percent)> Points;
    }

    // 绘图区留白：padT 给顶部图例留空间，padB 给 X 轴标签，padL 给 Y 轴标签
    private const double PadL = 48, PadR = 16, PadT = 40, PadB = 28;

    private readonly Grid _root = new();
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly Canvas _hover = new() { IsHitTestVisible = false };
    private readonly StackPanel _legend = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 16,
        Margin = new Thickness(PadL, 8, 16, 0),
        IsHitTestVisible = false,
    };

    private IReadOnlyList<Series> _series = Array.Empty<Series>();
    private DateTimeOffset? _domainStart, _domainEnd;
    /// <summary>相邻采样间隔超过此值视为断档，折线/面积在中间断开（离线/关机不能假装有电量）。</summary>
    private TimeSpan _gapBreak = TimeSpan.FromMinutes(15);
    private UIElement? _emptyState;

    // 最近一次渲染的几何信息，供 hover 反查（pointer x → 时间 → 最近采样点）
    private double _w, _h, _plotW, _plotH;
    private DateTimeOffset _tStart, _tEnd;
    private double _totalSec;

    /// <summary>当前悬停命中的采样点；相同则不重画气泡，避免 PointerMoved 每像素清建造成闪烁。</summary>
    private (string Series, DateTimeOffset Time, int Percent)? _hoverKey;

    public BatteryChartControl()
    {
        // 透明背景必须设在 Content 根上：子 Canvas 全 IsHitTestVisible=false 时，
        // 只给 UserControl 设 Background 在 WinUI 里仍可能穿透，导致悬停气泡从不出现。
        var hit = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        Background = hit;
        _root.Background = hit;
        _root.Children.Add(_canvas);
        _root.Children.Add(_hover);
        _root.Children.Add(_legend);
        Content = _root;
        SizeChanged += (_, _) => Render();
        // 明暗主题切换时网格/轴文字颜色来自 ThemeResource，必须重绘才能跟着变
        ActualThemeChanged += (_, _) => Render();
        _root.PointerMoved += OnPointerMoved;
        _root.PointerExited += (_, _) => ClearHover();
    }

    public void SetData(IReadOnlyList<Series> series,
        DateTimeOffset? domainStart = null, DateTimeOffset? domainEnd = null,
        TimeSpan? gapBreak = null)
    {
        _series = series;
        _domainStart = domainStart;
        _domainEnd = domainEnd;
        if (gapBreak is { } g && g > TimeSpan.Zero) _gapBreak = g;
        Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        ClearHover();
        if (_emptyState is not null)
        {
            _root.Children.Remove(_emptyState);
            _emptyState = null;
        }

        _w = Math.Max(ActualWidth, 100);
        _h = Math.Max(ActualHeight, 100);
        _canvas.Width = _w;
        _canvas.Height = _h;
        _hover.Width = _w;
        _hover.Height = _h;
        _plotW = Math.Max(_w - PadL - PadR, 10);
        _plotH = Math.Max(_h - PadT - PadB, 10);

        DateTimeOffset tStart, tEnd;
        if (_domainStart.HasValue && _domainEnd.HasValue)
        {
            tStart = _domainStart.Value;
            tEnd = _domainEnd.Value;
        }
        else
        {
            tStart = DateTimeOffset.MaxValue;
            tEnd = DateTimeOffset.MinValue;
            foreach (var s in _series)
                foreach (var p in s.Points)
                {
                    if (p.Time < tStart) tStart = p.Time;
                    if (p.Time > tEnd) tEnd = p.Time;
                }
            if (tStart > tEnd)
            {
                tStart = DateTimeOffset.Now.AddHours(-24);
                tEnd = DateTimeOffset.Now;
            }
            if ((tEnd - tStart).TotalSeconds < 60) tEnd = tStart.AddSeconds(60);
        }
        if (tEnd <= tStart) return;
        _tStart = tStart;
        _tEnd = tEnd;
        _totalSec = (tEnd - tStart).TotalSeconds;

        var gridBrush = Application.Current.Resources["TextFillColorTertiaryBrush"] is SolidColorBrush tertiary
                        ? new SolidColorBrush(Color.FromArgb(0x38, tertiary.Color.R, tertiary.Color.G, tertiary.Color.B))
                        : new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));
        var axisBrush = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush
                        ?? new SolidColorBrush(Color.FromArgb(160, 128, 128, 128));
        var textBrush = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                        ?? new SolidColorBrush(Color.FromArgb(200, 128, 128, 128));

        RenderLegend(textBrush);

        // 横向网格线 + Y 标签（只留 0/50/100，避免六条线把曲线压住）
        foreach (int yPct in new[] { 0, 50, 100 })
        {
            double y = PctToY(yPct);
            _canvas.Children.Add(new Line
            {
                X1 = PadL, Y1 = y, X2 = PadL + _plotW, Y2 = y,
                Stroke = gridBrush, StrokeThickness = 1,
            });
            var yLabel = MakeLabel($"{yPct}%", axisBrush, 11);
            Canvas.SetLeft(yLabel, 6);
            Canvas.SetTop(yLabel, y - 8);
            _canvas.Children.Add(yLabel);
        }

        RenderTimeAxis(axisBrush);

        if (!_series.Any(s => s.Points.Count > 0))
        {
            RenderEmptyState(axisBrush, textBrush);
            return;
        }

        foreach (var s in _series)
        {
            if (s.Points.Count == 0) continue;
            RenderSeries(s);
        }
    }

    private void RenderLegend(Brush textBrush)
    {
        _legend.Children.Clear();
        // 只有一个系列时图例与设备下拉框完全重复，纯占地方，直接不画
        var shown = _series.Where(s => s.Points.Count > 0).ToList();
        if (shown.Count < 2) return;

        foreach (var s in shown)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            item.Children.Add(new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(s.Color),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var name = MakeLabel(s.Name, textBrush, 11);
            name.VerticalAlignment = VerticalAlignment.Center;
            item.Children.Add(name);
            _legend.Children.Add(item);
        }
    }

    /// <summary>X 轴时间标签：档数按可用宽度动态取 3~6，标签太长时退化成 "HH:mm"。</summary>
    private void RenderTimeAxis(Brush axisBrush)
    {
        int ticks = _plotW < 300 ? 3 : _plotW < 520 ? 4 : _plotW < 800 ? 5 : 6;
        bool shortFormat = _plotW / ticks < 95;

        for (int i = 0; i <= ticks; i++)
        {
            var t = _tStart.AddSeconds(_totalSec * i / ticks);
            double x = PadL + _plotW * i / ticks;
            string text = t.LocalDateTime.ToString(shortFormat ? "HH:mm" : "MM-dd HH:mm");
            var label = MakeLabel(text, axisBrush, 10);
            double estWidth = text.Length * 6.2;
            Canvas.SetLeft(label, Math.Clamp(x - estWidth / 2, 4, Math.Max(_w - estWidth - 4, 4)));
            Canvas.SetTop(label, _h - 20);
            _canvas.Children.Add(label);
        }
    }

    private void RenderSeries(Series s)
    {
        if (s.Points.Count == 0) return;

        var stroke = new SolidColorBrush(s.Color);
        var fill = new SolidColorBrush(s.Color);
        var areaFill = new SolidColorBrush(Color.FromArgb(38, s.Color.R, s.Color.G, s.Color.B));
        bool drawDots = s.Points.Count <= 40;

        foreach (var seg in SplitByGap(s.Points, _gapBreak))
        {
            if (seg.Count == 1)
            {
                // 孤立采样：不断档连线，只打点
                var alone = new Point(TimeToX(seg[0].Time), PctToY(seg[0].Percent));
                var dot = new Ellipse { Width = 5, Height = 5, Fill = fill };
                Canvas.SetLeft(dot, alone.X - 2.5);
                Canvas.SetTop(dot, alone.Y - 2.5);
                _canvas.Children.Add(dot);
                continue;
            }

            var pts = new PointCollection();
            foreach (var p in seg)
                pts.Add(new Point(TimeToX(p.Time), PctToY(p.Percent)));

            var area = new PointCollection();
            foreach (var pt in pts) area.Add(pt);
            area.Add(new Point(pts[^1].X, PadT + _plotH));
            area.Add(new Point(pts[0].X, PadT + _plotH));
            _canvas.Children.Add(new Polygon { Points = area, Fill = areaFill });

            _canvas.Children.Add(new Polyline
            {
                Points = pts,
                Stroke = stroke,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
            });

            if (drawDots)
            {
                foreach (var pt in pts)
                {
                    var dot = new Ellipse { Width = 5, Height = 5, Fill = fill };
                    Canvas.SetLeft(dot, pt.X - 2.5);
                    Canvas.SetTop(dot, pt.Y - 2.5);
                    _canvas.Children.Add(dot);
                }
            }
        }

        // 末端数值标签挂在全系列最后一点（用实测宽度决定靠左还是靠右）
        var last = s.Points[^1];
        var tip = MakeLabel($"{last.Percent}%", stroke, 11);
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tipWidth = tip.DesiredSize.Width;
        double tipX = TimeToX(last.Time) + 6;
        if (tipX + tipWidth > _w - PadR) tipX = _w - PadR - tipWidth;
        Canvas.SetLeft(tip, tipX);
        Canvas.SetTop(tip, PctToY(last.Percent) - 8);
        _canvas.Children.Add(tip);
    }

    /// <summary>按时间断档拆段。间隔 &gt; gapBreak 的相邻点不连线——关机/离线区间不能画成斜坡。</summary>
    internal static List<List<(DateTimeOffset Time, int Percent)>> SplitByGap(
        IReadOnlyList<(DateTimeOffset Time, int Percent)> points, TimeSpan gapBreak)
    {
        var segments = new List<List<(DateTimeOffset Time, int Percent)>>();
        if (points.Count == 0) return segments;

        var current = new List<(DateTimeOffset Time, int Percent)> { points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].Time - points[i - 1].Time > gapBreak)
            {
                segments.Add(current);
                current = new List<(DateTimeOffset Time, int Percent)>();
            }
            current.Add(points[i]);
        }
        segments.Add(current);
        return segments;
    }

    private void RenderEmptyState(Brush axisBrush, Brush textBrush)
    {
        var panel = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new FontIcon
        {
            Glyph = "\uE9D2",
            FontSize = 26,
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = axisBrush,
        });
        panel.Children.Add(new TextBlock
        {
            Text = Strings.Current.Get("S_60f304aeca"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = textBrush,
        });
        panel.Children.Add(new TextBlock
        {
            Text = Strings.Current.Get("S_daba8f430e"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = axisBrush,
        });

        _emptyState = panel;
        _root.Children.Add(panel);
    }

    private void ClearHover()
    {
        _hover.Children.Clear();
        _hoverKey = null;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_series.Count == 0 || _totalSec <= 0 || !_series.Any(s => s.Points.Count > 0))
        {
            ClearHover();
            return;
        }

        var p = e.GetCurrentPoint(_root).Position;
        // 只在绘图区内响应，边缘留 6px 容差
        if (p.X < PadL - 6 || p.X > PadL + _plotW + 6 || p.Y < PadT - 6 || p.Y > PadT + _plotH + 6)
        {
            ClearHover();
            return;
        }

        // pointer x → 目标时间 → 全局最近的采样点
        double offset = Math.Clamp((p.X - PadL) / _plotW, 0, 1) * _totalSec;
        var target = _tStart.AddSeconds(offset);

        Series? bestSeries = null;
        (DateTimeOffset Time, int Percent) best = default;
        double bestDelta = double.MaxValue;
        foreach (var s in _series)
        {
            foreach (var pt in s.Points)
            {
                double delta = Math.Abs((pt.Time - target).TotalSeconds);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = pt;
                    bestSeries = s;
                }
            }
        }
        if (bestSeries is null) return;

        var key = (bestSeries.Name, best.Time, best.Percent);
        if (_hoverKey == key) return;
        _hoverKey = key;

        _hover.Children.Clear();
        double hx = TimeToX(best.Time);
        double hy = PctToY(best.Percent);
        var color = new SolidColorBrush(bestSeries.Color);

        _hover.Children.Add(new Line
        {
            X1 = hx, Y1 = PadT, X2 = hx, Y2 = PadT + _plotH,
            Stroke = color,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            Opacity = 0.55,
        });

        // 描边用主题色，暗色/亮色都能和曲线色点区分开
        var dotStroke = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush
                        ?? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        var dot = new Ellipse
        {
            Width = 10, Height = 10,
            Fill = color,
            Stroke = dotStroke,
            StrokeThickness = 2,
        };
        Canvas.SetLeft(dot, hx - 5);
        Canvas.SetTop(dot, hy - 5);
        _hover.Children.Add(dot);

        _hover.Children.Add(MakeTooltip(bestSeries, best, hx, hy));
    }

    /// <summary>跟随采样点的气泡：时间 + 电量；多系列时带系列名。色值走主题资源，明暗模式都可读。</summary>
    private UIElement MakeTooltip(Series series, (DateTimeOffset Time, int Percent) point, double hx, double hy)
    {
        var primary = Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush
                      ?? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        var secondary = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
                        ?? new SolidColorBrush(Color.FromArgb(200, 200, 200, 200));
        var tipBg = Application.Current.Resources["SolidBackgroundFillColorTertiaryBrush"] as Brush
                    ?? new SolidColorBrush(Color.FromArgb(242, 32, 32, 32));
        var tipBorder = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush
                        ?? new SolidColorBrush(Color.FromArgb(80, 128, 128, 128));

        var content = new StackPanel { Spacing = 2 };
        if (_series.Count > 1)
        {
            content.Children.Add(new TextBlock
            {
                Text = series.Name,
                FontSize = 11,
                Foreground = secondary,
            });
        }
        content.Children.Add(new TextBlock
        {
            Text = point.Time.LocalDateTime.ToString("MM-dd HH:mm"),
            FontSize = 12,
            Foreground = secondary,
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{point.Percent}%",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = primary,
        });

        var tip = new Border
        {
            Background = tipBg,
            BorderBrush = tipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = content,
            // 读屏：时间与电量一并报出（图表本身对读屏不友好）
        };
        AutomationProperties.SetName(tip,
            Strings.Current.Format("S_6783cdc749", point.Time.LocalDateTime, point.Percent));
        tip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tw = tip.DesiredSize.Width;
        double th = tip.DesiredSize.Height;

        Canvas.SetLeft(tip, Math.Clamp(hx - tw / 2, 4, Math.Max(_w - tw - 4, 4)));
        double ty = hy - th - 14;
        Canvas.SetTop(tip, ty < 2 ? hy + 14 : ty);
        return tip;
    }

    private double TimeToX(DateTimeOffset t) =>
        PadL + _plotW * Math.Clamp((t - _tStart).TotalSeconds / _totalSec, 0, 1);

    private double PctToY(int percent) =>
        PadT + _plotH * (1 - Math.Clamp(percent, 0, 100) / 100.0);

    private static TextBlock MakeLabel(string text, Brush brush, double size)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = brush,
        };
    }
}
