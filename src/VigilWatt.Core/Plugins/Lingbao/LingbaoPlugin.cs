using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Compx;
using VigilWatt.Core.Plugins.Mchose;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Lingbao;

/// <summary>
/// 功 能： 凌豹 (LINGBAO) K98 2.4G 键盘插件，复用 Compx V1 会话/推送帧。
/// 2026/10/20 17:20:00 Bamlax
/// 与 AULA F99 Pro 同为 3554:FA09，靠会话 ACK 型号码 0x0212 区分；见 docs/hardware/protocols.md §13。
/// </summary>
public sealed class LingbaoPlugin : BatteryPluginBase
{
    public const ushort LingbaoVid = 0x3554;
    public const ushort K98Pid = 0xFA09;

    /// <summary>K98 会话 ACK 的型号码（ACK 第 9/10 字节）；AULA F99 Pro 是 0x0158。</summary>
    public const ushort K98ModelCode = 0x0212;

    private const string SettingsKey = "Lingbao";

    public override string Name => SettingsKey;
    public override string DisplayName => "凌豹";
    public override string DisplayNameEn => "LINGBAO";
    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>失败后 10 秒内不再开会话。不参加 15 秒发现，写入留在 60 秒主轮询。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    /// <summary>没有周期性心跳，沉默无意义；查询失败由插件自己报账。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>只认 3554:FA09 的 0x13 vendor 接口（0xFF02/0x0002，20/20）。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == LingbaoVid && info.ProductId == K98Pid
           && info.UsagePage == 0xFF02 && info.Usage == 0x0002
           && info.InputReportLength >= MchoseV1Codec.FrameLength
           && info.OutputReportLength >= MchoseV1Codec.FrameLength;

    /// <summary>
    /// 3554:FA09 与狼蛛共用：基类在探测前就 TryClaim，排序在前的 AULA 会先把路径占住，
    /// 型号不符释放后本轮也不会再轮到凌豹。这里等 AULA 先探完再走基类，K98 才能被认领。
    /// </summary>
    public override async Task<IReadOnlyList<DeviceStatus>> AttachAsync(
        IReadOnlyList<HidDeviceInfo> availableDevices, DeviceClaimRegistry claims)
    {
        bool contested = false;
        foreach (var info in availableDevices)
        {
            if (info.VendorId == LingbaoVid && info.ProductId == K98Pid
                && !claims.IsClaimedBy(info.DevicePath, Name))
            {
                contested = true;
                break;
            }
        }

        if (contested) await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        return await base.AttachAsync(availableDevices, claims).ConfigureAwait(false);
    }

    /// <summary>探测 = 开一次会话，先验型号码，再等 0A 电量推送。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var read = TryRead(handle);
        return read is { } value ? ("lingbao-v1", value.Percent, value.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = TryRead(dev.Handle);
        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    /// <summary>发开会话帧，等 ACK 验型号，再解析 0A 推送；型号不符或拿不到电量返回 null。</summary>
    private static (int? Percent, ChargingState Charging)? TryRead(HidDeviceHandle handle)
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

    protected override string BuildDisplayName(HidDeviceInfo info) => "凌豹 K98";
}
