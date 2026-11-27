using System.Diagnostics;
using VigilWatt.App.Services;
using VigilWatt.Core.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Views;

/// <summary>
/// 功 能： 关于页：介绍、检查更新与项目联系。
/// 2026/10/16 15:37:49 xliu
/// </summary>
public sealed partial class AboutPage : Page
{
    private const string RepoUrl = "https://gitee.com/YGWG-LX/vigil-watt";
    private const string ReleasesUrl = "https://github.com/evren-oct/vigil-watt/releases";
    private const string GitHubUrl = "https://github.com/evren-oct/vigil-watt";

    private bool _notesRendered;
    private bool _hooked;

    public AboutPage()
    {
        InitializeComponent();
        DataContext = Strings.Current;
        NavigationCacheMode = NavigationCacheMode.Required;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    /// <summary>订阅跟着 Loaded/Unloaded 走（runtime-pitfalls.md §19）：页面缓存复用，构造期订阅会漏。</summary>
    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_hooked) return;
        _hooked = false;
        App.UpdateStatusChanged -= OnUpdateStatusChanged;
        UpdateDownloadUi.ProgressChanged -= OnUpdateProgressChanged;
    }

    private void OnUpdateStatusChanged(string status) => DispatcherQueue.TryEnqueue(() =>
        UpdateStatusText.Text = status);

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (!_hooked)
        {
            _hooked = true;
            App.UpdateStatusChanged += OnUpdateStatusChanged;
            UpdateDownloadUi.ProgressChanged += OnUpdateProgressChanged;
        }

        VersionText.Text = "v" + (GetType().Assembly.GetName().Version?.ToString(3) ?? "—");
        UpdateStatusText.Text = string.IsNullOrEmpty(App.UpdateStatusMessage)
            ? Strings.Current.Get("S_a6df38586d")
            : App.UpdateStatusMessage;
        RenderReleaseNotes();
    }

    // 页面缓存为 Required，Loaded 会反复触发；控件只建一次，避免重复叠加
    private void RenderReleaseNotes()
    {
        if (_notesRendered) return;
        _notesRendered = true;

        var version = GetType().Assembly.GetName().Version?.ToString(3);
        var current = ReleaseNotes.ForVersion(version);

        if (current != null)
        {
            NotesHost.Children.Add(ReleaseNotesView.BuildVersionHeader(current));
            NotesHost.Children.Add(ReleaseNotesView.BuildBody(current));
        }
        else
        {
            NotesHost.Children.Add(ReleaseNotesView.BuildMissingHint(version ?? "—"));
        }

        int olderCount = ReleaseNotes.All.Count - (current == null ? 0 : 1);
        if (olderCount > 0)
        {
            NotesHost.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 12, 0, 12),
                Background = Application.Current.Resources["DividerStrokeColorDefaultBrush"] as Brush,
            });
            NotesHost.Children.Add(new TextBlock
            {
                Text = Strings.Current.Get("S_e23e6d53fa"),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = Application.Current.Resources["TextFillColorTertiaryBrush"] as Brush,
            });
        }

        foreach (var section in ReleaseNotes.All)
        {
            if (ReferenceEquals(section, current)) continue;
            var block = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 0) };
            block.Children.Add(ReleaseNotesView.BuildVersionHeader(section));
            block.Children.Add(ReleaseNotesView.BuildBody(section));
            NotesHost.Children.Add(block);
        }
    }

    private void OnUpdateProgressChanged(bool active, int percent, string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!active)
            {
                UpdateProgressBar.Visibility = Visibility.Collapsed;
                UpdateProgressBar.IsIndeterminate = false;
                return;
            }

            UpdateProgressBar.Visibility = Visibility.Visible;
            if (percent <= 0)
            {
                UpdateProgressBar.IsIndeterminate = true;
            }
            else
            {
                UpdateProgressBar.IsIndeterminate = false;
                UpdateProgressBar.Value = Math.Clamp(percent, 0, 100);
            }
        });
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        App.UpdateStatusMessage = Strings.Current.Get("S_c8bea479ed");
        UpdateStatusText.Text = App.UpdateStatusMessage;
        try
        {
            var svc = new UpdateService();
            if (!svc.IsInstalled)
            {
                App.UpdateStatusMessage = Strings.Current.Get("S_9d61dfefd4");
                UpdateStatusText.Text = App.UpdateStatusMessage;
                return;
            }

            var info = await svc.CheckAsync();
            if (info == null)
            {
                // 回退到 Gitee 说明 GitHub 那边不通，顺带告诉用户为什么慢了
                var via = svc.SourceName == "Gitee" ? Strings.Current.Get("S_9adba325f8") : "";
                App.UpdateStatusMessage = Strings.Current.Format("S_23f7210d20", svc.CurrentVersion, via);
                UpdateStatusText.Text = App.UpdateStatusMessage;
                return;
            }

            if (!await UpdatePrompt.ShowAsync(svc, info, XamlRoot, Strings.Current.Get("S_4d0b4688c7")))
            {
                App.UpdateStatusMessage = Strings.Current.Get("S_8a2d2a670f");
                UpdateStatusText.Text = App.UpdateStatusMessage;
                return;
            }

            await UpdateDownloadUi.DownloadAndApplyAsync(svc, info, XamlRoot);
        }
        catch (Exception ex)
        {
            Log.Warn("检查更新失败: " + ex.Message);
            if (!App.UpdateStatusMessage.StartsWith(Strings.Current.Get("S_680e3e57e7"), StringComparison.Ordinal))
                App.UpdateStatusMessage = Strings.Current.Get("S_4c790c1808") + ex.Message;
            UpdateStatusText.Text = App.UpdateStatusMessage;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OpenRepo_Click(object sender, RoutedEventArgs e) => OpenUrl(RepoUrl);

    private void OpenReleases_Click(object sender, RoutedEventArgs e) => OpenUrl(ReleasesUrl);

    private void OpenGitHub_Click(object sender, RoutedEventArgs e) => OpenUrl(GitHubUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"打开链接失败: {ex.Message}");
        }
    }
}
