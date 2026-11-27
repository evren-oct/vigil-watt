using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.UI.Xaml.Media;
using VigilWatt.Core.Models;
using Windows.Foundation;
using Gdi = System.Drawing;
using Gdi2 = System.Drawing.Drawing2D;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： Tabler Icons 3.31.0 的设备图形：官方 SVG 路径解析成 XAML 与 GDI+ 共用的几何。
/// 2026/11/26 15:20:00 20211
/// 路径是官方 outline 图标的 d 原样抄录，不手画；两套渲染器共用解析结果才不会慢慢分叉。
/// </summary>
internal static class TablerDeviceIcons
{
    private static readonly ConcurrentDictionary<DeviceKind, IReadOnlyList<IconFigure>> Cache = new();

    /// <summary>24×24 网格的 XAML 几何（设备列表、详情 Hero、桌面小组件）。</summary>
    public static Geometry Geometry(DeviceKind kind)
    {
        var geometry = new PathGeometry();
        foreach (var figure in Figures(kind))
        {
            if (figure.Segments.Count == 0) continue;

            var pathFigure = new PathFigure
            {
                StartPoint = figure.Start,
                IsClosed = figure.Closed,
                IsFilled = false,
            };
            foreach (var segment in figure.Segments)
            {
                if (segment.Kind == IconSegmentKind.Line)
                {
                    pathFigure.Segments.Add(new LineSegment { Point = segment.End });
                }
                else
                {
                    pathFigure.Segments.Add(new BezierSegment
                    {
                        Point1 = segment.Control1,
                        Point2 = segment.Control2,
                        Point3 = segment.End,
                    });
                }
            }
            geometry.Figures.Add(pathFigure);
        }
        return geometry;
    }

    /// <summary>任务栏贴片用：同一份几何按 <paramref name="scale"/> 画进 GDI+。</summary>
    public static void Draw(Gdi.Graphics g, Gdi.Pen pen, DeviceKind kind, Gdi.PointF origin, float scale)
    {
        Gdi.PointF Map(Point p) => new(origin.X + (float)p.X * scale, origin.Y + (float)p.Y * scale);

        using var path = new Gdi2.GraphicsPath();
        foreach (var figure in Figures(kind))
        {
            if (figure.Segments.Count == 0) continue;

            path.StartFigure();
            var current = Map(figure.Start);
            foreach (var segment in figure.Segments)
            {
                var end = Map(segment.End);
                if (segment.Kind == IconSegmentKind.Line)
                {
                    path.AddLine(current, end);
                }
                else
                {
                    path.AddBezier(current, Map(segment.Control1), Map(segment.Control2), end);
                }
                current = end;
            }
            if (figure.Closed) path.CloseFigure();
        }
        g.DrawPath(pen, path);
    }

    private static IReadOnlyList<IconFigure> Figures(DeviceKind kind) =>
        Cache.GetOrAdd(kind, k =>
        {
            var figures = new List<IconFigure>();
            foreach (var path in Paths(k)) figures.AddRange(Parse(path));
            return figures;
        });

    // 数据来源：tabler/tabler-icons 3.31.0，icons/outline/<名称>.svg 的 d 属性
    private static string[] Paths(DeviceKind kind) => kind switch
    {
        DeviceKind.Keyboard =>
        [
            "M2 6m0 2a2 2 0 0 1 2 -2h16a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-16a2 2 0 0 1 -2 -2z",
            "M6 10l0 .01",
            "M10 10l0 .01",
            "M14 10l0 .01",
            "M18 10l0 .01",
            "M6 14l0 .01",
            "M18 14l0 .01",
            "M10 14l4 .01",
        ],
        DeviceKind.Mouse =>
        [
            "M6 3m0 4a4 4 0 0 1 4 -4h4a4 4 0 0 1 4 4v10a4 4 0 0 1 -4 4h-4a4 4 0 0 1 -4 -4z",
            "M12 7l0 4",
        ],
        DeviceKind.Headset =>
        [
            "M4 13m0 2a2 2 0 0 1 2 -2h1a2 2 0 0 1 2 2v3a2 2 0 0 1 -2 2h-1a2 2 0 0 1 -2 -2z",
            "M15 13m0 2a2 2 0 0 1 2 -2h1a2 2 0 0 1 2 2v3a2 2 0 0 1 -2 2h-1a2 2 0 0 1 -2 -2z",
            "M4 15v-3a8 8 0 0 1 16 0v3",
        ],
        DeviceKind.Speaker =>
        [
            "M5 3m0 2a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2z",
            "M12 14m-3 0a3 3 0 1 0 6 0a3 3 0 1 0 -6 0",
            "M12 7l0 .01",
        ],
        DeviceKind.Gamepad =>
        [
            "M12 5h3.5a5 5 0 0 1 0 10h-5.5l-4.015 4.227a2.3 2.3 0 0 1 -3.923 -2.035l1.634 -8.173a5 5 0 0 1 4.904 -4.019h3.4z",
            "M14 15l4.07 4.284a2.3 2.3 0 0 0 3.925 -2.023l-1.6 -8.232",
            "M8 9v2",
            "M7 10h2",
            "M14 10h2",
        ],
        DeviceKind.Computer =>
        [
            "M3 5a1 1 0 0 1 1 -1h16a1 1 0 0 1 1 1v10a1 1 0 0 1 -1 1h-16a1 1 0 0 1 -1 -1v-10z",
            "M7 20h10",
            "M9 16v4",
            "M15 16v4",
        ],
        DeviceKind.Phone =>
        [
            "M6 5a2 2 0 0 1 2 -2h8a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2v-14z",
            "M11 4h2",
            "M12 17v.01",
        ],
        DeviceKind.Tablet =>
        [
            "M5 4a1 1 0 0 1 1 -1h12a1 1 0 0 1 1 1v16a1 1 0 0 1 -1 1h-12a1 1 0 0 1 -1 -1v-16z",
            "M11 17a1 1 0 1 0 2 0a1 1 0 0 0 -2 0",
        ],
        DeviceKind.Watch =>
        [
            "M6 9a3 3 0 0 1 3 -3h6a3 3 0 0 1 3 3v6a3 3 0 0 1 -3 3h-6a3 3 0 0 1 -3 -3v-6z",
            "M9 18v3h6v-3",
            "M9 6v-3h6v3",
        ],
        _ =>
        [
            "M5 5a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2z",
            "M12 16v.01",
            "M12 13a2 2 0 0 0 .914 -3.782a1.98 1.98 0 0 0 -2.414 .483",
        ],
    };

    // 只覆盖 Tabler 设备图标用到的命令；遇到新命令直接抛，避免默默画错
    private static IReadOnlyList<IconFigure> Parse(string data)
    {
        var figures = new List<IconFigure>();
        var figure = new IconFigure();
        var current = new Point();
        var subpathStart = new Point();
        char command = '\0';
        int i = 0;

        while (true)
        {
            SkipSeparators(data, ref i);
            if (i >= data.Length) break;

            if (char.IsLetter(data[i]))
            {
                command = data[i];
                i++;
                if (command is 'z' or 'Z')
                {
                    figure.Closed = true;
                    current = subpathStart;
                    continue;
                }
            }
            else if (command == '\0')
            {
                throw new FormatException(Strings.Current.Format("S_519b492857", i));
            }

            bool relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    var point = ReadPoint(data, ref i, relative, current);
                    current = point;
                    subpathStart = point;
                    figure = new IconFigure { Start = point };
                    figures.Add(figure);
                    command = relative ? 'l' : 'L';
                    break;
                }
                case 'L':
                    current = AddLine(figure, ReadPoint(data, ref i, relative, current));
                    break;
                case 'H':
                {
                    double x = ReadNumber(data, ref i) + (relative ? current.X : 0);
                    current = AddLine(figure, new Point(x, current.Y));
                    break;
                }
                case 'V':
                {
                    double y = ReadNumber(data, ref i) + (relative ? current.Y : 0);
                    current = AddLine(figure, new Point(current.X, y));
                    break;
                }
                case 'C':
                {
                    var c1 = ReadPoint(data, ref i, relative, current);
                    var c2 = ReadPoint(data, ref i, relative, current);
                    var end = ReadPoint(data, ref i, relative, current);
                    figure.Segments.Add(IconSegment.Cubic(c1, c2, end));
                    current = end;
                    break;
                }
                case 'Q':
                {
                    var control = ReadPoint(data, ref i, relative, current);
                    var end = ReadPoint(data, ref i, relative, current);
                    figure.Segments.Add(IconSegment.Cubic(
                        new Point(
                            current.X + (control.X - current.X) * 2 / 3,
                            current.Y + (control.Y - current.Y) * 2 / 3),
                        new Point(
                            end.X + (control.X - end.X) * 2 / 3,
                            end.Y + (control.Y - end.Y) * 2 / 3),
                        end));
                    current = end;
                    break;
                }
                case 'A':
                {
                    double rx = ReadNumber(data, ref i);
                    double ry = ReadNumber(data, ref i);
                    double rotation = ReadNumber(data, ref i);
                    bool largeArc = ReadNumber(data, ref i) != 0;
                    bool sweep = ReadNumber(data, ref i) != 0;
                    var end = ReadPoint(data, ref i, relative, current);
                    AddArc(figure, current, rx, ry, rotation, largeArc, sweep, end);
                    current = end;
                    break;
                }
                default:
                    throw new NotSupportedException(Strings.Current.Format("S_f439333fd7", command));
            }
        }

        return figures;
    }

    private static Point AddLine(IconFigure figure, Point end)
    {
        figure.Segments.Add(IconSegment.Line(end));
        return end;
    }

    private static Point ReadPoint(string data, ref int i, bool relative, Point current)
    {
        double x = ReadNumber(data, ref i);
        double y = ReadNumber(data, ref i);
        return relative ? new Point(current.X + x, current.Y + y) : new Point(x, y);
    }

    private static void SkipSeparators(string data, ref int i)
    {
        while (i < data.Length && (char.IsWhiteSpace(data[i]) || data[i] == ',')) i++;
    }

    private static double ReadNumber(string data, ref int i)
    {
        SkipSeparators(data, ref i);
        int start = i;
        if (i < data.Length && data[i] is '+' or '-') i++;
        while (i < data.Length && char.IsAsciiDigit(data[i])) i++;
        if (i < data.Length && data[i] == '.')
        {
            i++;
            while (i < data.Length && char.IsAsciiDigit(data[i])) i++;
        }
        if (i < data.Length && data[i] is 'e' or 'E')
        {
            i++;
            if (i < data.Length && data[i] is '+' or '-') i++;
            while (i < data.Length && char.IsAsciiDigit(data[i])) i++;
        }
        if (i == start) throw new FormatException(Strings.Current.Format("S_571ac97bd8", start));
        return double.Parse(data.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // 椭圆弧按 ≤90° 拆成三次贝塞尔：XAML 和 GDI+ 拿到的是同一串曲线
    private static void AddArc(
        IconFigure figure, Point from, double rx, double ry, double rotation,
        bool largeArc, bool sweep, Point to)
    {
        if (from == to) return;
        if (rx == 0 || ry == 0)
        {
            figure.Segments.Add(IconSegment.Line(to));
            return;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        double phi = rotation * Math.PI / 180;
        double cosPhi = Math.Cos(phi);
        double sinPhi = Math.Sin(phi);

        double dx = (from.X - to.X) / 2;
        double dy = (from.Y - to.Y) / 2;
        double x1 = cosPhi * dx + sinPhi * dy;
        double y1 = -sinPhi * dx + cosPhi * dy;

        double radiusScale = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (radiusScale > 1)
        {
            double scale = Math.Sqrt(radiusScale);
            rx *= scale;
            ry *= scale;
        }

        double numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        double denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        double factor = Math.Sqrt(Math.Max(0, numerator / denominator)) * (largeArc == sweep ? -1 : 1);

        double centerX1 = factor * rx * y1 / ry;
        double centerY1 = -factor * ry * x1 / rx;
        double centerX = cosPhi * centerX1 - sinPhi * centerY1 + (from.X + to.X) / 2;
        double centerY = sinPhi * centerX1 + cosPhi * centerY1 + (from.Y + to.Y) / 2;

        double startAngle = Math.Atan2((y1 - centerY1) / ry, (x1 - centerX1) / rx);
        double endAngle = Math.Atan2((-y1 - centerY1) / ry, (-x1 - centerX1) / rx);
        double sweepAngle = endAngle - startAngle;
        if (!sweep && sweepAngle > 0) sweepAngle -= 2 * Math.PI;
        else if (sweep && sweepAngle < 0) sweepAngle += 2 * Math.PI;

        int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.PI / 2)));
        double step = sweepAngle / count;
        double handle = 4.0 / 3.0 * Math.Tan(step / 4);

        Point OnEllipse(double angle) => new(
            centerX + rx * Math.Cos(angle) * cosPhi - ry * Math.Sin(angle) * sinPhi,
            centerY + rx * Math.Cos(angle) * sinPhi + ry * Math.Sin(angle) * cosPhi);

        Point Derivative(double angle) => new(
            -rx * Math.Sin(angle) * cosPhi - ry * Math.Cos(angle) * sinPhi,
            -rx * Math.Sin(angle) * sinPhi + ry * Math.Cos(angle) * cosPhi);

        double angle0 = startAngle;
        for (int i = 0; i < count; i++)
        {
            double angle1 = angle0 + step;
            var start = i == 0 ? from : OnEllipse(angle0);
            var end = i == count - 1 ? to : OnEllipse(angle1);
            var d0 = Derivative(angle0);
            var d1 = Derivative(angle1);
            figure.Segments.Add(IconSegment.Cubic(
                new Point(start.X + handle * d0.X, start.Y + handle * d0.Y),
                new Point(end.X - handle * d1.X, end.Y - handle * d1.Y),
                end));
            angle0 = angle1;
        }
    }
}

internal enum IconSegmentKind
{
    Line,
    Cubic,
}

internal readonly struct IconSegment
{
    public IconSegmentKind Kind { get; init; }
    public Point Control1 { get; init; }
    public Point Control2 { get; init; }
    public Point End { get; init; }

    public static IconSegment Line(Point end) => new() { Kind = IconSegmentKind.Line, End = end };

    public static IconSegment Cubic(Point control1, Point control2, Point end) => new()
    {
        Kind = IconSegmentKind.Cubic,
        Control1 = control1,
        Control2 = control2,
        End = end,
    };
}

internal sealed class IconFigure
{
    public Point Start { get; init; }
    public bool Closed { get; set; }
    public List<IconSegment> Segments { get; } = [];
}
