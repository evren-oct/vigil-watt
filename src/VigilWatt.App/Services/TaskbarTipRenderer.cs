using Gdi = System.Drawing;
using Gdi2 = System.Drawing.Drawing2D;
using GdiImaging = System.Drawing.Imaging;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 任务栏贴片悬停提示的位图绘制（类 Win11：圆角、细边框、轻阴影）。
/// 2026/10/15 17:20:00 xliu
/// 贴片跨进程子窗口用不了系统 ToolTip，自绘分层窗；样式约定见 runtime-pitfalls.md §16.10。
/// </summary>
public static class TaskbarTipRenderer
{
    public const double PadH = 10;
    public const double PadV = 6;
    public const double FontSize = 12;
    public const double Radius = 6;
    /// <summary>提示最大宽度：要放得下"DPI 1600 · 档位 4 · 1000 Hz"这一行而不折行。</summary>
    public const double MaxWidth = 280;
    public const double Shadow = 6;
    public const double ShadowOffsetY = 1;

    public readonly record struct Skin(Gdi.Color Fill, Gdi.Color Border, Gdi.Color Text, Gdi.Color Shadow);

    public static Skin SkinFor(bool light) => light
        ? new Skin(
            Fill: Gdi.Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3),
            Border: Gdi.Color.FromArgb(0xFF, 0xE0, 0xE0, 0xE0),
            Text: Gdi.Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A),
            Shadow: Gdi.Color.FromArgb(0x38, 0x00, 0x00, 0x00))
        : new Skin(
            Fill: Gdi.Color.FromArgb(0xFF, 0x2C, 0x2C, 0x2C),
            Border: Gdi.Color.FromArgb(0xFF, 0x3F, 0x3F, 0x3F),
            Text: Gdi.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            Shadow: Gdi.Color.FromArgb(0x55, 0x00, 0x00, 0x00));

    /// <summary>画提示；位图含阴影边距，<paramref name="bubbleW"/>/<paramref name="bubbleH"/> 是可见气泡。</summary>
    public static Gdi.Bitmap Render(
        string text, Skin skin, float scale,
        out int widthPx, out int heightPx, out int shadowPx, out int bubbleW, out int bubbleH)
    {
        shadowPx = (int)Math.Round(Shadow * scale);
        int padH = (int)Math.Round(PadH * scale);
        int padV = (int)Math.Round(PadV * scale);
        int maxTextW = (int)Math.Round(MaxWidth * scale);
        float radius = (float)(Radius * scale);
        int shadowOy = (int)Math.Round(ShadowOffsetY * scale);

        using var font = new Gdi.Font("Microsoft YaHei UI", (float)(FontSize * scale),
            Gdi.FontStyle.Regular, Gdi.GraphicsUnit.Pixel);
        using var measureBmp = new Gdi.Bitmap(1, 1, GdiImaging.PixelFormat.Format32bppPArgb);
        using var measureG = Gdi.Graphics.FromImage(measureBmp);
        var textSize = measureG.MeasureString(text, font, maxTextW);

        bubbleW = Math.Max(1, (int)Math.Ceiling(textSize.Width) + padH * 2);
        bubbleH = Math.Max(1, (int)Math.Ceiling(textSize.Height) + padV * 2);
        widthPx = bubbleW + shadowPx * 2;
        heightPx = bubbleH + shadowPx * 2 + Math.Max(0, shadowOy);

        var bmp = new Gdi.Bitmap(widthPx, heightPx, GdiImaging.PixelFormat.Format32bppPArgb);
        using var g = Gdi.Graphics.FromImage(bmp);
        g.SmoothingMode = Gdi2.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Gdi.Color.Transparent);

        float bx = shadowPx;
        float by = shadowPx;
        // 阴影：几层递减 alpha 的圆角，近似软边
        for (int i = shadowPx; i >= 1; i--)
        {
            float t = i / (float)shadowPx;
            int a = (int)(skin.Shadow.A * t * t);
            using var path = RoundRect(bx - i, by - i + shadowOy, bubbleW + i * 2, bubbleH + i * 2, radius + i);
            using var brush = new Gdi.SolidBrush(Gdi.Color.FromArgb(a, skin.Shadow));
            g.FillPath(brush, path);
        }

        using (var path = RoundRect(bx, by, bubbleW, bubbleH, radius))
        {
            using (var brush = new Gdi.SolidBrush(skin.Fill))
                g.FillPath(brush, path);
            using var pen = new Gdi.Pen(skin.Border, Math.Max(1f, scale));
            g.DrawPath(pen, path);
        }

        using (var brush = new Gdi.SolidBrush(skin.Text))
            g.DrawString(text, font, brush, new Gdi.RectangleF(bx + padH, by + padV, bubbleW - padH * 2, bubbleH - padV * 2));

        return bmp;
    }

    private static Gdi2.GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2f));
        var path = new Gdi2.GraphicsPath();
        if (r < 0.5f)
        {
            path.AddRectangle(new Gdi.RectangleF(x, y, w, h));
            return path;
        }
        float d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
