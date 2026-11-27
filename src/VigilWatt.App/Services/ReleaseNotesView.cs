using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 把 ReleaseNotesSection 渲染成 WinUI 控件，关于页与更新确认框共用。
/// 2026/10/18 21:44:00 xliu
/// 只认 release-notes.md 约定的四种行，不做通用 Markdown（引第三方库不值）。
/// </summary>
internal static class ReleaseNotesView
{
    private static Brush? Secondary => Brush("TextFillColorSecondaryBrush");

    /// <summary>带滚动条的一段说明；空段返回 null，调用方据此决定是否显示整块。</summary>
    public static ScrollViewer? BuildScrollable(ReleaseNotesSection? section, double maxHeight)
    {
        if (section == null || section.IsEmpty) return null;

        return new ScrollViewer
        {
            Content = BuildBody(section),
            MaxHeight = maxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
        };
    }

    public static StackPanel BuildBody(ReleaseNotesSection? section)
    {
        var panel = new StackPanel { Spacing = 6 };
        if (section == null) return panel;

        foreach (var line in section.Lines)
        {
            switch (line.Kind)
            {
                case ReleaseNoteKind.Subheading:
                    panel.Children.Add(new TextBlock
                    {
                        Text = line.Text,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Secondary,
                        Margin = new Thickness(0, 6, 0, 0),
                    });
                    break;
                case ReleaseNoteKind.Bullet:
                    panel.Children.Add(BulletRow(line.Text));
                    break;
                default:
                    panel.Children.Add(new TextBlock
                    {
                        Text = line.Text,
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                    });
                    break;
            }
        }

        return panel;
    }

    /// <summary>「1.2.1 · 2026-10-18」这一行；没有日期就只显示版本号。</summary>
    public static TextBlock BuildVersionHeader(ReleaseNotesSection section)
    {
        return new TextBlock
        {
            Text = string.IsNullOrEmpty(section.Date) ? section.Version : $"{section.Version} · {section.Date}",
            FontSize = 12,
            Foreground = Secondary,
        };
    }

    public static TextBlock BuildMissingHint(string version)
    {
        return new TextBlock
        {
            Text = Strings.Current.Format("S_30549b4725", version),
            FontSize = 13,
            Foreground = Secondary,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    /// <summary>按 key 取主题画刷；取不到返回 null（老系统主题字典可能缺项），别让整块不显示。</summary>
    internal static Brush? Brush(string key)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return null;
        return resources.TryGetValue(key, out var value) ? value as Brush : null;
    }

    // 横向 StackPanel 里 TextWrapping 不生效（宽度无穷），长条目会顶出对话框，所以用两列 Grid
    private static Grid BulletRow(string text)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new TextBlock
        {
            Text = "·",
            FontSize = 13,
            Foreground = Secondary,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var body = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(body, 1);

        grid.Children.Add(dot);
        grid.Children.Add(body);
        return grid;
    }
}
