using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;

namespace VigilWatt.Core.Plugins.Atk;

/// <summary>
/// 功 能： ATK/VXE 无线鼠标电量插件——主动查询 FF02 口的 17 字节命令-应答协议。
/// 2026/10/20 22:14:00 Yang
/// 帧结构与实测依据见 docs/hardware/atk.md；编解码在 AtkCodec。
/// </summary>
public sealed class AtkPlugin : BatteryPluginBase
{
    public override string Name => "Atk";                 // AppSettings.PluginEnabled 的 key
    public override string DisplayName => "ATK";
    public override string DisplayNameEn => "ATK";
    public override DeviceKind Kind => DeviceKind.Mouse;

    // 会写帧（发查询帧）→ 按社区适配指南"主动通信不进 15 秒常规发现节拍"，
    // 与 Betop / Ganss / Aula 一致保持默认 false；发现延迟走常规扫描周期。
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    // null 与"被熔断挡住"不可区分 → 基类不自动记账，由 PollDevice 自报账
    protected override OfflinePolicy Offline => new() { Silence = TimeSpan.Zero, CountPollFailures = false };

    private const ushort VendorId = 0x373B;               // ⚠ 实测修正：非 0x375B
    private const ushort DonglePid = 0x101B;              // 8K dongle（产品名 "ATK Mouse 8K Dongle"）
    private const ushort DonglePid1K = 0x101A;            // 1K dongle
    private const ushort DonglePidV3 = 0x1303;            // 8K dongle（产品名 "Wireless mouse 8k dongle-M"）

    /// <summary>电量口的 usage page；必须精确匹配，否则会对 FF03/FF04/FF05/FF06 等厂商口各白发一帧查询。</summary>
    private const ushort UsagePageBattery = 0xFF02;

    /// <summary>已知的接收器 PID：这三个都认同一条查询帧、响应结构一致。</summary>
    private static bool IsReceiverPid(ushort pid) =>
        pid is DonglePid or DonglePid1K or DonglePidV3;

    private static DateTimeOffset _receiverCheckedAt;


    // 当前在位的接收器 PID 集合（5 秒缓存：IsCandidate 每接口调一次，全量重扫太贵）
    private static HashSet<ushort> _receiversInPlace = new();

    /// <summary>刷新"当前在位的接收器 PID 集合"。</summary>
    private static void RefreshReceiversInPlace()
    {
        if (_receiverCheckedAt != default && DateTimeOffset.Now - _receiverCheckedAt < TimeSpan.FromSeconds(5))
            return;

        _receiverCheckedAt = DateTimeOffset.Now;
        _receiversInPlace = HidDeviceScanner.Scan()
            .Where(d => d.VendorId == VendorId && d.UsagePage == UsagePageBattery)
            .Select(d => d.ProductId)
            .ToHashSet();
    }

    /// <summary>本机的**本体**（插充电线时枚举的鼠标本体）是否已被对应接收器顶替。</summary>
    private static bool ReceiverOfSameGroupInPlace(ushort bodyPid)
    {
        if (!DeviceGroups.TryGetValue(bodyPid, out var group))
            return _receiversInPlace.Count > 0;   // 未知分组：保守按"有接收器在位"处理

        return DeviceGroups.Any(kv =>
            kv.Value == group && IsReceiverPid(kv.Key) && _receiversInPlace.Contains(kv.Key));
    }

    /// <summary>认领接收器或有线本体，二者只认领其一（接收器优先），以免两条记录读数来跳。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != VendorId || info.UsagePage != UsagePageBattery)
            return false;

        // 长度判据：必须能收发 17 字节电量帧，否则写不进/读不出完整帧。
        // 接收器用严格判据（in=17 / out=17，本机实测 0x101B、0x101A、0x1303 三者一致）；
        // 本体的接口长度未实测（插充电线时才枚举出来），只要求"容得下"这一能力。
        if (IsReceiverPid(info.ProductId))
            return AtkCodec.IsSupportedInterface(info.InputReportLength, info.OutputReportLength);

        if (info.InputReportLength < AtkCodec.ReportLength || info.OutputReportLength < AtkCodec.ReportLength)
            return false;

        // 其余 PID 视为"有线本体"（插充电线时枚举出来的鼠标本体）。
        // 本体 PID **因型号而异**（实测 F1 Pro Max = 0x1015、F1 V3 ULTIMATE+ = 0x12A9），
        // 不能靠白名单穷举——改用"**本组对应的接收器**不在位"来判定，故新型号自动支持。
        // ⚠ 必须按"本组"判，不能按"全局有没有接收器"：同机另一只鼠标的接收器在位时，
        //   会把本鼠标的本体误判成"接收器还在"而不认领（2026-10-23 实机复现）。
        RefreshReceiversInPlace();
        return !ReceiverOfSameGroupInPlace(info.ProductId);
    }

    /// <summary>
    /// 设备归一组：同一台鼠标的接收器与有线本体归到同一组（同一台设备的两个形态）。
    /// 实测配对（F1 Pro Max 与 F1 V3 ULTIMATE+ 各一套）：接收器 0x101B ↔ 本体 0x1015；
    /// 接收器 0x1303 ↔ 本体 0x12A9。该鼠标的 HID 序列号为空、容器 ID 也不同，
    /// **硬件层没有可自动配对的标识**，只能按实测配对表归一。
    /// 未登记的 PID 交回基类默认（路径哈希），等于上游行为——宁可充电时多一条，
    /// 也不能把两台不同的鼠标并成一条。
    /// </summary>
    private static readonly Dictionary<ushort, string> DeviceGroups = new()
    {
        [0x101B] = "promax",   // F1 Pro Max 8K 接收器
        [0x101A] = "promax",   // F1 Pro Max 1K 接收器（同一只鼠标的另一种无线模式）
        [0x1015] = "promax",   // F1 Pro Max 本体（插充电线时枚举）
        [0x1303] = "v3",       // F1 V3 ULTIMATE+ 8K-M 接收器
        [0x12A9] = "v3",       // F1 V3 ULTIMATE+ 本体（插充电线时枚举）
    };

    /// <summary>各组的型号名（本体上报的产品名，即机型）；按组存，两只鼠标互不覆盖。</summary>
    private static readonly ConcurrentDictionary<string, string> ModelNames = new();

    /// <summary>从 HID 路径取 PID（形如 <c>vid_373b&amp;pid_101b&amp;mi_01&amp;col05</c>）。</summary>
    private static ushort PidFromPath(string devicePath)
    {
        var m = Regex.Match(devicePath, @"pid_([0-9a-f]{4})", RegexOptions.IgnoreCase);
        return m.Success ? (ushort)Convert.ToInt32(m.Groups[1].Value, 16) : (ushort)0;
    }

    /// <summary>设备 ID 按"配对组"归一：同组共用一个 ID，不同鼠标各自一条。</summary>
    protected override string IdFor(string devicePath)
    {
        var pid = PidFromPath(devicePath);
        return DeviceGroups.TryGetValue(pid, out var group)
            ? $"{Name.ToLowerInvariant()}:atk-{group}"
            : base.IdFor(devicePath);   // 未登记的 PID → 保持上游默认（路径哈希）
    }

    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        // 型号名取"本体"（非接收器）上报的产品名，比接收器的通用名更准确；
        // 按归一组存，两只鼠标各记各的，不会互相覆盖。
        var group = DeviceGroups.TryGetValue(info.ProductId, out var g)
            ? g
            : $"p{info.ProductId:X4}";

        if (!IsReceiverPid(info.ProductId) && !string.IsNullOrWhiteSpace(info.Product))
            ModelNames[group] = info.Product;

        return ModelNames.TryGetValue(group, out var model)
            ? model
            : (string.IsNullOrWhiteSpace(info.Product)
                ? $"{DisplayName} ({info.VidPid})"
                : info.Product);
    }

    // ------------------------------------------------------------------ 写读

    /// <summary>写查询帧；WriteOutput 不可用时回落 HidD_SetOutputReport（该设备独占 WriteFile 不可用）。</summary>
    private static bool WriteQuery(HidDeviceHandle handle, byte[] frame)
    {
        if (handle.WriteOutput(frame, TimeSpan.FromSeconds(1), quiet: true))
            return true;
        return NativeSetOutputReport(handle.Info.DevicePath, frame);
    }

    /// <summary>读电量响应：最多约 1.5 秒，过滤掉队列里的普通鼠标报文。</summary>
    private static byte[]? ReadBatteryResponse(HidDeviceHandle handle, TimeSpan window)
    {
        var deadline = DateTimeOffset.UtcNow + window;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(50));
            if (AtkCodec.TryParseReply(input) is not null)
                return input;
        }
        return null;
    }

    // ------------------------------------------------------------------ 基类钩子

    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        // 接口预检查：写不进 17 字节 output 的接口直接排除（北通经验：长度从 caps 读）
        if (handle.Info.OutputReportLength < AtkCodec.ReportLength) return null;

        if (!WriteQuery(handle, AtkCodec.BuildQuery())) return null;
        if (AtkCodec.TryParseReply(ReadBatteryResponse(handle, TimeSpan.FromSeconds(2))) is not { } r)
            return null;
        return ("atk-v1", r.Percent, r.Charging);
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev))
            return null;                          // 未发帧（开关关闭/熔断中），不计失败

        if (!WriteQuery(dev.Handle, AtkCodec.BuildQuery()))
        {
            // 写失败 = 句柄已失效（设备被拔出，或 Windows 重新枚举了 HID 设备），
            // **不计失败**：基类下一轮扫描会移除旧记录并重新认领。计失败会误触熔断，
            // 让另一台设备假离线 30~60 秒（2026-10-23 实机复现）。
            return null;
        }

        var parsed = AtkCodec.TryParseReply(ReadBatteryResponse(dev.Handle, TimeSpan.FromSeconds(2)));
        if (parsed.HasValue) ReportPollAnswered(dev);   // 应答 → 清零失败计数、提前解除熔断
        else ReportPollFailure(dev);                    // 读得到句柄但不回帧 = 鼠标睡了，该熔断
        return parsed;
    }

    // ------------------------------------------------------------------ P/Invoke 后备：HidD_SetOutputReport

    [System.Runtime.InteropServices.DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(IntPtr fileHandle, byte[] reportBuffer, uint reportBufferLength);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateFileW(string path, uint desiredAccess, int shareMode,
        IntPtr securityAttributes, int creationDisposition, int flags, IntPtr templateFile);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>独占 WriteFile 不可用时的写入后备：重新以 GENERIC_WRITE 打开并走 HidD_SetOutputReport。</summary>
    private static bool NativeSetOutputReport(string devicePath, byte[] report)
    {
        const uint GENERIC_WRITE = 0x40000000;
        const int FILE_SHARE_RW = 3, OPEN_EXISTING = 3;
        IntPtr handle = CreateFileW(devicePath, GENERIC_WRITE, FILE_SHARE_RW,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
        try
        {
            return HidD_SetOutputReport(handle, report, (uint)report.Length);
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}