using System.Diagnostics;

namespace VigilWatt.Core.Plugins.Akg;

/// <summary>
/// 功 能： 执行一次 N9 电量查询，核对输入空闲态并等待匹配结果。
/// 2026/10/23 21:07:10 30149
/// 原生命令超时后不重发，失败退避交给插件基类。
/// </summary>
internal static class AkgN9Transport
{
    public static (int Percent, byte[] Message)? Query(Func<bool> enabled, Func<byte[], bool> write,
        Func<byte[]?> read, Action<TimeSpan> delay, Action<string>? trace = null)
    {
        if (!enabled() || !WaitForIdle(enabled, read, delay, trace)) return null;
        if (!enabled() || !write(AkgN9Codec.CreateBatteryQuery())) return null;
        trace?.Invoke("AKG 已发送本轮固定电量查询");

        var assembler = new AkgN9PacketAssembler();
        byte[]? previous = null;
        bool acknowledged = false;
        var elapsed = Stopwatch.StartNew();
        for (int i = 0; i < 60 && elapsed.Elapsed < TimeSpan.FromSeconds(3); i++)
        {
            delay(TimeSpan.FromMilliseconds(50));
            if (!enabled()) return null;
            var report = read();
            if (report == null || (report.Length > 0 && report[0] != AkgN9Codec.InputReportId))
                continue;
            if (!AkgN9Codec.TryGetPayloadLength(report, out int length)) return null;
            if (length == 0)
            {
                previous = null;
                continue;
            }
            // 只比较有效区域：尾部旧缓存变化不代表收到了新片段。
            var snapshot = report.AsSpan(0, 3 + length);
            if (previous != null && snapshot.SequenceEqual(previous))
            {
                trace?.Invoke($"AKG 输入 #{i + 1} 跳过重复快照 len={length}");
                continue;
            }
            previous = snapshot.ToArray();
            if (!assembler.TryAccept(report, out var messages)) return null;
            foreach (var message in messages)
            {
                if (!AkgN9Codec.IsBatteryMessage(message)) continue;
                if (message[1] == 0x5B)
                {
                    if (!AkgN9Codec.IsSuccessfulAck(message)) return null;
                    acknowledged = true;
                }
                if (message[1] == 0x5D && acknowledged)
                {
                    if (!AkgN9Codec.TryParseBattery(message, out int percent)) return null;
                    trace?.Invoke($"AKG 本轮电量={percent}% 原始={Convert.ToHexString(message)}");
                    return (percent, message);
                }
            }
        }
        return null;
    }

    private static bool WaitForIdle(Func<bool> enabled, Func<byte[]?> read,
        Action<TimeSpan> delay, Action<string>? trace)
    {
        var elapsed = Stopwatch.StartNew();
        for (int i = 0; i < 20 && elapsed.Elapsed < TimeSpan.FromSeconds(1); i++)
        {
            if (!enabled()) return false;
            var report = read();
            int length = report is { Length: > 1 } ? report[1] : -1;
            trace?.Invoke($"AKG 排空 #{i + 1} len={length} head="
                + (report == null ? "null" : Convert.ToHexString(report.AsSpan(0, Math.Min(4, report.Length)))));
            if (report != null && (report.Length == 0 || report[0] == AkgN9Codec.InputReportId))
            {
                if (!AkgN9Codec.TryGetPayloadLength(report, out length)) return false;
                if (length == 0) return true;
            }
            delay(TimeSpan.FromMilliseconds(50));
        }
        trace?.Invoke("AKG 未确认输入空闲态，停止本轮写入");
        return false;
    }
}
