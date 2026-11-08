using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using VigilWatt.Core.Models;
using Windows.Storage;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 将当前电量行绘成 Windows 动态磁贴可读取的本地图像。
/// 2026/11/08 11:55:00 pupprise
/// </summary>
internal static class TileBatteryArt
{
    internal readonly record struct Row(string Name, DeviceKind Kind, int? Percent, bool Charging, bool Low)
    {
        public string Value => Percent is int percent ? $"{percent}%" : "—";
        public string Status => Charging ? "充电中" : Low ? "低电量" : Percent == null ? "暂无读数" : "";
        public string Description => $"{Name} {Value} {Status}".TrimEnd();
    }

    public static string BuildBindings(Row[] rows)
    {
        var description = rows.Length == 0 ? "暂无设备电量" : string.Join("，", rows.Select(row => row.Description));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("type-v2|" + description
            + "|" + string.Join(",", rows.Select(row => row.Kind)))))[..24];
        var folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "tile-art");
        Directory.CreateDirectory(folder);
        var medium = WriteImage(folder, key + "-medium", 150, rows);
        var wide = WriteImage(folder, key + "-wide", 310, rows);
        var large = WriteImage(folder, key + "-large", 310, rows, large: true);

        foreach (var file in Directory.EnumerateFiles(folder, "*.png"))
        {
            if (file != medium && file != wide && file != large && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddMinutes(-2))
                File.Delete(file);
        }

        var primary = rows.Length == 0 ? "—" : rows[0].Value;
        var alt = SecurityElement.Escape(description);
        return $"<binding template='TileSmall'><text>{primary}</text></binding>"
            + ImageBinding("TileMedium", Path.GetFileName(medium), alt)
            + ImageBinding("TileWide", Path.GetFileName(wide), alt)
            + ImageBinding("TileLarge", Path.GetFileName(large), alt);
    }

    private static string ImageBinding(string template, string file, string? alt) =>
        $"<binding template='{template}' branding='none'><image placement='background' hint-overlay='0' src='ms-appdata:///local/tile-art/{file}' alt='{alt}' /></binding>";

    private static string WriteImage(string folder, string name, int width, Row[] rows, bool large = false)
    {
        var path = Path.Combine(folder, name + ".png");
        if (File.Exists(path)) return path;
        using var bitmap = large ? RenderLarge(rows) : Render(width, rows);
        var temporary = path + ".tmp";
        try
        {
            bitmap.Save(temporary, ImageFormat.Png);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
        return path;
    }

    internal static Bitmap RenderLarge(Row[] rows) => RenderAtScale(150, rows, 620f / 150);

    internal static Bitmap Render(int width, Row[] rows) => RenderAtScale(width, rows, 2);

    private static Bitmap RenderAtScale(int width, Row[] rows, float scale)
    {
        var bitmap = new Bitmap((int)Math.Round(width * scale), (int)Math.Round(150 * scale));
        using var g = Graphics.FromImage(bitmap);
        g.ScaleTransform(scale, scale);
        g.Clear(Color.FromArgb(32, 42, 39));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var nameFont = new Font("Microsoft YaHei UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelFont = new Font("Microsoft YaHei UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
        using var foreground = new SolidBrush(Color.FromArgb(244, 249, 245));
        using var muted = new SolidBrush(Color.FromArgb(196, 213, 202));
        using var left = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
        using var right = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip) { Alignment = StringAlignment.Far };
        const float inset = 12;

        if (rows.Length == 0)
        {
            g.DrawString("暂无设备电量", nameFont, muted, new PointF(inset, 64));
            return bitmap;
        }

        if (rows.Length == 1)
        {
            DrawRing(g, width, rows[0], nameFont, labelFont, foreground, muted);
            return bitmap;
        }

        var top = 19;
        foreach (var row in rows)
        {
            var color = Accent(row);
            using var accent = new SolidBrush(color);
            DeviceIconFactory.DrawGlyph(g, row.Kind, foreground.Color, new RectangleF(inset, top + 2, 18, 17), 1.35f);
            var name = width != 150 ? row.Name
                : row.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ? "Xbox"
                : row.Name.Contains("EDIFIER", StringComparison.OrdinalIgnoreCase) ? "EDIFIER"
                : row.Name;
            g.DrawString(name, nameFont, muted, new RectangleF(inset + 23, top + 3, width - 88, 18), left);
            DrawNumber(g, row.Value, width == 150 ? 21 : 26, foreground,
                new RectangleF(width - 68, top - 4, 56, 29), right);

            using var track = new Pen(Color.FromArgb(62, 79, 70), 6) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var progress = new Pen(color, 6) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var length = width - 2 * inset - 6;
            var x = inset + 3;
            g.DrawLine(track, x, top + 29, x + length, top + 29);
            if (row.Percent is > 0 and <= 100) g.DrawLine(progress, x, top + 29, x + length * row.Percent.Value / 100, top + 29);

            if (row.Status.Length > 0)
                g.DrawString(row.Low ? "! " + row.Status : row.Status, labelFont, row.Percent == null ? muted : accent, new PointF(inset, top + 37));
            top += 63;
        }
        return bitmap;
    }

    private static Color Accent(Row row) => row.Percent == null ? Color.FromArgb(174, 188, 180)
        : row.Low ? Color.FromArgb(255, 153, 147)
        : row.Charging ? Color.FromArgb(166, 196, 255)
        : Color.FromArgb(121, 222, 170);

    private static void DrawRing(Graphics g, int width, Row row, Font nameFont, Font labelFont,
        SolidBrush foreground, SolidBrush muted)
    {
        var wide = width > 150;
        var ring = new RectangleF(wide ? 29 : 33, wide ? 32 : 15, 84, 84);
        using var track = new Pen(Color.FromArgb(62, 79, 70), 6);
        using var progress = new Pen(Accent(row), 6) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var accent = new SolidBrush(Accent(row));
        using var center = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip)
        {
            Alignment = StringAlignment.Center,
        };
        using var left = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
        g.DrawEllipse(track, ring);
        if (row.Percent is >= 100) g.DrawEllipse(progress, ring);
        else if (row.Percent is > 0) g.DrawArc(progress, ring, -90, 3.6f * row.Percent.Value);

        DeviceIconFactory.DrawGlyph(g, row.Kind, foreground.Color,
            new RectangleF(ring.X + 33, ring.Y + 16, 18, 17), 1.35f);
        DrawNumber(g, row.Value, 30, foreground, new RectangleF(ring.X, ring.Y + 32, ring.Width, 38), center);

        var name = wide ? row.Name
            : row.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ? "Xbox 手柄"
            : row.Name.Contains("EDIFIER", StringComparison.OrdinalIgnoreCase) ? "EDIFIER 耳机"
            : row.Name;
        var label = wide ? new RectangleF(137, 55, width - 151, 20) : new RectangleF(9, 107, width - 18, 19);
        if (wide) g.DrawString(name, nameFont, foreground, label, left);
        else
        {
            using var centeredName = new StringFormat(StringFormatFlags.NoWrap)
            {
                Alignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
            };
            g.DrawString(name, nameFont, foreground, label, centeredName);
        }

        if (row.Status.Length == 0) return;
        var status = row.Low ? "! " + row.Status : row.Status;
        label.Y += 19;
        g.DrawString(status, labelFont, row.Percent == null ? muted : accent, label, wide ? left : center);
    }

    private static void DrawNumber(Graphics g, string value, float size, SolidBrush color,
        RectangleF bounds, StringFormat format)
    {
        using var family = new FontFamily("Segoe UI");
        using var outline = new GraphicsPath();
        outline.AddString(value, family, (int)FontStyle.Bold, size, bounds, format);
        using var round = new Pen(color.Color, 0.7f) { LineJoin = LineJoin.Round };
        g.DrawPath(round, outline);
        g.FillPath(color, outline);
    }
}
