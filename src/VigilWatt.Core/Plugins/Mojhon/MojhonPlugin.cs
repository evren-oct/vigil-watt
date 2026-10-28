using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Mojhon;

/// <summary>
/// 功 能： 墨将（MOJHON，原 BIGBIGWON）2.4G 手柄插件 —— 厂商通道发 `A5 04 04 &lt;校验&gt;`
/// 读寄存器 0x04 取电量百分比。
/// 2026/10/26 00:45:00 workbuddy
///
/// 全部协议依据来自 USBPcap 实录（captures/mojhon-e2b7-20261026.pcap），帧格式与取值见
/// docs/hardware/mojhon.md §6；本文件不含任何推测字节。
/// </summary>
public sealed class MojhonPlugin : BatteryPluginBase
{
    /// <summary>墨将（MOJHON）USB 厂商 ID；`E2B7:122C` 实测为「烈风2 GALE2」的 2.4G 接收器。</summary>
    public const ushort MojhonVid = 0xE2B7;

    /// <summary>厂商通道的 usage page：接口 2（`MI_02`）实测 0xFFAB。</summary>
    public const ushort VendorUsagePage = 0xFFAB;

    /// <summary>厂商通道的报文长度（caps 报 65 = 1 字节报文 ID + 64 字节帧）。</summary>
    private const int VendorReportLength = 65;

    /// <summary>协议名（电量读取通道本身；在线/离线由 IsOnline + 电量是否为空表达）。</summary>
    private const string ProtocolName = "mojhon-a5-read";

    /// <summary>应答窗口：实机往返 &lt;15ms，留足唤醒余量。</summary>
    private static readonly TimeSpan ReplyWindow = TimeSpan.FromMilliseconds(1200);

    public override string Name => "Mojhon";
    public override string DisplayName => "墨将";
    public override string DisplayNameEn => "MOJHON";
    public override DeviceKind Kind => DeviceKind.Gamepad;

    /// <summary>读数全靠发帧，不能走零写入快速发现；但参与 15 秒节拍能把开机的发现从最坏 60 秒缩到 15 秒。</summary>
    public override bool SupportsDiscoveryProbe => true;

    /// <summary>10 秒 &lt; 15 秒节拍，保证每一拍都能重探（同迈从/樱桃，理由见 MochsePlugin 注释）。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    /// <summary>
    /// 协议里没有"连接状态"字段，也没有自发推送（实测页面闲停 11 秒零报文，见 §6.5），
    /// null 的三义（无应答/未开写入/熔断中）只有插件分得清，故自己记账。
    /// </summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>
    /// 候选接口：墨将 VID + 厂商页 0xFFAB。接收器的键盘/鼠标/XInput 接口都在标准页
    /// （0x0001/0x000C），据此天然排除——只有 `MI_02` 一条是 64 字节双向厂商通道。
    /// </summary>
    protected override bool IsCandidate(HidDeviceInfo info)
    {
        if (info.VendorId != MojhonVid) return false;
        if (info.UsagePage != VendorUsagePage && info.Usage != VendorUsagePage) return false;
        return info.OutputReportLength == VendorReportLength && info.InputReportLength == VendorReportLength;
    }

    /// <summary>探测 = 读一次电量寄存器；无应答（手柄休眠/对码中断）返回 null 进冷却。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        if (!TryReadBattery(handle, out int percent, out byte chargingFlag))
        {
            Log.Once(LogLevel.Debug, $"{handle.Info.DevicePath}|no-battery",
                "墨将 电量寄存器无有效读数（手柄可能休眠）");
            return null;
        }

        var charging = MapCharging(chargingFlag, percent);
        Log.ResetOnce($"{handle.Info.DevicePath}|no-battery");
        Log.Info($"墨将 握手成功: 电量={percent}% 充电={charging} 通道={handle.Info.DevicePath}");

        // 型号只问一次（认领那一轮跑）：读到的标识决定显示名，读不到就退回 PID 映射。
        EnsureModel(handle);
        return (ProtocolName, percent, charging);
    }

    /// <summary>轮询只读电量寄存器；发过帧才自己记账，未发帧（熔断/未授权）不计失败。</summary>
    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        (int? Percent, ChargingState Charging)? read = null;
        if (TryReadBattery(dev.Handle, out int percent, out byte chargingFlag))
            read = (percent, MapCharging(chargingFlag, percent));

        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    /// <summary>
    /// 把 payload[1] 映射成充电状态。**只映射实测过的两个标志值**（docs/hardware/mojhon.md §7.2）：
    /// 0x00 = 未接充电线、0x01 = 正在充电。其他取值从未观测到，一律 Unknown，不外推。
    /// 未充电时再走项目公用的收尾规则（<see cref="ChargingStateMap.FromChargingFlag"/>）：
    /// 100% ⇒ 已充满，其余 ⇒ 放电。满电这条已有实测支撑——2026-10-26 19:40 读到
    /// `电量=100 标志=0x00`（充到 100% 后拔线），与官方助手一致。
    /// </summary>
    private static ChargingState MapCharging(byte flag, int percent) => flag switch
    {
        MojhonCodec.ChargingFlagDischarging => ChargingStateMap.FromChargingFlag(false, percent),
        MojhonCodec.ChargingFlagCharging => ChargingState.Charging,
        _ => ChargingState.Unknown,
    };

    /// <summary>读一次电量寄存器并解析百分比与充电标志。</summary>
    private static bool TryReadBattery(HidDeviceHandle handle, out int percent, out byte chargingFlag)
    {
        percent = 0;
        chargingFlag = MojhonCodec.ChargingFlagUnknown;
        var request = MojhonCodec.BuildRead(MojhonCodec.RegisterBattery);
        if (!TrySend(handle, request)) return false;

        var deadline = DateTime.UtcNow + ReplyWindow;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            if (!MojhonCodec.TryParse(input, out byte register, out var payload)) continue;
            // 同一接口上可能还有上一轮迟到的其他寄存器应答，寄存器号是唯一可靠的关联依据。
            if (register != MojhonCodec.RegisterBattery) continue;
            return MojhonCodec.TryParseBattery(payload, out percent, out chargingFlag);
        }
        return false;
    }

    /// <summary>
    /// 设备自报标识 → 型号代号。口径来自官方助手本体（不是配置文件猜的）：
    /// `MojhonAssistant.exe` 里嵌着一张 devices[] 目录，原文为
    /// `{"type":"GamepadDeviceType_Gale2","deviceIdMark":"C4_SERVER",...,"webName":"GALE2","displayName":"Storm"}`，
    /// 以及 `{"type":"GamepadDeviceType_Gale2_Dongle","deviceIdMark":"C4_DONGLE",...,"webName":"R80"}`。
    /// 本机实测读寄存器 0xE2 回 `58 52 00` + `"C4_SERVER"`，故型号 = `GALE2`（烈风2）。
    /// 接收器单独在位（手柄关机）时报的是 `C4_DONGLE`，同属这一代产品，一并显示 GALE2。
    /// 见 docs/hardware/mojhon.md §2.1。
    /// </summary>
    private static readonly Dictionary<string, string> ModelByIdMark = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C4_SERVER"] = "GALE2",
        ["C4_DONGLE"] = "GALE2",
    };

    /// <summary>
    /// 拿不到设备自报标识时的兜底：PID → 型号。
    /// `0x122C` 是官方助手日志串里点名的协议通道 PID
    /// （`ignore XInput until 122C/122D protocol pipe appears`），即这台 2.4G 接收器。
    /// </summary>
    private static readonly Dictionary<ushort, string> ModelByPid = new()
    {
        [0x122C] = "GALE2",
    };

    /// <summary>接口路径 → 已问到的型号代号（只在认领那一轮读一次寄存器 0xE2）。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _modelByPath = new();

    /// <summary>
    /// 显示名按项目惯例是「品牌 + 型号代号」（黑爵 AK870 / GANSS GS3104T-Pro / 雷神 ML903），
    /// 不出现 VID/PID 这类机器串。优先用设备自报标识查到的型号，其次 PID 兜底，
    /// 两者都不认识才退回「墨将 手柄」。
    /// </summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
    {
        if (_modelByPath.TryGetValue(info.DevicePath, out string? byMark)) return $"墨将 {byMark}";
        if (ModelByPid.TryGetValue(info.ProductId, out string? byPid)) return $"墨将 {byPid}";
        return "墨将 手柄";
    }

    /// <summary>若该路径还没问过型号，就读一次标识寄存器并缓存；失败静默（不影响读数）。</summary>
    private void EnsureModel(HidDeviceHandle handle)
    {
        string path = handle.Info.DevicePath;
        if (_modelByPath.ContainsKey(path)) return;

        var request = MojhonCodec.BuildRead(MojhonCodec.RegisterDeviceName);
        if (!TrySend(handle, request)) return;

        var deadline = DateTime.UtcNow + ReplyWindow;
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;
            if (!MojhonCodec.TryParse(input, out byte register, out var payload)) continue;
            // 0x04 的应答也会落在这条通道上，按寄存器号配对（同 TryReadBattery）。
            if (register != MojhonCodec.RegisterDeviceName) continue;

            if (MojhonCodec.TryParseDeviceMark(payload, out string mark)
                && ModelByIdMark.TryGetValue(mark, out string? model))
            {
                _modelByPath[path] = model;
                Log.Info($"墨将 型号标识: {mark} → {model}");
            }
            else
            {
                Log.Once(LogLevel.Debug, $"{path}|unknown-mark", $"墨将 未知型号标识: {payload.Length} 字节，沿用 PID 映射");
            }
            return;
        }
    }

    /// <summary>
    /// 把 64 字节帧装进接口要求的报文缓冲。
    ///
    /// 两个口径对不上，实测结论如下（docs/hardware/mojhon.md §6.5）：
    /// - 报告描述符声明的是 **64 字节 IN / 64 字节 OUT，且无任何 Report ID 项**（描述符 37 字节全文见文档）；
    /// - 但 `HidP_GetCaps` 报 `OutputReportLength = 65`，且 hidapi 拒绝 64 字节写入。
    ///
    /// 真机行为：缓冲 **65 字节、`[0] = 0x00`、帧从下标 1 起** 才拿得到应答；
    /// 把帧放进下标 0（同样 65 字节）设备毫无反应。故那 1 字节是 Windows 要的填充，不是报文 ID，
    /// 值固定 0x00。偏移由长度差推导，不硬编码下标。
    /// </summary>
    private static bool TrySend(HidDeviceHandle handle, byte[] frame)
    {
        int reportLength = handle.Info.OutputReportLength;
        int offset = reportLength - frame.Length;
        if (offset is < 0 or > 1)
        {
            Log.Once(LogLevel.Warn, $"{handle.Info.DevicePath}|bad-len",
                $"墨将 报文长度不匹配：接口报 {reportLength} 字节，帧 {frame.Length} 字节，跳过");
            return false;
        }

        var buffer = new byte[reportLength];
        Array.Copy(frame, 0, buffer, offset, frame.Length);
        return handle.WriteOutput(buffer, TimeSpan.FromMilliseconds(500));
    }
}
