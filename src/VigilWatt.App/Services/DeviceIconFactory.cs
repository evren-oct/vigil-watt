using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VigilWatt.Core.Models;
using Windows.UI;
using Gdi = System.Drawing;
using Gdi2 = System.Drawing.Drawing2D;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 设备类型图标的绘制工厂：桌面小组件与任务栏贴片共用同一套图形，避免两处各画一份而慢慢分叉。
/// 2026/10/10 21:34:00 xliu
/// 全部用纯形状拼装，**不用字体图标**——Segoe 字体缺字形时会渲染成方块（见 dev/runtime-pitfalls.md §5.3）。
/// </summary>
public static class DeviceIconFactory
{
    /// <summary>带圆角方块底的完整图标（小组件用）；<paramref name="size"/> 是方块边长。</summary>
    public static FrameworkElement Boxed(DeviceKind kind, Color fill, Color stroke, double size) =>
        new Grid
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(fill),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Glyph(kind, stroke) },
        };

    /// <summary>定尺图标（设备列表页用）：名义框统一 14×13 再整体缩放，各类型视觉大小才一致。</summary>
    public static FrameworkElement Scaled(DeviceKind kind, Color stroke, double size) =>
        new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = new Grid
            {
                Width = 14,
                Height = 13,
                Children = { Glyph(kind, stroke) },
            },
        };

    /// <summary>
    /// 详情页 Hero 牌里的图形：名义框放到 20×20 再缩放。
    /// 2026/11/11 22:18:00 20211
    /// 直接复用 <see cref="Scaled"/> 会按 14×13 整体放大，34px 下描边变成 3px 以上、显得笨重。
    /// </summary>
    public static FrameworkElement Hero(DeviceKind kind, Color stroke, double size) =>
        new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = new Grid
            {
                Width = 20,
                Height = 20,
                Children = { Glyph(kind, stroke) },
            },
        };

    /// <summary>设备图形本身（无底），任务栏贴片直接用它。</summary>
    public static UIElement Glyph(DeviceKind kind, Color stroke)
    {
        var brush = new SolidColorBrush(stroke);

        switch (kind)
        {
            case DeviceKind.Keyboard:
            {
                var box = new Grid { Width = 13, Height = 9 };
                box.Children.Add(new Border
                {
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(2.5),
                    BorderBrush = brush,
                });
                box.Children.Add(new Border
                {
                    Width = 7,
                    Height = 1.4,
                    Background = brush,
                    CornerRadius = new CornerRadius(0.7),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 1.8),
                });
                return box;
            }

            case DeviceKind.Mouse:
            {
                var box = new Grid { Width = 9, Height = 13 };
                box.Children.Add(new Border
                {
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(4.5),
                    BorderBrush = brush,
                });
                box.Children.Add(new Border
                {
                    Width = 1.2,
                    Height = 3.5,
                    Background = brush,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                });
                return box;
            }

            case DeviceKind.Headset:
            {
                var box = new Grid { Width = 13, Height = 12 };
                box.Children.Add(new Border
                {
                    Width = 11,
                    Height = 7,
                    CornerRadius = new CornerRadius(5.5, 5.5, 0, 0),
                    // 右缘也要描边，底边开口。与 GDI 头梁同一造型，见 runtime-pitfalls.md §16.9。
                    BorderThickness = new Thickness(1.2, 1.2, 1.2, 0),
                    BorderBrush = brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                });
                foreach (var h in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
                {
                    box.Children.Add(new Border
                    {
                        Width = 2.6,
                        Height = 4.5,
                        CornerRadius = new CornerRadius(1.3),
                        Background = brush,
                        HorizontalAlignment = h,
                        VerticalAlignment = VerticalAlignment.Bottom,
                    });
                }
                return box;
            }

            case DeviceKind.Speaker:
            {
                var box = new Grid { Width = 9.5, Height = 13 };
                box.Children.Add(new Border
                {
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(2.5),
                    BorderBrush = brush,
                });
                box.Children.Add(new Ellipse
                {
                    Width = 3,
                    Height = 3,
                    Fill = brush,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2.2, 0, 0),
                });
                box.Children.Add(new Ellipse
                {
                    Width = 4.4,
                    Height = 4.4,
                    Fill = brush,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 2),
                });
                return box;
            }

            case DeviceKind.Gamepad:
            {
                var box = new Grid { Width = 14, Height = 9 };
                box.Children.Add(new Border
                {
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(4.5),
                    BorderBrush = brush,
                });
                foreach (var h in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
                {
                    box.Children.Add(new Ellipse
                    {
                        Width = 2.6,
                        Height = 2.6,
                        Fill = brush,
                        HorizontalAlignment = h,
                        Margin = new Thickness(2, 0, 2, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                return box;
            }

            case DeviceKind.Computer:
            {
                // 显示器造型：屏幕 + 支架。与设置/列表里的 \uE7F8 语义一致。
                var box = new Grid { Width = 14, Height = 11 };
                box.Children.Add(new Border
                {
                    Width = 14,
                    Height = 9.5,
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(1.8),
                    BorderBrush = brush,
                    VerticalAlignment = VerticalAlignment.Top,
                });
                box.Children.Add(new Border
                {
                    Width = 5,
                    Height = 1.4,
                    Background = brush,
                    CornerRadius = new CornerRadius(0.7),
                    VerticalAlignment = VerticalAlignment.Bottom,
                });
                return box;
            }

            case DeviceKind.Phone:
            {
                // 窄长机身 + 顶部听筒条 + 底部 home 条：与音箱（两圆）靠"横线 vs 圆"区分
                var box = new Grid { Width = 8, Height = 13 };
                box.Children.Add(new Border
                {
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(2),
                    BorderBrush = brush,
                });
                box.Children.Add(new Border
                {
                    Width = 3,
                    Height = 1,
                    Background = brush,
                    CornerRadius = new CornerRadius(0.5),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 1.2, 0, 0),
                });
                box.Children.Add(new Border
                {
                    Width = 2,
                    Height = 0.9,
                    Background = brush,
                    CornerRadius = new CornerRadius(0.45),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 1.2),
                });
                return box;
            }

            case DeviceKind.Tablet:
            {
                // 比手机宽、圆角更小、底部只有 home 圆点
                var box = new Grid { Width = 11, Height = 13 };
                box.Children.Add(new Border
                {
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(1.8),
                    BorderBrush = brush,
                });
                box.Children.Add(new Ellipse
                {
                    Width = 1.8,
                    Height = 1.8,
                    Fill = brush,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 0.7),
                });
                return box;
            }

            case DeviceKind.Watch:
            {
                // 唯一带外突元素的造型：上下表带 + 表壳
                var box = new Grid { Width = 9, Height = 13 };
                foreach (var v in new[] { VerticalAlignment.Top, VerticalAlignment.Bottom })
                {
                    box.Children.Add(new Border
                    {
                        Width = 4,
                        Height = 2.6,
                        CornerRadius = new CornerRadius(0.8),
                        Background = brush,
                        VerticalAlignment = v,
                    });
                }
                box.Children.Add(new Border
                {
                    Width = 8.4,
                    Height = 8.4,
                    CornerRadius = new CornerRadius(2.4),
                    BorderThickness = new Thickness(1.2),
                    BorderBrush = brush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                return box;
            }

            default:
                return new Ellipse
                {
                    Width = 9,
                    Height = 9,
                    Stroke = brush,
                    StrokeThickness = 1.4,
                };
        }
    }

    // ---- GDI+ 版本（任务栏贴片用）----
    // 贴片是**原生分层窗口**、内容走 ARGB 位图，用不了 XAML 元素，所以同一套造型在这里再画一遍。
    // 与上面 XAML 版的造型必须保持一致（同一份设计两套渲染器，改一处记得改另一处）。

    /// <summary>把设备图形画进 <paramref name="box"/>（像素、正方形），图形本体按名义尺寸居中。</summary>
    public static void DrawGlyph(Gdi.Graphics g, DeviceKind kind, Gdi.Color stroke, Gdi.RectangleF box, float scale)
    {
        var (nominalW, nominalH) = NominalSize(kind);
        var w = nominalW * scale;
        var h = nominalH * scale;
        var x = box.X + (box.Width - w) / 2f;
        var y = box.Y + (box.Height - h) / 2f;
        var rect = new Gdi.RectangleF(x, y, w, h);

        var penW = 1.2f * scale;
        using var pen = new Gdi.Pen(stroke, penW) { LineJoin = Gdi2.LineJoin.Round };
        using var fill = new Gdi.SolidBrush(stroke);

        switch (kind)
        {
            case DeviceKind.Keyboard:
                using (var p = RoundedRect(rect, 2.5f * scale))
                    g.DrawPath(pen, p);
                g.FillRectangle(fill, x + (w - 7 * scale) / 2f, y + h - 1.8f * scale - 1.4f * scale, 7 * scale, 1.4f * scale);
                break;

            case DeviceKind.Mouse:
                using (var p = RoundedRect(rect, 4.5f * scale))
                    g.DrawPath(pen, p);
                g.FillRectangle(fill, x + (w - penW) / 2f, y + 2 * scale, penW, 3.5f * scale);
                break;

            case DeviceKind.Headset:
            {
                DrawHeadsetBand(g, pen, x, y, w, scale);
                foreach (var left in new[] { true, false })
                {
                    var cx = left ? x : x + w - 2.6f * scale;
                    using var p = RoundedRect(new Gdi.RectangleF(cx, y + h - 4.5f * scale, 2.6f * scale, 4.5f * scale), 1.3f * scale);
                    g.FillPath(fill, p);
                }
                break;
            }

            case DeviceKind.Speaker:
                using (var p = RoundedRect(rect, 2.5f * scale))
                    g.DrawPath(pen, p);
                g.FillEllipse(fill, x + (w - 3 * scale) / 2f, y + 2.2f * scale, 3 * scale, 3 * scale);
                g.FillEllipse(fill, x + (w - 4.4f * scale) / 2f, y + h - 2 * scale - 4.4f * scale, 4.4f * scale, 4.4f * scale);
                break;

            case DeviceKind.Gamepad:
                using (var p = RoundedRect(rect, 4.5f * scale))
                    g.DrawPath(pen, p);
                foreach (var left in new[] { true, false })
                {
                    var cx = left ? x + 2 * scale : x + w - 2 * scale - 2.6f * scale;
                    g.FillEllipse(fill, cx, y + (h - 2.6f * scale) / 2f, 2.6f * scale, 2.6f * scale);
                }
                break;

            case DeviceKind.Computer:
            {
                var screenH = 9.5f * scale;
                using (var p = RoundedRect(new Gdi.RectangleF(x, y, w, screenH), 1.8f * scale))
                    g.DrawPath(pen, p);
                g.FillRectangle(fill, x + (w - 5 * scale) / 2f, y + h - 1.4f * scale, 5 * scale, 1.4f * scale);
                break;
            }

            case DeviceKind.Phone:
                using (var p = RoundedRect(rect, 2 * scale))
                    g.DrawPath(pen, p);
                g.FillRectangle(fill, x + (w - 3 * scale) / 2f, y + 1.2f * scale, 3 * scale, 1 * scale);
                g.FillRectangle(fill, x + (w - 2 * scale) / 2f, y + h - 2.1f * scale, 2 * scale, 0.9f * scale);
                break;

            case DeviceKind.Tablet:
                using (var p = RoundedRect(rect, 1.8f * scale))
                    g.DrawPath(pen, p);
                g.FillEllipse(fill, x + (w - 1.8f * scale) / 2f, y + h - 2.5f * scale, 1.8f * scale, 1.8f * scale);
                break;

            case DeviceKind.Watch:
            {
                foreach (var top in new[] { true, false })
                {
                    var bandY = top ? y : y + h - 2.6f * scale;
                    using var band = RoundedRect(
                        new Gdi.RectangleF(x + (w - 4 * scale) / 2f, bandY, 4 * scale, 2.6f * scale), 0.8f * scale);
                    g.FillPath(fill, band);
                }
                using var face = RoundedRect(
                    new Gdi.RectangleF(x + (w - 8.4f * scale) / 2f, y + (h - 8.4f * scale) / 2f, 8.4f * scale, 8.4f * scale),
                    2.4f * scale);
                g.DrawPath(pen, face);
                break;
            }

            default:
                using (var p = new Gdi2.GraphicsPath())
                {
                    p.AddEllipse(rect);
                    using var thin = new Gdi.Pen(stroke, 1.4f * scale);
                    g.DrawPath(thin, p);
                }
                break;
        }
    }

    /// <summary>头梁用对称贝塞尔并在名义框里居中。不用 DrawArc，见 runtime-pitfalls.md §16.9。</summary>
    private static void DrawHeadsetBand(Gdi.Graphics g, Gdi.Pen pen, float originX, float originY, float glyphW, float scale)
    {
        const float kappa = 0.5522848f; // 四分之一圆的贝塞尔系数，左右控制点镜像
        float bandW = 11f * scale;
        float bandX = originX + (glyphW - bandW) / 2f;
        float inset = pen.Width / 2f;
        float left = bandX + inset;
        float right = bandX + bandW - inset;
        float top = originY + inset;
        float bottom = originY + 7f * scale;
        float radius = (right - left) / 2f;
        float cx = (left + right) / 2f;
        float cy = top + radius;
        float k = kappa * radius;

        using var path = new Gdi2.GraphicsPath();
        path.AddLine(left, bottom, left, cy);
        path.AddBezier(left, cy, left, cy - k, cx - k, cy - radius, cx, cy - radius);
        path.AddBezier(cx, cy - radius, cx + k, cy - radius, right, cy - k, right, cy);
        path.AddLine(right, cy, right, bottom);
        g.DrawPath(pen, path);
    }

    private static (float W, float H) NominalSize(DeviceKind kind) => kind switch
    {
        DeviceKind.Keyboard => (13f, 9f),
        DeviceKind.Mouse => (9f, 13f),
        DeviceKind.Headset => (13f, 12f),
        DeviceKind.Speaker => (9.5f, 13f),
        DeviceKind.Gamepad => (14f, 9f),
        DeviceKind.Computer => (14f, 11f),
        DeviceKind.Phone => (8f, 13f),
        DeviceKind.Tablet => (11f, 13f),
        DeviceKind.Watch => (9f, 13f),
        _ => (9f, 9f),
    };

    private static Gdi2.GraphicsPath RoundedRect(Gdi.RectangleF r, float radius)
    {
        var path = new Gdi2.GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
