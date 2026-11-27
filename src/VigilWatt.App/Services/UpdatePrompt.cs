using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Velopack;
using VigilWatt.Core.Utils;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 发现新版本时的确认框，启动检查与关于页手动检查共用。
/// 2026/10/18 21:47:00 xliu
/// 更新内容取自更新源的 NotesMarkdown；取不到就只显示版本行，不要空框。
/// </summary>
internal static class UpdatePrompt
{
    /// <summary>true = 用户选了「下载并安装」。closeText 由调用方决定（稍后 / 取消）。</summary>
    public static async Task<bool> ShowAsync(UpdateService service, UpdateInfo info, XamlRoot root, string closeText)
    {
        var version = info.TargetFullRelease.Version.ToString();
        var muted = ReleaseNotesView.Brush("TextFillColorSecondaryBrush");

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = Strings.Current.Format("S_8065a3acdf", service.CurrentVersion, version),
            FontSize = 13,
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap,
        });

        var notes = ReleaseNotes.FromFeed(info.TargetFullRelease.NotesMarkdown, version);
        if (notes != null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Strings.Current.Get("S_e86c7cc37a"),
                FontSize = 12,
                Foreground = muted,
                Margin = new Thickness(0, 4, 0, 0),
            });
            panel.Children.Add(ReleaseNotesView.BuildScrollable(notes, 320)!);
        }
        else
        {
            Log.Info($"版本 {version} 没有可显示的更新说明（更新源未提供 notes）");
        }

        panel.Children.Add(new TextBlock
        {
            Text = Strings.Current.Get("S_e1fd9a1da1"),
            FontSize = 12,
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new ContentDialog
        {
            Title = Strings.Current.Get("S_bdeaca9059") + version,
            Content = panel,
            PrimaryButtonText = Strings.Current.Get("S_f4df9977ea"),
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
