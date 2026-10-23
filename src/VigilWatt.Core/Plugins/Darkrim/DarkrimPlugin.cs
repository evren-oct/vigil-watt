using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Darkrim;

/// <summary>
/// 功 能： 寸渊影弑 (DARKRIM) 2.4G 耳机插件：被动优先 + 一条官方同款只读查询。
/// 2026/10/19 19:40:00 zc165
/// 帧格式、CRC、查询帧与"为什么必须查询"见 docs/hardware/darkrim.md §1。
/// </summary>
public sealed class DarkrimPlugin : BatteryPluginBase
{
    /// <summary>寸渊影弑（杰理方案）USB 厂商 ID。</summary>
    public const ushort DarkrimVid = 0x3654;

    /// <summary>影弑 dongle 的 PID（同一 VID 下另有 0x5055，属别的型号）。</summary>
    public const ushort ShadowPid = 0x6901;

    /// <summary>厂商自定义接口的 usage_page（HID 规范厂商页 0xFF02）。</summary>
    public const ushort VendorUsagePage = 0xFF02;

    /// <summary>厂商接口的 usage。</summary>
    public const ushort VendorUsage = 0x0002;

    public override string Name => "Darkrim";
    public override string DisplayName => "寸渊影弑";
    public override string DisplayNameEn => "DARKRIM";
    public override DeviceKind Kind => DeviceKind.Headset;

    /// <summary>查询要写帧，不参与 15 秒快速发现（走 <see cref="ProbeCooldown"/> 控制重试）。</summary>
    public override bool SupportsFastDiscovery => false;

    /// <summary>探测失败后 30 秒再试，等用户把耳机开机/重新戴上。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(30);

    protected override bool SupportsPassiveListen => true;

    /// <summary>
    /// 离线判定：耳机停用/关机后 dongle 停止上报；接口静音 90 秒作兜底。
    /// <c>CountPollFailures = false</c>——<c>PollDevice</c> 的 null 与"写入被开关挡住/熔断中"
    /// 不可区分，失败记账改由插件自己在发过帧的分支里做（<see cref="ReportPollFailure"/>）。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Confirm = TimeSpan.FromSeconds(6),
        Silence = TimeSpan.FromSeconds(90),
        CountPollFailures = false,
    };

    /// <summary>
    /// 候选接口：寸渊影弑 VID/PID + 厂商页 0xFF02。
    /// dongle 是复合设备，只有 MI_03 与 MI_04&Col02 两个厂商集合会跑 0x11 通知，
    /// 其余（音频 / consumer / HID-compliant headset）不碰。
    ///
    /// 官方客户端在跑时直接不认领：两边撞同一个应答队列会让 dongle 重置链路（见 darkrim.md §1.7）。
    /// 不认领 = 不打开句柄 = 真正让位；官方退出后下一轮自动恢复。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (OfficialAppGuard.IsRunning()) return false;
        if (info.VendorId != DarkrimVid || info.ProductId != ShadowPid) return false;
        if (info.UsagePage != VendorUsagePage) return false;
        return info.Usage == VendorUsage || info.InputReportLength >= 32;
    }

    /// <summary>让位期间即便句柄还在（官方刚启动），也不再发查询。</summary>
    protected override bool ProbeWrites => !OfficialAppGuard.IsRunning();

    /// <summary>
    /// 探测 = 发一条 GetBasicInfo 查询，窗口内等电量通知。
    ///
    /// **为什么必须发这一条**：真机实测 dongle 不主动推电量——纯被动监听 45 秒、
    /// 重新插拔 dongle、重开句柄都拿不到 <c>0x11/01</c>；而发这条官方同款只读查询后
    /// 立刻回电量帧（90% 与官方界面一致）。详见 darkrim.md §1.3。
    /// </summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
        => ProbeAsync(handle).GetAwaiter().GetResult();

    protected override async Task<(string Protocol, int? Percent, ChargingState Charging)?> ProbeAsync(
        HidDeviceHandle handle)
    {
        var found = await QueryAsync(handle, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        if (found is not { } value) return null;

        return (ProtocolName, value.Percent, DarkrimCodec.MapCharging(value.Percent, value.Charging));
    }

    /// <summary>轮询 = 发一条 GetBasicInfo 查询。写入被开关/熔断/官方让位挡住时不发帧。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (OfficialAppGuard.IsRunning()) return null;
        if (!CanWrite(dev)) return null;

        var read = QueryAsync(dev.Handle, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        if (read == null)
        {
            ReportPollFailure(dev);
            return null;
        }

        ReportPollAnswered(dev);
        return (read.Value.Percent, DarkrimCodec.MapCharging(read.Value.Percent, read.Value.Charging));
    }

    /// <summary>解析 dongle 上报：电量通知出读数；链路通知只用来撤销/标记离线候选。</summary>
    protected override (int? Percent, ChargingState Charging, bool Offline)? TryParsePassiveInput(
        TrackedDevice dev, byte[] input)
    {
        if (DarkrimCodec.TryParseBattery(input, out var report))
        {
            if (!report.HasAny)
            {
                Log.InfoOnChange($"darkrim-empty:{dev.Status.Id}", "empty",
                    $"寸渊影弑 dongle 上报了空电量（耳机未连接）: {Log.Hex(input)}");
                return null;
            }

            int? percent = DarkrimCodec.Combine(report);
            bool charging = report.Charging;

            Log.InfoOnChange($"darkrim-battery:{dev.Status.Id}", $"{percent}|{charging}",
                $"寸渊影弑 电量通知: 电量={percent}% 充电={charging} 原始={Log.Hex(input)}");

            return (percent, DarkrimCodec.MapCharging(percent, charging), Offline: false);
        }

        if (DarkrimCodec.TryParseLink(input, out bool linked))
        {
            // 链路位在耳机空闲时会 1↔0 反复跳（实测 45 秒内 3 个来回），只能当"疑似"信号，
            // 不能直接判离线——否则会像早期雷柏那样不停闪烁。
            Log.InfoOnChange($"darkrim-link:{dev.Status.Id}", linked.ToString(),
                $"寸渊影弑 链路通知: 耳机{(linked ? "已连上" : "疑似断开")} 原始={Log.Hex(input)}");

            if (linked) ClearOfflineCandidate(dev);
            else MarkOfflineCandidate(dev);
            return null;
        }

        return null;
    }

    /// <summary>协议名（被动通知 + 一条官方同款只读查询）。</summary>
    private const string ProtocolName = "darkrim-notify11";

    /// <summary>
    /// 发一条 GetBasicInfo 查询并在窗口内等电量通知。返回 null = 窗口内没拿到可解析读数。
    ///
    /// 写入长度**必须写满接口声明的输出报文长度**（MI_03 实测 65 = 1 report ID + 64）：
    /// 只写 "1 + 请求帧长"（15 字节）会被 HID 驱动直接拒绝（ERROR_INVALID_PARAMETER / 参数错误）。
    /// 请求帧放在 <c>[1..]</c>，其余补零。
    /// </summary>
    private static async Task<(int Percent, bool Charging)?> QueryAsync(HidDeviceHandle handle, TimeSpan window)
    {
        var reportId = handle.GetOutputReportId();
        if (reportId == null)
        {
            Log.Info($"寸渊影弑 读不到输出 report ID: {handle.Info.DevicePath}");
            return null;
        }

        int length = handle.Info.OutputReportLength >= 16 ? handle.Info.OutputReportLength : 65;
        var request = DarkrimCodec.BuildGetBasicInfoRequest();
        var frame = new byte[length];
        frame[0] = reportId.Value;
        request.CopyTo(frame, 1);

        if (!handle.WriteOutput(frame, TimeSpan.FromMilliseconds(500)))
        {
            Log.Info($"寸渊影弑 查询写失败（report_id=0x{reportId:X2} len={length}）: {handle.Info.DevicePath}");
            return null;
        }

        var deadline = DateTime.UtcNow + window;
        int frames = 0;
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var slice = remaining < TimeSpan.FromMilliseconds(300) ? remaining : TimeSpan.FromMilliseconds(300);
            if (slice <= TimeSpan.Zero) break;

            var input = await handle.ReadInputAsync(slice).ConfigureAwait(false);
            if (input == null) continue;
            frames++;

            // 有些接口会把自己写出去的帧原样读回（实测 MI_03 如此），那不是设备应答，跳过。
            if (DarkrimCodec.IsEchoOfRequest(input))
            {
                Log.Debug($"寸渊影弑 跳过请求回显: {Log.Hex(input)}");
                continue;
            }

            if (!DarkrimCodec.TryParseBattery(input, out var report) || !report.HasAny) continue;

            int? percent = DarkrimCodec.Combine(report);
            if (!percent.HasValue) continue;
            Log.Info($"寸渊影弑 查询应答: 电量={percent}% 充电={report.Charging} 原始={Log.Hex(input)}");
            return (percent.Value, report.Charging);
        }

        Log.Info($"寸渊影弑 查询窗口内无有效应答（收到 {frames} 帧，report_id=0x{reportId:X2}）: {handle.Info.DevicePath}");
        return null;
    }

    /// <summary>显示名：dongle 的 USB product string 是 "USB Composite Device"，不可用，故用品牌 + 型号。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info) => "寸渊影弑 SHADOW";
}
