using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Compx;
using VigilWatt.Core.Plugins.Mchose;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Lingbao;

/// <summary>
/// 功 能： 凌豹 (LINGBAO) 2.4G 插件：M5PRO Max 鼠标走 feature 查询，K98 键盘走 Compx 会话。
/// 2026/10/24 17:42:00 L；2026/10/25 并入 K98（Bamlax）。
/// 两套帧与验证边界见 docs/hardware/brands/lingbao.md §1（M5PRO Max）与 §2（K98）；会写帧，故默认关闭。
/// </summary>
public sealed class LingbaoPlugin : BatteryPluginBase
{
    /// <summary>2.4G 接收器（本机实测 VID_3151&amp;PID_402D）。有线形态 4026 只回显命令、不返回状态，故不认领。</summary>
    public const ushort LingbaoVid = 0x3151;

    public const ushort M5ProMaxDonglePid = 0x402D;

    /// <summary>厂商集合：usage page 0xFFFF、usage 2、feature 65 字节。</summary>
    private const ushort VendorUsagePage = 0xFFFF;

    private const ushort VendorUsage = 0x0002;

    /// <summary>K98 与 AULA F99 Pro 同为 3554:FA09，靠会话 ACK 型号码区分。</summary>
    public const ushort CompxVid = 0x3554;

    public const ushort K98Pid = 0xFA09;

    /// <summary>K98 的型号码（ACK 第 9/10 字节）；AULA F99 Pro 是 0x0158。</summary>
    public const ushort K98ModelCode = 0x0212;

    private const string SettingsKey = "Lingbao";

    private const string ProtocolM5ProMax = "lingbao-fea";

    private const string ProtocolK98 = "lingbao-v1";

    public override string Name => SettingsKey;
    public override string DisplayName => "凌豹";
    public override string DisplayNameEn => "LINGBAO";

    /// <summary>一个插件管鼠标与键盘（罗技/迈从先例）：具体图标由 <see cref="ResolveKind"/> 按协议给。</summary>
    public override DeviceKind Kind => DeviceKind.Unknown;

    protected override DeviceKind ResolveKind(string protocol)
        => protocol == ProtocolK98 ? DeviceKind.Keyboard : DeviceKind.Mouse;

    /// <summary>两条链路都只发一条查询帧，代价可忽略；冷却 10 秒避免睡着时被 15 秒节拍反复戳。</summary>
    public override bool SupportsDiscoveryProbe => true;

    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    /// <summary>休眠静音是常态，null 的含义由插件自己报账，不交给基类猜。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    protected override bool IsCandidate(HidDeviceInfo info)
        => IsM5ProMaxInterface(info) || IsK98Interface(info);

    private static bool IsM5ProMaxInterface(HidDeviceInfo info)
        => info.VendorId == LingbaoVid && info.ProductId == M5ProMaxDonglePid
           && info.UsagePage == VendorUsagePage && info.Usage == VendorUsage
           && info.FeatureReportLength >= LingbaoCodec.ReportLength;

    /// <summary>只认 3554:FA09 的 0x13 vendor 接口（0xFF02/0x0002，20/20）。</summary>
    private static bool IsK98Interface(HidDeviceInfo info)
        => info.VendorId == CompxVid && info.ProductId == K98Pid
           && info.UsagePage == 0xFF02 && info.Usage == 0x0002
           && info.InputReportLength >= MchoseV1Codec.FrameLength
           && info.OutputReportLength >= MchoseV1Codec.FrameLength;

    /// <summary>
    /// 3554:FA09 与 AULA 共用：插件并行认领，AULA 排序在前会先占住路径，型号不符释放后本轮不再轮到本插件。
    /// 有这条争议接口时等 AULA 先探完，K98 才认领得到。
    /// </summary>
    public override async Task<IReadOnlyList<DeviceStatus>> AttachAsync(
        IReadOnlyList<HidDeviceInfo> availableDevices, DeviceClaimRegistry claims)
    {
        bool contested = availableDevices.Any(info => IsK98Interface(info) && !claims.IsClaimedBy(info.DevicePath, Name));
        if (contested) await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        return await base.AttachAsync(availableDevices, claims).ConfigureAwait(false);
    }

    /// <summary>探测：M5PRO Max 发一条状态查询读 feature 响应，K98 开会话验型号后等 0A 推送。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        if (IsK98Interface(handle.Info))
        {
            var k98 = TryReadK98(handle);
            return k98 is { } k ? (ProtocolK98, k.Percent, k.Charging) : null;
        }

        var read = Query(handle);
        return read is { } value ? (ProtocolM5ProMax, value.Percent, value.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = dev.Protocol == ProtocolK98 ? TryReadK98(dev.Handle) : Query(dev.Handle);
        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    /// <summary>产品名是泛化的「2.4G Wireless Mouse」，型号只能按 PID 写死。</summary>
    protected override string BuildDisplayName(HidDeviceInfo info)
        => IsK98Interface(info) ? "凌豹 K98" : "凌豹 M5PRO Max";

    /// <summary>SetFeature 后设备约 10 ms 才刷新响应；读太早会拿到上一条状态块。</summary>
    private static (int? Percent, ChargingState Charging)? Query(HidDeviceHandle handle)
    {
        if (handle.Info.FeatureReportLength < LingbaoCodec.ReportLength) return null;
        if (!handle.SetFeature(LingbaoCodec.ReportId, LingbaoCodec.BuildStatusQuery()))
        {
            Log.Once(LogLevel.Debug, LogKey(handle, "fea"), $"凌豹 状态查询写入失败: {handle.Info.DevicePath}");
            return null;
        }

        Thread.Sleep(20);
        var report = handle.GetFeature(LingbaoCodec.ReportId);
        if (report == null || !LingbaoCodec.TryParseStatus(report, out int percent, out bool charging))
        {
            // 只记一次：发现节拍每 15 秒重探，鼠标睡着时逐次记会把日志刷满
            Log.Once(LogLevel.Debug, LogKey(handle, "fea"), $"凌豹 响应里没有可信电量: {Log.Hex(report ?? [])}");
            return null;
        }

        Log.ResetOnce(LogKey(handle, "fea"));
        Log.Info($"凌豹 状态查询: 电量={percent}% 充电={charging} 原始={Log.Hex(report)}");
        return (percent, ChargingStateMap.FromChargingFlag(charging, percent));
    }

    /// <summary>发开会话帧，先验型号码再解析 0A 推送；型号不是 K98 或拿不到电量返回 null。</summary>
    private static (int? Percent, ChargingState Charging)? TryReadK98(HidDeviceHandle handle)
    {
        if (handle.Info.OutputReportLength < MchoseV1Codec.FrameLength) return null;
        if (!handle.WriteOutput(MchoseV1Codec.BuildFrame(MchoseV1Codec.SessionCmd, 0x01),
                TimeSpan.FromMilliseconds(500)))
            return null;

        bool modelVerified = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            if (CompxSessionAck.TryGetModelCode(input, out ushort model))
            {
                if (model != K98ModelCode)
                {
                    Log.Debug($"凌豹 会话ACK型号=0x{model:X4}，不是 K98，放弃认领");
                    return null;
                }
                modelVerified = true;
                if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"凌豹 会话ACK: {Log.Hex(input)}");
                continue;
            }

            // 必须先确认型号码：同 VID/PID 的 AULA F99 Pro 也会推 0A 电量帧
            if (!modelVerified) continue;

            var push = MchoseV1Codec.ParsePush(input);
            if (push.Kind == V1PushKind.Battery)
            {
                Log.Info($"凌豹 0A 电量推送: 电量={push.Percent} 标志=0x{push.ChargeFlag:X2} 原始={Log.Hex(input)}");
                return (push.Percent, ParseCharge(push.ChargeFlag, push.Percent));
            }

            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"凌豹 输入帧: {Log.Hex(input)}");
        }
        return null;
    }

    private static ChargingState ParseCharge(byte flag, int percent)
        => ChargingStateMap.FromChargingFlag((flag & 0x10) != 0, percent);

    private static string LogKey(HidDeviceHandle handle, string protocol) => $"{handle.Info.DevicePath}|lingbao-{protocol}";
}
