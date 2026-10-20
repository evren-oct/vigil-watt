using System.Threading;
using Velopack;
using VigilWatt.App.Services;
using VigilWatt.Core.Devices;
using VigilWatt.Core.Settings;
using VigilWatt.Core.Storage;
using VigilWatt.Core.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VigilWatt.App;

/// <summary>应用级服务容器。</summary>
public sealed class AppServices : IDisposable
{
    public AppSettings Settings { get; }
    public HistoryStore History { get; }
    public DeviceManager Manager { get; }
    public TrayService Tray { get; private set; } = null!;
    public WidgetWindow? Widget { get; set; }
    public TaskbarPillWindow? TaskbarBattery { get; set; }
    public MainWindow? MainWindow { get; set; }

    public AppServices()
    {
        Settings = AppSettings.Load();
        History = new HistoryStore();
        Manager = new DeviceManager(Settings, History);
    }

    public void AttachTray(TrayService tray)
    {
        Tray = tray;
        // 事件从后台线程来，气泡必须回 UI 线程
        Manager.LowBatteryDetected += device => MainWindow?.DispatcherQueue.TryEnqueue(
            () => Tray.ShowLowBattery(DevicePresentation.Apply(device, Settings)));

            // 设备状态变化时刷新托盘图标/提示。以前只在启动时渲染一次，而启动那会儿设备还没扫出来，
            // 于是托盘永远显示"暂无在线设备"。两个事件都要订：DeviceUpdated 覆盖电量/在线变化，
            // DevicesChanged 覆盖设备增删。
        Manager.DeviceUpdated += _ => tray.RequestUpdate();
        Manager.DevicesChanged += tray.RequestUpdate;
    }

    public void Dispose()
    {
        // 幂等：退出路径（TrayService.Exit）与任何未来的显式释放都可能重复调用
        if (_disposed) return;
        _disposed = true;
        // 逐个兜底：释放 HID 句柄 / Timer / SQLite 连接。任一项失败不能连累其余（CR-07）。
        try { Manager.Dispose(); } catch (Exception ex) { Log.Warn($"DeviceManager 释放失败: {ex.Message}"); }
        try { History.Dispose(); } catch (Exception ex) { Log.Warn($"HistoryStore 释放失败: {ex.Message}"); }
    }

    private bool _disposed;
}

/// <summary>
/// 功 能： 应用入口：单实例、全局异常熔断，并拉起主窗口。
/// 2026/10/06 15:53:36 xliu
/// </summary>
public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;
    private static string _updateStatusMessage = "启动时检查更新";
    public static event Action<string>? UpdateStatusChanged;
    public static string UpdateStatusMessage
    {
        get => _updateStatusMessage;
        internal set
        {
            _updateStatusMessage = value;
            UpdateStatusChanged?.Invoke(value);
        }
    }
    private static Mutex? _singleInstanceMutex;

    /// <summary>全局异常熔断：窗口内累计次数（见构造里的 UnhandledException）。</summary>
    private static int _unhandledBurst;
    private static DateTimeOffset _lastUnhandledAt;
    private static UpdateService? _startupUpdateService;
    private static UpdateInfo? _pendingStartupUpdate;
    private static int _updatePromptVisible;

    /// <summary>熔断统计窗口：距上次异常超过它就重新计数（"连续"= 短时间内密集发生）。</summary>
    private static readonly TimeSpan UnhandledBurstWindow = TimeSpan.FromMinutes(1);

    /// <summary>窗口内未处理异常达到这个数就停止吞异常。</summary>
    private const int UnhandledBurstLimit = 5;

    public App()
    {
        InitializeComponent();

        // 全局异常兜底：写日志便于排错。
        // 熔断（CR-08）：以前一律 e.Handled=true 无限吞掉，应用会"看起来在跑、实际什么都没做"。
        // 短窗口内连续多次未处理异常说明状态已不可信，此时不再吞、交给进程按崩溃路径退出（日志已留痕）。
        UnhandledException += (s, e) =>
        {
            var now = DateTimeOffset.Now;
            if (now - _lastUnhandledAt > UnhandledBurstWindow) _unhandledBurst = 0;
            _lastUnhandledAt = now;
            _unhandledBurst++;

            Log.Error($"XAML 未处理异常（{UnhandledBurstWindow.TotalSeconds:0}s 内第 {_unhandledBurst} 次）", e.Exception);
            if (_unhandledBurst < UnhandledBurstLimit)
            {
                e.Handled = true;
            }
            else
            {
                Log.Error($"未处理异常已达 {UnhandledBurstLimit} 次，不再吞掉，交给进程退出");
                e.Handled = false;
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Log.Error("未处理异常", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Error("未观察的 Task 异常", e.Exception);
            e.SetObserved();
        };

        // 退出兜底：任何退出路径（含非托盘路径、Environment.Exit）都会触发 ProcessExit，
        // 保证单实例互斥量被释放、日志被刷盘。与 TrayService.Exit 里的显式调用互为保险。
        AppDomain.CurrentDomain.ProcessExit += (s, e) => ShutdownLogging();
    }

        /// <summary>
        /// 退出收尾：先放掉单实例互斥量（让新实例能立刻启动），再刷盘并释放 app.log 句柄。
        /// **幂等**——托盘退出与 ProcessExit 兜底会各调一次。必须挂在真实退出路径上
        /// （AppServices.Dispose 全仓无人调用）。
        /// </summary>
    internal static void ShutdownLogging()
    {
        var mutex = Interlocked.Exchange(ref _singleInstanceMutex, null);
        if (mutex != null)
        {
            try { mutex.ReleaseMutex(); } catch { /* 非持有线程调用会抛，忽略 */ }
            try { mutex.Dispose(); } catch { /* 忽略 */ }
        }
        Log.Shutdown();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 单实例：重复启动直接退出
        _singleInstanceMutex = new Mutex(true, "VigilWatt_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Log.Info("检测到已有实例运行，退出");
            ShutdownLogging();
            Environment.Exit(0);
            return;
        }

        Services = new AppServices();
        // 日志级别与按日保留天数：启动时应用（重启生效，不追求热更新）
        Log.MinLevel = LogLevelNames.Parse(Services.Settings.LogLevel);
        Log.RetentionDays = Services.Settings.LogRetentionDays;

        Log.Info($"启动 VigilWatt，参数: {args.Arguments}");
        Services.Manager.Start();

        Services.MainWindow = new MainWindow();

        Services.AttachTray(new TrayService(Services));
        Services.Tray.Initialize();

        Services.MainWindow.Activate();
        if (Services.Settings.StartMinimized && AppVisibilityPolicy.CanHideMainWindow(Services.Settings))
        {
            Services.MainWindow.AppWindow.Hide();
        }

        if (Services.Settings.WidgetEnabled)
        {
            Services.Widget = new WidgetWindow();
            Services.Widget.Activate();
        }

        if (Services.Settings.TaskbarBatteryEnabled)
        {
            Services.TaskbarBattery = new TaskbarPillWindow();
        }

        _ = CheckForStartupUpdateAsync();
    }

    private static async Task CheckForStartupUpdateAsync()
    {
        try
        {
            var updater = new UpdateService();
            _startupUpdateService = updater;
            if (!updater.IsInstalled)
            {
                UpdateStatusMessage = "当前是未安装的运行副本，无法自动安装更新";
                Log.Info("当前运行副本未通过安装器安装，跳过启动更新检查");
                return;
            }

            UpdateStatusMessage = "正在检查更新…";
            Log.Info("启动时检查更新（GitHub 优先，失败回退 Gitee）");
            var update = await updater.CheckAsync();
            if (update == null)
            {
                UpdateStatusMessage = $"已是最新版本（{updater.CurrentVersion}）";
                Log.Info($"{updater.SourceName} 更新检查完成，当前已是最新版本 {updater.CurrentVersion}");
                return;
            }

            _pendingStartupUpdate = update;
            var version = update.TargetFullRelease.Version.ToString();
            UpdateStatusMessage = $"发现新版本 {version}";
            Log.Info($"{updater.SourceName} 发现新版本 {version}");
            if (Services.Settings.StartMinimized && AppVisibilityPolicy.CanHideMainWindow(Services.Settings))
            {
                Services.Tray.ShowUpdateAvailable(version);
            }
            else
            {
                Services.MainWindow?.DispatcherQueue.TryEnqueue(() => _ = ShowPendingUpdateAsync());
            }
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = "自动检查失败：" + ex.Message;
            Log.Warn("启动时检查更新失败: " + ex.Message);
        }
    }

    internal static void OnMainWindowActivated()
    {
        if (_pendingStartupUpdate != null)
            _ = ShowPendingUpdateAsync();
    }

    /// <summary>窗口首次 Loaded 时补弹启动更新确认框（XamlRoot 就绪前无法弹，见 ShowPendingUpdateAsync）。</summary>
    private static void RetryWhenLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement root) root.Loaded -= RetryWhenLoaded;
        _ = ShowPendingUpdateAsync();
    }

    private static async Task ShowPendingUpdateAsync()
    {
        if (Interlocked.Exchange(ref _updatePromptVisible, 1) != 0) return;
        var update = Interlocked.Exchange(ref _pendingStartupUpdate, null);
        if (update == null)
        {
            Interlocked.Exchange(ref _updatePromptVisible, 0);
            return;
        }

        try
        {
            var window = Services.MainWindow;
            if (window?.Content is not FrameworkElement root)
            {
                _pendingStartupUpdate = update;
                return;
            }

            // 启动瞬间窗口可能还没 Loaded，此时 XamlRoot 为空、弹框必失败；而窗口若已处于激活态
            // 就不会再触发 Activated，确认框会永久丢失。等根元素 Loaded 再补弹（一次性）。
            if (root.XamlRoot == null)
            {
                _pendingStartupUpdate = update;
                root.Loaded += RetryWhenLoaded;
                return;
            }

            var updater = _startupUpdateService ?? new UpdateService();
            var version = update.TargetFullRelease.Version.ToString();
            if (!await UpdatePrompt.ShowAsync(updater, update, root.XamlRoot, "稍后"))
            {
                UpdateStatusMessage = $"已选择稍后安装版本 {version}";
                Log.Info($"用户选择稍后安装版本 {version}");
                return;
            }

            await UpdateDownloadUi.DownloadAndApplyAsync(updater, update, root.XamlRoot);
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = "更新安装失败：" + ex.Message;
            Log.Warn("启动更新安装失败: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _updatePromptVisible, 0);
        }
    }
}
