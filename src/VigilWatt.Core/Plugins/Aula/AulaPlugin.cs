using VigilWatt.Core.Hid;
using VigilWatt.Core.Models;
using VigilWatt.Core.Plugins.Compx;
using VigilWatt.Core.Plugins.Mchose;
using VigilWatt.Core.Utils;

namespace VigilWatt.Core.Plugins.Aula;

/// <summary>
/// 功 能： AULA F99 Pro 2.4G 键盘插件，复用迈从 0x13 会话后读推送。
/// 2026/10/19 15:10:00 xliu
/// 只发已见到的会话帧，不发无应答的 4A，也不进 15 秒发现。见 docs/hardware/protocols.md §9。
/// </summary>
public sealed class AulaPlugin : BatteryPluginBase
{
    public const ushort AulaVid = 0x3554;
    public const ushort F99ProPid = 0xFA09;

    /// <summary>F99 Pro 会话 ACK 的型号码；凌豹 K98 是 0x0212，靠它避免抢错同 VID/PID 的接口。</summary>
    public const ushort F99ProModelCode = 0x0158;

    private const string SettingsKey = "Aula";

    public override string Name => SettingsKey;
    public override string DisplayName => "狼蛛";
    public override string DisplayNameEn => "AULA";
    public override DeviceKind Kind => DeviceKind.Keyboard;

    /// <summary>失败后 10 秒内不再开会话。不进 15 秒发现，写入留在 60 秒主轮询。</summary>
    protected override TimeSpan ProbeCooldown => TimeSpan.FromSeconds(10);

    /// <summary>没有周期性心跳，沉默无意义；查询失败由插件自己报账。</summary>
    protected override OfflinePolicy Offline { get; } = new()
    {
        Silence = TimeSpan.Zero,
        CountPollFailures = false,
    };

    /// <summary>只认 3554:FA09 的 0x13 vendor 接口（0xFF02/0x0002，20/20）。</summary>
    protected override bool IsCandidate(HidDeviceInfo info)
        => info.VendorId == AulaVid && info.ProductId == F99ProPid
           && info.UsagePage == 0xFF02 && info.Usage == 0x0002
           && info.InputReportLength >= MchoseV1Codec.FrameLength
           && info.OutputReportLength >= MchoseV1Codec.FrameLength;

    /// <summary>探测 = 开一次会话，等设备回 0A 电量推送。</summary>
    protected override (string Protocol, int? Percent, ChargingState Charging)? Probe(HidDeviceHandle handle)
    {
        var read = TryRead(handle);
        return read is { } value ? ("aula-v1", value.Percent, value.Charging) : null;
    }

    protected override (int? Percent, ChargingState Charging)? PollDevice(TrackedDevice dev)
    {
        if (!CanWrite(dev)) return null;

        var read = TryRead(dev.Handle);
        if (read.HasValue) ReportPollAnswered(dev);
        else ReportPollFailure(dev);
        return read;
    }

    /// <summary>发开会话帧，等 0A 推送或 4A 响应；拿不到返回 null。</summary>
    private static (int? Percent, ChargingState Charging)? TryRead(HidDeviceHandle handle)
    {
        if (handle.Info.OutputReportLength < MchoseV1Codec.FrameLength) return null;
        if (!handle.WriteOutput(MchoseV1Codec.BuildFrame(MchoseV1Codec.SessionCmd, 0x01),
                TimeSpan.FromMilliseconds(500)))
            return null;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            var input = handle.ReadInput(TimeSpan.FromMilliseconds(300));
            if (input == null) continue;

            if (CompxSessionAck.TryGetModelCode(input, out ushort model))
            {
                if (model != F99ProModelCode)
                {
                    Log.Debug($"AULA 会话ACK型号=0x{model:X4}，不是 F99 Pro，放弃认领");
                    return null;
                }
                continue;
            }

            var push = MchoseV1Codec.ParsePush(input);
            if (push.Kind == V1PushKind.Battery)
            {
                Log.Info($"AULA 会话电量推送: 电量={push.Percent} 标志=0x{push.ChargeFlag:X2} 原始={Log.Hex(input)}");
                return (push.Percent, ParseCharge(push.ChargeFlag, push.Percent));
            }

            if (MchoseV1Codec.ParseBatteryResponse(input, out int pct, out byte flag) == V1ResponseKind.Battery)
            {
                Log.Info($"AULA 4A 响应: 电量={pct} 状态字节=0x{flag:X2} 原始={Log.Hex(input)}");
                return (pct, ParseCharge(flag, pct));
            }

            if (Log.IsEnabled(LogLevel.Debug)) Log.Debug($"AULA 输入帧: {Log.Hex(input)}");
        }
        return null;
    }

    private static ChargingState ParseCharge(byte flag, int percent)
        => ChargingStateMap.FromChargingFlag((flag & 0x10) != 0, percent);

    protected override string BuildDisplayName(HidDeviceInfo info) => "AULA F99 Pro";
}
