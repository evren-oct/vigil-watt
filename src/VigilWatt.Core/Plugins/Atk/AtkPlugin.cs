using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Atk;

/// <summary>
/// 功 能： ATK/VXE 无线鼠标电量插件——主动查询 FF02 口的 17 字节命令-应答协议。
/// 2026/10/20 22:14:00 Yang
/// 2026/10/26 22:40:03 ranxue：接入 A9 Air 大师版+（接收器 0x1278 / 本体 0x1293），双连接改留本体
/// 帧结构与实测依据见 docs/hardware/atk.md（§2.2 §3.3）；编解码在 AtkCodec，配对在 AtkDevicePairing。
/// </summary>
public sealed class AtkPlugin : BatteryPluginBase
{
    public override string Name => "Atk";                 // AppSettings.PluginEnabled 的 key
    public override string DisplayName => "ATK";
    public override string DisplayNameEn => "ATK";
    /// <summary>一套开关管鼠标与键盘两条链路：具体图标由 <see cref="ResolveKind"/> 按链路给。</summary>
    public override DeviceKind Kind => DeviceKind.Unknown;

    protected override DeviceKind ResolveKind(string protocol)
        => protocol == ProtocolVia ? DeviceKind.Keyboard : DeviceKind.Mouse;

    // 会写帧（发查询帧）→ 按社区适配指南"主动通信不进 15 秒常规发现节拍"，
    // 与 Betop / Ganss / Aula 一致保持默认 false；发现延迟走常规扫描周期。
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    // null 与"被熔断挡住"不可区分 → 基类不自动记账，由 PollDevice 自报账
    protected override OfflinePolicy Offline => new() { Silence = TimeSpan.Zero, CountPollFailures = false };

    private const ushort VendorId = 0x373B;               // ⚠ 实测修正：非 0x375B

    /// <summary>电量口的 usage page；必须精确匹配，否则会对 FF03/FF04/FF05/FF06 等厂商口各白发一帧查询。</summary>
    private const ushort UsagePageBattery = 0xFF02;

    /// <summary>
    /// 已知的接收器 PID：0x101B（8K）、0x101A（1K）、0x1303（8K-M）、0x1278（GEM 8K-L），
    /// 四者都认同一条查询帧、响应结构一致。配对表统一在 <see cref="AtkDevicePairing"/>。
    /// </summary>
    private static bool IsReceiverPid(ushort pid) => AtkDevicePairing.IsReceiver(pid);

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

    /// <summary>
    /// 整机是否已有任何 ATK 接收器在位。**只给"未登记分组"的本体兜底**用：
    /// 那种型号我们不知道它跟哪个接收器是一台，比较不了组，只能保守起来。
    /// 已登记分组一律交给 <see cref="AtkDevicePairing"/> 按组配对，不走这条。
    /// </summary>
    private static bool AnyReceiverInPlace() => _receiversInPlace.Count > 0;

    /// <summary>鼠标链路（op 帧）与键盘链路（VIA）各一套协议名。</summary>
    private const string ProtocolMouse = "atk-v1";
    private const string ProtocolVia = "atk-keyboard-via";

    /// <summary>
    /// 认领接收器或有线本体，二者同一时刻只留一条（避免两条记录读数互相打架）。
    /// 双连接时优先**本体**（插线形态）：只有它带充电状态、且产品名就是机型，
    /// 摘掉接收器的动作在 <see cref="SkipPathsForThisPass"/> 里做。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        // 键盘链路（VIA，page 0xFF60）：与鼠标同 VID，靠 usage page 分流，必须先判。
        if (AtkViaCodec.IsSupportedInterface(info.VendorId, info.UsagePage,
                info.InputReportLength, info.OutputReportLength, info.OutputReportId))
            return true;

        if (info.VendorId != VendorId || info.UsagePage != UsagePageBattery)
            return false;

        // 长度判据：必须能收发 17 字节电量帧，否则写不进/读不出完整帧。
        // 接收器用严格判据（in=17 / out=17，本机实测 0x101B、0x101A、0x1303、0x1278 四者一致）；
        // 本体只要求"容得下"——A9 Air+ 实测也是 17/17，但其余型号的本体尚未逐个实测。
        if (IsReceiverPid(info.ProductId))
            return AtkCodec.IsSupportedInterface(info.InputReportLength, info.OutputReportLength);

        if (info.InputReportLength < AtkCodec.ReportLength || info.OutputReportLength < AtkCodec.ReportLength)
            return false;

        // 其余 PID 视为"有线本体"（插充电线时枚举出来的鼠标本体）。本体 PID **因型号而异**
        //（实测 F1 Pro Max = 0x1015、F1 V3 ULTIMATE+ = 0x12A9、A9 Air+ = 0x1293），不能靠白名单穷举。
        //
        // 已登记分组：本体**一律可认领**。双连接（接收器 + 插线同时在场）时由
        // SkipPathsForThisPass 把接收器那条摘掉，只留带充电状态的本体。
        // 旧写法"接收器在位就别认领本体"会在 A9 Air 双连接时**一条读数都不出**——
        // 实测那只鼠标切到有线后接收器已沉默、只有本体应答（85%、充电中），见 atk.md §3.1。
        if (AtkDevicePairing.GroupOf(info.ProductId) is not null)
            return true;

        // 未登记分组的新型号：不知道它跟哪个接收器是同一台，只能保守——
        // 整机已有 ATK 接收器在位时先不认领，免得同一只鼠标在列表里出两行。
        RefreshReceiversInPlace();
        return !AnyReceiverInPlace();
    }

    /// <summary>
    /// 同一只鼠标的接收器与本体同时在场时，只留**本体**（插线形态）：
    /// 只有它带充电状态、且产品名就是机型。配对依据是型号归一组（实测配对表），
    /// 序列号只在两边都有时作为否决条件（防止两只同型号鼠标被误并）。
    /// 实测依据见 atk.md §3.1 / §4。
    /// </summary>
    protected override IReadOnlyCollection<string> SkipPathsForThisPass(
        IReadOnlyList<HidDeviceInfo> availableDevices)
    {
        var refs = availableDevices
            .Where(d => d.VendorId == VendorId && d.UsagePage == UsagePageBattery)
            .Select(d => new AtkInterfaceRef(d.DevicePath, d.ProductId, d.Product, d.SerialNumber))
            .ToList();
        return AtkDevicePairing.ReceiverPathsToSkip(refs);
    }

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
        return AtkDevicePairing.GroupOf(pid) is { } group
            ? $"{Name.ToLowerInvariant()}:atk-{group}"
            : base.IdFor(devicePath);   // 未登记的 PID → 保持上游默认（路径哈希）
    }

    /// <summary>已验证型号表（键盘 A98 Pro 与 F1 ULTIMATE 2.0 两个 PID）。</summary>
    private static readonly Dictionary<ushort, string> KnownModelByPid = new()
    {
        [0x1152] = "ATK A98 Pro",
        [0x11D9] = "ATK F1 Ultimate 2.0",
        [0x11E4] = "ATK F1 Ultimate 2.0",
    };

    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        if (KnownModelByPid.TryGetValue(info.ProductId, out var known)) return known;

        // 型号名取"本体"（非接收器）上报的产品名，比接收器的通用名更准确；
        // 按归一组存，两只鼠标各记各的，不会互相覆盖。
        var group = AtkDevicePairing.GroupOf(info.ProductId) ?? $"p{info.ProductId:X4}";

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
        // 键盘链路先判：同一接收器 VID 相同，反过来会把 VIA 查询帧发给鼠标。
        if (AtkViaCodec.IsSupportedInterface(handle.Info.VendorId, handle.Info.UsagePage,
                handle.Info.InputReportLength, handle.Info.OutputReportLength, handle.Info.OutputReportId))
        {
            var kb = QueryVia(handle);
            return kb.HasValue
                ? (ProtocolVia, kb.Value.Percent, kb.Value.Charging ? ChargingState.Charging : ChargingState.Discharging)
                : null;
        }

        if (handle.Info.OutputReportLength < AtkCodec.ReportLength) return null;

        if (!WriteQuery(handle, AtkCodec.BuildQuery())) return null;
        if (AtkCodec.TryParseReply(ReadBatteryResponse(handle, TimeSpan.FromSeconds(2))) is not { } r)
            return null;
        return (ProtocolMouse, r.Percent, r.Charging);
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev))
            return null;                          // 未发帧（开关关闭/熔断中），不计失败

        if (dev.Protocol == ProtocolVia)
        {
            var via = QueryVia(dev.Handle);
            if (via.HasValue) ReportPollAnswered(dev);
            else ReportPollFailure(dev);
            return via.HasValue
                ? (via.Value.Percent, via.Value.Charging ? ChargingState.Charging : ChargingState.Discharging)
                : null;
        }

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

    /// <summary>键盘链路：发一条 VIA 查询，在 1 秒内等电量响应。</summary>
    private static (int Percent, bool Charging)? QueryVia(HidDeviceHandle handle)
    {
        if (!IsWritesEnabled("Atk")) return null;
        if (!handle.WriteOutput(AtkViaCodec.CreateQuery(handle.Info.OutputReportLength),
                TimeSpan.FromMilliseconds(500)))
            return null;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            if (!AtkViaCodec.TryParse(input, out int percent, out bool charging))
            {
                if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"ATK 键盘忽略非电量响应: {Log.Hex(input)}");
                continue;
            }
            Log.Info($"ATK 键盘 VIA 电量查询: {percent}% 充电={charging} 原始={Log.Hex(input)}");
            return (percent, charging);
        }
        Log.Once(LogLevel.Debug, $"atk-kb-silent:{handle.Info.DevicePath}",
            $"ATK 键盘查询无响应（多半在睡，恢复应答后会再记一条）: {handle.Info.DevicePath}");
        return null;
    }
}
