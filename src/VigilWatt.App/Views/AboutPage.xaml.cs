using System.Diagnostics;
using System.Reflection;
using VigilWatt.App.Services;
using VigilWatt.Core.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

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

    private static string? AppVersion => typeof(AboutPage).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AboutPage).Assembly.GetName().Version?.ToString(3);

    private bool _notesRendered;

    public AboutPage()
    {
        InitializeComponent();
        App.UpdateStatusChanged += status => DispatcherQueue.TryEnqueue(() =>
            UpdateStatusText.Text = status);
        UpdateDownloadUi.ProgressChanged += OnUpdateProgressChanged;
        NavigationCacheMode = NavigationCacheMode.Required;
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = AppVersion ?? "—";
        UpdateStatusText.Text = string.IsNullOrEmpty(App.UpdateStatusMessage)
            ? "检查更新"
            : App.UpdateStatusMessage;
        RenderReleaseNotes();
    }

    // 页面缓存为 Required，Loaded 会反复触发；控件只建一次，避免重复叠加
    private void RenderReleaseNotes()
    {
        if (_notesRendered) return;
        _notesRendered = true;

        var version = AppVersion;
        var current = ReleaseNotes.ForVersion(version);

        if (current != null)
        {
            CurrentNotesHost.Children.Add(ReleaseNotesView.BuildVersionHeader(current));
            CurrentNotesHost.Children.Add(ReleaseNotesView.BuildBody(current));
        }
        else
        {
            CurrentNotesHost.Children.Add(ReleaseNotesView.BuildMissingHint(version ?? "—"));
        }

        foreach (var section in ReleaseNotes.All)
        {
            if (ReferenceEquals(section, current)) continue;
            var block = new StackPanel { Spacing = 6 };
            block.Children.Add(ReleaseNotesView.BuildVersionHeader(section));
            block.Children.Add(ReleaseNotesView.BuildBody(section));
            OlderNotesHost.Children.Add(block);
        }

        OlderNotesExpander.Visibility = OlderNotesHost.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        App.UpdateStatusMessage = "正在检查更新…";
        UpdateStatusText.Text = App.UpdateStatusMessage;
        try
        {
            if (TileIntegration.HasPackage)
            {
                App.UpdateStatusMessage = "MSIX 版通过签名安装包更新";
                UpdateStatusText.Text = App.UpdateStatusMessage;
                return;
            }
            var svc = new UpdateService();
            if (!svc.IsInstalled)
            {
                App.UpdateStatusMessage = "当前是未安装的运行副本（bin/publish），无法自动更新";
                UpdateStatusText.Text = App.UpdateStatusMessage;
                return;
            }

            var info = await svc.CheckAsync();
            if (info == null)
            {
                // 回退到 Gitee 说明 GitHub 那边不通，顺带告诉用户为什么慢了
                var via = svc.SourceName == "Gitee" ? "（GitHub 不可达，已回退 Gitee）" : "";
                App.UpdateStatusMessage = $"已是最新版本（{svc.CurrentVersion}）{via}";
                UpdateStatusText.Text = App.UpdateStatusMessage;
                return;
            }

            if (!await UpdatePrompt.ShowAsync(svc, info, XamlRoot, "取消"))
            {
                App.UpdateStatusMessage = "已取消更新";
                UpdateStatusText.Text = App.UpdateStatusMessage;
                return;
            }

            await UpdateDownloadUi.DownloadAndApplyAsync(svc, info, XamlRoot);
        }
        catch (Exception ex)
        {
            Log.Warn("检查更新失败: " + ex.Message);
            if (!App.UpdateStatusMessage.StartsWith("更新安装失败", StringComparison.Ordinal))
                App.UpdateStatusMessage = "检查更新失败：" + ex.Message;
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
