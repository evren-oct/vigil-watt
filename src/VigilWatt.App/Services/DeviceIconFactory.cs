using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VigilWatt.Core.Models;
using Windows.UI;
using Gdi = System.Drawing;
using Gdi2 = System.Drawing.Drawing2D;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 设备类型图标的绘制工厂：桌面小组件与任务栏贴片共用同一套图形，避免两处各画一份而慢慢分叉。
/// 2026/10/10 21:34:00 xliu
/// 图形取自 Tabler Icons 3.31.0（MIT），描边粗细按显示尺寸调整，所以不用字体图标（见 docs/dev/ui-style.md §5）。
/// </summary>
public static class DeviceIconFactory
{
    private const double GridSize = 24;
    private const double ListStroke = 1.6;
    private const double HeroStroke = 0.9;

    /// <summary>带圆角方块底的完整图标（小组件用）；<paramref name="size"/> 是方块边长。</summary>
    public static FrameworkElement Boxed(DeviceKind kind, Color fill, Color stroke, double size) =>
        new Grid
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(fill),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Viewbox
                {
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Uniform,
                    Child = Glyph(kind, stroke, ListStroke),
                },
            },
        };

    /// <summary>定尺图标（设备列表页用）：24×24 网格整体缩放，各类型视觉大小才一致。</summary>
    public static FrameworkElement Scaled(DeviceKind kind, Color stroke, double size) =>
        new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = Glyph(kind, stroke, ListStroke),
        };

    /// <summary>详情页 Hero 牌里的图形：24×24 网格、0.9 描边，80px 下做光学减细。</summary>
    public static FrameworkElement Hero(DeviceKind kind, Color stroke, double size) =>
        new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = Glyph(kind, stroke, HeroStroke),
        };

    /// <summary>设备图形本身（无底），任务栏贴片直接用它。</summary>
    public static UIElement Glyph(DeviceKind kind, Color stroke, double strokeThickness = ListStroke)
    {
        var canvas = new Canvas { Width = GridSize, Height = GridSize };
        canvas.Children.Add(new XamlPath
        {
            Data = TablerDeviceIcons.Geometry(kind),
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });
        return canvas;
    }

    /// <summary>把设备图形画进 <paramref name="box"/>（像素、正方形），图形本体按 24 网格居中。</summary>
    public static void DrawGlyph(Gdi.Graphics g, DeviceKind kind, Gdi.Color stroke, Gdi.RectangleF box)
    {
        float scale = Math.Min(box.Width, box.Height) / (float)GridSize;
        float originX = box.X + (box.Width - (float)GridSize * scale) / 2f;
        float originY = box.Y + (box.Height - (float)GridSize * scale) / 2f;

        using var pen = new Gdi.Pen(stroke, Math.Max(1f, (float)ListStroke * scale))
        {
            LineJoin = Gdi2.LineJoin.Round,
            StartCap = Gdi2.LineCap.Round,
            EndCap = Gdi2.LineCap.Round,
        };
        TablerDeviceIcons.Draw(g, pen, kind, new Gdi.PointF(originX, originY), scale);
    }
}
