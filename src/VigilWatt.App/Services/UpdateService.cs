using System.Reflection;
using Velopack;
using Velopack.Sources;
using VigilWatt.Core.Utils;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 自动更新：包一层 Velopack 的 UpdateManager，GitHub 优先、连不上再回退 Gitee。
/// 2026/10/14 22:17:14 xliu；2026/11/24 20211 补 beta 通道与预发布源隔离。
/// 两个仓库的 Release 资产必须一致（同一次 build-installer 的产物各传一份），见 docs/dev/build.md §8。
/// </summary>
public sealed class UpdateService
{
    private const string GitHubRepoUrl = "https://github.com/evren-oct/vigil-watt";
    private const string GiteeRepoUrl = "https://gitee.com/YGWG-LX/vigil-watt";
    private const string BetaChannel = "beta";

    // GitHub 被墙时表现为"连得上但不回包"，不设上限会一直等到 HttpClient 默认超时（100 秒），
    // 启动检查会白等；超过这个时间就直接当它不可用。
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);

    private readonly UpdateManager _github;
    private readonly UpdateManager _gitee;

    // 检查成功后固定下来：delta 包必须从"给出清单的那个源"继续下载，中途换源会对不上
    private UpdateManager _active;

    public UpdateService()
    {
        // 匿名访问即可（公开仓库）；accessToken / downloader 传 null 用默认实现
        // 预发布包只看 beta 通道和 prerelease 资产；稳定包继续只看稳定通道，避免互相串版本。
        var prerelease = IsPrereleaseBuild();
        if (prerelease)
        {
            var options = new UpdateOptions { ExplicitChannel = BetaChannel };
            _github = new UpdateManager(new GithubSource(GitHubRepoUrl, null, true, null), options);
            _gitee = new UpdateManager(new GiteaSource(GiteeRepoUrl, null, true, null), options);
        }
        else
        {
            _github = new UpdateManager(new GithubSource(GitHubRepoUrl, null, false, null));
            _gitee = new UpdateManager(new GiteaSource(GiteeRepoUrl, null, false, null));
        }
        _active = _github;
    }

    public bool IsInstalled => _active.IsInstalled;

    public string? CurrentVersion => _active.CurrentVersion?.ToString();

    /// <summary>本次检查实际走通的源（GitHub / Gitee），供界面文案与日志显示。</summary>
    public string SourceName { get; private set; } = "GitHub";

    /// <summary>检查更新。null = 已是最新；两个源都不可达才抛异常，由调用方呈现。</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        string githubError;
        try
        {
            var info = await WithTimeout(_github.CheckForUpdatesAsync()).ConfigureAwait(false);
            _active = _github;
            SourceName = "GitHub";
            return StableOnly(info);
        }
        catch (Exception ex)
        {
            githubError = ex.Message;
            Log.Warn("GitHub 更新源不可用，回退 Gitee: " + ex.Message);
        }

        try
        {
            var info = await _gitee.CheckForUpdatesAsync().ConfigureAwait(false);
            _active = _gitee;
            SourceName = "Gitee";
            return StableOnly(info);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"GitHub 与 Gitee 都不可达（GitHub：{githubError}；Gitee：{ex.Message}）", ex);
        }
    }

    public Task DownloadAsync(UpdateInfo info, Action<int>? progress = null, CancellationToken cancel = default)
    {
        return _active.DownloadUpdatesAsync(info, progress, cancel);
    }

    /// <summary>应用更新并重启。Velopack 的流程是“Update.exe 等本进程退出 → 应用更新 → 重启”。</summary>
    public void ApplyAndRestart(UpdateInfo info)
    {
        _active.ApplyUpdatesAndRestart(info);
    }

    private static bool IsPrereleaseBuild()
    {
        var version = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return version?.Contains('-') == true;
    }

    private static UpdateInfo? StableOnly(UpdateInfo? info)
    {
        var target = info?.TargetFullRelease?.Version;
        if (target is null || !target.IsPrerelease || IsPrereleaseBuild()) return info;

        Log.Warn("稳定版忽略预发布更新: " + target);
        return null;
    }

    private static async Task<T> WithTimeout<T>(Task<T> task)
    {
        if (await Task.WhenAny(task, Task.Delay(ProbeTimeout)).ConfigureAwait(false) != task)
        {
            // 被放弃的任务还会继续跑，接住它后面的异常，免得变成 UnobservedTaskException
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw new TimeoutException($"{(int)ProbeTimeout.TotalSeconds} 秒内无响应");
        }

        return await task.ConfigureAwait(false);
    }
}
