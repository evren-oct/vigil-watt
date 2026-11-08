using Velopack;
using VigilWatt.App.Services;
using VigilWatt.Core.Utils;

namespace VigilWatt.App;

/// <summary>
/// 功 能： 自定义入口（csproj 用 DISABLE_XAML_GENERATED_MAIN 关掉了 XAML 生成的 Main）。
/// 2026/10/06 21:37:31 xliu
/// VelopackApp.Run() 必须先于一切 UI 跑：安装/更新/卸载时进程会带 --veloapp-* 参数被拉起， 钩子在这里被消费后直接退出，只有正常启动才走到下面的 WinUI 逻辑。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (TileIntegration.HasPackage)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppPaths.DataDirVariable)))
                Environment.SetEnvironmentVariable(AppPaths.DataDirVariable, Windows.Storage.ApplicationData.Current.LocalFolder.Path);
        }
        else
        {
            RunVelopack();
        }

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    private static void RunVelopack()
    {
        VelopackApp.Build()
            // 卸载时清掉开机自启残留（HKCU Run 的 VigilWatt 值是应用自己在设置页写的）
            .OnBeforeUninstallFastCallback(_ =>
            {
                // 清不掉只留痕：卸载本身不能被注册表权限问题挡住
                if (!AutostartService.TrySetEnabled(false, out var error))
                    Log.Warn("卸载时清除开机自启失败: " + error);
            })
            .Run();
    }
}
