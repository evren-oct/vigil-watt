using Microsoft.UI.Dispatching;
using VigilWatt.Core.Utils;

namespace VigilWatt.App.Services;

/// <summary>
/// 功 能： 隐藏到托盘后把驻留页还给系统，任务管理器「内存」列随之回落。
/// 2026/11/25 10:30:00 xliu
/// 只裁工作集、不改提交；下次显示要重新缺页，是拿首帧换数字的取舍（runtime-pitfalls.md §26）。
/// </summary>
internal static class WorkingSetTrim
{
    /// <summary>等 XAML 把内容树摘完再裁：Hide 之后还有一段异步尾部在释放，太早裁会把刚释放的页又算进来。</summary>
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(1200);

    private static DispatcherQueueTimer? _timer;

    /// <summary>隐藏后调用，重复调用只保留最后一次。非 UI 线程直接放弃（拿不到队列就没法延后）。</summary>
    public static void Queue()
    {
        var queue = DispatcherQueue.GetForCurrentThread();
        if (queue is null) return;

        if (_timer is null)
        {
            _timer = queue.CreateTimer();
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) => Trim();
        }

        _timer.Interval = Delay;
        _timer.Start();
    }

    /// <summary>窗口重新显示时取消：刚裁完又要整页缺页回来，纯亏。</summary>
    public static void Cancel() => _timer?.Stop();

    private static void Trim()
    {
        _timer?.Stop();
        try
        {
            if (!Native.SetProcessWorkingSetSizeEx(
                    Native.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1), 0))
            {
                Log.Warn($"工作集修剪失败，Win32 错误 {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                return;
            }

            Log.Info("隐藏到托盘，工作集已修剪（提交量不变）");
        }
        catch (Exception ex)
        {
            Log.Warn($"工作集修剪异常: {ex.Message}");
        }
    }
}
