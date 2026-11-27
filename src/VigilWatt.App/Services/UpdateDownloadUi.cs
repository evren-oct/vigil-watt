using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Velopack;
using VigilWatt.Core.Utils;

using VigilWatt.App.Localization;
namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 更新下载共用 UI（进度对话框 + 状态/托盘提示）。
/// 2026/10/16 14:40:00 xliu
/// 确认框关掉后原先只剩状态一行小字，用户不知道是否在下。
/// </summary>
internal static class UpdateDownloadUi
{
    /// <summary>关于页进度条等旁路订阅；Percent 在 Active=false 时无意义。</summary>
    public static event Action<bool, int, string>? ProgressChanged;

    /// <summary>弹进度框、下载、成功则 ApplyAndRestart（进程会退出）；失败抛异常。</summary>
    public static async Task DownloadAndApplyAsync(UpdateService svc, UpdateInfo info, XamlRoot xamlRoot)
    {
        var version = info.TargetFullRelease.Version.ToString();
        var dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(Strings.Current.Get("S_5aa43f8953"));

        var statusText = new TextBlock
        {
            Text = Strings.Current.Get("S_5b612cbd3a"),
            TextWrapping = TextWrapping.Wrap,
        };
        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true,
            Height = 8,
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(statusText);
        panel.Children.Add(bar);

        var dialog = new ContentDialog
        {
            Title = Strings.Current.Get("S_d180648db7") + version,
            Content = panel,
            XamlRoot = xamlRoot,
        };
        var allowClose = false;
        dialog.Closing += (_, e) =>
        {
            if (!allowClose) e.Cancel = true;
        };

        Publish(true, 0, Strings.Current.Format("S_5171766471", version));
        App.UpdateStatusMessage = Strings.Current.Format("S_5171766471", version);
        try { App.Services.Tray.ShowUpdateDownloading(version); }
        catch (Exception ex) { Log.Warn($"下载开始托盘提示失败: {ex.Message}"); }

        var showTask = dialog.ShowAsync().AsTask();
        var sw = Stopwatch.StartNew();
        var lastUiMs = -1L;
        var lastLogBucket = -1;
        // Velopack 按文件报 0–100，下完 delta 再下 full 会从 0 重来；只展示高水位，避免进度条倒退
        var lastRaw = -1;
        var highWater = 0;
        var stage = 1;
        var gate = new object();

        try
        {
            await svc.DownloadAsync(info, pct =>
            {
                int display;
                int stageNow;
                lock (gate)
                {
                    // 明显回退 = 新开一段下载（阈值别太紧，避免抖动误判）
                    if (lastRaw >= 0 && pct + 8 < lastRaw && lastRaw >= 15)
                        stage++;
                    lastRaw = pct;
                    if (pct > highWater) highWater = pct;
                    display = highWater;
                    stageNow = stage;
                }

                var now = sw.ElapsedMilliseconds;
                var bucket = display / 10;
                if (bucket != lastLogBucket)
                {
                    lastLogBucket = bucket;
                    Log.Info(stageNow > 1
                        ? Strings.Current.Format("S_0549429bf5", display, pct, stageNow)
                        : Strings.Current.Format("S_f51d35c4a9", display));
                }
                if (display < 100 && lastUiMs >= 0 && now - lastUiMs < 300) return;
                lastUiMs = now;

                var msg = stageNow > 1
                    ? Strings.Current.Format("S_ff445d08fb", version, display, stageNow)
                    : Strings.Current.Format("S_d1ee227f36", version, display);
                var displayCopy = display;
                dispatcher.TryEnqueue(() =>
                {
                    // 队列乱序时拒绝把条往回调退
                    if (!bar.IsIndeterminate && displayCopy < bar.Value) return;
                    bar.IsIndeterminate = false;
                    bar.Value = Math.Clamp(displayCopy, 0, 100);
                    statusText.Text = msg;
                    Publish(true, displayCopy, msg);
                    App.UpdateStatusMessage = msg;
                });
            }).ConfigureAwait(true);

            allowClose = true;
            dialog.Hide();
            try { await showTask.ConfigureAwait(true); } catch { /* Hide 后偶发取消 */ }

            var done = Strings.Current.Format("S_eed676de7e", version);
            Publish(false, 100, done);
            App.UpdateStatusMessage = done;
            try { App.Services.Tray.ShowUpdateDownloadDone(version); }
            catch (Exception ex) { Log.Warn($"下载完成托盘提示失败: {ex.Message}"); }

            Log.Info($"版本 {version} 下载完成，开始应用并重启");
            svc.ApplyAndRestart(info);
        }
        catch (Exception ex)
        {
            allowClose = true;
            try { dialog.Hide(); } catch { /* 对话框可能已关 */ }
            try { await showTask.ConfigureAwait(true); } catch { /* ignore */ }

            var fail = Strings.Current.Get("S_a39977f860") + ex.Message;
            Publish(false, 0, fail);
            App.UpdateStatusMessage = fail;
            try { App.Services.Tray.ShowUpdateDownloadFailed(ex.Message); }
            catch (Exception trayEx) { Log.Warn($"下载失败托盘提示失败: {trayEx.Message}"); }
            throw;
        }
    }

    private static void Publish(bool active, int percent, string message) =>
        ProgressChanged?.Invoke(active, percent, message);
}
