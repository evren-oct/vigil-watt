using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 接收器体系图标：按目录声明的语义 key 画图，插件决定用哪个、App 只保证同一套画法。
/// 2026/11/11 11:58:00 20211
/// bolt / unifying 直接复用 OpenLogi（MIT / Apache-2.0）的官方 path，其余为自绘几何；见 architecture.md 连接章节。
/// </summary>
public static class ReceiverIconFactory
{
    /// <summary>标签里图标的边长：官方 Unifying 的星形镂空在 11px 会塌成"方块 + 白十字"，14px 起才立得住。</summary>
    public const double BadgeSize = 14;

    /// <summary>OpenLogi `action-icons/bolt.svg` 的 path 原文（MIT, Copyright (c) 2026 AprilNEA），未做简化；viewBox 7 7 32 32。</summary>
    private const string BoltMark =
        "M25.5367 36.4498L15.7592 21.831H22.9999L15.7911 10.772L7.66663 23.3069L16.1851 36.4498H25.5367Z" +
        "M29.8498 36.3957L38.3333 23.3069L29.8148 10.1641L20.0492 10.1641L29.3888 24.9927H22.361L29.8498 36.3957Z";

    /// <summary>OpenLogi `action-icons/unifying.svg` 的 path 原文（MIT, Copyright (c) 2026 AprilNEA）；源文件的 translate 由 <see cref="Mark"/> 抵消。</summary>
    private const string UnifyingMark =
        "m 53.531063,377.8206 c -4.26933,0 -4.488,-0.38133 -4.708,-0.75866 -0.22,-0.38134 -0.43866,-0.76 1.696,-4.456 " +
        "0.36267,-0.628 0.14667,-1.42934 -0.47866,-1.78934 -0.62667,-0.36266 -1.42667,-0.148 -1.78934,0.47867 " +
        "-2.13333,3.69733 -2.57333,3.69733 -3.01066,3.69733 -0.44,0 -0.87734,0 -3.012,-3.69733 " +
        "-0.36134,-0.62667 -1.16267,-0.84133 -1.78934,-0.47867 -0.62666,0.36 -0.84133,1.16134 -0.47866,1.78934 " +
        "2.13333,3.696 1.91333,4.07466 1.696,4.456 -0.22134,0.37733 -0.44,0.75866 -4.70934,0.75866 " +
        "-0.72266,0 -1.308,0.58534 -1.308,1.30934 0,0.724 0.58534,1.30933 1.308,1.30933 4.26934,0 4.488,0.38133 " +
        "4.70934,0.75867 0.21733,0.38133 0.43733,0.76133 -1.696,4.46 -0.36267,0.624 -0.148,1.42666 0.47866,1.78533 " +
        "0.62667,0.364 1.428,0.14667 1.78934,-0.47867 2.13466,-3.696 2.572,-3.696 3.012,-3.696 0.43733,0 0.87733,0 " +
        "3.01066,3.696 0.36267,0.62534 1.16267,0.84267 1.78934,0.47867 0.62533,-0.35867 0.84133,-1.16133 " +
        "0.47866,-1.78533 -2.13466,-3.69867 -1.916,-4.07867 -1.696,-4.46 0.22,-0.37734 0.43867,-0.75867 " +
        "4.708,-0.75867 0.72134,0 1.30934,-0.58533 1.30934,-1.30933 0,-0.724 -0.588,-1.30934 -1.30934,-1.30934 " +
        "m -9.792,2.80934 c -0.46133,-0.46134 -0.46133,-2.53867 0,-3 0.46134,-0.46134 2.53867,-0.46134 3,0 " +
        "0.46134,0.46133 0.46134,2.53866 0,3 -0.46133,0.46266 -2.53866,0.46266 -3,0 " +
        "m 1.50134,10.5 c -4.00134,0 -8.00134,0 -10.00134,-1.99867 -1.999996,-2 -1.999996,-6 -1.999996,-10.00133 " +
        "0,-4 0,-8 1.999996,-10.00134 2,-1.99866 6,-1.99866 10.00134,-1.99866 4,0 7.99866,0 10,1.99866 " +
        "2.00133,2.00134 2.00133,6.00134 2.00133,10.00134 0,4.00133 0,8.00133 -2.00133,10.00133 " +
        "-2.00134,1.99867 -6,1.99867 -10,1.99867";

    /// <summary>名义框 9×9 再整体缩放，与设备图标同样的做法，保证各体系视觉大小一致。</summary>
    public static UIElement Scaled(string? key, Color stroke, double size) => new Viewbox
    {
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        Child = Glyph(key, stroke),
    };

    /// <summary>体系图形本身（无底）；key 不区分大小写，空值/未知值都回退到 wireless。</summary>
    public static UIElement Glyph(string? key, Color stroke) => (key ?? "").Trim().ToLowerInvariant() switch
    {
        "bolt" => Mark(BoltMark, 7, 7, 32, stroke),
        "unifying" => Mark(UnifyingMark, 33.239067, 367.12994, 24.002666, stroke),
        "nano" => Nano(stroke),
        "lightning" => Lightning(stroke),
        "multi" => Multi(stroke),
        _ => Wireless(stroke),
    };

    /// <summary>官方 path 的承载：解析成几何后缩放平移到 9×9 名义框，颜色由 Fill 决定（不依赖 currentColor）。</summary>
    private static UIElement Mark(string pathData, double minX, double minY, double size, Color fill)
    {
        var scale = 9 / size;
        var path = new ShapePath
        {
            Data = PathData.Parse(pathData),
            Fill = new SolidColorBrush(fill),
            RenderTransform = new CompositeTransform
            {
                ScaleX = scale,
                ScaleY = scale,
                TranslateX = -minX * scale,
                TranslateY = -minY * scale,
            },
        };
        var box = new Canvas { Width = 9, Height = 9 };
        box.Children.Add(path);
        return box;
    }

    /// <summary>信号弧：两条弧 + 一个点，表达"经无线接收器"，不绑定任何厂商。</summary>
    private static UIElement Wireless(Color stroke)
    {
        var brush = new SolidColorBrush(stroke);
        var box = new Grid { Width = 9, Height = 9 };
        box.Children.Add(Arc(1.3, 6.7, 7.7, 6.7, 4.1, brush));
        box.Children.Add(Arc(3.4, 6.7, 5.6, 6.7, 1.5, brush));
        box.Children.Add(new Ellipse
        {
            Width = 1,
            Height = 1,
            Fill = brush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(4, 7.3, 0, 0),
        });
        return box;
    }

    /// <summary>Nano：小插头（短插舌 + 机身轮廓）；它是无品牌标志的通用 dongle，不该借用 Bolt/Unifying 的造型。</summary>
    private static UIElement Nano(Color stroke)
    {
        var brush = new SolidColorBrush(stroke);
        var box = new Grid { Width = 9, Height = 9 };
        box.Children.Add(Bar(3.55, 0.7, 1.9, 3.5, 0.35, fill: brush, stroke: null));
        box.Children.Add(Bar(2.5, 3.9, 4.0, 4.5, 0.9, fill: null, stroke: brush));
        return box;
    }

    /// <summary>闪电：LIGHTSPEED 这类"低延迟通道"的通用符号，与厂商标志无关。</summary>
    private static UIElement Lightning(Color stroke)
    {
        var brush = new SolidColorBrush(stroke);
        var bolt = new Polygon { Fill = brush };
        foreach (var (x, y) in new[] { (5.4, 0.8), (2.4, 5.1), (4.2, 5.1), (3.4, 8.2), (6.6, 3.7), (4.8, 3.7) })
            bolt.Points.Add(new Point(x, y));
        var box = new Grid { Width = 9, Height = 9 };
        box.Children.Add(bolt);
        return box;
    }

    /// <summary>多设备：两个交叠的环，表达"一个通道带多台设备"，不指代某个接收器族。</summary>
    private static UIElement Multi(Color stroke)
    {
        var brush = new SolidColorBrush(stroke);
        var box = new Grid { Width = 9, Height = 9 };
        box.Children.Add(Ring(brush, 0.4));
        box.Children.Add(Ring(brush, 3.4));
        return box;
    }

    private static Ellipse Ring(Brush brush, double left) => new()
    {
        Width = 5.2,
        Height = 5.2,
        Stroke = brush,
        StrokeThickness = 1.1,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(left, 1.9, 0, 0),
    };

    private static Rectangle Bar(double x, double y, double w, double h, double radius, Brush? fill, Brush? stroke) => new()
    {
        Width = w,
        Height = h,
        RadiusX = radius,
        RadiusY = radius,
        Fill = fill,
        Stroke = stroke,
        StrokeThickness = stroke is null ? 0 : 1.1,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(x, y, 0, 0),
    };

    private static ShapePath Arc(double x1, double y1, double x2, double y2, double radius, Brush brush)
    {
        var figure = new PathFigure { StartPoint = new Point(x1, y1), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(x2, y2),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = false,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new ShapePath
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = 1.1,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
    }

    /// <summary>WinUI 没有 WPF 的 `Geometry.Parse`，这里只实现两个官方标志用到的 M/L/H/V/C/Z 子集。</summary>
    private static class PathData
    {
        /// <summary>解析成 PathGeometry；相对命令以当前点累加，'M' 之后的隐式坐标按 'L' 处理（SVG 规定）。</summary>
        public static PathGeometry Parse(string data)
        {
            var geometry = new PathGeometry();
            var tokens = Tokenize(data);
            var i = 0;
            var command = '\0';
            var figure = (PathFigure?)null;
            var current = new Point();
            var start = new Point();

            double Next() => double.Parse(tokens[i++], NumberStyles.Float, CultureInfo.InvariantCulture);

            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i][0]))
                {
                    command = tokens[i][0];
                    i++;
                }

                if (command is 'Z' or 'z')
                {
                    if (figure is not null)
                    {
                        figure.IsClosed = true;
                        geometry.Figures.Add(figure);
                        figure = null;
                    }
                    current = start;
                    command = '\0';
                    continue;
                }

                var relative = char.IsLower(command);
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        if (figure is not null) geometry.Figures.Add(figure);
                        var moveX = Next();
                        var moveY = Next();
                        if (relative) { moveX += current.X; moveY += current.Y; }
                        figure = new PathFigure { StartPoint = new Point(moveX, moveY), IsClosed = false, IsFilled = true };
                        current = new Point(moveX, moveY);
                        start = current;
                        command = relative ? 'l' : 'L';
                        break;

                    case 'L':
                        var lineX = Next();
                        var lineY = Next();
                        if (relative) { lineX += current.X; lineY += current.Y; }
                        Figure(ref figure, current).Segments.Add(new LineSegment { Point = new Point(lineX, lineY) });
                        current = new Point(lineX, lineY);
                        break;

                    case 'H':
                        var horizontal = Next();
                        if (relative) horizontal += current.X;
                        Figure(ref figure, current).Segments.Add(new LineSegment { Point = new Point(horizontal, current.Y) });
                        current = new Point(horizontal, current.Y);
                        break;

                    case 'V':
                        var vertical = Next();
                        if (relative) vertical += current.Y;
                        Figure(ref figure, current).Segments.Add(new LineSegment { Point = new Point(current.X, vertical) });
                        current = new Point(current.X, vertical);
                        break;

                    case 'C':
                        var c1X = Next();
                        var c1Y = Next();
                        var c2X = Next();
                        var c2Y = Next();
                        var endX = Next();
                        var endY = Next();
                        if (relative)
                        {
                            c1X += current.X; c1Y += current.Y;
                            c2X += current.X; c2Y += current.Y;
                            endX += current.X; endY += current.Y;
                        }
                        Figure(ref figure, current).Segments.Add(new BezierSegment
                        {
                            Point1 = new Point(c1X, c1Y),
                            Point2 = new Point(c2X, c2Y),
                            Point3 = new Point(endX, endY),
                        });
                        current = new Point(endX, endY);
                        break;

                    default:
                        i++;
                        break;
                }
            }

            if (figure is not null) geometry.Figures.Add(figure);
            return geometry;
        }

        /// <summary>数据不合法时补一个起点并回写，避免整块图标因为少一个 M 就消失。</summary>
        private static PathFigure Figure(ref PathFigure? figure, Point current)
        {
            figure ??= new PathFigure { StartPoint = current, IsClosed = false, IsFilled = true };
            return figure;
        }

        /// <summary>切出命令字母与数字：逗号、空白都算分隔，负号开头即新数字。</summary>
        private static List<string> Tokenize(string data)
        {
            var tokens = new List<string>();
            var i = 0;
            while (i < data.Length)
            {
                var c = data[i];
                if (char.IsLetter(c))
                {
                    tokens.Add(c.ToString());
                    i++;
                }
                else if (char.IsWhiteSpace(c) || c == ',')
                {
                    i++;
                }
                else
                {
                    var startIndex = i;
                    while (i < data.Length && (char.IsDigit(data[i]) || data[i] is '.' or '-' or '+' or 'e' or 'E'))
                        i++;
                    tokens.Add(data[startIndex..i]);
                }
            }
            return tokens;
        }
    }
}
