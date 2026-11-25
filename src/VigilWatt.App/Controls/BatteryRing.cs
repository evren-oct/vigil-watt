using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace VigilWatt.App.Controls;

/// <summary>
/// 功 能： 电量环：按百分比画一段圆头圆弧，中心留空给页面叠数字。
/// 2026/11/11 22:15:00 20211
/// 不用 ProgressRing——那是不确定态动画，语义不符；圆弧才能同时表达"有值 / 无值"。
/// </summary>
public sealed class BatteryRing : ContentControl
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(BatteryRing), new PropertyMetadata(0d, OnVisualChanged));

    /// <summary>false = 没有读数：只画轨道，不画弧（不要用 0% 冒充"没读到"）。</summary>
    public static readonly DependencyProperty IsKnownProperty = DependencyProperty.Register(
        nameof(IsKnown), typeof(bool), typeof(BatteryRing), new PropertyMetadata(true, OnVisualChanged));

    public static readonly DependencyProperty RingColorProperty = DependencyProperty.Register(
        nameof(RingColor), typeof(Color), typeof(BatteryRing),
        new PropertyMetadata(Microsoft.UI.Colors.Gray, OnVisualChanged));

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(BatteryRing), new PropertyMetadata(76d, OnVisualChanged));

    private const double StrokeWidth = 6.5;

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public bool IsKnown
    {
        get => (bool)GetValue(IsKnownProperty);
        set => SetValue(IsKnownProperty, value);
    }

    public Color RingColor
    {
        get => (Color)GetValue(RingColorProperty);
        set => SetValue(RingColorProperty, value);
    }

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public BatteryRing()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Rebuild();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((BatteryRing)d).Rebuild();

    /// <summary>整块重建：环只有两个形状，重建比逐属性增量改几何更不容易出错。</summary>
    private void Rebuild()
    {
        double size = Diameter;
        Width = size;
        Height = size;

        double radius = (size - StrokeWidth) / 2;
        double center = size / 2;
        var grid = new Grid { Width = size, Height = size };

        // 轨道用同色压淡：换成灰色会和"离线"撞色，同色轨道才读得出"这是同一条尺子"
        grid.Children.Add(new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = new SolidColorBrush(Color.FromArgb(0x33, RingColor.R, RingColor.G, RingColor.B)),
            StrokeThickness = StrokeWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        double fraction = IsKnown ? Math.Clamp(Percent / 100d, 0d, 1d) : 0d;
        var brush = new SolidColorBrush(RingColor);

        if (fraction >= 0.9999)
        {
            // 满格：起终点重合的 ArcSegment 画不出来，直接画整圆
            grid.Children.Add(new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = brush,
                StrokeThickness = StrokeWidth,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else if (fraction > 0)
        {
            grid.Children.Add(BuildArc(center, radius, fraction, brush));
        }

        Content = grid;
    }

    /// <summary>从 12 点方向顺时针扫 fraction 圈。</summary>
    private static XamlPath BuildArc(double center, double radius, double fraction, Brush brush)
    {
        double sweep = fraction * 360d;
        var figure = new PathFigure
        {
            StartPoint = PointOnCircle(center, radius, -90),
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = PointOnCircle(center, radius, -90 + sweep),
            Size = new Size(radius, radius),
            IsLargeArc = sweep > 180,
            SweepDirection = SweepDirection.Clockwise,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new XamlPath
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
    }

    private static Point PointOnCircle(double center, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180d;
        return new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
    }
}
