using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Inphic;

/// <summary>
/// 功 能： 英菲克 IN9（2.4G 接收器 248A:5B2F）的电量：GetFeature(0x0C) 读快照，轮询前发一条刷新请求。
/// 2026/10/29 22:35:11 bitbad
/// 与 ZHEN 共用「英菲克」开关但必须独立成类：IN9 会写帧，ZHEN 零写入且要进 15 秒发现节拍（inphic.md §2）。
/// </summary>
public sealed class InphicIn9Plugin : BatteryPluginBase
{
    public override string Name => "Inphic";

    public override string DisplayName => "英菲克";

    public override string DisplayNameEn => "INPHIC";

    public override DeviceKind Kind => DeviceKind.Mouse;

    /// <summary>0 已由 codec 当占位值挡掉；这里放宽到 0 是为了别把 1% 这种真实低电量也滤掉。</summary>
    protected override int MinValidPercent(HidDeviceInfo info) => 0;

    /// <summary>
    /// 关机后接收器仍拿缓存值应答，轮询空读分不清"鼠标关了"与"应答正常"，
    /// 故不自动记账——只在 GetFeature 真的失败时自己报，离线主要靠接口消失。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        CountPollFailures = false,
    };

    private DateTimeOffset _wiredCheckedAt;
    private bool _wiredBodyInPlace;

    /// <summary>刷新帧序号区（inphic.md §2.2）：重复值会被设备丢弃。</summary>
    private int _commandSeq;

    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == InphicIn9Codec.VendorId && info.ProductId == InphicIn9Codec.DonglePid
           && info.UsagePage == 0xFF00 && info.Usage == 0x0001
           && info.FeatureReportLength == InphicIn9Codec.ReportLength;

    /// <summary>
    /// 插线后鼠标切换成独立的 `248A:5B2E`，它在不在位就是"是否在充电"。
    /// 5 秒缓存：扫描本身会枚举全部 HID 接口，不必每台设备每轮重扫（同 `AtkPlugin` 的做法）。
    /// </summary>
    private bool WiredBodyInPlace()
    {
        if (_wiredCheckedAt != default && DateTimeOffset.Now - _wiredCheckedAt < TimeSpan.FromSeconds(5))
            return _wiredBodyInPlace;

        _wiredCheckedAt = DateTimeOffset.Now;
        var inphic = HidDeviceScanner.Scan()
            .Where(d => d.VendorId == InphicIn9Codec.VendorId)
            .ToList();

        // 有线本体的 PID 抄自 issue 作者的机器，本机可能不同：把 248A 号段当前在位的 PID 记下来，
        // 插线前后各看一眼日志就能确认，不用再猜。
        var pids = string.Join(",", inphic.Select(d => $"0x{d.ProductId:X4}").Distinct().OrderBy(p => p));
        Log.InfoOnChange("inphic-in9-248a-pids", pids, $"英菲克 248A 号段在位 PID: {pids}");

        _wiredBodyInPlace = inphic.Any(d => d.ProductId == InphicIn9Codec.WiredPid);
        return _wiredBodyInPlace;
    }

    /// <summary>R+W 打不开时退回 0 权限句柄：能读快照，写刷新会被判失败并记录（少数批次把该集合当受保护集合）。</summary>
    protected override HidDeviceHandle OpenDevice(HidDeviceInfo info)
    {
        try
        {
            return info.Open();
        }
        catch (UnauthorizedAccessException)
        {
            Log.Warn($"英菲克 IN9 接口需要只读权限才能打开: {info.DevicePath}");
            return info.OpenFeature();
        }
    }

    /// <summary>IN9 刷新重试节拍区（safety.md §3 规则 4：单轮最多 1 条 + 1 次重试）。</summary>
    private const int RefreshWriteAttempts = 2;
    private const int RefreshRetryDelayMs = 500;
    private const int RefreshReadRetries = 2;
    private const int RefreshReadDelayMs = 80;
    private const int RefreshSettleDelayMs = 30;

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var (frame, refreshed, reads) = RefreshAndRead(handle, WritesEnabled);

        if (!InphicIn9Codec.TryParse(frame, out int percent, out byte flag))
        {
            // 读不到也照样认领（电量 null → 先显示离线）：接管了接口却让设备凭空消失，
            // 用户看到的就是"搜不到"；认领后还能每轮重试并把原始帧记进日志。
            Log.InfoOnChange("inphic-in9-probe-empty", refreshed ? "w" : "r",
                $"英菲克 IN9 认领了但没读到电量（刷新帧{(refreshed ? "已发" : "未发")}，读了 {reads} 次）：" +
                $"{(frame == null ? "读失败" : Log.Hex(frame))}");
            return (InphicIn9Codec.Protocol, null, ChargingState.Unknown);
        }

        var probeCharging = ResolveCharging(InphicIn9Codec.IsCharging(frame), flag, WiredBodyInPlace());
        return (InphicIn9Codec.Protocol,
            probeCharging == ChargingState.Charging ? null : percent,
            probeCharging);
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        // 刷新帧是**硬前置**：不发就回全 0（真机确认），不是"先写更好"而是"不写拿不到"。
        // 每轮最多一条；插件关着 / 熔断中由 CanWrite 挡住。
        var (frame, refreshed, reads) = RefreshAndRead(dev.Handle, CanWrite(dev));

        if (frame == null)
        {
            ReportPollFailure(dev);
            return null;
        }

        if (!InphicIn9Codec.TryParse(frame, out int percent, out byte flag))
        {
            // 既不报失败也不报已应答：全 0 是"设备在睡"或"写入被拦"的正常表现，
            // 报 answered 会把熔断误解除。挂在这里等 CanWrite 退避期满自然放行。
            Log.InfoOnChange($"inphic-in9-invalid:{dev.Status.Id}", refreshed ? "w" : "r",
                $"英菲克 IN9 没读到电量（刷新帧{(refreshed ? "已发" : "未发")}，读了 {reads} 次），" +
                $"沿用上次读数 原始={(frame.Length == 0 ? "空帧" : Log.Hex(frame))}");
            return null;
        }

        ReportPollAnswered(dev);

        bool pluggedIn = InphicIn9Codec.IsCharging(frame);
        bool wired = WiredBodyInPlace();
        var charging = ResolveCharging(pluggedIn, flag, wired);

        Log.InfoOnChange($"inphic-in9:{dev.Status.Id}", $"{percent}|{flag}|{pluggedIn}|{charging}|{wired}",
            charging == ChargingState.Charging
                ? $"英菲克 IN9 充电中，电量不上报 充电位={(pluggedIn ? "插线" : "未插")} " +
                  $"链路标志=0x{flag:X2} 有线本体={(wired ? "在位" : "不在位")} 原始={Log.Hex(frame)}"
                : $"英菲克 IN9 电量={percent}% 充电位={(pluggedIn ? "插线" : "未插")} " +
                  $"链路标志=0x{flag:X2} 原始={Log.Hex(frame)}");

        // 充电时的百分比不可信（2.4G 侧是缓存/换算值）：只报充电状态，不报数值。
        return charging == ChargingState.Charging
            ? ((int?)null, charging)
            : (percent, charging);
    }

    /// <summary>充电判据区（inphic.md §2.3）：[19] 是插线位，优先于 [21]（充电时 [21] 仍报放电）。</summary>
    private static ChargingState ResolveCharging(bool pluggedIn, byte linkFlag, bool wiredBodyInPlace)
    {
        if (pluggedIn) return ChargingState.Charging;
        var byFlag = InphicIn9Codec.ToChargingState(linkFlag);
        if (byFlag != ChargingState.Unknown) return byFlag;
        return wiredBodyInPlace ? ChargingState.Charging : ChargingState.Unknown;
    }

    /// <summary>IN9 刷新与回读区：重复序号会被设备丢弃，重试必须换号（inphic.md §2.2）。</summary>
    private (byte[]? Frame, bool Refreshed, int Reads) RefreshAndRead(
        HidDeviceHandle handle, bool canWrite)
    {
        if (!canWrite || !TryRefresh(handle))
            return (ReadFrame(handle), false, 1);

        byte[]? frame = null;
        int reads = 0;

        // 序号重复时设备直接丢掉请求并回全 0；换号重发是唯一恢复手段，官方驱动同样如此。
        for (int attempt = 0; attempt < RefreshWriteAttempts; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(RefreshRetryDelayMs);
                if (!TryRefresh(handle)) break;
            }
            Thread.Sleep(RefreshSettleDelayMs);
            frame = ReadFrame(handle);
            reads++;
            if (InphicIn9Codec.TryParse(frame, out _, out _)) return (frame, true, reads);
        }

        // 设备已接受查询但回填慢时，新序号会打断它，这里只等待重读。
        for (int i = 0; i < RefreshReadRetries; i++)
        {
            Thread.Sleep(RefreshReadDelayMs * (i + 1));
            frame = ReadFrame(handle);
            reads++;
            if (InphicIn9Codec.TryParse(frame, out _, out _)) break;
        }

        return (frame, true, reads);
    }

    /// <summary>序号区（1..255、跳过 0）：官方驱动每次启动都从 01 重来（inphic.md §2.2）。</summary>
    private byte NextCommand() =>
        InphicIn9Codec.CommandFromCounter((uint)Interlocked.Increment(ref _commandSeq));

    private bool TryRefresh(HidDeviceHandle handle)
    {
        var payload = InphicIn9Codec.BuildRefreshPayload(NextCommand());
        try
        {
            if (handle.SetFeature(InphicIn9Codec.ReportId, payload, InphicIn9Codec.ReportLength))
            {
                if (Log.IsEnabled(LogLevel.Debug))
                    Log.Debug($"英菲克 IN9 刷新请求已发出: id=0x0C 载荷={Log.Hex(payload)}");
                return true;
            }

            Log.InfoOnChange("inphic-in9-refresh-rejected", "1",
                $"英菲克 IN9 刷新请求被拒（Win32 写入失败），本轮读到的多半是全 0 " +
                $"id=0x0C 载荷={Log.Hex(payload)}");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Log.Warn($"英菲克 IN9 刷新请求异常，本轮读到的多半是全 0: {ex.Message}");
        }

        return false;
    }

    private static byte[]? ReadFrame(HidDeviceHandle handle)
    {
        try
        {
            var frame = handle.GetFeature(InphicIn9Codec.ReportId, out int error, InphicIn9Codec.ReportLength);
            if (frame == null)
                Log.InfoOnChange("inphic-in9-read-failed", $"e{error}",
                    $"英菲克 IN9 电量帧读取失败: Win32={error}");
            return frame;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Log.InfoOnChange("inphic-in9-read-ex", ex.GetType().Name,
                $"英菲克 IN9 电量帧读取异常: {ex.Message}");
            return null;
        }
    }

    protected override string BuildDisplayName(HidDeviceInfo info) => "英菲克 IN9";
}
