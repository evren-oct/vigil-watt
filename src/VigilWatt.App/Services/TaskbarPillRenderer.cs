using VigilWatt.Core.Models;
using VigilWatt.Core.Devices;
using Gdi = System.Drawing;
using Gdi2 = System.Drawing.Drawing2D;
using GdiImaging = System.Drawing.Imaging;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 任务栏贴片的绘制：把"每台设备一格"画进一张 32 位预乘 ARGB 位图，交给原生分层窗口提交 （贴片是 <c>SetParent</c> 进任务栏的**原生子窗口**，用不了 XAML，原理见 runtime-pitfalls.md §16）。
/// 2026/10/12 17:20:14 xliu
/// **不画底色与边框**——贴片是浮在任务栏上的一行读数，加了底框就会糊出一个突兀的方块。
/// </summary>
public static class TaskbarPillRenderer
{
    /// <summary>贴片左右内边距（DIP）。</summary>
    public const double PadH = 6;

    private const double IconBox = 15;
    private const double IconTextGap = 4;
    private const double BoltW = 7, BoltH = 11;
    private const double DividerW = 1, DividerH = 14;
    private const double PercentFontSize = 13;
    private const double EmptyFontSize = 12;
    private const double EmptyTextWidthDip = 104;

    /// <summary>宽度余量（像素）：字宽量出来差一两个像素就会把最右一格挤出窗口（历史事故）。</summary>
    private const int SlackPx = 8;

    private static readonly Gdi.Bitmap MeasureBmp = new(1, 1, GdiImaging.PixelFormat.Format32bppPArgb);
    private static readonly Gdi.Graphics MeasureG = Gdi.Graphics.FromImage(MeasureBmp);

    /// <summary>贴片前景配色。**没有底色/边框**，只有前景三项。</summary>
    public readonly record struct Skin(Gdi.Color Divider, Gdi.Color IconStroke, Gdi.Color Text);

    public static Skin SkinFor(bool light) => light
        ? new Skin(
            Divider: Gdi.Color.FromArgb(0x33, 0x00, 0x00, 0x00),
            IconStroke: Gdi.Color.FromArgb(0xFF, 0x5F, 0x5E, 0x5A),
            Text: Gdi.Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F))
        : new Skin(
            Divider: Gdi.Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF),
            IconStroke: Gdi.Color.FromArgb(0xFF, 0xD2, 0xD2, 0xD2),
            Text: Gdi.Color.FromArgb(0xFF, 0xFF, 0xFF));

    /// <summary>悬停命中一格：客户区 X/宽与提示文案同源自绘制，避免另算宽度偏一两个像素。</summary>
    public readonly record struct HitChip(string Id, string Tip, int X, int Width);

    /// <summary>折叠格（+N）的稳定 Id，重绘后仍能对上同一悬停目标。</summary>
    public const string OverflowHitId = ":overflow:";

    /// <summary>画一整条贴片，返回位图；<paramref name="widthPx"/> 是裁掉右侧空白后的实际宽度。</summary>
    public static Gdi.Bitmap Render(
        IReadOnlyList<DeviceStatus> devices, IReadOnlyList<string> overflowNames,
        IReadOnlyDictionary<string, RemainingTimeEstimateResult>? remainingTimeEstimates,
        Skin skin, int heightPx, float scale, out int widthPx, out IReadOnlyList<HitChip> hits)
    {
        using var percentFont = new Gdi.Font("Microsoft YaHei UI", (float)(PercentFontSize * scale), Gdi.FontStyle.Bold, Gdi.GraphicsUnit.Pixel);
        using var plainFont = new Gdi.Font("Microsoft YaHei UI", (float)(EmptyFontSize * scale), Gdi.FontStyle.Regular, Gdi.GraphicsUnit.Pixel);
        using var sf = (Gdi.StringFormat)Gdi.StringFormat.GenericTypographic.Clone();
        sf.FormatFlags |= Gdi.StringFormatFlags.NoWrap | Gdi.StringFormatFlags.MeasureTrailingSpaces;
        sf.LineAlignment = Gdi.StringAlignment.Center;

        var pieces = BuildPieces(devices, overflowNames, scale, percentFont, plainFont, sf, out float contentW);
        int width = (int)Math.Ceiling(contentW + PadH * 2 * scale) + SlackPx;
        var bmp = new Gdi.Bitmap(width, heightPx, GdiImaging.PixelFormat.Format32bppPArgb);

        using (var g = Gdi.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Gdi2.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Gdi.Color.Transparent);
            using (var hit = new Gdi.SolidBrush(Gdi.Color.FromArgb(1, 0, 0, 0)))
                g.FillRectangle(hit, 0, 0, width, heightPx);
            DrawPieces(g, pieces, skin, heightPx, scale, percentFont, plainFont, sf);
        }

        widthPx = Math.Min(width, ContentRight(bmp) + (int)Math.Ceiling(PadH * scale));
        hits = BuildHits(pieces, scale, remainingTimeEstimates);
        return bmp;
    }

    private enum PieceKind { Chip, Divider, Overflow, Empty }

    private readonly record struct Piece(PieceKind Kind, DeviceStatus? Device, int Overflow, string? OverflowTip, float X, float Width);

    private static List<Piece> BuildPieces(
        IReadOnlyList<DeviceStatus> devices, IReadOnlyList<string> overflowNames, float scale,
        Gdi.Font percentFont, Gdi.Font plainFont, Gdi.StringFormat sf, out float contentWidth)
    {
        var pieces = new List<Piece>();
        float x = 0;
        int overflow = overflowNames.Count;

        if (devices.Count == 0)
        {
            float w = (float)(EmptyTextWidthDip * scale);
            pieces.Add(new Piece(PieceKind.Empty, null, 0, null, x, w));
            x += w;
        }
        else
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (i > 0)
                {
                    x += (float)(IconTextGap * scale);
                    pieces.Add(new Piece(PieceKind.Divider, null, 0, null, x, (float)(DividerW * scale)));
                    x += (float)(DividerW * scale) + (float)(IconTextGap * scale);
                }
                float w = ChipWidth(devices[i], scale, percentFont, sf);
                pieces.Add(new Piece(PieceKind.Chip, devices[i], 0, null, x, w));
                x += w;
            }
            if (overflow > 0)
            {
                x += (float)(IconTextGap * scale);
                pieces.Add(new Piece(PieceKind.Divider, null, 0, null, x, (float)(DividerW * scale)));
                x += (float)(DividerW * scale) + (float)(IconTextGap * scale);
                float w = Measure(MeasureG, $"+{overflow}", plainFont, sf);
                string tip = string.Join("\n", overflowNames);
                pieces.Add(new Piece(PieceKind.Overflow, null, overflow, tip, x, w));
                x += w;
            }
        }

        contentWidth = x;
        return pieces;
    }

    private static List<HitChip> BuildHits(List<Piece> pieces, float scale, IReadOnlyDictionary<string, RemainingTimeEstimateResult>? remainingTimeEstimates)
    {
        float padLeft = (float)(PadH * scale);
        var hits = new List<HitChip>();
        foreach (var piece in pieces)
        {
            if (piece.Kind == PieceKind.Chip && piece.Device != null)
            {
                string tip = piece.Device.DisplayName;
                if (remainingTimeEstimates != null && remainingTimeEstimates.TryGetValue(piece.Device.Id, out var remaining))
                    tip += remaining.IsAvailable
                        ? $"\n约{RemainingTimeEstimator.Format(remaining.Remaining!.Value)}剩余"
                        : $"\n{remaining.Explain()}";
                // 贴片横向放不下 DPI（单格会从 60px 涨到 120px），只挂在悬停提示里，宽度零成本
                if (PointerTelemetryFormatter.Summary(piece.Device, DateTimeOffset.Now) is { } pointer)
                    tip += $"\n{pointer}";
                hits.Add(new HitChip(
                    piece.Device.Id, tip,
                    (int)Math.Floor(padLeft + piece.X), (int)Math.Ceiling(piece.Width)));
            }
            else if (piece.Kind == PieceKind.Overflow && !string.IsNullOrEmpty(piece.OverflowTip))
            {
                hits.Add(new HitChip(
                    OverflowHitId, piece.OverflowTip,
                    (int)Math.Floor(padLeft + piece.X), (int)Math.Ceiling(piece.Width)));
            }
        }
        return hits;
    }

    private static float ChipWidth(DeviceStatus d, float scale, Gdi.Font percentFont, Gdi.StringFormat sf)
    {
        float w = (float)(IconBox * scale) + (float)(IconTextGap * scale);
        if (DeviceStatusFormatter.IsCharging(d.Charging))
            w += (float)(BoltW * scale) + (float)(IconTextGap * scale);
        return w + Measure(MeasureG, PercentText(d), percentFont, sf);
    }

    private static void DrawPieces(
        Gdi.Graphics g, List<Piece> pieces, Skin skin, int heightPx, float scale,
        Gdi.Font percentFont, Gdi.Font plainFont, Gdi.StringFormat sf)
    {
        float padLeft = (float)(PadH * scale);

        foreach (var piece in pieces)
        {
            float x = padLeft + piece.X;
            switch (piece.Kind)
            {
                case PieceKind.Divider:
                    using (var b = new Gdi.SolidBrush(skin.Divider))
                        g.FillRectangle(b, x, (heightPx - (float)(DividerH * scale)) / 2f, (float)(DividerW * scale), (float)(DividerH * scale));
                    break;

                case PieceKind.Empty:
                    DrawText(g, "暂无在线设备", plainFont, Fade(skin.Text, 0.6f), new Gdi.RectangleF(x, 0, piece.Width, heightPx), sf);
                    break;

                case PieceKind.Overflow:
                    DrawText(g, $"+{piece.Overflow}", percentFont, Fade(skin.Text, 0.6f), new Gdi.RectangleF(x, 0, piece.Width + 2, heightPx), sf);
                    break;

                case PieceKind.Chip:
                    DrawChip(g, piece.Device!, skin, x, heightPx, scale, percentFont, sf);
                    break;
            }
        }
    }

    private static void DrawChip(
        Gdi.Graphics g, DeviceStatus d, Skin skin, float x, int heightPx, float scale,
        Gdi.Font percentFont, Gdi.StringFormat sf)
    {
        var charging = DeviceStatusFormatter.IsCharging(d.Charging);
        var state = UiPalette.ToDrawing(UiPalette.BatteryLevel(d.BatteryPercent, charging));

        float iconBox = (float)(IconBox * scale);
        DeviceIconFactory.DrawGlyph(g, d.Kind, skin.IconStroke, new Gdi.RectangleF(x, (heightPx - iconBox) / 2f, iconBox, iconBox), scale);
        x += iconBox + (float)(IconTextGap * scale);

        if (charging)
        {
            DrawBolt(g, state, x, heightPx, scale);
            x += (float)(BoltW * scale) + (float)(IconTextGap * scale);
        }

        var text = PercentText(d);
        DrawText(g, text, percentFont, state, new Gdi.RectangleF(x, 0, Measure(g, text, percentFont, sf) + 2, heightPx), sf);
    }

    /// <summary>充电闪电：**自绘多边形而不是画 "⚡" 字符**——字体缺字形时会渲染成方块（runtime-pitfalls.md §5.3）。</summary>
    private static void DrawBolt(Gdi.Graphics g, Gdi.Color color, float x, int heightPx, float scale)
    {
        float w = (float)(BoltW * scale), h = (float)(BoltH * scale);
        float top = (heightPx - h) / 2f;
        using var path = new Gdi2.GraphicsPath();
        path.AddPolygon(
        [
            new Gdi.PointF(x + w * 0.58f, top),
            new Gdi.PointF(x, top + h * 0.56f),
            new Gdi.PointF(x + w * 0.42f, top + h * 0.56f),
            new Gdi.PointF(x + w * 0.30f, top + h),
            new Gdi.PointF(x + w, top + h * 0.42f),
            new Gdi.PointF(x + w * 0.54f, top + h * 0.42f),
        ]);
        using var brush = new Gdi.SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static void DrawText(Gdi.Graphics g, string text, Gdi.Font font, Gdi.Color color, Gdi.RectangleF rect, Gdi.StringFormat sf)
    {
        using var brush = new Gdi.SolidBrush(color);
        g.DrawString(text, font, brush, rect, sf);
    }

    private static float Measure(Gdi.Graphics g, string text, Gdi.Font font, Gdi.StringFormat sf) =>
        g.MeasureString(text, font, Gdi.PointF.Empty, sf).Width;

    private static string PercentText(DeviceStatus d) => d.BatteryPercent.HasValue ? $"{d.BatteryPercent}%" : "--";

    private static Gdi.Color Fade(Gdi.Color c, float factor) =>
        Gdi.Color.FromArgb((int)(c.A * factor), c.R, c.G, c.B);

    /// <summary>最右侧"有内容"的像素列（alpha &gt; 1，跳过铺底的 alpha=1）。裁掉右侧空白，宽度就不靠估算。</summary>
    private static int ContentRight(Gdi.Bitmap bmp)
    {
        var data = bmp.LockBits(
            new Gdi.Rectangle(0, 0, bmp.Width, bmp.Height), GdiImaging.ImageLockMode.ReadOnly,
            GdiImaging.PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[bmp.Width * 4];
            int right = 0;
            for (int y = 0; y < bmp.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = bmp.Width - 1; x > right; x--)
                {
                    if (row[x * 4 + 3] > 1)
                    {
                        right = x;
                        break;
                    }
                }
            }
            return right;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
